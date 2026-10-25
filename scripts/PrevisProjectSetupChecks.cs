using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace Previs;

public partial class PrevisApp
{
    public async Task<int> RunProjectSetupChecks()
    {
        var original=Snapshot();string originalPath=_projectPath;double originalTime=_time;bool pilot=_pilot;var undo=_undo.ToArray();var redo=_redo.ToArray();bool playing=_playing;
        int checks=0;bool passed=false;string? failure=null;var observations=new List<object>();
        string output=ProjectSettings.GlobalizePath("res://output/project-setup-checks");Directory.CreateDirectory(output);
        void Check(bool condition,string message){observations.Add(new{message,passed=condition});Require(condition,"Project setup: "+message);checks++;}
        async Task Settle(int frames=4){for(int i=0;i<frames;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);if(DisplayServer.GetName()!="headless")await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);}
        static float Difference(Color a,Color b)=>Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B);
        async Task ReferenceReady()
        {
            for(int i=0;i<240;i++){await Settle(1);if(!_referenceDecoding&&_cameraReferenceTexture!=null&&(!_referenceIsVideo||Math.Abs(_referenceDisplayedTime-ReferenceSampleTime())<.1))return;}
            throw new InvalidOperationException("The reference did not finish loading/decoding.");
        }
        try
        {
            _playing=false;if(_pilot)TogglePilot();
            foreach(var format in ProjectFormats)
            {
                ApplyProjectSetup(new ProjectSetupData{Width=format.Width,Height=format.Height});await Settle(2);
                Check(Math.Abs(ProjectAspect-format.Width/(float)format.Height)<.00001&&_cameras.All(c=>Math.Abs(c.Rig.AspectRatio-ProjectAspect)<.00001),format.Name+" updates the project and every camera frustum");
                var size=_layoutCameraPreview.Size;Check(size.Y>0&&Math.Abs(size.X/size.Y-ProjectAspect)<.03,format.Name+" preview uses the actual project aspect");
            }
            foreach(int fps in new[]{12,15,24,25,30,37})
            {ChangeProjectFps(fps);Check(_fps==fps&&Math.Abs(_scrub.Step-1.0/fps)<.00001&&_fpsChoice.GetSelectedId()==fps,fps+" fps is selectable and controls timeline sampling");}
            var before=SnapshotProjectSetup();bool rejected=false;try{ApplyProjectSetup(new ProjectSetupData{Width=777});}catch(ArgumentException){rejected=true;}
            Check(rejected&&ProjectResolution==new Vector2I(before.Width,before.Height),"invalid odd dimensions fail before changing the project");
            var gate=AspectGate(new Vector2(1600,900),9f/16);Check(Math.Abs(gate.Size.X/gate.Size.Y-9f/16)<.00001&&gate.Position.X>0,"portrait camera gate is exact inside a landscape viewport");
            PushUndo();ApplyProjectSetup(new ProjectSetupData{Width=800,Height=800});Undo();Check(ProjectResolution==new Vector2I(before.Width,before.Height),"Undo restores the project format");Redo();Check(ProjectResolution==new Vector2I(800,800),"Redo restores the project format");

            string imagePath=Path.Combine(output,"reference.png");using(var image=Image.CreateEmpty(128,64,false,Image.Format.Rgb8)){image.Fill(new Color(1,0,1));image.SavePng(imagePath);}
            var setup=new ProjectSetupData{Width=512,Height=512,ReferencePath=imagePath,ReferenceFill=true,ReferenceOpacity=1};ApplyProjectSetup(setup);await ReferenceReady();
            Check(_cameraReferenceTexture!=null&&_cameraReferencePlane?.Visible==true&&_cameraReferencePlane.Layers==CameraReferenceLayer,"an image is an actual far-background plane restricted to the editor reference layer");
            Check((_editorCamera.CullMask&CameraReferenceLayer)==0,"the working orbit camera cannot see the reference layer");
            string projectFile=Path.Combine(output,"reference-scene.previs");SaveTo(projectFile);var serialized=JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(projectFile))!;
            Check(serialized.Setup.Width==512&&serialized.Fps==37&&!Path.IsPathRooted(serialized.Setup.ReferencePath)&&File.Exists(Path.Combine(output,serialized.Setup.ReferencePath)),"portable projects bundle the original reference and custom frame rate");
            LoadProjectFromPath(projectFile);await ReferenceReady();Check(ProjectResolution==new Vector2I(512,512)&&_fps==37&&_fpsChoice.GetSelectedId()==37,"custom frame rate, aspect and reference reopen correctly");
            BuildShotExportDialog();EnsureOptionId(_shotExportFps,_fps,_fps+" fps");Check(ShotExportDimensions(0)==ProjectResolution&&Math.Abs(ShotExportDimensions(1).X/(float)ShotExportDimensions(1).Y-ProjectAspect)<.00001,"export dimensions start from the project and keep its aspect when scaled");

            if(DisplayServer.GetName()!="headless")
            {
                ClearObjects();ClearCameras();var camera=CreateCamera("Reference test",null,true);camera.Rig.ClearKeys();camera.Rig.SetPose(new Vector3(0,.5f,4),new Vector3(0,.5f,0));SyncCameraFromRig(camera);
                var cube=AddObject("box",Vector3.Zero,"Opaque foreground");Select(cube);SetSelectedObjectColor(new Color(.5f,.5f,.5f));
                var display=SnapshotSceneSettings();display.GroundVisible=false;display.ObjectLabelsVisible=false;display.BackgroundColor=new[]{.025f,.055f,.16f,1f};ApplySceneSettings(display);SetTime(0);
                string baselinePath=Path.Combine(output,"export-no-reference.png");var off=SnapshotProjectSetup();off.ReferenceEnabled=false;ApplyProjectSetup(off);await Settle();await RenderFrame(baselinePath);using var clean=Image.LoadFromFile(baselinePath);
                var on=SnapshotProjectSetup();on.ReferenceEnabled=true;ApplyProjectSetup(on);TogglePilot();await Settle(8);
                using(var preview=_previewViewport.GetTexture().GetImage())
                {
                    preview.SavePng(Path.Combine(output,"reference-camera-preview.png"));var background=preview.GetPixel(8,8);var foreground=preview.GetPixel(preview.GetWidth()/2,preview.GetHeight()/2);
                    Check(background.R>.9f&&background.B>.9f&&background.G<.1f,"camera preview pixels show the reference image behind the 3D scene");
                    Check(Difference(foreground,clean.GetPixel(256,256))<.08f,"opaque 3D objects stay unchanged rather than being faded under a reference overlay");
                }
                using(var main=_sceneViewport.GetTexture().GetImage())
                {
                    var mainGate=AspectGate(new Vector2(main.GetWidth(),main.GetHeight()),ProjectAspect);var p=mainGate.Position+mainGate.Size*.08f;var background=main.GetPixel((int)p.X,(int)p.Y);
                    main.SavePng(Path.Combine(output,"reference-camera-view.png"));Check(background.R>.9f&&background.B>.9f&&background.G<.1f,"the main camera viewport shows the same reference and exact project gate");
                }
                string exportPath=Path.Combine(output,"export-reference-enabled.png");await RenderFrame(exportPath);using(var exported=Image.LoadFromFile(exportPath))
                {Check(exported.GetWidth()==512&&exported.GetHeight()==512,"still exports use the project resolution");Check(SHA256.HashData(exported.GetData()).SequenceEqual(SHA256.HashData(clean.GetData())),"reference background pixels are entirely excluded from actual exported RGB");}
                TogglePilot();await Settle();Check((_editorCamera.CullMask&CameraReferenceLayer)==0,"leaving camera view removes the main reference background");
                var rig=new StudioCameraRig{Visible=false};_world.AddChild(rig);var subject=new FramingSubject(new Aabb(new Vector3(-1,0,-.3f),new Vector3(2,2,.6f)),Vector3.Forward,new Vector3(0,1.8f,0),Vector3.Forward,new Vector3(-.5f,1.4f,0),new Vector3(.5f,1.4f,0));
                CameraFramingCatalog.Apply(rig,"front-wide",subject,Transform3D.Identity,50,9f/16);float portraitDistance=rig.GlobalPosition.DistanceTo(subject.WorldBounds.GetCenter());CameraFramingCatalog.Apply(rig,"front-wide",subject,Transform3D.Identity,50,2.39f);Check(rig.GlobalPosition.DistanceTo(subject.WorldBounds.GetCenter())>portraitDistance,"framing refits the vertical subject region for the selected aspect");rig.QueueFree();
            }

            string frames=Path.Combine(output,"video-source");Directory.CreateDirectory(frames);for(int i=0;i<2;i++){using var frame=Image.CreateEmpty(128,64,false,Image.Format.Rgb8);frame.Fill(i==0?new Color(1,0,0):new Color(0,0,1));frame.SavePng(Path.Combine(frames,i.ToString("D6")+".png"));}
            string video=Path.Combine(output,"reference-"+Guid.NewGuid().ToString("N")[..8]+".mp4");await ShotVideoEncoder.EncodeAsync(frames,video,1,128,64);var videoSetup=SnapshotProjectSetup();videoSetup.ReferencePath=video;videoSetup.ReferenceOffsetSeconds=0;SetTime(0);ApplyProjectSetup(videoSetup);await ReferenceReady();
            Color VideoPixel(){long key=(long)Math.Round(_referenceDisplayedTime*1000000);using var image=new Image();image.LoadPngFromBuffer(_referenceFrames[key]);return image.GetPixel(20,20);}
            var red=VideoPixel();Check(red.R>.8f&&red.B<.2f,"a real MP4 reference decodes its first frame asynchronously");
            SetTime(1.25);await ReferenceReady();var blue=VideoPixel();Check(blue.B>.8f&&blue.R<.2f,"video reference follows a forward timeline seek");
            SetTime(0);await ReferenceReady();Check(VideoPixel().R>.8f,"reverse seeking restores the original reference frame deterministically");
            videoSetup=SnapshotProjectSetup();videoSetup.ReferenceOffsetSeconds=1.25;ApplyProjectSetup(videoSetup);await ReferenceReady();Check(VideoPixel().B>.8f,"reference time offset selects the matching source-video frame");
            Check(_referenceFrames.Count<=24,"video reference cache stays bounded");
            passed=true;GD.Print("PROJECT_SETUP_OK checks="+checks);return checks;
        }
        catch(Exception ex){failure=ex.ToString();throw;}
        finally
        {
            if(_pilot)TogglePilot();Restore(original,"");_projectPath=originalPath;SetTime(originalTime);if(pilot&&!_pilot)TogglePilot();_playing=playing;
            _undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);
            File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,failure,observations},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
    async Task RunProjectSetupChecksAndQuit()
    {try{await RunProjectSetupChecks();GetTree().Quit(0);}catch(Exception ex){GD.PushError("PROJECT_SETUP_FAILED "+ex);GetTree().Quit(1);}}
}
