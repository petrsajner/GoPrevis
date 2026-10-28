using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    bool CompatiblePose(Entry entry,LibraryPose pose)=>pose.Pose.RigModelId==null?entry.Node is RiggedActor:entry.NativeTrack!=null&&entry.Data.LibraryModelId==pose.Pose.RigModelId;
    void ApplyAnimalLibraryPose(Entry entry,LibraryPose pose)
    {
        if(!CompatiblePose(entry,pose))throw new InvalidDataException("This animal pose requires its matching library model.");
        entry.Data.StaticPose=JsonSerializer.Deserialize<StaticPoseData>(JsonSerializer.Serialize(pose.Pose))!;ClearPropertyPoseChannels(entry);entry.Data.PoseCorrections=new();entry.Data.MotionClips.Clear();entry.Data.MotionPath=null;entry.Data.MotionName=null;_selectedClipId="";RebuildTrack(entry);
    }
    LibraryPose SaveAnimalCapturedPose(string name)
    {
        var entry=_selected!;var native=entry.NativeTrack!;
        if(string.IsNullOrWhiteSpace(entry.Data.LibraryModelId))throw new InvalidDataException("Save animal poses from a prepared library model.");
        _playing=false;_play.Text="▶";SetTime(_time);var snapshot=native.CaptureEvaluatedPose(name);snapshot.RigModelId=entry.Data.LibraryModelId;snapshot.Name=string.IsNullOrWhiteSpace(name)?"Animal pose":name.Trim();snapshot.CatalogId="pose-user-"+Guid.NewGuid().ToString("N");
        var pose=new LibraryPose{Id=snapshot.CatalogId,Name=snapshot.Name,Category="My poses",Tags=new[]{"animal","saved",entry.Data.Name},Description="Native pose for "+ContentCatalog.Find(snapshot.RigModelId)?.Name+". Captured at frame "+(int)Math.Round(_time*_fps)+"; source motion is preserved.",Pose=snapshot};
        _=AllLibraryPoses.Count();SavedPoseLibrary.Save(pose);_userPoses.Add(pose);ShowCategory("Poses");_=RenderSavedPoseCover(pose);Status("Saved "+pose.Name+" in My poses for this animal model.");return pose;
    }
    async Task RenderAnimalPoseCover(LibraryPose pose)
    {
        if(DisplayServer.GetName()=="headless")return;
        var view=new SubViewport{Size=new Vector2I(160,128),OwnWorld3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Once};AddChild(view);
        try
        {
            var model=ContentCatalog.Find(pose.Pose.RigModelId!)??throw new InvalidDataException("The saved animal model is unavailable.");
            var world=new Node3D();view.AddChild(world);world.AddChild(new WorldEnvironment{Environment=StudioLighting.CreateEnvironment()});
            var document=new GltfDocument();var state=new GltfState();if(document.AppendFromFile(ContentCatalog.MaterializeSource(model),state)!=Godot.Error.Ok)throw new InvalidDataException("The animal cover model could not be loaded.");
            var generated=(Node3D)document.GenerateScene(state);world.AddChild(generated);generated.Scale*=model.Scale;var player=Descendants(generated).OfType<AnimationPlayer>().First();var native=new NativeMotionTrack(generated,player);native.Evaluate(0,false,null,pose.Pose);
            Vector3 min=native.GetPoseJointWorld(native.RootBoneName),max=min;foreach(string joint in native.PoseBoneNames){var point=native.GetPoseJointWorld(joint);min=min.Min(point);max=max.Max(point);}var bounds=new Aabb(min,max-min);Vector3 target=bounds.GetCenter();float radius=Math.Max(.4f,bounds.Size.Length()*1.4f);
            var camera=new Camera3D{Current=true,Fov=38,Near=.01f};world.AddChild(camera);camera.Position=target+new Vector3(radius*.7f,radius*.35f,radius);camera.LookAt(target);
            for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);if(SavedPoseLibrary.Exists(pose.Id))view.GetTexture().GetImage().SavePng(SavedPoseLibrary.ThumbnailPath(pose.Id));
        }
        catch(Exception ex){GD.PushWarning("Saved animal pose thumbnail: "+ex.Message);}
        finally{view.QueueFree();}
    }
}
