using System;

namespace Previs;

/// <summary>Live object constraints. Existing absolute camera keys are retained beneath controlled channels.</summary>
public sealed class CameraTrackingData
{
    public bool FollowEnabled { get; set; }
    public string FollowTargetId { get; set; } = "";
    public float[] FollowOffset { get; set; } = new[]{0f,3f,6f};
    public double FollowSmoothness { get; set; }
    public bool LookAtEnabled { get; set; }
    public string LookAtTargetId { get; set; } = "";
    public float[] LookAtOffset { get; set; } = new[]{0f,.8f,0f};
    public double LookAtSmoothness { get; set; }
    public bool TargetRelativeOffsets { get; set; } = true;
    public float[] RotationOffset { get; set; } = new float[3];

    public CameraTrackingData ValidatedCopy()
    {
        static float[] Vector(float[]? value,string name)
        {
            if(value is not {Length:3}||!float.IsFinite(value[0])||!float.IsFinite(value[1])||!float.IsFinite(value[2])||Math.Abs(value[0])>10000||Math.Abs(value[1])>10000||Math.Abs(value[2])>10000)
                throw new ArgumentException(name+" must contain three finite values within 10000 metres/degrees.");
            return (float[])value.Clone();
        }
        if(!double.IsFinite(FollowSmoothness)||FollowSmoothness<0||FollowSmoothness>5||!double.IsFinite(LookAtSmoothness)||LookAtSmoothness<0||LookAtSmoothness>5)
            throw new ArgumentException("Camera smoothness must be between zero and five seconds.");
        return new CameraTrackingData{FollowEnabled=FollowEnabled,FollowTargetId=FollowTargetId??"",FollowOffset=Vector(FollowOffset,"Follow offset"),FollowSmoothness=FollowSmoothness,
            LookAtEnabled=LookAtEnabled,LookAtTargetId=LookAtTargetId??"",LookAtOffset=Vector(LookAtOffset,"Look at offset"),LookAtSmoothness=LookAtSmoothness,
            TargetRelativeOffsets=TargetRelativeOffsets,RotationOffset=Vector(RotationOffset,"Camera angle correction")};
    }
}
