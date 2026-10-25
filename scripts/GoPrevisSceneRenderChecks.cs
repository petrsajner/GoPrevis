using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    /// <summary>Pixel-level checks that scene display choices reach actual camera and depth renders.</summary>
    public async Task<int> RunSceneDisplayRenderChecks()
    {
        if(DisplayServer.GetName()=="headless")throw new InvalidOperationException("Scene display render checks need the graphics renderer.");
        var original=Snapshot();string project=_projectPath;double time=_time;bool playing=_playing;var undo=_undo.ToArray();var redo=_redo.ToArray();var editor=_editorCamera.Transform;
        int checks=0;bool passed=false;string? failure=null;var observations=new List<object>();
        string output=ProjectSettings.GlobalizePath("res://output");Directory.CreateDirectory(output);SubViewport? depthView=null;ControlPassRenderer? depthRenderer=null;
        void Check(bool value,string description){observations.Add(new{description,passed=value});Require(value,"Scene rendered: "+description);checks++;}
        static float Luma(Color color)=>color.R*.2126f+color.G*.7152f+color.B*.0722f;
        static float Difference(Color a,Color b)=>Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B);
        async Task Settle(int count=4){for(int i=0;i<count;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);}
        async Task<Image> ColorFrame(string name){await Settle();string path=Path.Combine(output,"scene-display-"+name+".png");await RenderFrame(path);return Image.LoadFromFile(path);}
        try
        {
            _playing=false;StopPoseEditing();ClearObjects();ClearCameras();var camera=CreateCamera("Scene display camera",null,true);camera.Rig.ClearKeys();camera.Rig.SetPose(new Vector3(3,2.2f,5),new Vector3(0,.5f,0));SyncCameraFromRig(camera);_lighting.DirectionalEnabled=false;SetTime(0);
            var cube=AddObject("box",Vector3.Zero,"Display test cube");cube.Data.Appearance.ShowLabel=true;RefreshAllObjectLabels();Select(cube);
            var scene=new SceneSettingsData{BackgroundColor=new float[]{.025f,.055f,.16f,1},Brightness=1,GroundVisible=false,GroundHeight=0,GroundOpacity=1,ObjectLabelsVisible=false};ApplySceneSettings(scene);await Settle(8);
            using var blue=await ColorFrame("background-blue");Color bluePixel=blue.GetPixel(100,50),boxBright=blue.GetPixel(960,540);
            Check(blue.GetWidth()==1920&&blue.GetHeight()==1080,"scene settings are tested through the real full-HD active camera export");
            Check(bluePixel.B>bluePixel.R*1.5f&&bluePixel.B>.05f,"selected blue background appears in rendered pixels");
            scene=SnapshotSceneSettings();scene.BackgroundColor=new float[]{.2f,.035f,.02f,1};ApplySceneSettings(scene);using var red=await ColorFrame("background-red");Color redPixel=red.GetPixel(100,50);
            Check(redPixel.R>redPixel.B*1.5f&&Difference(redPixel,bluePixel)>.15f,"changing background color changes the camera image rather than only a control value");
            scene=SnapshotSceneSettings();scene.Brightness=.25f;ApplySceneSettings(scene);await Settle(10);using var dim=await ColorFrame("brightness-25");Color boxDim=dim.GetPixel(960,540);
            Check(Luma(boxDim)<Luma(boxBright)*.8f&&Luma(boxBright)-Luma(boxDim)>.03f,"25 percent global fill visibly dims the matte object without a directional light");
            Check(Difference(dim.GetPixel(100,50),redPixel)<.015f,"global lighting brightness does not unexpectedly recolor the chosen flat background");

            scene=SnapshotSceneSettings();scene.Brightness=1;scene.BackgroundColor=new float[]{.025f,.055f,.16f,1};scene.GroundVisible=true;scene.GroundHeight=0;ApplySceneSettings(scene);await Settle(8);
            using var ground=await ColorFrame("ground-shown");Color floorPixel=ground.GetPixel(1600,950);
            scene=SnapshotSceneSettings();scene.GroundVisible=false;ApplySceneSettings(scene);using var hidden=await ColorFrame("ground-hidden");Color emptyPixel=hidden.GetPixel(1600,950);
            Check(Difference(floorPixel,emptyPixel)>.08f,"hiding the ground removes it from the actual camera render");
            Check(Difference(emptyPixel,hidden.GetPixel(100,50))<.015f,"hidden ground exposes the selected background rather than an editor grid");
            scene=SnapshotSceneSettings();scene.GroundVisible=true;scene.GroundOpacity=.35f;ApplySceneSettings(scene);using var transparent=await ColorFrame("ground-opacity-35");Color transparentPixel=transparent.GetPixel(1600,950);
            Check(Difference(transparentPixel,floorPixel)>.025f&&Difference(transparentPixel,emptyPixel)>.025f,"ground opacity visibly blends its surface with the scene background");
            scene=SnapshotSceneSettings();scene.GroundOpacity=1;scene.ObjectLabelsVisible=false;ApplySceneSettings(scene);using var labelsOff=await ColorFrame("labels-hidden");
            scene=SnapshotSceneSettings();scene.ObjectLabelsVisible=true;ApplySceneSettings(scene);using var labelsOn=await ColorFrame("labels-shown");
            Check(SHA256.HashData(labelsOff.GetData()).SequenceEqual(SHA256.HashData(labelsOn.GetData())),"editor object labels never contaminate the active-camera image");

            depthView=new SubViewport{Size=new Vector2I(640,360),World3D=_sceneViewport.World3D,RenderTargetUpdateMode=SubViewport.UpdateMode.Always};AddChild(depthView);
            var capture=new Camera3D{Current=true,CullMask=1,KeepAspect=Camera3D.KeepAspectEnum.Width,Fov=camera.Rig.ShotCamera.Fov,Near=.05f,Far=100};depthView.AddChild(capture);capture.GlobalTransform=camera.Rig.ShotCamera.GlobalTransform;depthRenderer=new ControlPassRenderer(_world,capture,.05f,20);
            async Task<Image> DepthFrame(string name)
            {
                depthRenderer.BeginDepthPass();try{await Settle();var image=depthView.GetTexture().GetImage();image.SavePng(Path.Combine(output,"scene-display-"+name+".png"));return image;}finally{depthRenderer.EndDepthPass();}
            }
            var sample=new Vector2(540,320);float ExpectedGroundDepth(float height)
            {
                var from=capture.ProjectRayOrigin(sample);var direction=capture.ProjectRayNormal(sample);var point=from+direction*((height-from.Y)/direction.Y);return -(capture.GlobalTransform.AffineInverse()*point).Z;
            }
            scene=SnapshotSceneSettings();scene.GroundHeight=0;ApplySceneSettings(scene);using var depthZero=await DepthFrame("depth-ground-zero");float zeroValue=depthZero.GetPixel(540,320).R,expectedZero=ExpectedGroundDepth(0);
            Check(Math.Abs(depthRenderer.DecodeDepth(depthZero.GetPixel(540,320))-expectedZero)<.13f,"depth export sees the configured ground plane at zero metres");
            scene=SnapshotSceneSettings();scene.GroundHeight=.6f;ApplySceneSettings(scene);using var depthRaised=await DepthFrame("depth-ground-raised");float raisedValue=depthRaised.GetPixel(540,320).R,expectedRaised=ExpectedGroundDepth(.6f);
            Check(raisedValue>zeroValue+.025f&&Math.Abs(depthRenderer.DecodeDepth(depthRaised.GetPixel(540,320))-expectedRaised)<.13f,"raising the ground changes its measured camera-Z depth at the correct world height");
            using var raised=await ColorFrame("ground-raised");Check(SHA256.HashData(raised.GetData()).SequenceEqual(SHA256.HashData(labelsOn.GetData()))==false,"ground height also changes the beauty render");
            scene=SnapshotSceneSettings();scene.GroundVisible=false;ApplySceneSettings(scene);using var depthHidden=await DepthFrame("depth-ground-hidden");
            Check(depthHidden.GetPixel(540,320).R<.005f,"hidden ground is absent from the depth pass as well as RGB");
            passed=true;GD.Print("SCENE_DISPLAY_RENDER_OK checks="+checks);return checks;
        }
        catch(Exception ex){failure=ex.ToString();throw;}
        finally
        {
            depthRenderer?.Dispose();depthView?.QueueFree();
            File.WriteAllText(Path.Combine(output,"scene-display-render-report.json"),JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,failure,observations},new JsonSerializerOptions{WriteIndented=true}));
            Restore(original,"");_projectPath=project;SetTime(time);_editorCamera.Transform=editor;_playing=playing;_undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);
            for(int i=0;i<2;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
    }
}
