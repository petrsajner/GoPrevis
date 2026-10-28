using Godot;
using System;
using System.Linq;

namespace GoPrevis;

public partial class GoPrevisApp
{
    void RunExtendedPropertyChecks(Action<bool,string> check)
    {
        var original=Snapshot();string project=_projectPath;double time=_time;string? objectId=_selected?.Data.Id,cameraId=_selectedCamera?.Id;
        var undo=_undo.ToArray();var redo=_redo.ToArray();
        try
        {
            var car=AddObject("car",Vector3.Zero,"Progress curve car");
            car.Data.Path=new(){Enabled=true,Duration=4,Points=new(){new float[]{0,0,0},new float[]{4,0,0},new float[]{8,0,0}},PointTimes=new(){0,2,4}};
            var progress=AnimationBindings(car.Data.Id).Single(b=>b.Path=="Path.Progress").EnsureCurve();
            var first=PropertyCurveRuntime.Upsert(progress,0,0);PropertyCurveRuntime.Upsert(progress,2,.25);PropertyCurveRuntime.Upsert(progress,4,1);SetTime(2);
            check(Math.Abs(car.Node.Position.X-2)<.001,"a path progress curve drives the actual car along the route rather than only displaying a graph");
            first.Interpolation="Hold";SetTime(1.9);check(car.Node.Position.Length()<.001,"Hold on path progress keeps the real car stationary until the next key");
            progress.Keys.Clear();SetTime(2);check(Math.Abs(car.Node.Position.X-4)<.001,"removing progress keys restores the authored waypoint timing");
            var duration=AnimationBindings(car.Data.Id).Single(b=>b.Path=="Path.Duration").EnsureCurve();PropertyCurveRuntime.Upsert(duration,0,4);PropertyCurveRuntime.Upsert(duration,4,8);SetTime(2);
            check(Math.Abs(car.Node.Position.X-8.0/3)<.003&&car.Data.Path.Duration==4,"animated travel duration rescales real arrival times without baking into the base path");
            var camera=_cameras.First();camera.Movement=new(){Mode="Orbit",Duration=4,Radius=8,OrbitDegrees=90,StartHeight=2,EndHeight=2};camera.Tracking=new();camera.PropertyAnimation=new();
            var radius=AnimationBindings(camera.Id).Single(b=>b.Path=="Movement.Radius").EnsureCurve();PropertyCurveRuntime.Upsert(radius,0,8);PropertyCurveRuntime.Upsert(radius,4,12);SetTime(2);SelectCamera(camera);
            var desired=camera.Rig.GlobalPosition+Vector3.Right;PushUndo();ApplySelectedManualTransform("Position",desired);
            check(camera.Rig.GlobalPosition.DistanceTo(desired)<.001,"viewport camera editing uses the currently animated orbit radius when solving its offset");
            var free=CreateCamera("Scalar camera view",null,true);var position=AnimationBindings(free.Id).Single(b=>b.Path=="Transform.Position.X").EnsureCurve();
            PropertyCurveRuntime.Upsert(position,0,free.BasePosition.X);PropertyCurveRuntime.Upsert(position,4,free.BasePosition.X+4);SetTime(2);
            var pilotPosition=free.Rig.GlobalPosition+Vector3.Right;free.Rig.GlobalPosition=pilotPosition;StorePilotedCameraPose();SetTime(2);
            check(free.Rig.GlobalPosition.DistanceTo(pilotPosition)<.001,"camera piloting updates an existing split position channel rather than a masked legacy key");
            var viewPosition=pilotPosition+Vector3.Up;_editorCamera.GlobalPosition=viewPosition;UseCurrentView();SetTime(2);
            check(free.Rig.GlobalPosition.DistanceTo(viewPosition)<.001,"Current view framing writes through the same independent camera channels");
            var studio=AnimationBindings("scene").Single(b=>b.Path=="Scene.StudioKeyEnabled").EnsureCurve();PropertyCurveRuntime.Upsert(studio,0,0);PropertyCurveRuntime.Upsert(studio,2,1);SetTime(1);
            check(!_lighting.DirectionalEnabled,"the studio light switch can be animated as a held discrete property");
            bool source=Snapshot().DirectionalLightEnabled;SetTime(2);check(_lighting.DirectionalEnabled&&Snapshot().DirectionalLightEnabled==source,"animated studio lighting reaches the scene while keeping its saved base state stable");
            var playback=new PropertyAnimationData();playback.Channels["Speed"]=new();PropertyCurveRuntime.Upsert(playback.Channels["Speed"],0,0);PropertyCurveRuntime.Upsert(playback.Channels["Speed"],2,0);
            check(MotionClipClock.SourceTime(playback,2,0,4,.5,1)==.5,"zero keyed mocap rate freezes source time exactly");
        }
        finally
        {
            Restore(original,"");_projectPath=project;SetTime(time);RestoreInteractionSelection(objectId,cameraId);
            _undo.Clear();_redo.Clear();foreach(var state in undo.Reverse())_undo.Push(state);foreach(var state in redo.Reverse())_redo.Push(state);
        }
    }
}
