using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GenPrevis;

/// <summary>GPU-render all authored static poses using the actual prepared-rig runtime.</summary>
public partial class PoseContentProbe : SceneTree
{
    public override async void _Initialize()
    {
        try
        {
            string output=ProjectSettings.GlobalizePath("res://output");Directory.CreateDirectory(output);
            string thumbnails=ProjectSettings.GlobalizePath("res://assets/content/thumbnails");Directory.CreateDirectory(thumbnails);
            var viewport=new SubViewport{Size=new Vector2I(192,160),OwnWorld3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Always,Msaa3D=Viewport.Msaa.Msaa4X};Root.AddChild(viewport);
            var stage=new Node3D();viewport.AddChild(stage);stage.AddChild(new WorldEnvironment{Environment=StudioLighting.CreateEnvironment()});
            var floor=new MeshInstance3D{Mesh=new PlaneMesh{Size=new Vector2(12,12)},MaterialOverride=new StandardMaterial3D{AlbedoColor=new Color("303035"),Roughness=1}};stage.AddChild(floor);floor.Position=new Vector3(0,-.015f,0);
            var actor=new RiggedActor();stage.AddChild(actor);actor.SetBodyColor(new Color(.65f,.71f,.95f));
            var camera=new Camera3D{Current=true,Projection=Camera3D.ProjectionType.Orthogonal,KeepAspect=Camera3D.KeepAspectEnum.Height,Near=.01f,Far=100};stage.AddChild(camera);
            for(int frame=0;frame<3;frame++)await ToSignal(this,SignalName.ProcessFrame);
            var report=new List<object>();
            foreach(var pose in PoseCatalog.All)
            {
                actor.SetStaticPose(pose.Pose);actor.EvaluateTimeline(0);
                var bounds=actor.GetPreparedPoseBounds();var center=bounds.GetCenter();
                if(!bounds.Position.IsFinite()||!bounds.Size.IsFinite()||bounds.Size.Length()<.2f)throw new InvalidOperationException("Empty or nonfinite pose bounds: "+pose.Id);
                var direction=new Vector3(2,1.45f,-3).Normalized();camera.Position=center+direction*5;camera.LookAt(center);
                float spanX=0,spanY=0;
                for(int i=0;i<8;i++){var delta=bounds.GetEndpoint(i)-center;spanX=Math.Max(spanX,Math.Abs(delta.Dot(camera.Basis.X))*2);spanY=Math.Max(spanY,Math.Abs(delta.Dot(camera.Basis.Y))*2);}
                camera.Size=Math.Max(.25f,Math.Max(spanY,spanX/(192f/160))*1.23f);
                for(int frame=0;frame<2;frame++)await ToSignal(this,SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                string destination=Path.Combine(thumbnails,pose.Id+".png");var image=viewport.GetTexture().GetImage();if(image.SavePng(destination)!=Godot.Error.Ok)throw new IOException(destination);
                // Seeking the whole scene must not turn this static figure back into a standing rig.
                var skeleton=actor.GetNode<Skeleton3D>("PreparedHumanoid");var before=Enumerable.Range(0,skeleton.GetBoneCount()).Select(skeleton.GetBonePose).ToArray();actor.EvaluateTimeline(100);var after=Enumerable.Range(0,skeleton.GetBoneCount()).Select(skeleton.GetBonePose).ToArray();if(before.Where((value,index)=>!value.IsEqualApprox(after[index])).Any())throw new InvalidOperationException("Static pose changes under seek: "+pose.Id);
                report.Add(new{pose.Id,pose.Name,pose.Category,MinY=bounds.Position.Y,Width=bounds.Size.X,Height=bounds.Size.Y,Depth=bounds.Size.Z,HipY=.94+pose.Pose.RootPosition[1],Bones=skeleton.GetBoneCount(),SourceFilesRequired=0,StaticSeeking=true,Thumbnail=pose.Id+".png"});
                GD.Print("POSE_RENDER "+report.Count+"/"+PoseCatalog.All.Count+" "+pose.Id);
            }
            // A real catalog chair checks the practical scale/seat relationship;
            // the pose itself remains independent of any prop or source asset.
            actor.SetStaticPose(PoseCatalog.Find("pose-sit-upright")!.Pose);actor.EvaluateTimeline(0);
            var chairDescriptor=ContentCatalog.Find("kenney-furniture-chair")!;var document=new GltfDocument();var state=new GltfState();
            if(document.AppendFromFile(ContentCatalog.MaterializeSource(chairDescriptor),state)!=Godot.Error.Ok)throw new IOException("The reference chair could not be imported.");
            var chairHolder=new Node3D();stage.AddChild(chairHolder);var chair=(Node3D)document.GenerateScene(state);chairHolder.AddChild(chair);chair.Scale*=chairDescriptor.Scale;
            var chairBounds=PropFactory.CalculateBounds(chairHolder);var chairCenter=chairBounds.GetCenter();chair.Position-=new Vector3(chairCenter.X,chairBounds.Position.Y,chairCenter.Z);chairHolder.RotationDegrees=new Vector3(0,180,0);
            camera.Size=1.8f;camera.Position=new Vector3(2,1.45f,-3);camera.LookAt(new Vector3(0,.73f,-.08f));
            viewport.Size=new Vector2I(960,800);
            for(int frame=0;frame<3;frame++)await ToSignal(this,SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            viewport.GetTexture().GetImage().SavePng(Path.Combine(output,"pose-chair-reference.png"));
            File.WriteAllText(Path.Combine(output,"pose-content-validation.json"),JsonSerializer.Serialize(new{Status="passed",Engine=Engine.GetVersionInfo()["string"].AsString(),Renderer=DisplayServer.GetName(),Expected=PoseCatalog.All.Count,Passed=report.Count,Failed=0,Thumbnails=report.Count,NoDirectionalLights=true,ChairReference="pose-chair-reference.png",Poses=report},new JsonSerializerOptions{WriteIndented=true}));
            GD.Print("POSE_CONTENT_OK count="+report.Count+" thumbnails="+report.Count);viewport.QueueFree();await ToSignal(this,SignalName.ProcessFrame);Quit(0);
        }
        catch(Exception error){GD.PushError("POSE_CONTENT_FAILED "+error);Quit(1);}
    }
}
