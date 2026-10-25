using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    public async Task RunShotExportChecks()
    {
        if(DisplayServer.GetName()=="headless")throw new InvalidOperationException("Shot export checks need the graphics renderer.");
        var original=Snapshot();double originalTime=_time;string originalPath=_projectPath;var undo=_undo.ToArray();var redo=_redo.ToArray();bool originalPlaying=_playing;
        int checks=0;bool passed=false;string? failure=null;var observations=new List<object>();
        string root=ProjectSettings.GlobalizePath("res://output/shot-export-tests/run-"+Guid.NewGuid().ToString("N")[..10]);Directory.CreateDirectory(root);
        void Check(bool condition,string message){observations.Add(new{message,passed=condition});Require(condition,"Shot export: "+message);checks++;}
        async Task Settle(){for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        static ulong ResourceId(GodotObject? resource)=>resource==null?0:resource.GetInstanceId();
        Dictionary<ulong,(ulong Override,ulong Overlay)> Materials()=>Descendants(_world).OfType<MeshInstance3D>().Where(m=>(m.Layers&1u)!=0).ToDictionary(m=>m.GetInstanceId(),m=>(ResourceId(m.MaterialOverride),ResourceId(m.MaterialOverlay)));
        bool MaterialsMatch(Dictionary<ulong,(ulong Override,ulong Overlay)> before)
        {
            var now=Materials();return before.Count==now.Count&&before.All(pair=>now.TryGetValue(pair.Key,out var state)&&state==pair.Value);
        }
        ShotExportSettings Settings(string name,int width=640,int height=360,double end=8,bool mp4=true,bool png=true)=>new(){OutputDirectory=Path.Combine(root,name),Width=width,Height=height,Fps=24,StartSeconds=0,EndSeconds=end,ExportMp4=mp4,ExportPngSequence=png,IncludeColor=true,IncludeDepth=true,IncludePose=true,IncludeNativeRigs=true,DepthNear=.05f,DepthFar=30};
        try
        {
            _playing=false;StopPoseEditing();await Settle();
            Check(_cameras.Count>=2,"the fixture provides distinct active and inactive cameras");
            var cameraB=_cameras[1];cameraB.Name="Camera B · export test";cameraB.Rig.ClearKeys();cameraB.Rig.SetPose(new Vector3(-1.5f,1.65f,4.5f),new Vector3(-1.2f,1,0));cameraB.Rig.CaptureKey(0);cameraB.Rig.SetPose(new Vector3(-1.3f,1.8f,4.8f),new Vector3(-1.2f,1,0));cameraB.Rig.CaptureKey(8);SyncCameraFromRig(cameraB);
            _duration=8;_scrub.MaxValue=8;SetActiveCamera(cameraB.Id);
            var human=_entries.First(e=>e.Node is RiggedActor);var actor=(RiggedActor)human.Node;
            if(human.Data.MotionClips.Count==0)DropMotion("human-walk-gesture",human,human.Node.Position);
            human.Data.MotionClips[0].Duration=8;human.Data.MotionClips[0].Loop=true;RebuildTrack(human);
            human.Data.Path=new ActorPathData{Enabled=true,OrientToPath=true,Duration=8,Points=new(){new float[]{-1.5f,0,.5f},new float[]{-.6f,0,-.6f},new float[]{-1.3f,0,-1.2f}}};
            human.Data.PoseCorrections=new();human.Data.PoseCorrections.Bones["Head"]=new();PoseCorrectionRuntime.UpsertRotation(human.Data.PoseCorrections.Bones["Head"],0,Quaternion.Identity);PoseCorrectionRuntime.UpsertRotation(human.Data.PoseCorrections.Bones["Head"],4,new Quaternion(Vector3.Up,Mathf.DegToRad(28)));PoseCorrectionRuntime.UpsertRotation(human.Data.PoseCorrections.Bones["Head"],8,Quaternion.Identity);
            SetTime(1.25);Select(human);await Settle();
            string sceneBefore=JsonSerializer.Serialize(Snapshot()),clipsBefore=JsonSerializer.Serialize(human.Data.MotionClips);var materials=Materials();double timeBefore=_time;string cameraBefore=ActiveCameraId,selectedBefore=_selected!.Data.Id;int history=_undo.Count;
            var full=Settings("whole-shot",1920,1080);var result=await ExportShotAsync(full);
            Check(result.FrameCount==192&&result.Videos.Count==3,"the complete eight-second shot exports 192 frames and all three videos");
            Check(!_exportBusy&&Math.Abs(_time-timeBefore)<1e-9&&ActiveCameraId==cameraBefore&&_selected?.Data.Id==selectedBefore,"export restores time, active camera, object selection and idle state");
            Check(sceneBefore==JsonSerializer.Serialize(Snapshot())&&clipsBefore==JsonSerializer.Serialize(human.Data.MotionClips)&&_undo.Count==history,"rendering preserves scene animation, source clips and Undo history");
            Check(MaterialsMatch(materials),"depth export restores every scene material and overlay");
            foreach(string pass in new[]{"color","depth","pose"})
            {
                string folder=Path.Combine(result.Directory,pass);Check(Directory.EnumerateFiles(folder,"*.png").Count()==192&&File.Exists(Path.Combine(folder,"000191.png")),pass+" PNG sequence contains the exact first-to-last frame range");
                var probe=await ShotVideoEncoder.ProbeAsync(Path.Combine(result.Directory,pass+".mp4"));Check(probe.Codec=="h264"&&probe.Width==full.Width&&probe.Height==full.Height&&probe.FrameCount==192&&Math.Abs(probe.Fps-24)<.001&&Math.Abs(probe.DurationSeconds-8)<.01,pass+" MP4 independently decodes as 1920x1080, 192 frames, 24 fps, eight seconds");
            }
            using(var manifest=JsonDocument.Parse(File.ReadAllText(result.ManifestPath)))
            {
                var data=manifest.RootElement;var frames=data.GetProperty("frames");Check(data.GetProperty("status").GetString()=="complete"&&data.GetProperty("camera").GetProperty("id").GetString()==cameraB.Id,"the manifest identifies the selected Camera B instead of the first scene camera");
                Check(frames.GetArrayLength()==192&&Enumerable.Range(0,192).All(i=>Math.Abs(frames[i].GetProperty("timeSeconds").GetDouble()-i/24d)<1e-9),"every color/control frame shares exact end-exclusive timeline times");
                Check(Math.Abs(frames[191].GetProperty("timeSeconds").GetDouble()-191/24d)<1e-9&&data.GetProperty("editorHelpersExcluded").GetBoolean(),"the final frame is 7.95833 seconds and the export excludes editor helpers");
                var pose=JsonSerializer.Deserialize<PosePassFrame>(File.ReadAllText(Path.Combine(result.Directory,"pose","000000.json")))!;var person=pose.People.Single(p=>p.Id==human.Data.Id);
                Check(pose.Width==full.Width&&pose.Height==full.Height&&pose.TimeSeconds==0&&person.PoseKeypoints2D.Length==54&&person.Joints.Count==18,"pose annotations carry the same resolution/time and complete human COCO-18 data");
                Check(person.Joints.Count(j=>j.Visible)>=8&&person.Joints.Where(j=>j.Visible).All(j=>j.InsideFrame&&j.Confidence>0),"a visible performer produces visible, confident pose joints");
                Check(pose.NativeRigs.Count>0&&pose.NativeRigs.All(r=>r.JointNames.Length==r.Joints.Count&&r.Parents.Length==r.Joints.Count),"animal/native rigs retain their own complete topology rather than human relabelling");
                var frame=frames[0];var viewport=new SubViewport{Size=new Vector2I(full.Width,full.Height),OwnWorld3D=true};AddChild(viewport);
                try
                {
                    var projection=new Camera3D{Current=true,KeepAspect=Camera3D.KeepAspectEnum.Width,Fov=frame.GetProperty("horizontalFovDegrees").GetSingle(),Near=frame.GetProperty("near").GetSingle(),Far=frame.GetProperty("far").GetSingle()};viewport.AddChild(projection);
                    Vector3 VectorFrom(JsonElement values)=>new(values[0].GetSingle(),values[1].GetSingle(),values[2].GetSingle());projection.GlobalPosition=VectorFrom(frame.GetProperty("cameraPosition"));projection.GlobalRotationDegrees=VectorFrom(frame.GetProperty("cameraRotationDegrees"));await Settle();
                    Check(person.Joints.Where(j=>j.InsideFrame).All(j=>projection.UnprojectPosition(Vector(j.World)).DistanceTo(new Vector2(j.Pixel[0],j.Pixel[1]))<.03f),"pose pixels project through the exact exported camera transform, lens and aspect ratio");
                }
                finally{viewport.QueueFree();}
                SetTime(0);Check(person.Joints.Single(j=>j.Name=="right_wrist").World is {} wrist&&(actor.GlobalTransform*actor.GetPoseJointPosition("RightHand")).DistanceTo(Vector(wrist))<.001f,"pose world coordinates use the actual evaluated mocap/correction/path pose");SetTime(timeBefore);
            }
            string firstColor=Path.Combine(result.Directory,"color","000000.png"),lastColor=Path.Combine(result.Directory,"color","000191.png");Check(Hash(firstColor)!=Hash(lastColor),"rendering samples animated camera and performer movement rather than repeating one still");
            using(var depth=Image.LoadFromFile(Path.Combine(result.Directory,"depth","000000.png")))
            {
                depth.Convert(Image.Format.Rgb8);var bytes=depth.GetData();Check(Enumerable.Range(0,bytes.Length/3).All(i=>bytes[i*3]==bytes[i*3+1]&&bytes[i*3]==bytes[i*3+2]),"depth PNG is an equal-channel grayscale control map");Check(bytes.Any(b=>b>0)&&bytes.Max()-bytes.Min()>30,"depth contains actual visible surfaces and a meaningful distance range");
            }
            using(var poseImage=Image.LoadFromFile(Path.Combine(result.Directory,"pose","000000.png")))
            {
                poseImage.Convert(Image.Format.Rgb8);var bytes=poseImage.GetData();Check(bytes.Any(b=>b>0)&&Enumerable.Range(0,bytes.Length/3).Any(i=>bytes[i*3]!=bytes[i*3+1]||bytes[i*3]!=bytes[i*3+2]),"pose PNG contains colored body limbs over its black background");
            }
            bool rejected=false;try{await ExportShotAsync(full);}catch(IOException){rejected=true;}Check(rejected&&File.Exists(firstColor),"an existing export directory is never overwritten");
            var hd=await ExportShotAsync(Settings("full-hd",1920,1080,2/24d,false,true));using(var image=Image.LoadFromFile(Path.Combine(hd.Directory,"color","000001.png")))Check(image.GetWidth()==1920&&image.GetHeight()==1080&&hd.FrameCount==2,"full-HD PNG export renders the requested 1920x1080 dimensions");Check(hd.Videos.Count==0&&!File.Exists(Path.Combine(hd.Directory,"color.mp4")),"PNG-only export does not create unwanted video files");
            var mp4Only=await ExportShotAsync(Settings("mp4-only",320,180,4/24d,true,false));Check(mp4Only.Videos.Count==3&&!Directory.Exists(Path.Combine(mp4Only.Directory,".frames")),"MP4-only export removes its temporary rendered frame cache");Check(!Directory.EnumerateFiles(mp4Only.Directory,"*.png",SearchOption.AllDirectories).Any()&&Directory.EnumerateFiles(Path.Combine(mp4Only.Directory,"pose"),"*.json").Count()==4,"MP4-only export keeps pose annotations without leaving PNG sequences");
            using(var cancelled=new CancellationTokenSource())
            {
                cancelled.Cancel();var settings=Settings("pre-cancelled",320,180,8,false,true);rejected=false;try{await ExportShotAsync(settings,cancelled.Token);}catch(OperationCanceledException){rejected=true;}Check(rejected&&!Directory.Exists(settings.OutputDirectory)&&!_exportBusy,"a pre-cancelled export creates no output or busy state");
            }
            using(var cancelled=new CancellationTokenSource())
            {
                var settings=Settings("cancelled",320,180,8,false,true);var restore=Materials();double before=_time;string selected=_selected?.Data.Id??"";cancelled.CancelAfter(120);rejected=false;
                try{await ExportShotAsync(settings,cancelled.Token);}catch(OperationCanceledException){rejected=true;}
                Check(rejected&&!_exportBusy&&Math.Abs(_time-before)<1e-9&&MaterialsMatch(restore)&&(_selected?.Data.Id??"")==selected,"cancelling a rendering job restores materials, time, selection and exporter state");
                using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(settings.OutputDirectory,"manifest.json")));Check(manifest.RootElement.GetProperty("status").GetString()=="cancelled"&&manifest.RootElement.GetProperty("completedFrames").GetInt32()<192,"partial output records cancellation rather than claiming a completed shot");
            }
            passed=true;GD.Print("SHOT_EXPORT_OK checks="+checks+" frames=192 passes=3 output="+root);
        }
        catch(Exception ex){failure=ex.ToString();throw;}
        finally
        {
            string report=ProjectSettings.GlobalizePath("res://output/shot-export-report.json");Directory.CreateDirectory(Path.GetDirectoryName(report)!);File.WriteAllText(report,JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,failure,outputDirectory=root,observations},new JsonSerializerOptions{WriteIndented=true}));
            Restore(original,"");_projectPath=originalPath;SetTime(originalTime);_playing=originalPlaying;_undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);
        }
    }
    async Task RunShotExportChecksAndQuit()
    {
        try{await RunShotExportChecks();GetTree().Quit(0);}catch(Exception ex){GD.PushError("SHOT_EXPORT_FAILED "+ex);GetTree().Quit(1);}
    }
}
