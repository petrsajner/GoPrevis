using Godot;
using System;
using System.Linq;

namespace GoPrevis;

/// <summary>Exact time-domain camera travel, independent of frame rate and transform-key density.</summary>
public sealed class CameraMovementData
{
    public string Mode {get;set;}="None";
    public string TargetId {get;set;}="";
    public float[] TargetPosition {get;set;}=new float[3];
    public float[] TargetOffset {get;set;}=new[]{0f,.8f,0f};
    public bool AimAtTarget {get;set;}=true;
    public double Start {get;set;}
    public double Duration {get;set;}=8;
    public float Radius {get;set;}=8;
    public float StartAngleDegrees {get;set;}
    public float OrbitDegrees {get;set;}=360;
    public float StartHeight {get;set;}=2;
    public float EndHeight {get;set;}=2;
    public string Easing {get;set;}="Linear";
    public ActorPathData Path {get;set;}=new();
    public bool AimPathEnabled {get;set;}
    public ActorPathData AimPath {get;set;}=new();
    public float[] PositionOffset {get;set;}=new float[3];
    public float[] RotationOffset {get;set;}=new float[3];

    public CameraMovementData ValidatedCopy()
    {
        static float[] Copy(float[] values,string name)
        {if(values is not{Length:3}||values.Any(v=>!float.IsFinite(v)||Math.Abs(v)>10000))throw new ArgumentException(name+" needs three finite XYZ values.");return values.ToArray();}
        if(Mode is not("None" or "Orbit" or "Path"))throw new ArgumentException("Camera movement must be None, Orbit or Path.");
        if(!double.IsFinite(Start)||Start<0||Start>600||!double.IsFinite(Duration)||Duration<=0||Duration>600)throw new ArgumentException("Camera movement start/duration must fit within 600 seconds.");
        if(!float.IsFinite(Radius)||Radius<.1f||Radius>2000||!float.IsFinite(StartAngleDegrees)||Math.Abs(StartAngleDegrees)>36000||!float.IsFinite(OrbitDegrees)||Math.Abs(OrbitDegrees)>3600||!float.IsFinite(StartHeight)||!float.IsFinite(EndHeight)||Math.Abs(StartHeight)>1000||Math.Abs(EndHeight)>1000)throw new ArgumentException("Invalid camera orbit radius, angle or height.");
        if(!KeyInterpolation.Modes.Contains(Easing))throw new ArgumentException("Unknown camera movement easing.");
        var path=System.Text.Json.JsonSerializer.Deserialize<ActorPathData>(System.Text.Json.JsonSerializer.Serialize(Path??new()))!;
        var aimPath=System.Text.Json.JsonSerializer.Deserialize<ActorPathData>(System.Text.Json.JsonSerializer.Serialize(AimPath??new()))!;
        if(Mode=="Path")
        {
            if(path.Points.Count<2||path.Points.Count>256||path.PointTimes.Count!=path.Points.Count)throw new ArgumentException("A camera path needs 2–256 points and matching arrival times.");
            double before=-1;foreach(var point in path.Points)Copy(point,"Camera path point");
            foreach(double time in path.PointTimes){if(!double.IsFinite(time)||time<0||time<=before||time>600)throw new ArgumentException("Camera path times must strictly increase.");before=time;}
            path.Enabled=true;
        }
        if(AimPathEnabled)
        {
            if(aimPath.Points.Count<2||aimPath.Points.Count!=aimPath.PointTimes.Count)throw new ArgumentException("An aim path needs matching points and arrival times.");
            foreach(var point in aimPath.Points)Copy(point,"Camera aim path");double previous=-1;foreach(double time in aimPath.PointTimes){if(!double.IsFinite(time)||time<0||time<=previous||time>600)throw new ArgumentException("Camera aim arrival times must strictly increase.");previous=time;}aimPath.Enabled=true;
        }
        return new(){Mode=Mode,TargetId=TargetId??"",TargetPosition=Copy(TargetPosition,"Camera target"),TargetOffset=Copy(TargetOffset,"Camera aim offset"),AimAtTarget=AimAtTarget,Start=Start,Duration=Duration,Radius=Radius,StartAngleDegrees=StartAngleDegrees,OrbitDegrees=OrbitDegrees,StartHeight=StartHeight,EndHeight=EndHeight,Easing=Easing,Path=path,AimPathEnabled=AimPathEnabled,AimPath=aimPath,PositionOffset=Copy(PositionOffset,"Camera position correction"),RotationOffset=Copy(RotationOffset,"Camera rotation correction")};
    }
}

public static class CameraMovementRuntime
{
    public static Vector3 Position(CameraMovementData data,double time,Vector3 center)
    {
        Vector3 result;
        if(data.Mode=="Orbit")
        {
            double t=KeyInterpolation.Weight(new(){Mode=data.Easing},(time-data.Start)/data.Duration);
            double angle=(data.StartAngleDegrees+data.OrbitDegrees*t)*Math.PI/180;
            result=center+new Vector3((float)Math.Sin(angle)*data.Radius,(float)(data.StartHeight+(data.EndHeight-data.StartHeight)*t),(float)Math.Cos(angle)*data.Radius);
        }
        else if(data.Mode=="Path")result=ActorPathRuntime.Evaluate(data.Path,time).Position;
        else throw new ArgumentException("No procedural camera movement is active.");
        return result+Vector(data.PositionOffset);
    }
    public static Quaternion Aim(CameraMovementData data,Vector3 position,Vector3 center,Quaternion fallback)
    {
        var direction=center-position;Quaternion rotation=fallback;
        if(data.AimAtTarget&&direction.LengthSquared()>.000001f)
            rotation=Basis.LookingAt(direction.Normalized(),Math.Abs(direction.Normalized().Dot(Vector3.Up))>.999f?Vector3.Back:Vector3.Up).GetRotationQuaternion();
        return(rotation*Quaternion.FromEuler(Vector(data.RotationOffset)*Mathf.Pi/180)).Normalized();
    }
    static Vector3 Vector(float[] value)=>new(value[0],value[1],value[2]);
}
