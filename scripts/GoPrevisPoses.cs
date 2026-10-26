using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    readonly List<LibraryPose> _userPoses=new();
    bool _userPosesLoaded;
    IEnumerable<LibraryPose> AllLibraryPoses
    {
        get{if(!_userPosesLoaded){_userPoses.AddRange(SavedPoseLibrary.Load());_userPosesLoaded=true;}return PoseCatalog.All.Concat(_userPoses);}
    }
    LibraryPose? FindLibraryPose(string id)=>AllLibraryPoses.FirstOrDefault(p=>p.Id==id);

    void BuildPoseLibrary()
    {
        BuildLibraryBrowser("Poses",AllLibraryPoses.Select(p=>new ContentCard(p.Id,p.Name,p.Category+(p.Pose.RigModelId==null?" · static human pose":" · static animal pose")+"\n"+p.Description+"\nDrop on a matching figure or empty ground.",null,null,p)));
        var saved=new LineEdit{PlaceholderText="Name for saved pose",TooltipText="Capture the selected figure at the current frame, without changing its animation."};_library.AddChild(saved);
        _library.AddChild(Button("Save current pose",()=>{try{SaveCapturedPose(string.IsNullOrWhiteSpace(saved.Text)?DefaultPoseName():saved.Text);}catch(Exception ex){Error(ex.Message);}}));
        _library.AddChild(Button("Edit selected pose",StartPoseEditing));_library.AddChild(Button("Reset pose",ClearSelectedPose));
    }

    void DropPose(string id,Entry? target,Vector3 position)
    {
        var pose=FindLibraryPose(id)??throw new InvalidDataException("This pose is not available.");
        if(target!=null&&!CompatiblePose(target,pose)){Status("This pose requires its matching human or animal rig.");return;}
        PushUndo();if(target==null){target=AddCatalogModel(ContentCatalog.Find(pose.Pose.RigModelId??"human-average")!,position,false);target.Data.Name=pose.Name;}
        ApplyPoseToEntry(target,pose);Select(target);SetTime(_time);RecordLibraryUse("pose",id);Status(pose.Name+" applied · static pose, no motion clip.");
    }
    void ApplyPoseToEntry(Entry target,LibraryPose pose)
    {
        if(pose.Pose.RigModelId!=null){ApplyAnimalLibraryPose(target,pose);return;}
        if(target.Node is not RiggedActor actor)throw new InvalidDataException("Choose a prepared human figure for this pose.");
        actor.EndPoseCorrectionEdit();target.Data.PoseCorrections=new();actor.SetStaticPose(pose.Pose);target.Data.StaticPose=actor.GetStaticPoseSnapshot();target.Data.MotionClips.Clear();target.Data.MotionPath=null;target.Data.MotionName=null;_selectedClipId="";RebuildTrack(target);
    }
    void ClearSelectedPose()
    {
        if(_selected?.NativeTrack!=null){PushUndo();_selected.Data.PoseCorrections=new();_selected.Data.StaticPose=null;SetTime(_time);Select(_selected);Status("Animal pose restored; motion clips remain active.");return;}
        if(_selected?.Node is not RiggedActor actor){Status("Select a human figure to reset its pose.");return;}
        PushUndo();actor.EndPoseCorrectionEdit();_selected.Data.PoseCorrections=new();_selected.Data.StaticPose=null;actor.SetStaticPose(null);SetTime(_time);Select(_selected);Status(_selected.Data.MotionClips.Count==0?"Standing pose restored.":"Stored pose cleared; motion clips remain active.");
    }
    string DefaultPoseName()=>(_selected?.Data.Name??"Figure")+" · frame "+(int)Math.Round(_time*_fps);
    void CaptureSelectedPose(){try{SaveCapturedPose(DefaultPoseName());}catch(Exception ex){Error(ex.Message);}}
    LibraryPose SaveCapturedPose(string name)
    {
        if(_selected?.NativeTrack!=null)return SaveAnimalCapturedPose(name);
        if(_selected?.Node is not RiggedActor actor)throw new InvalidDataException("Select a human figure to save its current pose.");
        _playing=false;_play.Text="▶";SetTime(_time);var snapshot=actor.CaptureEvaluatedPose(name);
        if(actor.MotionTrackClipCount>0&&_poseBaseSnapshots.TryGetValue(_selected.Data.Id,out var source)){snapshot.RootPosition[0]-=source.RootPosition[0];snapshot.RootPosition[2]-=source.RootPosition[2];}
        snapshot.Name=string.IsNullOrWhiteSpace(name)?"Custom pose":name.Trim();string id="pose-user-"+Guid.NewGuid().ToString("N");snapshot.CatalogId=id;
        var pose=new LibraryPose{Id=id,Name=snapshot.Name,Category="My poses",Tags=new[]{"human","saved","static"},Description="Saved from the selected figure at frame "+(int)Math.Round(_time*_fps)+". Independent of its motion file.",Pose=snapshot};
        _=AllLibraryPoses.Count();SavedPoseLibrary.Save(pose);_userPoses.Add(pose);ShowCategory("Poses");_ = RenderSavedPoseCover(pose);Status("Saved "+pose.Name+" in My poses. The figure's animation is unchanged.");return pose;
    }
    async Task RenderSavedPoseCover(LibraryPose pose)
    {
        if(pose.Pose.RigModelId!=null){await RenderAnimalPoseCover(pose);return;}
        if(DisplayServer.GetName()=="headless")return;
        var view=new SubViewport{Size=new Vector2I(160,128),OwnWorld3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Once};AddChild(view);
        try
        {
            var world=new Node3D();view.AddChild(world);world.AddChild(new WorldEnvironment{Environment=StudioLighting.CreateEnvironment()});
            var actor=new RiggedActor();world.AddChild(actor);actor.SetStaticPose(pose.Pose);var bounds=actor.GetPreparedPoseBounds();var camera=new Camera3D{Current=true,Fov=38,Near=.01f};world.AddChild(camera);
            Vector3 target=bounds.GetCenter();float radius=Math.Max(.4f,bounds.Size.Length()*1.1f);camera.Position=target+new Vector3(radius*.65f,radius*.35f,-radius);camera.LookAt(target);
            for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            if(SavedPoseLibrary.Exists(pose.Id))view.GetTexture().GetImage().SavePng(SavedPoseLibrary.ThumbnailPath(pose.Id));
        }
        catch(Exception ex){GD.PushWarning("Saved pose thumbnail: "+ex.Message);}
        finally{view.QueueFree();}
    }
    async Task RunPoseChecksAndQuit()
    {
        try{await RunPoseLibraryChecks();GetTree().Quit(0);}
        catch(Exception ex){GD.PushError("POSE_LIBRARY_FAILED "+ex);GetTree().Quit(1);}
    }
}
