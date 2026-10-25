using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    public async Task<int> RunCameraReferenceChecks()
    {
        var original=Snapshot();string originalPath=_projectPath;double originalTime=_time;bool pilot=_pilot;
        var undo=_undo.ToArray();var redo=_redo.ToArray();int checks=0;bool passed=false;string? failure=null;var observations=new List<object>();
        string output=ProjectSettings.GlobalizePath("res://output/camera-reference-checks");Directory.CreateDirectory(output);
        void Check(bool valid,string message){observations.Add(new{message,passed=valid});Require(valid,"Camera backgrounds: "+message);checks++;}
        async Task Settle(int frames=4){for(int i=0;i<frames;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);if(DisplayServer.GetName()!="headless")await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);}
        async Task Ready()
        {
            var watch=Stopwatch.StartNew();
            while(watch.Elapsed.TotalSeconds<10)
            {
                await Settle(1);
                if(_referenceLoadedCameraId==ActiveCameraId&&_referenceLoadedPath==ActiveCameraReference.Path&&_cameraReferenceTexture!=null&&!_referenceDecoding&&(!_referenceIsVideo||Math.Abs(_referenceDisplayedTime-ReferenceSampleTime())<.001))return;
            }
            throw new InvalidOperationException("The active camera background did not finish loading.");
        }
        string MakeImage(string name,Color color)
        {string path=Path.GetFullPath(Path.Combine(output,name+".png"));using var image=Image.CreateEmpty(128,64,false,Image.Format.Rgb8);image.Fill(color);image.SavePng(path);return path;}
        try
        {
            _playing=false;if(_pilot)TogglePilot();ClearObjects();ClearCameras();ApplyProjectSetup(new ProjectSetupData{Width=512,Height=512});
            var display=SnapshotSceneSettings();display.GroundVisible=false;display.ObjectLabelsVisible=false;display.BackgroundColor=new[]{.025f,.055f,.16f,1f};ApplySceneSettings(display);
            var a=CreateCamera("Camera A",null,true);a.Rig.SetPose(new Vector3(0,.5f,4),new Vector3(0,.5f,0));SyncCameraFromRig(a);
            var b=CreateCamera("Camera B",null,false);b.Rig.SetPose(a.Rig.GlobalPosition,new Vector3(0,.5f,0));SyncCameraFromRig(b);
            string aId=a.Id,bId=b.Id;
            CameraEntry A()=>_cameras.Single(camera=>camera.Id==aId);CameraEntry B()=>_cameras.Single(camera=>camera.Id==bId);
            string red=MakeImage("red",Colors.Red),blue=MakeImage("blue",Colors.Blue),green=MakeImage("green",Colors.Green);
            SelectCamera(a);RefreshCameraBackgroundControls();
            Check(_cameraBackgroundInspector?.Visible==true&&_cameraBackgroundControls[0].Name.Text=="No image or video","selecting a camera exposes its background controls directly in Properties");
            Check(_cameraBackgroundInspector!.GetIndex()<_parameterLabels["Position"].GetParent().GetIndex(),"camera background controls appear above transforms rather than at the bottom of the inspector");
            int undoCount=_undo.Count;_cameraBackgroundControls[0].Load.EmitSignal(BaseButton.SignalName.Pressed);
            var picker=GetChildren().OfType<FileDialog>().Single(dialog=>dialog.Title.StartsWith("Choose background for"));picker.EmitSignal(FileDialog.SignalName.FileSelected,red);await Ready();
            Check(A().Reference.Path==red&&B().Reference.Path==""&&_undo.Count==undoCount+1,"the actual inspector picker updates only its camera with one Undo action");
            _cameraBackgroundControls[0].Opacity.Value=100;Check(A().Reference.Opacity==1&&B().Reference.Opacity==.5f,"camera opacity controls do not modify another camera");
            SelectCamera(B());Check(_cameraReferenceTexture==null&&_cameraReferencePlane?.Visible!=true,"a camera without a background never inherits the previous image");
            var cube=AddObject("box",Vector3.Zero,"Opaque foreground");Select(cube);OpenActiveCameraBackground();
            Check(_selected==cube&&_cameraBackgroundDialogId==bId,"the preview background action targets the active camera while an object remains selected");
            var modal=_cameraBackgroundControls.Last();modal.Load.EmitSignal(BaseButton.SignalName.Pressed);
            var nested=_cameraBackgroundDialog!.GetChildren().OfType<FileDialog>().Single();nested.EmitSignal(FileDialog.SignalName.FileSelected,blue);await Ready();_cameraBackgroundDialog.Hide();
            Check(A().Reference.Path==red&&B().Reference.Path==blue,"the camera background modal uses an independent per-camera file");
            ApplyActiveCameraReference(new CameraReferenceData{Path=blue,Opacity=1,Fill=true});
            Check(!ReferenceEquals(A().Reference,B().Reference),"per-camera background objects have independent identity");
            async Task<Color> Pixel(string filename)
            {
                await Ready();await Settle();using var image=_previewViewport.GetTexture().GetImage();image.SavePng(Path.Combine(output,filename));return image.GetPixel(8,image.GetHeight()/2);
            }
            if(DisplayServer.GetName()!="headless")
            {
                var pixel=await Pixel("camera-b.png");Check(pixel.B>.9f&&pixel.R<.1f,"Camera B actually renders its blue background");
                SelectCamera(A());pixel=await Pixel("camera-a.png");Check(pixel.R>.9f&&pixel.B<.1f,"switching to Camera A actually restores its red background");
                string first=Path.Combine(output,"clean-a.png");await RenderFrame(first);SelectCamera(B());await Ready();string second=Path.Combine(output,"clean-b.png");await RenderFrame(second);
                using var firstImage=Image.LoadFromFile(first);using var secondImage=Image.LoadFromFile(second);
                Check(SHA256.HashData(firstImage.GetData()).SequenceEqual(SHA256.HashData(secondImage.GetData())),"different camera background images never change the matching clean RGB export");
            }
            SelectCamera(A());EditCameraBackground(A(),data=>data.Enabled=false);Check(!A().Reference.Enabled&&B().Reference.Enabled,"the show switch belongs to its camera");
            Undo();Check(A().Reference.Enabled&&A().Reference.Path==red&&B().Reference.Path==blue,"Undo restores camera background data and leaves other cameras intact");
            Redo();Check(!A().Reference.Enabled&&B().Reference.Enabled,"Redo restores only the edited camera visibility");Undo();
            SelectCamera(A());AddCameraFromView(true);string duplicate=ActiveCameraId;var copy=_cameras.Single(camera=>camera.Id==duplicate);
            Check(copy.Reference.Path==A().Reference.Path&&!ReferenceEquals(copy.Reference,A().Reference),"camera duplication deep-copies its background and controls");
            EditCameraBackground(copy,data=>{data.Path=green;data.Opacity=.25f;});Check(A().Reference.Path==red&&A().Reference.Opacity==1,"editing a duplicated camera cannot alter the source camera");
            RemoveActiveCamera();SelectCamera(B());
            string projectFile=Path.Combine(output,"two-cameras.previs");SaveTo(projectFile);var saved=JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(projectFile))!;
            Check(saved.Version==ProjectData.CurrentVersion&&saved.Cameras.All(camera=>!Path.IsPathRooted(camera.Reference.Path)&&File.Exists(Path.Combine(output,camera.Reference.Path))),"current format bundles every camera source with portable relative paths");
            Check(saved.Cameras.Select(camera=>camera.Reference.Path).Distinct().Count()==2&&saved.Setup.ReferencePath=="","each camera asset has its own destination and new projects store no global reference");
            LoadProjectFromPath(projectFile);await Ready();Check(ActiveCameraId==bId&&A().Reference.Opacity==1&&B().Reference.Opacity==1,"save/reopen retains independent references, settings and active camera");
            Check(SHA256.HashData(File.ReadAllBytes(A().Reference.Path)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(red)))&&SHA256.HashData(File.ReadAllBytes(B().Reference.Path)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(blue))),"both reopened camera sources preserve the original image bytes");
            string priorBundledRed=A().Reference.Path;B().Reference=new CameraReferenceData{Path=priorBundledRed,Opacity=1};A().Reference=new CameraReferenceData{Path=blue,Opacity=1};SaveTo(projectFile);LoadProjectFromPath(projectFile);
            Check(SHA256.HashData(File.ReadAllBytes(A().Reference.Path)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(blue)))&&SHA256.HashData(File.ReadAllBytes(B().Reference.Path)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(red))),"saving after another camera reuses an earlier bundled source cannot overwrite that source's bytes");
            A().Reference=new CameraReferenceData{Path=red,Opacity=1};B().Reference=new CameraReferenceData{Path=blue,Opacity=1};SaveTo(projectFile);LoadProjectFromPath(projectFile);await Ready();
            var legacy=Snapshot();legacy.Version=7;legacy.Setup.ReferencePath=red;legacy.Setup.ReferenceOpacity=.75f;legacy.Setup.ReferenceFill=true;legacy.ActiveCameraId=bId;foreach(var camera in legacy.Cameras)camera.Reference=new();
            Restore(legacy,"");await Ready();Check(A().Reference.Path==""&&B().Reference.Path==red&&B().Reference.Opacity==.75f&&B().Reference.Fill,"format 7 global references migrate only to the saved active camera");
            Check(legacy.Cameras.All(camera=>camera.Reference.Path=="")&&legacy.Setup.ReferencePath==red,"migration does not mutate its input or an Undo snapshot");
            LoadProjectFromPath(projectFile);SelectCamera(B());await Ready();RemoveActiveCamera();await Ready();Check(ActiveCameraId==aId&&ActiveCameraReference.Path==A().Reference.Path,"deleting the active camera switches to the remaining camera's own image");Undo();SelectCamera(B());await Ready();Check(B().Reference.Path.EndsWith(".png",StringComparison.OrdinalIgnoreCase),"Undo restores a deleted camera together with its background");

            string frames=Path.Combine(output,"video-frames");Directory.CreateDirectory(frames);File.Copy(red,Path.Combine(frames,"000000.png"),true);File.Copy(blue,Path.Combine(frames,"000001.png"),true);
            string video=Path.Combine(output,"reference-"+Guid.NewGuid().ToString("N")[..8]+".mp4");await ShotVideoEncoder.EncodeAsync(frames,video,1,128,64);
            SelectCamera(A());SetTime(0);ApplyActiveCameraReference(new CameraReferenceData{Path=video,Opacity=1,OffsetSeconds=1.25});
            SelectCamera(B());ApplyActiveCameraReference(new CameraReferenceData{Path=green,Opacity=1});await Ready();await Settle(8);
            Check(_referenceLoadedCameraId==bId&&_referenceLoadedPath==green&&_cameraReferenceTexture!.GetImage().GetPixel(10,10).G>.4f,"switching away during video decoding cancels the old result without overwriting another camera");
            SelectCamera(A());await Ready();using(var image=_cameraReferenceTexture!.GetImage())Check(image.GetPixel(10,10).B>.8f,"Camera A video uses its own source-time offset");
            SelectCamera(B());ApplyActiveCameraReference(new CameraReferenceData{Path=video,Opacity=.35f,OffsetSeconds=0});await Ready();using(var image=_cameraReferenceTexture!.GetImage())Check(image.GetPixel(10,10).R>.8f&&ActiveCameraReference.Opacity==.35f,"two cameras sharing a video still retain distinct offsets and opacity");
            SelectCamera(A());await Ready();Check(ActiveCameraReference.OffsetSeconds==1.25&&ActiveCameraReference.Opacity==1,"switching back restores the first camera's video settings");
            if(DisplayServer.GetName()!="headless")
            {
                SelectCamera(A());OpenActiveCameraBackground();await Settle();GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,"camera-background-ui.png"));_cameraBackgroundDialog!.Hide();
            }
            passed=true;GD.Print("CAMERA_REFERENCES_OK checks="+checks);return checks;
        }
        catch(Exception ex){failure=ex.ToString();throw;}
        finally
        {
            _cameraBackgroundDialog?.Hide();if(_pilot)TogglePilot();Restore(original,"");_projectPath=originalPath;SetTime(originalTime);if(pilot&&!_pilot)TogglePilot();
            _undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);
            File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,failure,observations},_json));
        }
    }
    async Task RunCameraReferenceChecksAndQuit()
    {try{await RunCameraReferenceChecks();GetTree().Quit(0);}catch(Exception ex){GD.PushError("CAMERA_REFERENCES_FAILED "+ex);GetTree().Quit(1);}}
}
