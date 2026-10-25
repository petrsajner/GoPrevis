using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Previs;

/// <summary>One additive local-bone rotation. Identity leaves the source pose unchanged.</summary>
public sealed class PoseCorrectionKeyData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public double Time { get; set; }
    public KeyInterpolationData Interpolation { get; set; } = new();
    public float[] Rotation { get; set; } = new float[] { 0,0,0,1 };
}

/// <summary>A separate, source-independent correction layer above blended skeletal motion.</summary>
public sealed class PoseCorrectionAnimationData
{
    public Dictionary<string,List<PoseCorrectionKeyData>> Bones { get; set; } = new(StringComparer.Ordinal);
    public List<TransformKeyData> Root { get; set; } = new();
}

public static class PoseCorrectionRuntime
{
    public const double TimeTolerance=.000001;

    public static bool HasKeys(PoseCorrectionAnimationData? data) => data!=null&&
        ((data.Root?.Any(ValidRoot)??false)||(data.Bones?.Values.Any(keys=>keys?.Any(ValidRotation)==true)??false));

    /// <summary>Shortest-arc local quaternion interpolation, holding the first and last valid values.</summary>
    public static Quaternion EvaluateRotation(IReadOnlyList<PoseCorrectionKeyData>? keys,double time)
    {
        if(keys==null||keys.Count==0)return Quaternion.Identity;
        if(double.IsNaN(time))time=0;
        PoseCorrectionKeyData? before=null,after=null;
        foreach(var key in keys)
        {
            if(!ValidRotation(key))continue;
            if(key.Time<=time){if(before==null||key.Time>=before.Time)before=key;}
            else if(after==null||key.Time<=after.Time)after=key;
        }
        if(before==null)return after==null?Quaternion.Identity:ReadRotation(after.Rotation);
        if(after==null)return ReadRotation(before.Rotation);
        var from=ReadRotation(before.Rotation);var to=ReadRotation(after.Rotation);
        if(from.Dot(to)<0)to=new Quaternion(-to.X,-to.Y,-to.Z,-to.W);
        float weight=KeyInterpolation.Weight(before.Interpolation,(time-before.Time)/(after.Time-before.Time));
        return from.Slerp(to,weight).Normalized();
    }

    public static Vector3 EvaluateRoot(PoseCorrectionAnimationData? data,double time)
        =>TransformLayerRuntime.EvaluateVector(data?.Root,time,Vector3.Zero);

    public static PoseCorrectionKeyData UpsertRotation(List<PoseCorrectionKeyData> keys,double time,Quaternion rotation,double tolerance=TimeTolerance)
    {
        ArgumentNullException.ThrowIfNull(keys);ValidateTime(time);
        if(!Finite(rotation)||rotation.LengthSquared()<.000001f)throw new FormatException("A pose key needs a finite, nonzero quaternion.");
        rotation=rotation.Normalized();if(!double.IsFinite(tolerance)||tolerance<0)tolerance=TimeTolerance;
        PoseCorrectionKeyData? key=keys.Where(k=>k!=null&&double.IsFinite(k.Time)&&Math.Abs(k.Time-time)<=tolerance).OrderBy(k=>Math.Abs(k.Time-time)).FirstOrDefault();
        if(key==null){key=new PoseCorrectionKeyData();keys.Add(key);}
        key.Time=time;key.Rotation=Values(rotation);keys.Sort((a,b)=>a.Time.CompareTo(b.Time));return key;
    }

    public static IReadOnlyList<string> ChangedBones(StaticPoseData before,StaticPoseData after,float tolerance=.000001f)
        =>before.BoneRotations.Keys.Concat(after.BoneRotations.Keys).Distinct(StringComparer.Ordinal)
            .Where(name=>RotationDifferenceSquared(ReadBone(before,name),ReadBone(after,name))>tolerance*tolerance).ToArray();

    public static bool RootChanged(StaticPoseData before,StaticPoseData after,float tolerance=.000001f)
        =>RootValue(before).DistanceSquaredTo(RootValue(after))>tolerance*tolerance;

