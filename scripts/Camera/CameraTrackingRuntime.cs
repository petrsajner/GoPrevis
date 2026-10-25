using Godot;
using System;

namespace Previs;

public readonly record struct TrackingTargetPose(Vector3 Position,Basis Basis);

/// <summary>Causal absolute-time filtering. No state from prior playback or export frames is read.</summary>
public static class CameraTrackingRuntime
{
    const int Samples=24;
    public static TrackingTargetPose Filter(Func<double,TrackingTargetPose> sample,double time,double seconds)
    {
        time=double.IsFinite(time)?Math.Max(0,time):0;
        if(!double.IsFinite(seconds)||seconds<=.000001)return sample(time);
        double window=seconds*6,step=window/Samples;
        Vector3 position=Vector3.Zero,x=Vector3.Zero,y=Vector3.Zero,z=Vector3.Zero;
        void Add(TrackingTargetPose pose,float weight){position+=pose.Position*weight;x+=pose.Basis.X*weight;y+=pose.Basis.Y*weight;z+=pose.Basis.Z*weight;}
        for(int i=0;i<Samples;i++)
        {
            double from=i*step,to=(i+1)*step;
            float weight=(float)(Math.Exp(-from/seconds)-Math.Exp(-to/seconds));
            Add(sample(Math.Max(0,time-(from+to)*.5)),weight);
        }
        Add(sample(Math.Max(0,time-window)),(float)Math.Exp(-6));
        return new(position,new Basis(x,y,z));
    }
    public static Vector3 Follow(Func<double,TrackingTargetPose> sample,double time,CameraTrackingData data)
    {
        var pose=Filter(sample,time,data.FollowSmoothness);var offset=Read(data.FollowOffset);
        return pose.Position+(data.TargetRelativeOffsets?pose.Basis*offset:offset);
    }
    public static Quaternion LookRotation(Vector3 position,Vector3 aim,Quaternion fallback)
    {
        var direction=aim-position;if(!direction.IsFinite()||direction.LengthSquared()<.00000001f)return fallback;
        var up=Math.Abs(direction.Normalized().Dot(Vector3.Up))>.999f?Vector3.Back:Vector3.Up;
        return Basis.LookingAt(direction.Normalized(),up).GetRotationQuaternion().Normalized();
    }
    public static Quaternion FilterRotation(Func<double,Quaternion> sample,double time,double seconds)
    {
        time=double.IsFinite(time)?Math.Max(0,time):0;
        if(seconds<=.000001||!double.IsFinite(seconds))return sample(time);
        var reference=sample(time);Vector4 sum=Vector4.Zero;
        void Add(Quaternion rotation,float weight)
        {if(rotation.Dot(reference)<0)rotation=new Quaternion(-rotation.X,-rotation.Y,-rotation.Z,-rotation.W);sum+=new Vector4(rotation.X,rotation.Y,rotation.Z,rotation.W)*weight;}
        double step=seconds*6/Samples;
        for(int i=0;i<Samples;i++)
        {double from=i*step,to=(i+1)*step;Add(sample(Math.Max(0,time-(from+to)*.5)),(float)(Math.Exp(-from/seconds)-Math.Exp(-to/seconds)));}
        Add(sample(Math.Max(0,time-seconds*6)),(float)Math.Exp(-6));
        var result=new Quaternion(sum.X,sum.Y,sum.Z,sum.W);return result.LengthSquared()<.000001f?reference:result.Normalized();
    }
    public static Vector3 Read(float[] value)=>new(value[0],value[1],value[2]);
}
