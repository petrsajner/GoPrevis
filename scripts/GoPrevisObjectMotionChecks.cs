using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    public async Task<int> RunObjectMotionChecks()
    {
        var original=Snapshot();var originalPath=_projectPath;var originalTime=_time;var undo=_undo.ToArray();var redo=_redo.ToArray();bool auto=_autoKey;var editor=_editorCamera.Transform;string workspace=_animationWorkspaceMode,animationSearch=_animationSearch?.Text??"";
        int checks=0;bool passed=false;string? failure=null;var observations=new List<object>();
        void Check(bool value,string message){observations.Add(new{message,passed=value});Require(value,"Object motion: "+message);checks++;}
        try
        {
            StopPoseEditing();_playing=false;_autoKey=false;ClearObjects();SetTime(0);
            foreach(string mode in KeyInterpolation.Modes)
            {
                var timing=new KeyInterpolationData{Mode=mode};
                Check(KeyInterpolation.Weight(timing,0)==0&&KeyInterpolation.Weight(timing,1)==1,mode+" reaches both exact key values");
                float previous=0;bool monotone=true;for(int i=0;i<=100;i++){float sample=KeyInterpolation.Weight(timing,i/100.0);monotone&=float.IsFinite(sample)&&sample>=previous-.000001&&sample>=0&&sample<=1;previous=sample;}
                Check(monotone,mode+" has finite monotone timing without overshoot");
            }
            var ease=new KeyInterpolationData{Mode="Ease in/out"};var stop=new KeyInterpolationData{Mode="Stop"};var hold=new KeyInterpolationData{Mode="Hold"};
            Check(KeyInterpolation.Weight(null,.25)==.25f,"missing timing remains linear for old scenes");
            Check(KeyInterpolation.Weight(ease,.25)<.25&&KeyInterpolation.Weight(ease,.75)>.75,"Ease in/out accelerates out of one key and slows into the next");
            Check(KeyInterpolation.Weight(stop,.01)<KeyInterpolation.Weight(ease,.01)*.1,"Stop has a longer stationary departure with zero endpoint acceleration");
            Check(KeyInterpolation.Weight(hold,.9999)==0,"Hold keeps the exact outgoing value until the next key");
            var curved=new KeyInterpolationData{Mode="Curved",Control1=new[]{.2f,.85f},Control2=new[]{.8f,.95f}};
            Check(KeyInterpolation.Weight(curved,.3)>.6,"custom Bezier handles shape actual time rather than only the drawn curve");
            curved.Control1=new[]{float.NaN,float.PositiveInfinity};Check(float.IsFinite(KeyInterpolation.Weight(curved,.3)),"malformed serialized handles fall back to finite timing");
            Check(KeyInterpolation.Weight(ease,double.NaN)==0&&KeyInterpolation.Weight(ease,-2)==0&&KeyInterpolation.Weight(ease,2)==1,"out-of-range and NaN sampling is bounded");

            var car=AddObject("car",Vector3.Zero,"Keyed car");Select(car);RefreshUnifiedWorkflow();
            Check(_drawPathButton.Visible,"a static car exposes the same Draw path action as a character");
            StartDrawingPath();Check(_drawingPath&&_pathEntry==car,"a static prop can enter the real path drawing tool");
            car.Data.Path.Points=new(){new float[]{-3,0,0},new float[]{0,0,0},new float[]{3,0,0}};FinishDrawingPath();
            Check(car.Data.Path.Enabled&&car.Data.Path.PointTimes.Count==3,"finishing a prop path creates visible timed waypoint keys");
            SetTime(2);var linearPosition=car.Node.Position;SelectPathKey(car,0);
            Check(_keyInterpolationPanel.Visible&&_keyInterpolationMode.ItemCount==5,"selecting a path diamond opens the shared five-choice key timing inspector");
            int undoCount=_undo.Count;_keyInterpolationMode.Select(3);_keyInterpolationMode.EmitSignal(OptionButton.SignalName.ItemSelected,3L);
            Check(car.Data.Path.PointInterpolation[0].Mode=="Stop"&&_undo.Count==undoCount+1,"choosing Stop edits a prop waypoint in one Undo action");
            SetTime(1);Check(car.Node.Position.X<ActorPathRuntime.Evaluate(new ActorPathData{Points=car.Data.Path.Points,Duration=8},1).Position.X,"waypoint easing changes the actual car travel speed");
            string carId=car.Data.Id;Undo();car=_entries.Single(e=>e.Data.Id==carId);Check(car.Data.Path.PointInterpolation.Count==0,"Undo restores the preceding path timing");Redo();car=_entries.Single(e=>e.Data.Id==carId);Check(car.Data.Path.PointInterpolation[0].Mode=="Stop","Redo restores prop acceleration timing");
            car.Data.Path.PointInterpolation[0].Mode="Hold";SetTime(3.9);Check(car.Node.Position.DistanceTo(Vector(car.Data.Path.Points[0]))<.0001,"a held prop waypoint keeps the car parked");
            SetTime(4);Check(car.Node.Position.DistanceTo(Vector(car.Data.Path.Points[1]))<.001,"a held waypoint changes exactly at the next arrival time");
            car.Data.Path.PointInterpolation[0].Mode="Ease in/out";
            Select(car);SetTime(0);CaptureSelectedTransformKey("Position");SetTime(4);ApplySelectedManualTransform("Position",new Vector3(0,.6f,0));
            Check(car.Data.PropertyAnimation.Channels["Transform.Position.Y"].Keys.Count==2&&new[]{"X","Z"}.All(axis=>car.Data.PropertyAnimation.Channels["Transform.Position."+axis].Keys.Count==1),"a grouped prop capture splits XYZ while an isolated Y edit inserts only its own next key");
            SetTime(2);Check(Math.Abs(car.Node.Position.Y-.3)<.0001,"manual position keys remain additive above path movement");
            car.Data.Rotation=new[]{0f,25f,0f};TransformLayerRuntime.Upsert(car.Data.TransformAnimation.Rotation,0,new Vector3(0,5,0));TransformLayerRuntime.Upsert(car.Data.TransformAnimation.Rotation,4,new Vector3(0,45,0));
            SetTime(2);Check(Math.Abs(Mathf.Wrap(car.Node.RotationDegrees.Y+40,-180,180))<.001,"manual yaw keys add to the car path heading");
            var carPose=car.Node.Transform;SetTime(7);SetTime(2);Check(car.Node.Transform.IsEqualApprox(carPose),"eased path and manual car keys are deterministic after reverse seeking");
            var positionY=car.Data.PropertyAnimation.Channels["Transform.Position.Y"];positionY.Keys[0].Interpolation="Hold";
            SetTime(3.9);Check(Math.Abs(car.Node.Position.Y)<.0001,"Hold also works on independent object transform axes");
            SetTime(4);Check(Math.Abs(car.Node.Position.Y-.6)<.0001,"object transform Hold hits the exact next value");
            var yBinding=AnimationBindings().Single(b=>b.TargetId==carId&&b.Path=="Transform.Position.Y");
            PropertyCurveRuntime.ApplyPreset(positionY,positionY.Keys[0].Id,"Bezier");
            _animationSelectedChannels.Clear();_animationSelectedChannels.Add(yBinding.Identity);_animationSearch!.Text=car.Data.Name;
            SetAnimationWorkspaceMode("Graph");for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);_animationGraph!.FitCurves();
            var point=_animationGraph.KeyScreenPoint(yBinding.Identity,positionY.Keys[0].Id);
            _animationGraph._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=point});
            _animationGraph._Input(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false});
            var handle=_animationGraph.TangentScreenPoint(yBinding.Identity,positionY.Keys[0].Id,false);
            Check(_animationGraphPanel!.Visible&&handle.IsFinite(),"Bezier exposes the actual draggable value-graph handles");
            double oldSlope=positionY.Keys[0].OutSlope,oldSample=PropertyCurveRuntime.Evaluate(positionY,1,0);
            undoCount=_undo.Count;_animationGraph._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=handle});
            _animationGraph._Input(new InputEventMouseMotion{Position=_animationGraph.GetGlobalTransformWithCanvas()*(handle+new Vector2(12,-20)),Relative=new Vector2(12,-20)});
            _animationGraph._Input(new InputEventMouseMotion{Position=_animationGraph.GetGlobalTransformWithCanvas()*(handle+new Vector2(16,-23)),Relative=new Vector2(4,-3)});
            _animationGraph._Input(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false});
            Check(Math.Abs(positionY.Keys[0].OutSlope-oldSlope)>.00001&&Math.Abs(PropertyCurveRuntime.Evaluate(positionY,1,0)-oldSample)>.00001&&_undo.Count==undoCount+1,"a real graph handle gesture changes evaluated animation with exactly one Undo snapshot");

            var camera=_cameras[0];EnsureCameraAnimation(camera);camera.PropertyAnimation.Channels.Remove("Lens");camera.LensKeys=new(){new LensKeyData{Time=0,Value=25,Interpolation=new(){Mode="Hold"}},new LensKeyData{Time=4,Value=85}};
            Check(Math.Abs(EvaluateLens(camera,3)-25)<.001&&Math.Abs(EvaluateLens(camera,4)-85)<.001,"camera focal length keys share the same Hold behavior");
            foreach(string axis in new[]{"X","Y","Z"})camera.PropertyAnimation.Channels.Remove("Transform.Position."+axis);
            camera.Animation.Position=new();var cameraStart=TransformLayerRuntime.Upsert(camera.Animation.Position,0,new Vector3(0,2,0));TransformLayerRuntime.Upsert(camera.Animation.Position,4,new Vector3(8,2,0));cameraStart.Interpolation=new(){Mode="Ease in/out"};EvaluateCamera(camera,1);
            Check(camera.Rig.Position.X<2,"camera transform keys share easing with scene objects");
            var bones=new List<PoseCorrectionKeyData>();var bone=PoseCorrectionRuntime.UpsertRotation(bones,0,Quaternion.Identity);bone.Interpolation=new(){Mode="Hold"};var turn=new Quaternion(Vector3.Up,Mathf.Pi/2);PoseCorrectionRuntime.UpsertRotation(bones,4,turn);
            Check(PoseCorrectionRuntime.EvaluateRotation(bones,3).IsEqualApprox(Quaternion.Identity)&&Math.Abs(PoseCorrectionRuntime.EvaluateRotation(bones,4).Dot(turn))>.99999,"bone pose correction keys share Hold without changing the underlying motion layer");

            string directory=ProjectSettings.GlobalizePath("res://output/object-motion-checks");Directory.CreateDirectory(directory);string file=Path.Combine(directory,"keyed-car.goprevis");SaveTo(file);var saved=JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(file))!;
            Check(saved.Version==ProjectData.CurrentVersion&&saved.Objects.Single().Path.PointInterpolation[0].Mode=="Ease in/out","current format stores path and transform interpolation without external assets");
            var expectedSlope=positionY.Keys[0].OutSlope;var expectedWeight=positionY.Keys[0].OutWeight;LoadProjectFromPath(file);car=_entries.Single(e=>e.Data.Id==carId);positionY=car.Data.PropertyAnimation.Channels["Transform.Position.Y"];
            Check(Math.Abs(positionY.Keys[0].OutSlope-expectedSlope)<.00001&&Math.Abs(positionY.Keys[0].OutWeight-expectedWeight)<.00001&&_cameras[0].LensKeys[0].Interpolation.Mode=="Hold","custom value-curve handles and legacy camera timing survive portable project reopen");
            Select(car);yBinding=AnimationBindings().Single(b=>b.TargetId==carId&&b.Path=="Transform.Position.Y");_animationSelectedChannels.Clear();_animationSelectedChannels.Add(yBinding.Identity);SetAnimationWorkspaceMode("Graph");
            Check(_animationGraphPanel!.Visible&&_animationGraph!.Channels.Any(b=>b.Identity==yBinding.Identity)&&positionY.Keys[0].Interpolation=="Bezier","reopened scalar keys remain editable in the real graph editor");
            if(DisplayServer.GetName()!="headless")
            {
                FrameSelected();for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                string image=ProjectSettings.GlobalizePath("res://output/object-motion-ui.png");GetViewport().GetTexture().GetImage().SavePng(image);
            }
            passed=true;GD.Print("OBJECT_MOTION_OK checks="+checks);return checks;
        }
        catch(Exception ex){failure=ex.ToString();throw;}
        finally
        {
            EndNumericGesture();CancelDrawingPath();_interpolationSelection=null;string report=ProjectSettings.GlobalizePath("res://output/object-motion-report.json");Directory.CreateDirectory(Path.GetDirectoryName(report)!);File.WriteAllText(report,JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,failure,observations},new JsonSerializerOptions{WriteIndented=true}));
            Restore(original,"");_projectPath=originalPath;SetTime(originalTime);_editorCamera.Transform=editor;_autoKey=auto;_undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);_animationSearch!.Text=animationSearch;SetAnimationWorkspaceMode(workspace);
        }
    }
}
