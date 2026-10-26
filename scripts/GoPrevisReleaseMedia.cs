using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    async Task RunReleaseMediaAndQuit()
    {
        try{await CaptureReleaseMedia();GetTree().Quit(0);}catch(Exception e){GD.PushError("RELEASE_MEDIA_FAILED "+e);GetTree().Quit(1);}
    }
    async Task CaptureReleaseMedia()
    {
        string project=OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--media-project=",StringComparison.Ordinal))?[16..]??Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath())!,"examples/city-street-15s.goprevis");
        string folder=OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--media-output=",StringComparison.Ordinal))?[15..]??ProjectSettings.GlobalizePath("res://output/release-media/run-"+Guid.NewGuid().ToString("N")[..10]);folder=Path.GetFullPath(folder);Directory.CreateDirectory(folder);
        var window=GetWindow();window.ContentScaleFactor=1;window.Mode=Window.ModeEnum.Windowed;window.Size=new Vector2I(1280,800);_libraryPreferences=new();_libraryExpanded=false;_libraryExpandButton!.Text="Expand";_layoutBreakpoint=-1;ApplyResponsiveLayout();LoadProjectFromPath(project);SetTime(0);
        async Task Settle(){for(int i=0;i<6;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        async Task Capture(string name){await Settle();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);using var image=GetViewport().GetTexture().GetImage();image.SavePng(Path.Combine(folder,name));}
        var camera=_cameras.Single(c=>c.Id==ActiveCameraId);SelectCamera(camera);_movementExpanded=true;RefreshCameraMovementInspector();
        _editorCamera.GlobalPosition=new Vector3(19,15,24);_editorCamera.LookAt(new Vector3(0,1,0));_orbitTarget=new Vector3(0,1,0);await Capture("01-street-scene-ui.png");
        TogglePilot();SetTime(6);await Capture("02-camera-framing-ui.png");
        _libraryExpanded=true;_libraryExpandButton.Text="Collapse";_layoutBreakpoint=-1;ApplyResponsiveLayout();ShowCategory("Motions");_librarySearch!.Text="walk";PopulateLibraryBrowser(true);await Capture("03-motion-library-ui.png");
        _libraryExpanded=false;_libraryExpandButton.Text="Expand";_layoutBreakpoint=-1;ApplyResponsiveLayout();ShowCategory("Models");if(_pilot)TogglePilot();SetTime(14.9);var person=_entries.First(e=>e.Node is RiggedActor&&!e.Data.Path.Enabled);Select(person);FrameSelected();StartPoseEditing();SelectPoseBone("Head");await Capture("04-pose-editing-ui.png");StopPoseEditing();
        SelectCamera(camera);if(!_pilot)TogglePilot();SetTime(6);OpenAiDirector();await Settle();_aiModeChoice.Select(1);_aiPrompt.Text="Create a 15-second street shot: a four-lane road with traffic in both directions, parked cars, pedestrians walking dogs on leashes, trees and shop fronts. Start on the right pavement, pan left to the couple across the road, rise and fly across. Descend to shoulder height beside the shop wall and finish with a slow close approach to both faces. Use independent camera position and aim paths.";await Capture("05-ai-director-ui.png");_aiDialog!.Hide();
        var export=await ExportShotAsync(new(){OutputDirectory=Path.Combine(folder,"street-shot"),Width=1280,Height=720,Fps=24,EndSeconds=15,IncludeColor=true,IncludeDepth=true,IncludePose=true,IncludeNativeRigs=true,ExportMp4=true,ExportPngSequence=true});
        Require(export.FrameCount==360&&export.Videos.Count==3,"Release example exports the full actual street shot and all control passes");
        File.WriteAllText(Path.Combine(folder,"media-manifest.json"),JsonSerializer.Serialize(new{application="GoPrevis",version=AppVersion,sourceProject=project,objects=_entries.Count,cameras=_cameras.Count,uiSize=new[]{1280,800},videoSize=new[]{1280,720},fps=24,seconds=15,frames=export.FrameCount,noApiCalls=true,published=false,aiWindow="Unsubmitted example prompt; own API key is required for new requests.",files=Directory.GetFiles(folder).Select(Path.GetFileName)},_json));GD.Print("RELEASE_MEDIA_OK frames="+export.FrameCount+" output="+folder);
    }
}
