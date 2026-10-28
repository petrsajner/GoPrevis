using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    async Task RunSelectedKeyDragChecks(Action<bool,string> check) { _ = await RunKeyGroupDragChecks(check); }
    async Task<int> RunKeyGroupDragChecks(Action<bool,string>? report=null)
    {
        int checks=0;void Check(bool yes,string text){if(report!=null)report(yes,"Key group drag: "+text);else Require(yes,"Key group drag: "+text);checks++;}
        var original=Snapshot();string path=_projectPath;double at=_time;bool autoKey=_autoKey;var undo=_undo.ToArray();var redo=_redo.ToArray();
        var editor=_editorCamera.GlobalTransform;var orbitTarget=_orbitTarget;float yaw=_yaw,pitch=_pitch,distance=_distance;
        string mode=_animationWorkspaceMode;var graph=new PropertyGraphEditor{Position=new Vector2(20,80),Size=new Vector2(800,360),Duration=4,Fps=24};AddChild(graph);
        try
        {
            var curveA=new ScalarAnimationCurve();var curveB=new ScalarAnimationCurve();
            var a=PropertyCurveRuntime.Upsert(curveA,1,1);var fixedA=PropertyCurveRuntime.Upsert(curveA,2,2);
            var b=PropertyCurveRuntime.Upsert(curveB,1.5,4);var fixedB=PropertyCurveRuntime.Upsert(curveB,3.5,6);
            int materialized=0;
            var bindA=new AnimationChannelBinding{TargetId="testA",Path="X",Label="X",Curve=()=>curveA,EnsureCurve=()=>{materialized++;return curveA;},Read=()=>1};
            var bindB=new AnimationChannelBinding{TargetId="testB",Path="Y",Label="Y",Curve=()=>curveB,EnsureCurve=()=>{materialized++;return curveB;},Read=()=>4};
            int gestures=0;graph.Channels=new(){bindA,bindB};graph.BeginEdit=()=>gestures++;graph.FitCurves();
            void Press(Vector2 point,bool ctrl=false)=>graph._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=point,CtrlPressed=ctrl});
            void Release(Vector2 point)=>graph._Input(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=graph.GetGlobalTransformWithCanvas()*point});
            var pointA=graph.KeyScreenPoint(bindA.Identity,a.Id);var pointB=graph.KeyScreenPoint(bindB.Identity,b.Id);
            Press(pointA);Release(pointA);Press(pointB,true);Release(pointB);
            Check(graph.SelectedKeyCount==2,"Ctrl selects graph keys on two different property layers");
            Press(pointA,true);Release(pointA);
            Check(graph.SelectedKeyCount==1&&gestures==0,"Ctrl toggles one selected graph key without clearing the other selected layer");
            Press(pointA,true);Release(pointA);
            Press(pointA);Check(graph.SelectedKeyCount==2&&gestures==0,"pressing an already selected graph key preserves the group and creates no Undo yet");
            graph._Input(new InputEventMouseMotion{Position=graph.GetGlobalTransformWithCanvas()*(pointA+Vector2.Right*3)});Release(pointA+Vector2.Right*3);
            Check(gestures==0&&materialized==0,"a graph drag below the snapped frame threshold creates no Undo or materialized curve");
            Press(pointA);
            float dx=graph.KeyScreenPoint(bindA.Identity,fixedA.Id).X-pointA.X;
            graph._Input(new InputEventMouseMotion{Position=graph.GetGlobalTransformWithCanvas()*(pointA+Vector2.Right*dx)});Release(pointA+Vector2.Right*dx);
            double delta=a.Time-1;
            Check(delta>0&&Math.Abs((b.Time-1.5)-delta)<1e-9,"graph drag applies one time delta to every selected layer");
            Check(curveA.Keys.Contains(fixedA)&&fixedA.Time==2&&curveB.Keys.Contains(fixedB)&&fixedB.Time==3.5,"retiming against an occupied frame preserves unselected keys");
            Check(graph.SelectedKeyCount==2&&gestures==1,"the selected graph group remains selected after one Undo gesture");
            graph.SetSelectedTime(10);
            Check(Math.Abs(b.Time-a.Time-.5)<1e-9&&Math.Abs(b.Time-4)<1e-9,"retiming to the graph boundary preserves relative spacing");
            var limitPoint=graph.KeyScreenPoint(bindA.Identity,a.Id);int editsAtLimit=gestures,curvesAtLimit=materialized;
            Press(limitPoint);graph._Input(new InputEventMouseMotion{Position=graph.GetGlobalTransformWithCanvas()*(limitPoint+Vector2.Right*30)});Release(limitPoint+Vector2.Right*30);
            Check(gestures==editsAtLimit&&materialized==curvesAtLimit,"a graph group blocked at the shot boundary creates no Undo or materialized curve");
            bindA.Minimum=bindB.Minimum=0;bindA.Maximum=a.Value;bindB.Maximum=b.Value;
            Press(limitPoint);graph._Input(new InputEventMouseMotion{Position=graph.GetGlobalTransformWithCanvas()*(limitPoint+Vector2.Up*20)});Release(limitPoint+Vector2.Up*20);
            Check(gestures==editsAtLimit&&materialized==curvesAtLimit,"graph values clamped at their parameter limits create no Undo or materialized curve");
            var offGrid=new[]{new KeyGroupPoint("a","a",.03),new KeyGroupPoint("b","b",.89)};
            var bounded=GroupKeyRetiming.ResolveDelta(offGrid,offGrid,.5,1,24);
            Check(bounded<=.11+1e-9&&Math.Abs(bounded*24-Math.Round(bounded*24))<1e-9,"frame snapping never exceeds a group boundary or independently clamps its members");

            ClearObjects();_timeline.FitMotionSourceRange();_duration=6;_fps=24;_scrub.MaxValue=6;SetAnimationWorkspaceMode("Clips");
            var first=AddObject("box",Vector3.Zero,"Group object A");var second=AddObject("box",Vector3.Right,"Group object B");
            var keyA=TransformLayerRuntime.Upsert(first.Data.TransformAnimation.Position,1,Vector3.Zero);TransformLayerRuntime.Upsert(first.Data.TransformAnimation.Position,4,Vector3.Right);
            var sibling=TransformLayerRuntime.Upsert(first.Data.TransformAnimation.Rotation,1,new Vector3(0,45,0));
            var keyB=TransformLayerRuntime.Upsert(second.Data.TransformAnimation.Rotation,2,Vector3.Zero);TransformLayerRuntime.Upsert(second.Data.TransformAnimation.Rotation,5,new Vector3(0,90,0));
            var camera=_cameras.First();camera.Movement=new();camera.Tracking=new();camera.Animation=new();camera.LensKeys.Clear();camera.Rig.ClearKeys();camera.AnimationInitialized=true;
            var cameraKey=TransformLayerRuntime.Upsert(camera.Animation.Position,3,camera.BasePosition);
            var routed=AddObject("car",Vector3.Zero,"Group path");routed.Data.Path=new(){Enabled=true,Duration=6,Points=new(){new float[]{0,0,0},new float[]{2,0,0},new float[]{4,0,0}},PointTimes=new(){0,3,6}};
            var posed=AddObject("actor",Vector3.Zero,"Group pose");var poseKey=new PoseCorrectionKeyData{Time=2.5};posed.Data.PoseCorrections.Bones["Head"]=new(){poseKey};
            Select(first);SetTime(1);_timeline.ExpandObject(first.Data.Id);_timeline.ExpandObject(second.Data.Id);_timeline.ExpandPoseCorrections(posed.Data.Id);
            for(int i=0;i<4;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            void Hit(Vector2 point,bool ctrl=false)
            {_timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=point,CtrlPressed=ctrl});_timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=point});}
            var rowA=_timeline.KeyRowRect("Transform",first.Data.Id,"Position");
            var pa=new Vector2(rowA.Position.X+rowA.Size.X/6,rowA.Position.Y+11);
            var rowB=_timeline.KeyRowRect("Transform",second.Data.Id,"Rotation");var pb=new Vector2(rowB.Position.X+rowB.Size.X/3,rowB.Position.Y+11);
            var rowC=_timeline.KeyRowRect("Transform",camera.Id,"All");var pc=new Vector2(rowC.Position.X+rowC.Size.X/2,rowC.Position.Y+11);
            var rowPath=_timeline.KeyRowRect("Path",routed.Data.Id,"All");var pp=new Vector2(rowPath.Position.X+rowPath.Size.X/2,rowPath.Position.Y+11);
            var rowPose=_timeline.KeyRowRect("Pose",posed.Data.Id,"Head");var posePoint=new Vector2(rowPose.Position.X+rowPose.Size.X*(2.5f/6),rowPose.Position.Y+11);
            Hit(pa);Hit(pb,true);Hit(pc,true);Hit(pp,true);Hit(posePoint,true);
            Check(_timeline.SelectedKeyCount==5,"the Clips timeline can select grouped keys across objects, cameras, path points and pose joints");
            int history=_undo.Count;
            var summary=_timeline.KeyRowRect("Transform",first.Data.Id,"All");var summaryPoint=new Vector2(pa.X,summary.Position.Y+11);
            _timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=summaryPoint});
            Check(_timeline.SelectedKeyCount==5,"pressing a partially selected legacy summary preserves the exact selected group");
            var shifted=summaryPoint+Vector2.Right*(rowA.Size.X/12);
            _timeline._GuiInput(new InputEventMouseMotion{Position=shifted});
            _timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=shifted});
            Check(Math.Abs(keyA.Time-1.5)<1e-6&&Math.Abs(keyB.Time-2.5)<1e-6&&Math.Abs(cameraKey.Time-3.5)<1e-6&&Math.Abs(routed.Data.Path.PointTimes[1]-3.5)<1e-6&&Math.Abs(poseKey.Time-3)<1e-6,"one legacy timeline drag retimes all selected object, camera, path and pose keys together");
            Check(Math.Abs(sibling.Time-1)<1e-9,"dragging a partially selected summary leaves its unselected sibling key unchanged");
            Check(_undo.Count==history+1&&_timeline.SelectedKeyCount==5,"legacy group dragging is one Undo action and retains selection on release");
            string firstId=first.Data.Id;Undo();first=_entries.Single(e=>e.Data.Id==firstId);
            Check(first.Data.TransformAnimation.Position.Any(k=>k.Id==keyA.Id&&Math.Abs(k.Time-1)<1e-9),"Undo restores the original grouped key times and identities");
            routed=_entries.Single(e=>e.Data.Id==routed.Data.Id);routed.Data.Path.Start=1;routed.Data.Path.Duration=4;routed.Data.Path.PointTimes=new(){0,2,4};SetTime(1);
            rowPath=_timeline.KeyRowRect("Path",routed.Data.Id,"All");var firstPath=new Vector2(rowPath.Position.X+rowPath.Size.X/6,rowPath.Position.Y+11);
            rowA=_timeline.KeyRowRect("Transform",first.Data.Id,"Position");pa=new Vector2(rowA.Position.X+rowA.Size.X/6,rowA.Position.Y+11);
            Hit(firstPath);Hit(pa,true);history=_undo.Count;
            _timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=pa});var blocked=pa-Vector2.Right*(rowA.Size.X/6);
            _timeline._GuiInput(new InputEventMouseMotion{Position=blocked});_timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=blocked});
            Check(routed.Data.Path.PointTimes.SequenceEqual(new double[]{0,2,4})&&first.Data.TransformAnimation.Position.Any(k=>k.Id==keyA.Id&&Math.Abs(k.Time-1)<1e-9)&&_undo.Count==history,"a path starting later blocks its selected group before relative waypoint time becomes negative without creating Undo");
            _timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=pa});var forward=pa+Vector2.Right*(rowA.Size.X/12);
            _timeline._GuiInput(new InputEventMouseMotion{Position=forward});_timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=forward});
            Check(Math.Abs(routed.Data.Path.PointTimes[0]-.5)<1e-9&&ActorPathRuntime.PointTimes(routed.Data.Path).SequenceEqual(routed.Data.Path.PointTimes)&&_undo.Count==history+1,"the first waypoint can still move later with its selected group using valid relative timings and one Undo");
            _autoKey=true;rowPath=_timeline.KeyRowRect("Path",routed.Data.Id,"All");var lastPath=new Vector2(rowPath.Position.X+rowPath.Size.X*(5f/6),rowPath.Position.Y+11);
            pa=new Vector2(rowA.Position.X+rowA.Size.X*(1.5f/6),rowA.Position.Y+11);Hit(lastPath);Hit(pa,true);history=_undo.Count;
            int objectCurves=first.Data.PropertyAnimation.Channels.Count,pathCurves=routed.Data.PropertyAnimation.Channels.Count;
            _timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=lastPath});var lastForward=lastPath+Vector2.Right*(rowPath.Size.X/12);
            _timeline._GuiInput(new InputEventMouseMotion{Position=lastForward});_timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=lastForward});
            Check(Math.Abs(routed.Data.Path.PointTimes[^1]-4.5)<1e-9&&Math.Abs(routed.Data.Path.Duration-4.5)<1e-9&&first.Data.TransformAnimation.Position.Any(k=>k.Id==keyA.Id&&Math.Abs(k.Time-2)<1e-9)&&routed.Data.PropertyAnimation.Channels.Count==pathCurves&&first.Data.PropertyAnimation.Channels.Count==objectCurves&&!routed.Data.PropertyAnimation.Channels.ContainsKey("Path.Duration")&&_undo.Count==history+1,"retiming the last waypoint and another object's key with Auto key changes path duration without creating parameter curves or extra Undo");
            return checks;
        }
        finally
        {
            _autoKey=autoKey;graph.QueueFree();Restore(original,"");_projectPath=path;SetTime(at);SetAnimationWorkspaceMode(mode);_editorCamera.GlobalTransform=editor;_orbitTarget=orbitTarget;_yaw=yaw;_pitch=pitch;_distance=distance;
            _undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);
        }
    }
}
