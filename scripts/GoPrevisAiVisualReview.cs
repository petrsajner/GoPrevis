using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    string StableAiSceneSignature()
    {
        var document=Snapshot();if(document.Cameras.Count>0){document.CameraKeys.Clear();document.ShotPosition=Array.Empty<float>();document.ShotRotation=Array.Empty<float>();document.FocalLength=0;}
        return JsonSerializer.Serialize(document);
    }
    public sealed record GaitMeasurement(string Actor,int ContactSamples,float OppositionScore,float ToeDirectionDot,float FaceDirectionDot,float AverageTravelSpeed,float AverageStanceSpeed);
    GaitMeasurement MeasureActualGait(Entry entry,double from,double to,double step=.025)
    {
        var actor=(RiggedActor)entry.Node;float score=0,weight=0,toeScore=0,faceScore=0,travelSpeed=0,stanceSpeed=0;int count=0,frames=0;
        (Vector3 Root,Vector3 Left,Vector3 Right,Vector3 ToeL,Vector3 ToeR,Vector3 Face) Sample(double time)
        {
            SetTime(time);Vector3 Joint(string name)=>actor.GlobalTransform*actor.GetPoseJointPosition(name);
            return(Joint("Hips"),Joint("LeftFoot"),Joint("RightFoot"),Joint("LeftToe"),Joint("RightToe"),actor.VisibleFaceDirectionWorld());
        }
        var before=Sample(from);
        for(double time=from+step;time<=to+1e-7;time+=step)
        {
            var after=Sample(time);var travel=after.Root-before.Root;travel.Y=0;if(travel.Length()<.0001f){before=after;continue;}var direction=travel.Normalized();float speed=travel.Length()/(float)step;
            var toes=(after.ToeL-after.Left)+(after.ToeR-after.Right);toes.Y=0;if(toes.LengthSquared()>.00001f)toeScore+=toes.Normalized().Dot(direction);
            var face=after.Face;face.Y=0;if(face.LengthSquared()>.00001f)faceScore+=face.Normalized().Dot(direction);travelSpeed+=speed;frames++;
            float low=Math.Min(after.Left.Y,after.Right.Y),previousLow=Math.Min(before.Left.Y,before.Right.Y);
            foreach(var foot in new[]{(before.Left,after.Left),(before.Right,after.Right)})
            {
                if(foot.Item2.Y>low+.035f||foot.Item1.Y>previousLow+.035f||Math.Abs(foot.Item2.Y-foot.Item1.Y)>.012f)continue;
                var relative=(foot.Item2-after.Root)-(foot.Item1-before.Root);relative.Y=0;float distance=relative.Length();if(distance<.0001f||distance>.15f)continue;
                score-=relative.Dot(direction);weight+=distance;stanceSpeed+=distance/(float)step;count++;
            }
            before=after;
        }
        return new(entry.Data.Id,count,weight>.000001f?score/weight:0,frames>0?toeScore/frames:0,frames>0?faceScore/frames:0,frames>0?travelSpeed/frames:0,count>0?stanceSpeed/count:0);
    }
    async Task<AiVisualReview> PreviewAiPlan(AiScenePlan plan,bool replace,CancellationToken cancellation)
    {
        if(DisplayServer.GetName()=="headless")throw new InvalidOperationException("AI visual review requires the graphics renderer.");
        var original=Snapshot();string project=_projectPath;double originalTime=_time;string? selected=_selected?.Data.Id,selectedCamera=_selectedCamera?.Id;var undo=_undo.ToArray();var redo=_redo.ToArray();bool pilot=_pilot;var editor=_editorCamera.Transform;
        var review=new AiVisualReview();SubViewport? view=null;var images=new List<Image>();
        try
        {
            if(_aiProgress!=null)_aiProgress.Text="Rehearsing proposed shot · checking the actual rendered animation…";
            await ApplyAiPlan(plan,replace,cancellation);
            int width=480,height=(int)Math.Round(width/ProjectAspect);if(height>360){height=360;width=(int)Math.Round(height*ProjectAspect);}width=Math.Max(64,width);height=Math.Max(64,height);
            view=new SubViewport{Size=new Vector2I(width,height),World3D=_sceneViewport.World3D,RenderTargetUpdateMode=SubViewport.UpdateMode.Once,PositionalShadowAtlasSize=2048,Msaa3D=Viewport.Msaa.Msaa4X};AddChild(view);
            var camera=new Camera3D{Current=true,CullMask=1,KeepAspect=Camera3D.KeepAspectEnum.Width};view.AddChild(camera);
            var active=_cameras.Single(c=>c.Id==ActiveCameraId);var shotTimes=Enumerable.Range(0,6).Select(i=>Math.Max(0,_duration-1.0/_fps)*i/5).ToArray();
            foreach(double time in shotTimes)
            {
                cancellation.ThrowIfCancellationRequested();SetTime(time);camera.GlobalTransform=active.Rig.ShotCamera.GlobalTransform;camera.Fov=active.Rig.ShotCamera.Fov;camera.Near=active.Rig.ShotCamera.Near;camera.Far=active.Rig.ShotCamera.Far;images.Add(await CaptureShotImage(view,cancellation));
            }
            review.Images.Add(new("Actual active-camera frames, left to right, top then bottom; times "+string.Join(", ",shotTimes.Select(t=>t.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)))+" seconds. Check framing, occlusion and continuity.",Sheet(images,3)));
            foreach(var image in images)image.Dispose();images.Clear();
            foreach(var entry in _entries.Where(e=>e.Node is RiggedActor&&e.Data.Path.Enabled&&e.Data.MotionClips.Any(c=>AiLocomotion(c.Name))).Take(2).ToArray())
            {
                var clip=entry.Data.MotionClips.First(c=>AiLocomotion(c.Name));double start=Math.Clamp(clip.Start+.12,0,_duration),end=Math.Min(_duration,Math.Min(clip.Start+clip.Duration-.05,start+2.1));
                if(end-start<.3)continue;
                var measured=MeasureActualGait(entry,start,end);review.Measurements.Add(measured);
                if(measured.ContactSamples>=10&&measured.OppositionScore<-.15f)
                {review.Passed=false;review.Errors.Add(entry.Data.Name+": actual stance feet move WITH the travel direction relative to the hips. The gait slides backward (opposition score "+measured.OppositionScore.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+"). Correct the source/pose-to-route relationship; do not approve this preview.");}
                double duration=Math.Min(1.35,end-start);SetTime(start+duration*.5);var root=entry.Node.GlobalTransform*((RiggedActor)entry.Node).GetPoseJointPosition("Hips");
                var tangent=ActorPathRuntime.Evaluate(entry.Data.Path,start+duration*.5).Tangent;tangent.Y=0;if(tangent.LengthSquared()<.001)tangent=Vector3.Forward;tangent=tangent.Normalized();
                camera.Position=root+tangent.Cross(Vector3.Up)*4.5f;camera.LookAt(root,Vector3.Up);camera.Fov=48;view.Size=new Vector2I(360,280);
                var hidden=Descendants(_world).OfType<MeshInstance3D>().Where(mesh=>mesh.IsVisibleInTree()&&!entry.Node.IsAncestorOf(mesh)&&mesh!=_sceneGround).ToArray();foreach(var mesh in hidden)mesh.Visible=false;
                try
                {
                    for(int i=0;i<9;i++){cancellation.ThrowIfCancellationRequested();SetTime(start+duration*i/8);images.Add(await CaptureShotImage(view,cancellation));}
                }
                finally{foreach(var mesh in hidden)if(GodotObject.IsInstanceValid(mesh))mesh.Visible=true;}
                review.Images.Add(new("Actual side-view gait sequence of "+entry.Data.Name+", nine chronological frames in three rows. The character travels LEFT TO RIGHT in this camera. Inspect foot contact, limb stepping, body/front direction and sliding; this is the actual retargeted skeleton, not a direction arrow. Measures: stance-opposition="+measured.OppositionScore.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+", toe/travel dot="+measured.ToeDirectionDot.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+".",Sheet(images,3)));
                foreach(var image in images)image.Dispose();images.Clear();
            }
            review.Summary="Actual program rehearsal completed. Inspect all attached rendered sequences before accepting; forward walking should advance left-to-right in the side view and planted feet should move opposite travel relative to the hips.";
            if(DiagnosticRun)
            {
                string folder=ProjectSettings.GlobalizePath("res://output/ai-visual-reviews/run-"+Guid.NewGuid().ToString("N")[..10]);Directory.CreateDirectory(folder);
                for(int i=0;i<review.Images.Count;i++){var data=Convert.FromBase64String(review.Images[i].DataUrl.Split(',')[1]);File.WriteAllBytes(Path.Combine(folder,"preview-"+i+".png"),data);}
                File.WriteAllText(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new{review.Passed,review.Summary,review.Errors,review.Measurements,captions=review.Images.Select(i=>i.Caption)},_json));
                GD.Print("AI_VISUAL_REVIEW passed="+review.Passed+" output="+folder);
            }
            return review;
        }
        finally
        {
            foreach(var image in images)image.Dispose();view?.QueueFree();Restore(original,"");_projectPath=project;SetTime(originalTime);RestoreInteractionSelection(selected,selectedCamera);_pilot=pilot;_editorCamera.Transform=editor;
            _undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);RefreshHistoryControls();
        }
        static string Sheet(IReadOnlyList<Image> frames,int columns)
        {
            int width=frames[0].GetWidth(),height=frames[0].GetHeight();using var sheet=Image.CreateEmpty(width*columns,height*((frames.Count+columns-1)/columns),false,Image.Format.Rgba8);sheet.Fill(new Color("1d1d23"));
            for(int i=0;i<frames.Count;i++){if(frames[i].GetFormat()!=Image.Format.Rgba8)frames[i].Convert(Image.Format.Rgba8);sheet.BlitRect(frames[i],new Rect2I(0,0,width,height),new Vector2I(i%columns*width,i/columns*height));}
            return "data:image/png;base64,"+Convert.ToBase64String(sheet.SavePngToBuffer());
        }
    }
}
