using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GoPrevis;

/// <summary>Actual GPU depth calibration, posed skeleton alignment and material restoration.</summary>
public partial class ControlPassProbe:SceneTree
{
    int _checks;
    void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);_checks++;GD.Print("PASS Control passes: "+text);}
    public override async void _Initialize()
    {
        try
        {
            if(DisplayServer.GetName()=="headless")throw new InvalidOperationException("GPU rendering is required for this probe.");
            var viewport=new SubViewport{Size=new Vector2I(320,240),OwnWorld3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Always};Root.AddChild(viewport);
            var world=new Node3D();viewport.AddChild(world);world.AddChild(new WorldEnvironment{Environment=StudioLighting.CreateEnvironment()});
            var camera=new Camera3D{Current=true,Fov=60,KeepAspect=Camera3D.KeepAspectEnum.Width,Near=.05f,Far=100};world.AddChild(camera);camera.Position=new Vector3(0,0,5);
            var originalMaterial=new StandardMaterial3D{AlbedoColor=new Color("b97b64")};var overlay=new StandardMaterial3D{AlbedoColor=new Color(0,1,0,.1f),Transparency=BaseMaterial3D.TransparencyEnum.Alpha};
            var box=new MeshInstance3D{Mesh=new BoxMesh{Size=Vector3.One*2},MaterialOverride=originalMaterial,MaterialOverlay=overlay};world.AddChild(box);
            string output=ProjectSettings.GlobalizePath("res://output");Directory.CreateDirectory(output);
            async Task Settle(){for(int i=0;i<3;i++)await ToSignal(this,SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);}
            async Task<Image> Capture(){await Settle();return viewport.GetTexture().GetImage();}
            await Settle();var originalEnvironment=camera.Environment;
            using var renderer=new ControlPassRenderer(world,camera,1,9);
            renderer.BeginDepthPass();Check(renderer.DepthPassActive&&box.MaterialOverride is ShaderMaterial&&box.MaterialOverlay==null,"depth swaps the real mesh material and suppresses overlay passes");
            using var first=await Capture();var pixel=first.GetPixel(160,120);float expected=(9-4)/8f;
            GD.Print("DEPTH_CALIBRATION value="+pixel.R+" expected="+expected+" decoded="+renderer.DecodeDepth(pixel));
            Check(Math.Abs(pixel.R-expected)<.012f&&Math.Abs(pixel.R-pixel.G)<.004f&&Math.Abs(pixel.G-pixel.B)<.004f,"RGB byte values encode actual linear camera-Z depth");
            Check(Math.Abs(renderer.DecodeDepth(pixel)-4)<.1f,"depth range decodes a known four-metre front surface");first.SavePng(Path.Combine(output,"control-pass-probe-depth.png"));
            box.Position=Vector3.Forward*2;using var farther=await Capture();Check(farther.GetPixel(160,120).R<pixel.R-.20f&&Math.Abs(renderer.DecodeDepth(farther.GetPixel(160,120))-6)<.1f,"farther geometry is darker and decodes six metres");
            renderer.EndDepthPass();Check(!renderer.DepthPassActive&&ReferenceEquals(box.MaterialOverride,originalMaterial)&&ReferenceEquals(box.MaterialOverlay,overlay)&&ReferenceEquals(camera.Environment,originalEnvironment),"ending depth restores every original material and camera environment");
            renderer.EndDepthPass();Check(ReferenceEquals(box.MaterialOverride,originalMaterial),"restoration is idempotent");box.Visible=false;

            var actor=new RiggedActor();world.AddChild(actor);actor.SetMotionClips(new[]{new MotionClipData{MotionPath=MotionCatalog.MaterializeSource(MotionCatalog.Find("human-walk-gesture")!),SourceScale=.01f,Duration=4,Loop=true}});actor.EvaluateTimeline(.7,true);
            camera.Position=new Vector3(0,1.1f,4);camera.LookAt(new Vector3(0,.9f,0));camera.Fov=48;await Settle();renderer.BeginDepthPass();using var humanDepth=await Capture();renderer.EndDepthPass();
            var posed=renderer.RenderPose(new[]{new PoseActorSample("performer","Test performer",actor)},humanDepth,.7);using var pose=posed.Image;
            Check(posed.Annotation.People.Count==1&&posed.Annotation.People[0].PoseKeypoints2D.Length==54&&posed.Annotation.People[0].Joints.Count==18,"prepared humans export COCO-18 coordinates and confidence");
            Check(pose.GetData().Any(value=>value!=0),"actual posed skeleton produces colored guidance pixels");pose.SavePng(Path.Combine(output,"control-pass-probe-pose.png"));humanDepth.SavePng(Path.Combine(output,"control-pass-probe-human-depth.png"));
            var wrist=posed.Annotation.People[0].Joints.Single(j=>j.Name=="right_wrist");var expectedWorld=actor.GlobalTransform*actor.GetPoseJointPosition("RightHand");var expectedPixel=camera.UnprojectPosition(expectedWorld);
            Check(new Vector3(wrist.World[0],wrist.World[1],wrist.World[2]).DistanceTo(expectedWorld)<.00001f&&new Vector2(wrist.Pixel[0],wrist.Pixel[1]).DistanceTo(expectedPixel)<.0001f,"pose coordinates align with the exact capture camera and evaluated rig");
            Check(posed.Annotation.People[0].Joints.Where(j=>j.Derived).Count()==5&&posed.Annotation.FaceLandmarks.Contains("proxies"),"face approximations are explicitly identified rather than claimed as captured landmarks");
            actor.EvaluateTimeline(1.4,true);await Settle();var later=renderer.RenderPose(new[]{new PoseActorSample("performer","Test performer",actor)},null,1.4);using var laterImage=later.Image;
            Check(posed.Annotation.People[0].Joints.Zip(later.Annotation.People[0].Joints).Any(pair=>Vector3From(pair.First.World).DistanceTo(Vector3From(pair.Second.World))>.01f),"pose annotations change with the actual mocap evaluation");
            var wall=new MeshInstance3D{Mesh=new BoxMesh{Size=new Vector3(4,4,.2f)},Position=new Vector3(0,1,2),MaterialOverride=originalMaterial};world.AddChild(wall);renderer.BeginDepthPass();using var occludedDepth=await Capture();renderer.EndDepthPass();var blocked=renderer.RenderPose(new[]{new PoseActorSample("performer","Test performer",actor)},occludedDepth,1.4);using var blockedImage=blocked.Image;
            Check(blocked.Annotation.People[0].Joints.All(j=>!j.Visible)&&blockedImage.GetData().All(value=>value==0),"foreground geometry occludes both joint annotations and pose strokes");wall.Visible=false;actor.Visible=false;

            var document=new GltfDocument();var state=new GltfState();Check(document.AppendFromFile(MotionCatalog.MaterializeSource(MotionCatalog.Find("fox-walk")!),state)==Error.Ok,"native animal source imports for the control-pass check");
            var model=(Node3D)document.GenerateScene(state);model.Scale=Vector3.One*.01f;world.AddChild(model);var player=Descendants(model).OfType<AnimationPlayer>().First();var track=new NativeMotionTrack(model,player);track.SetClips(new[]{new MotionClipData{SourceAnimation="Walk",Name="Walk",Duration=1,Loop=true}});track.Evaluate(.2,true);
            camera.Position=new Vector3(1.8f,1.2f,2.2f);camera.LookAt(new Vector3(0,.4f,0));camera.Fov=48;await Settle();renderer.BeginDepthPass();using var nativeDepth=await Capture();renderer.EndDepthPass();var native=renderer.RenderPose(Array.Empty<PoseActorSample>(),nativeDepth,.2,new[]{new NativePoseActorSample("fox","Fox",model)});using var nativeImage=native.Image;
            Check(native.Annotation.People.Count==0&&native.Annotation.NativeRigs.Count==1&&native.Annotation.NativeRigs[0].Joints.Count==24&&native.Annotation.Format.Contains("native-rig"),"animals retain their real native topology instead of fake human COCO labels");
            Check(nativeImage.GetData().Any(value=>value!=0),"native animal bones produce an actual rig guidance map");nativeImage.SavePng(Path.Combine(output,"control-pass-probe-native-pose.png"));nativeDepth.SavePng(Path.Combine(output,"control-pass-probe-native-depth.png"));
            track.Evaluate(.5,true);await Settle();renderer.BeginDepthPass();using var changedDepth=await Capture();renderer.EndDepthPass();var originalBytes=nativeDepth.GetData();var changedBytes=changedDepth.GetData();Check(originalBytes.Where((value,index)=>value!=changedBytes[index]).Take(10).Count()>5,"the depth pass follows native skinned animation rather than a rest-pose proxy");
            var settings=new ShotExportSettings{OutputDirectory=output,StartSeconds=1,EndSeconds=2,Fps=24};settings.Validate(8);Check(settings.FrameCount==24&&settings.FrameTimes().Last()<2&&Math.Abs(settings.MediaDuration-1)<.000001,"all passes share a precise end-exclusive sampling range");
            File.WriteAllText(Path.Combine(output,"control-pass-probe-report.json"),JsonSerializer.Serialize(new{Status="passed",Checks=_checks,DepthEncoding=renderer.DepthEncoding,KnownSurfaceMetres=4,MeasuredGray=pixel.R,PoseHumans=posed.Annotation.People.Count,NativeRigs=native.Annotation.NativeRigs.Count},new JsonSerializerOptions{WriteIndented=true}));
            GD.Print("CONTROL_PASS_OK checks="+_checks);viewport.QueueFree();await ToSignal(this,SignalName.ProcessFrame);Quit(0);
        }
        catch(Exception ex){GD.PushError("CONTROL_PASS_FAILED "+ex);Quit(1);}
    }
    static Vector3 Vector3From(float[] values)=>new(values[0],values[1],values[2]);
    static System.Collections.Generic.IEnumerable<Node> Descendants(Node root){yield return root;foreach(Node child in root.GetChildren())foreach(var nested in Descendants(child))yield return nested;}
}
