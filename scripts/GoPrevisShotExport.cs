using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GoPrevis;

public sealed record ShotExportResult(string Directory,int FrameCount,IReadOnlyDictionary<string,VideoProbeInfo> Videos,string ManifestPath);

public partial class GoPrevisApp
{
    bool _exportBusy;
    CancellationTokenSource? _exportCancellation;
    ConfirmationDialog? _shotExportDialog;
    AcceptDialog? _shotExportProgressDialog;
    FileDialog? _shotExportFolderDialog;
    LineEdit _shotExportFolder=null!,_shotExportName=null!;
    OptionButton _shotExportFormat=null!,_shotExportResolution=null!,_shotExportFps=null!,_shotExportPoseLayout=null!;
    CheckButton _shotExportColor=null!,_shotExportDepth=null!,_shotExportPose=null!;
    SpinBox _shotExportDepthNear=null!,_shotExportDepthFar=null!;
    Label _shotExportSummary=null!,_shotExportProgressText=null!;
    ProgressBar _shotExportProgress=null!;
    ColorRect? _shotExportCurtain;

    async void OpenShotExport()
    {
        if(_exportBusy)return;
        if(_shotExportDialog==null)BuildShotExportDialog();
        _shotExportName.Text=string.IsNullOrEmpty(_projectPath)?"Shot":Path.GetFileNameWithoutExtension(_projectPath);
        _shotExportResolution.SetItemText(0,"Project · "+ProjectResolution.X+" × "+ProjectResolution.Y);_shotExportResolution.Select(0);
        EnsureOptionId(_shotExportFps,_fps,_fps+" fps");
        _shotExportFps.Select(Enumerable.Range(0,_shotExportFps.ItemCount).FirstOrDefault(i=>_shotExportFps.GetItemId(i)==_fps));
        _shotExportPoseLayout.Select(_entries.Any(e=>e.NativeTrack!=null)?1:0);
        UpdateShotExportSummary();_shotExportDialog!.Size=new Vector2I(550,590);
        // Let wrapped labels measure at the actual dialog width before showing
        // the window, otherwise their zero-width initial height can fill a screen.
        for(int i=0;i<2;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        _shotExportDialog.Size=new Vector2I(550,590);_shotExportDialog.PopupCentered(new Vector2I(550,590));
        for(int i=0;i<2;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        _shotExportDialog.WrapControls=false;_shotExportDialog.Size=new Vector2I(550,590);_shotExportDialog.MoveToCenter();
    }
    void BuildShotExportDialog()
    {
        _shotExportDialog=new ConfirmationDialog{Title="Export shot",Exclusive=true,WrapControls=false};_shotExportDialog.GetOkButton().Text="Export";AddChild(_shotExportDialog);
        var box=new VBoxContainer();box.AddThemeConstantOverride("separation",8);_shotExportDialog.AddChild(box);
        _shotExportSummary=Text("",12,true);_shotExportSummary.AutowrapMode=TextServer.AutowrapMode.WordSmart;box.AddChild(_shotExportSummary);
        _shotExportName=new LineEdit{PlaceholderText="Shot name"};box.AddChild(_shotExportName);
        _shotExportFolder=new LineEdit{Text=Path.Combine(OS.GetSystemDir(OS.SystemDir.Documents),"GoPrevis Exports"),SizeFlagsHorizontal=SizeFlags.ExpandFill};box.AddChild(Row(_shotExportFolder,Button("Browse…",()=>_shotExportFolderDialog!.PopupCentered(new Vector2I(900,600)))));
        _shotExportFolderDialog=new FileDialog{Access=FileDialog.AccessEnum.Filesystem,FileMode=FileDialog.FileModeEnum.OpenDir,UseNativeDialog=false,Title="Choose export folder"};_shotExportFolderDialog.DirSelected+=path=>_shotExportFolder.Text=path;_shotExportDialog.AddChild(_shotExportFolderDialog);
        _shotExportFormat=new OptionButton{FitToLongestItem=false};foreach(string format in new[]{"MP4 + PNG sequences","MP4 videos","PNG sequences"})_shotExportFormat.AddItem(format);box.AddChild(_shotExportFormat);
        _shotExportResolution=new OptionButton{SizeFlagsHorizontal=SizeFlags.ExpandFill};foreach(string resolution in new[]{"Project resolution","Half resolution","Double resolution"})_shotExportResolution.AddItem(resolution);
        _shotExportFps=new OptionButton();foreach(int fps in ProjectFrameRates)_shotExportFps.AddItem(fps+" fps",fps);box.AddChild(Row(_shotExportResolution,_shotExportFps));
        _shotExportColor=new CheckButton{Text="Color",ButtonPressed=true};_shotExportDepth=new CheckButton{Text="Depth",ButtonPressed=true};_shotExportPose=new CheckButton{Text="Pose",ButtonPressed=true};box.AddChild(Row(_shotExportColor,_shotExportDepth,_shotExportPose));
        _shotExportPoseLayout=new OptionButton{FitToLongestItem=false};_shotExportPoseLayout.AddItem("Human body · COCO-18");_shotExportPoseLayout.AddItem("All scene rigs · humans + animals");box.AddChild(_shotExportPoseLayout);
        var poseNote=Text("Animal poses use their native skeleton topology. Human facial anchors are inferred from the head rig.",11,true);poseNote.AutowrapMode=TextServer.AutowrapMode.WordSmart;box.AddChild(poseNote);
        _shotExportDepthNear=Spin(0,499,.01,.05);_shotExportDepthFar=Spin(.1,500,.1,30);box.AddChild(Row(Text("Depth range (m)",11,true),_shotExportDepthNear,_shotExportDepthFar));
        var note=Text("Exports the whole shot from the active camera. All passes share exact frame times. A new folder is created; existing exports are kept.",11,true);note.AutowrapMode=TextServer.AutowrapMode.WordSmart;box.AddChild(note);
        _shotExportFps.ItemSelected+=_=>UpdateShotExportSummary();_shotExportDialog.Confirmed+=StartShotExportFromDialog;
        _shotExportProgressDialog=new AcceptDialog{Title="Exporting shot",Exclusive=true,WrapControls=false,DialogHideOnOk=false};_shotExportProgressDialog.GetOkButton().Text="Cancel export";_shotExportProgressDialog.Confirmed+=CancelShotExport;_shotExportProgressDialog.CloseRequested+=CancelShotExport;AddChild(_shotExportProgressDialog);
        var progressBox=new VBoxContainer();_shotExportProgressText=Text("Preparing…",12);_shotExportProgressText.AutowrapMode=TextServer.AutowrapMode.WordSmart;progressBox.AddChild(_shotExportProgressText);_shotExportProgress=new ProgressBar{MinValue=0,MaxValue=100,ShowPercentage=true,CustomMinimumSize=new Vector2(440,24)};progressBox.AddChild(_shotExportProgress);_shotExportProgressDialog.AddChild(progressBox);
        _shotExportCurtain=new ColorRect{Color=new Color(0,0,0,.25f),MouseFilter=MouseFilterEnum.Stop,FocusMode=FocusModeEnum.All,Visible=false};_shotExportCurtain.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);_shotExportCurtain.GuiInput+=input=>{if(input is InputEventKey{Keycode:Key.Escape,Pressed:true})CancelShotExport();_shotExportCurtain.AcceptEvent();};AddChild(_shotExportCurtain);
    }
    void UpdateShotExportSummary()
    {
        if(_shotExportDialog==null)return;int fps=_shotExportFps.GetSelectedId();_shotExportSummary.Text=ActiveCameraName+" · "+_duration.ToString("0.###")+" s · "+(int)Math.Ceiling(_duration*fps-1e-8)+" frames";
    }
    Vector2I ShotExportDimensions(int choice)
    {
        if(choice==0)return ProjectResolution;
        int a=ProjectResolution.X,b=ProjectResolution.Y;while(b!=0){int remainder=a%b;a=b;b=remainder;}
        int units=Math.Max(2,2*(int)Math.Round(a*(choice==1?.5:2)/2));
        return new Vector2I(ProjectResolution.X/a*units,ProjectResolution.Y/a*units);
    }
    async void StartShotExportFromDialog()
    {
        var dimensions=ShotExportDimensions(_shotExportResolution.Selected);
        string name=_shotExportName.Text.Trim();foreach(char invalid in Path.GetInvalidFileNameChars())name=name.Replace(invalid,'_');if(string.IsNullOrWhiteSpace(name))name="Shot";
        var settings=new ShotExportSettings{OutputDirectory=Path.Combine(_shotExportFolder.Text,name+"-"+Guid.NewGuid().ToString("N")[..6]),Width=dimensions.X,Height=dimensions.Y,Fps=_shotExportFps.GetSelectedId(),EndSeconds=_duration,ExportMp4=_shotExportFormat.Selected!=2,ExportPngSequence=_shotExportFormat.Selected!=1,IncludeColor=_shotExportColor.ButtonPressed,IncludeDepth=_shotExportDepth.ButtonPressed,IncludePose=_shotExportPose.ButtonPressed,IncludeNativeRigs=_shotExportPoseLayout.Selected==1,DepthNear=(float)_shotExportDepthNear.Value,DepthFar=(float)_shotExportDepthFar.Value};
        try
        {
            settings.Validate(_duration);_exportCancellation=new();_shotExportCurtain!.Show();_shotExportCurtain.GrabFocus();_shotExportProgressDialog!.GetOkButton().Disabled=false;_shotExportProgress.Value=0;_shotExportProgressDialog.PopupCentered(new Vector2I(490,150));
            var result=await ExportShotAsync(settings,_exportCancellation.Token);_shotExportProgressDialog.Hide();Status("Exported "+result.FrameCount+" frames and selected passes to "+result.Directory);
            ShowShotExported(result);
        }
        catch(OperationCanceledException){_shotExportProgressDialog?.Hide();Status("Export cancelled. The manifest records any completed frames.");}
        catch(Exception ex){_shotExportProgressDialog?.Hide();Error("Shot export failed: "+ex.Message);}
        finally{_shotExportCurtain?.Hide();_exportCancellation?.Dispose();_exportCancellation=null;}
    }
    void CancelShotExport(){_exportCancellation?.Cancel();if(_shotExportProgressDialog!=null){_shotExportProgressText.Text="Cancelling…";_shotExportProgressDialog.GetOkButton().Disabled=true;}}
    void ReportShotExport(double progress,string text)
    {
        if(_shotExportProgressDialog==null)return;_shotExportProgress.Value=Math.Clamp(progress,0,1)*100;_shotExportProgressText.Text=text;
    }
    async Task<Image> CaptureShotImage(SubViewport view,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();view.RenderTargetUpdateMode=SubViewport.UpdateMode.Once;
        for(int i=0;i<2;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);cancellation.ThrowIfCancellationRequested();
        var image=view.GetTexture().GetImage();if(image==null||image.IsEmpty())throw new IOException("The renderer returned an empty frame.");image.Convert(Image.Format.Rgb8);return image;
    }
    static void SaveShotPng(Image image,string path){var error=image.SavePng(path);if(error!=Godot.Error.Ok)throw new IOException("Could not save frame: "+path+" ("+error+").");}
    static void RemoveShotScratch(string scratch,string exportDirectory)
    {
        string root=Path.GetFullPath(exportDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,full=Path.GetFullPath(scratch);
        if(!full.StartsWith(root,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal)||Path.GetFileName(full)!=".frames")throw new IOException("The export scratch path is invalid.");
        if(Directory.Exists(full))Directory.Delete(full,true);
    }
    public async Task<ShotExportResult> ExportShotAsync(ShotExportSettings settings,CancellationToken cancellation=default)
    {
        if(_exportBusy)throw new InvalidOperationException("Another shot is already exporting.");settings=JsonSerializer.Deserialize<ShotExportSettings>(JsonSerializer.Serialize(settings))!;settings.Validate(_duration);cancellation.ThrowIfCancellationRequested();if(DisplayServer.GetName()=="headless")throw new InvalidOperationException("Shot rendering needs the graphics renderer.");
        if(settings.ExportMp4){_=ShotVideoEncoder.FindBinary("ffmpeg");_=ShotVideoEncoder.FindBinary("ffprobe");}
        string directory=Path.GetFullPath(settings.OutputDirectory);if(Directory.Exists(directory)||File.Exists(directory))throw new IOException("The export folder already exists. Choose a new folder.");
        ReleasePoseViewportDrag();ReleaseObjectGizmoDrag();EndArrangeDrag();bool wasPlaying=_playing;double originalTime=_time;string cameraId=ActiveCameraId,cameraName=ActiveCameraName,inspectorSection=_inspectorSection;string? selectedObject=_selected?.Data.Id,selectedCamera=_selectedCamera?.Id;
        Directory.CreateDirectory(directory);string scratch=Path.Combine(directory,".frames"),manifestPath=Path.Combine(directory,"manifest.json");
        var passes=new List<string>();if(settings.IncludeColor)passes.Add("color");if(settings.IncludeDepth)passes.Add("depth");if(settings.IncludePose)passes.Add("pose");
        var frameDirectories=passes.ToDictionary(pass=>pass,pass=>Path.Combine(settings.ExportPngSequence?directory:scratch,pass));foreach(var folder in frameDirectories.Values)Directory.CreateDirectory(folder);
        string poseAnnotations=Path.Combine(directory,"pose");if(settings.IncludePose)Directory.CreateDirectory(poseAnnotations);
        var frames=new List<object>();var videos=new Dictionary<string,VideoProbeInfo>();string status="rendering";string? failure=null;SubViewport? view=null;ControlPassRenderer? controls=null;
        // The shot's content for the manifest (GoPrevisShotContent.cs): the document as exported, and when the camera sees each object.
        var shotDocument=Snapshot();var visibility=new ShotFrameTracker();var stillBounds=new Dictionary<string,Aabb>(StringComparer.Ordinal);
        void WriteManifest()=>File.WriteAllText(manifestPath,JsonSerializer.Serialize(new{application="GoPrevis",version=AppVersion,status,failure,createdUtc=DateTime.UtcNow,camera=new{id=cameraId,name=cameraName},settings,frameCount=settings.FrameCount,completedFrames=frames.Count,mediaDurationSeconds=settings.MediaDuration,timeSampling="start + frameIndex / fps; end exclusive",depthEncoding="linear camera-Z: white near, black far/background; RGB8",poseFormat=settings.IncludeNativeRigs?"human COCO-18 + native animal rigs":"human COCO-18 body",editorHelpersExcluded=true,shot=ShotContent(shotDocument,cameraId,visibility,settings.FrameTime(0)+frames.Count/(double)settings.Fps),frames,videos},_json));
        _exportBusy=true;_playing=false;_play.Text="▶";
        try
        {
            WriteManifest();view=new SubViewport{Size=new Vector2I(settings.Width,settings.Height),World3D=_sceneViewport.World3D,RenderTargetUpdateMode=SubViewport.UpdateMode.Once,PositionalShadowAtlasSize=2048,Msaa3D=Viewport.Msaa.Msaa4X};AddChild(view);
            var camera=new Camera3D{Current=true,CullMask=1,KeepAspect=Camera3D.KeepAspectEnum.Width};view.AddChild(camera);controls=new ControlPassRenderer(_world,camera,settings.DepthNear,settings.DepthFar);stillBounds=ShotStillBounds();var frameSize=new Vector2(settings.Width,settings.Height);
            var humans=_entries.Where(e=>e.Node is RiggedActor).Select(e=>new PoseActorSample(e.Data.Id,e.Data.Name,(RiggedActor)e.Node)).ToArray();
            var native=settings.IncludeNativeRigs?_entries.Where(e=>e.NativeTrack!=null).Select(e=>new NativePoseActorSample(e.Data.Id,e.Data.Name,e.NativeTrack!.ModelRoot as Node3D??e.Node)).ToArray():Array.Empty<NativePoseActorSample>();
            for(int frame=0;frame<settings.FrameCount;frame++)
            {
                cancellation.ThrowIfCancellationRequested();double sampleTime=settings.FrameTime(frame);SetTime(sampleTime);var shot=_cameras.Single(c=>c.Id==cameraId).Rig.ShotCamera;
                camera.GlobalTransform=new Transform3D(shot.GlobalBasis.Orthonormalized(),shot.GlobalPosition);camera.Fov=shot.Fov;camera.Near=shot.Near;camera.Far=shot.Far;string filename=frame.ToString("D6")+".png";
                TrackShotVisibility(visibility,camera,frameSize,sampleTime,stillBounds);
                if(settings.IncludeColor){using var image=await CaptureShotImage(view,cancellation);SaveShotPng(image,Path.Combine(frameDirectories["color"],filename));}
                Image? depth=null;
                try
                {
                    if(settings.IncludeDepth||settings.IncludePose){controls.BeginDepthPass();try{depth=await CaptureShotImage(view,cancellation);}finally{controls.EndDepthPass();}}
                    if(settings.IncludeDepth)SaveShotPng(depth!,Path.Combine(frameDirectories["depth"],filename));
                    if(settings.IncludePose){var pose=controls.RenderPose(humans,depth,sampleTime,native,settings.PoseOcclusionTolerance);using(pose.Image)SaveShotPng(pose.Image,Path.Combine(frameDirectories["pose"],filename));File.WriteAllText(Path.Combine(poseAnnotations,frame.ToString("D6")+".json"),JsonSerializer.Serialize(pose.Annotation,_json));}
                }
                finally{depth?.Dispose();}
                frames.Add(new{index=frame,timeSeconds=sampleTime,cameraPosition=Array3(camera.GlobalPosition),cameraRotationDegrees=Array3(camera.GlobalRotationDegrees),horizontalFovDegrees=camera.Fov,focalLengthMm=_cameras.Single(c=>c.Id==cameraId).Rig.FocalLengthMm,near=camera.Near,far=camera.Far});
                ReportShotExport((frame+1)/(double)settings.FrameCount*(settings.ExportMp4?.8:1),"Rendering frame "+(frame+1)+" / "+settings.FrameCount);
            }
            controls.Dispose();controls=null;status=settings.ExportMp4?"encoding":"complete";WriteManifest();
            if(settings.ExportMp4)
            {
                for(int i=0;i<passes.Count;i++)
                {
                    string pass=passes[i];int index=i;string output=Path.Combine(directory,pass+".mp4");
                    await ShotVideoEncoder.EncodeAsync(frameDirectories[pass],output,settings.Fps,settings.Width,settings.Height,p=>ReportShotExport(.8+.2*(index+p)/passes.Count,"Encoding "+pass+" video…"),cancellation);
                    videos[pass]=await ShotVideoEncoder.ProbeAsync(output,cancellation);
                }
            }
            if(!settings.ExportPngSequence)RemoveShotScratch(scratch,directory);status="complete";WriteManifest();ReportShotExport(1,"Export complete");return new ShotExportResult(directory,settings.FrameCount,videos,manifestPath);
        }
        catch(OperationCanceledException){status="cancelled";WriteManifest();if(!settings.ExportPngSequence)RemoveShotScratch(scratch,directory);throw;}
        catch(Exception ex){status="failed";failure=ex.Message;WriteManifest();throw;}
        finally
        {
            controls?.Dispose();view?.QueueFree();SetTime(originalTime);RestoreInteractionSelection(selectedObject,selectedCamera);ShowInspectorSection(inspectorSection);_playing=wasPlaying;_play.Text=wasPlaying?"Ⅱ":"▶";_exportBusy=false;
            for(int i=0;i<2;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
    }
}