    /// <summary>
    /// Converts an edited displayed pose into deltas against the current uncorrected source.
    /// Pass the union of joints changed by this gesture so unrelated existing channels are not stamped.
    /// The first edit after zero gets an identity key at zero for a smooth, initially unchanged shot.
    /// </summary>
    public static void WriteDifferences(PoseCorrectionAnimationData data,StaticPoseData basePose,StaticPoseData editedPose,double time,
        IEnumerable<string>? affectedBones=null,bool includeRoot=true,bool addIdentityAtZero=true)
    {
        ArgumentNullException.ThrowIfNull(data);ArgumentNullException.ThrowIfNull(basePose);ArgumentNullException.ThrowIfNull(editedPose);ValidateTime(time);
        data.Bones??=new(StringComparer.Ordinal);data.Root??=new();
        foreach(string name in (affectedBones??basePose.BoneRotations.Keys.Concat(editedPose.BoneRotations.Keys)).Distinct(StringComparer.Ordinal))
        {
            if(!basePose.BoneRotations.ContainsKey(name)||!editedPose.BoneRotations.ContainsKey(name))continue;
            Quaternion delta=(ReadBone(basePose,name).Inverse()*ReadBone(editedPose,name)).Normalized();
            if(!data.Bones.TryGetValue(name,out var keys)||keys==null)data.Bones[name]=keys=new();
            if(addIdentityAtZero&&time>TimeTolerance&&!keys.Any(ValidRotation))UpsertRotation(keys,0,Quaternion.Identity);
            UpsertRotation(keys,time,delta);
        }
        if(includeRoot)
        {
            var delta=RootValue(editedPose)-RootValue(basePose);
            if(delta.LengthSquared()>.000000000001f||data.Root.Any(ValidRoot))
            {
                if(addIdentityAtZero&&time>TimeTolerance&&!data.Root.Any(ValidRoot))TransformLayerRuntime.Upsert(data.Root,0,Vector3.Zero);
                TransformLayerRuntime.Upsert(data.Root,time,delta);
            }
        }
    }

    public static double[] KeyTimes(PoseCorrectionAnimationData? data,string? bone=null)
    {
        if(data==null)return Array.Empty<double>();
        if(bone is "All" or "")bone=null;
        IEnumerable<double> values;
        if(bone=="Root")values=data.Root?.Where(ValidRoot).Select(k=>k.Time)??Array.Empty<double>();
        else if(bone!=null)values=data.Bones!=null&&data.Bones.TryGetValue(bone,out var keys)&&keys!=null?keys.Where(ValidRotation).Select(k=>k.Time):Array.Empty<double>();
        else values=(data.Bones?.Values.SelectMany(keys=>keys??new List<PoseCorrectionKeyData>()).Where(ValidRotation).Select(k=>k.Time)??Array.Empty<double>()).Concat(data.Root?.Where(ValidRoot).Select(k=>k.Time)??Array.Empty<double>());
        return values.Distinct().OrderBy(t=>t).ToArray();
    }
    public static Quaternion ReadBone(StaticPoseData pose,string bone)=>pose.BoneRotations.TryGetValue(bone,out var values)?ReadRotation(values):Quaternion.Identity;
    public static Quaternion ReadRotation(float[]? values)
    {
        if(values==null||values.Length!=4||values.Any(v=>!float.IsFinite(v)))return Quaternion.Identity;
        var q=new Quaternion(values[0],values[1],values[2],values[3]);return Finite(q)&&q.LengthSquared()>.000001f?q.Normalized():Quaternion.Identity;
    }
    public static Vector3 RootValue(StaticPoseData pose)=>pose.RootPosition?.Length==3?new Vector3(pose.RootPosition[0],pose.RootPosition[1],pose.RootPosition[2]):Vector3.Zero;
    public static float[] Values(Quaternion rotation)=>new[]{rotation.X,rotation.Y,rotation.Z,rotation.W};
    public static float RotationDifferenceSquared(Quaternion first,Quaternion second)
    {
        if(first.Dot(second)<0)second=new Quaternion(-second.X,-second.Y,-second.Z,-second.W);
        float x=first.X-second.X,y=first.Y-second.Y,z=first.Z-second.Z,w=first.W-second.W;return x*x+y*y+z*z+w*w;
    }
    public static bool ValidRotation(PoseCorrectionKeyData? key)=>key!=null&&double.IsFinite(key.Time)&&key.Time>=0&&key.Rotation?.Length==4&&key.Rotation.All(float.IsFinite)&&Finite(new Quaternion(key.Rotation[0],key.Rotation[1],key.Rotation[2],key.Rotation[3]))&&new Quaternion(key.Rotation[0],key.Rotation[1],key.Rotation[2],key.Rotation[3]).LengthSquared()>.000001f;
    static bool ValidRoot(TransformKeyData? key)=>key!=null&&double.IsFinite(key.Time)&&key.Time>=0&&key.Value?.Length==3&&key.Value.All(float.IsFinite);
    static bool Finite(Quaternion q)=>float.IsFinite(q.X)&&float.IsFinite(q.Y)&&float.IsFinite(q.Z)&&float.IsFinite(q.W)&&float.IsFinite(q.LengthSquared());
    static void ValidateTime(double time){if(!double.IsFinite(time)||time<0)throw new FormatException("A pose key time must be finite and zero or greater.");}
}
