using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GoPrevis;

public sealed partial class NativeMotionTrack
{
    int[] _authoredParents=Array.Empty<int>();
    readonly Dictionary<int,int> _linkedFeet=new();
    public IReadOnlyList<string> FootEffectors=>_linkedFeet.Keys.Select(BoneName).ToArray();
    public bool IsFootEffector(string name)=>_linkedFeet.ContainsKey(FindPoseBone(name));

    // Quaternius feet are deforming skin joints, despite their IK names. In the
    // authored export they are siblings of Body, so a normal FK leg edit leaves
    // the foot behind. Connect them at runtime, preserving every sampled world pose.
    void PrepareEditableHierarchy()
    {
        _authoredParents=_bones.Select(b=>{int parent=b.Skeleton.GetBoneParent(b.Index);return parent<0?-1:Array.FindIndex(_bones,p=>p.Skeleton==b.Skeleton&&p.Index==parent);}).ToArray();
        foreach(var pair in _bones.Select((bone,index)=>(bone,index)))
        {
            string name=pair.bone.Skeleton.GetBoneName(pair.bone.Index);
            string? parentName=name.StartsWith("IKFrontLeg.",StringComparison.Ordinal)?name.Replace("IKFrontLeg.","FrontLowerLeg.",StringComparison.Ordinal):name.StartsWith("IKBackLeg.",StringComparison.Ordinal)?name.Replace("IKBackLeg.","BackLowerLeg.",StringComparison.Ordinal):
                name.StartsWith("FrontFoot_",StringComparison.Ordinal)&&_bones.Any(b=>b.Skeleton==pair.bone.Skeleton&&b.Skeleton.GetBoneName(b.Index)==name.Replace("FrontFoot_","FrontLowLeg_",StringComparison.Ordinal))?name.Replace("FrontFoot_","FrontLowLeg_",StringComparison.Ordinal):
                name.StartsWith("BackFoot_",StringComparison.Ordinal)&&_bones.Any(b=>b.Skeleton==pair.bone.Skeleton&&b.Skeleton.GetBoneName(b.Index)==name.Replace("BackFoot_","BackLowLeg_",StringComparison.Ordinal))?name.Replace("BackFoot_","BackLowLeg_",StringComparison.Ordinal):null;
            if(parentName==null)continue;
            int parent=Array.FindIndex(_bones,b=>b.Skeleton==pair.bone.Skeleton&&b.Skeleton.GetBoneName(b.Index)==parentName);
            if(parent<0||_authoredParents[pair.index]==parent)continue;
            _linkedFeet[pair.index]=parent;
        }
        ConvertAuthoredPose(_rest);
        foreach(var pair in _linkedFeet)
        {
            var bone=_bones[pair.Key];bone.Skeleton.SetBoneParent(bone.Index,_bones[pair.Value].Index);
            bone.Skeleton.SetBoneRest(bone.Index,PoseTransform(_rest,pair.Key));
        }
        Apply(_rest);
    }
    static Transform3D PoseTransform(Pose pose,int index)=>new(new Basis(pose.Rotation[index]).Scaled(pose.Scale[index]),pose.Position[index]);
    Transform3D[] AuthoredGlobals(Pose pose)
    {
        var result=new Transform3D[_bones.Length];var ready=new bool[_bones.Length];
        Transform3D Get(int index){if(ready[index])return result[index];int parent=_authoredParents[index];result[index]=parent<0?PoseTransform(pose,index):Get(parent)*PoseTransform(pose,index);ready[index]=true;return result[index];}
        for(int i=0;i<result.Length;i++)Get(i);return result;
    }
    void ConvertAuthoredPose(Pose pose)
    {
        if(_linkedFeet.Count==0)return;var global=AuthoredGlobals(pose);
        foreach(var pair in _linkedFeet)
        {
            var local=global[pair.Value].AffineInverse()*global[pair.Key];pose.Position[pair.Key]=local.Origin;
            pose.Rotation[pair.Key]=local.Basis.Orthonormalized().GetRotationQuaternion();pose.Scale[pair.Key]=local.Basis.Scale;
        }
    }
    public string PoseBoneDisplayName(string name)=>name
        .Replace("IKFrontLeg.","Front foot ",StringComparison.Ordinal)
        .Replace("IKBackLeg.","Back foot ",StringComparison.Ordinal)
        .Replace("FFB.","Back toes ",StringComparison.Ordinal)
        .Replace("FF.","Front toes ",StringComparison.Ordinal);
}
