using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GenPrevis;

public sealed partial class NativeMotionTrack
{
    string[]? _poseNames,_allPoseNames;
    string BoneName(int index)
    {
        if(_allPoseNames==null)
        {
            var names=_bones.Select(bone=>bone.Skeleton.GetBoneName(bone.Index).ToString()).ToArray();
            _allPoseNames=names.Select((name,i)=>names.Count(other=>other==name)>1?_bones[i].Skeleton.Name+"/"+name:name).ToArray();
        }
        return _allPoseNames[index];
    }
    int FindPoseBone(string name)
    {
        for(int i=0;i<_bones.Length;i++)if(BoneName(i)==name)return i;
        throw new FormatException("This animal rig has no joint named '"+name+"'.");
    }
    public IReadOnlyList<string> PoseBoneNames=>_poseNames??=Enumerable.Range(0,_bones.Length).Select(BoneName).Where(name=>!name.StartsWith("PoleTarget",StringComparison.OrdinalIgnoreCase)).ToArray();
    public string RootBoneName=>BoneName(_roots[0]);
    public string DefaultPoseBone=>PoseBoneNames.FirstOrDefault(name=>name.Contains("head",StringComparison.OrdinalIgnoreCase))??RootBoneName;
    public bool IsEndJoint(string name){var bone=_bones[FindPoseBone(name)];return !Enumerable.Range(0,bone.Skeleton.GetBoneCount()).Any(i=>bone.Skeleton.GetBoneParent(i)==bone.Index);}
    public string? ParentPoseBone(string name)
    {
        var bone=_bones[FindPoseBone(name)];int parent=bone.Skeleton.GetBoneParent(bone.Index);if(parent<0)return null;
        return BoneName(Array.FindIndex(_bones,other=>other.Skeleton==bone.Skeleton&&other.Index==parent));
    }
    Transform3D BoneTransform(Bone bone,int index)
    {
        var skeleton=bone.Skeleton;
        var local=new Transform3D(new Basis(Safe(skeleton.GetBonePoseRotation(index))).Scaled(skeleton.GetBonePoseScale(index)),skeleton.GetBonePosePosition(index));
        int parent=skeleton.GetBoneParent(index);return parent<0?local:BoneTransform(bone,parent)*local;
    }
    public Vector3 GetPoseJointWorld(string name){var bone=_bones[FindPoseBone(name)];return bone.Skeleton.GlobalTransform*BoneTransform(bone,bone.Index).Origin;}
    public Basis GetPoseJointWorldBasis(string name){var bone=_bones[FindPoseBone(name)];return (bone.Skeleton.GlobalBasis*BoneTransform(bone,bone.Index).Basis).Orthonormalized();}
    public Quaternion GetPoseBoneRotation(string name){var bone=_bones[FindPoseBone(name)];return Safe(bone.Skeleton.GetBonePoseRotation(bone.Index));}
    public Vector3 GetPoseBoneRotationDegrees(string name)=>new Basis(GetPoseBoneRotation(name)).GetEuler()*180f/Mathf.Pi;
    public void SetPoseBoneRotationDegrees(string name,Vector3 degrees)
    {
        if(!degrees.IsFinite())throw new FormatException("Joint rotation must contain finite values.");
        var bone=_bones[FindPoseBone(name)];bone.Skeleton.SetBonePoseRotation(bone.Index,Basis.FromEuler(degrees*Mathf.Pi/180f).GetRotationQuaternion());
    }
    public StaticPoseData CaptureEvaluatedPose(string name="Animal pose")=>CapturePose(null,name);
    public StaticPoseData CaptureSourcePose()=>CapturePose(_source,"Source pose");
    StaticPoseData CapturePose(Pose? pose,string name)
    {
        var data=new StaticPoseData{Name=name,NativeHierarchyVersion=1};
        for(int i=0;i<_bones.Length;i++)
        {
            string key=BoneName(i);data.BoneRotations[key]=PoseCorrectionRuntime.Values(pose==null?GetPoseBoneRotation(key):pose.Rotation[i]);
            var b=_bones[i];Vector3 bonePosition=pose==null?b.Skeleton.GetBonePosePosition(b.Index):pose.Position[i],scale=pose==null?b.Skeleton.GetBonePoseScale(b.Index):pose.Scale[i];
            if(!_roots.Contains(i))data.NativeBonePositions[key]=new[]{bonePosition.X,bonePosition.Y,bonePosition.Z};data.NativeBoneScales[key]=new[]{scale.X,scale.Y,scale.Z};
        }
        var root=_bones[_roots[0]];Vector3 position=(pose==null?root.Skeleton.GetBonePosePosition(root.Index):pose.Position[_roots[0]])-_rest.Position[_roots[0]];
        data.RootPosition=new[]{position.X,position.Y,position.Z};return data;
    }
    void ApplyStaticPose(Pose pose,StaticPoseData? data)
    {
        if(data==null)return;
        if(data.NativeHierarchyVersion==0&&_linkedFeet.Count>0)
        {
            Copy(_authoredRest,pose);for(int i=0;i<_bones.Length;i++)if(data.BoneRotations.ContainsKey(BoneName(i)))pose.Rotation[i]=PoseCorrectionRuntime.ReadBone(data,BoneName(i));
            Vector3 legacyRoot=PoseCorrectionRuntime.RootValue(data);if(legacyRoot.IsFinite())foreach(int index in _roots)pose.Position[index]+=legacyRoot;
            ConvertAuthoredPose(pose);return;
        }
        for(int i=0;i<_bones.Length;i++)if(data.BoneRotations.ContainsKey(BoneName(i)))pose.Rotation[i]=PoseCorrectionRuntime.ReadBone(data,BoneName(i));
        for(int i=0;i<_bones.Length;i++)
        {
            string key=BoneName(i);
            if(!_roots.Contains(i)&&data.NativeBonePositions.TryGetValue(key,out var position)&&position.Length==3&&position.All(float.IsFinite))pose.Position[i]=new(position[0],position[1],position[2]);
            if(data.NativeBoneScales.TryGetValue(key,out var scale)&&scale.Length==3&&scale.All(float.IsFinite))pose.Scale[i]=new(scale[0],scale[1],scale[2]);
        }
        Vector3 root=PoseCorrectionRuntime.RootValue(data);if(root.IsFinite())foreach(int index in _roots)pose.Position[index]+=root;
    }
    void ApplyCorrections(Pose pose,PoseCorrectionAnimationData? corrections,double time)
    {
        if(corrections==null)return;
        for(int i=0;i<_bones.Length;i++)if(corrections.Bones?.TryGetValue(BoneName(i),out var keys)==true)pose.Rotation[i]=Safe(pose.Rotation[i]*PoseCorrectionRuntime.EvaluateRotation(keys,time));
        Vector3 root=PoseCorrectionRuntime.EvaluateRoot(corrections,time);if(root.IsFinite())foreach(int index in _roots)pose.Position[index]+=root;
    }
    /// <summary>CCD rotates only ancestors of the grabbed joint. Its descendants retain their local transforms.</summary>
    public void MovePoseJointWorld(string name,Vector3 target)
    {
        if(!target.IsFinite())throw new FormatException("The joint target must contain finite values.");
        var selected=_bones[FindPoseBone(name)];var skeleton=selected.Skeleton;
        if(Math.Abs(skeleton.GlobalBasis.Determinant())<.00000001f)throw new FormatException("The animal scale is too small to edit its pose.");
        Vector3 goal=skeleton.GlobalTransform.AffineInverse()*target;
        int ancestor=skeleton.GetBoneParent(selected.Index);
        if(ancestor<0)
        {
            Vector3 delta=goal-BoneTransform(selected,selected.Index).Origin;
            foreach(int index in _roots.Where(index=>_bones[index].Skeleton==skeleton))skeleton.SetBonePosePosition(_bones[index].Index,skeleton.GetBonePosePosition(_bones[index].Index)+delta);
            return;
        }
        var chain=new List<int>();
        while(ancestor>=0&&chain.Count<4)
        {
            int children=Enumerable.Range(0,skeleton.GetBoneCount()).Count(index=>skeleton.GetBoneParent(index)==ancestor);
            if(children>1&&chain.Count>0)break;
            chain.Add(ancestor);ancestor=skeleton.GetBoneParent(ancestor);
        }
        for(int iteration=0;iteration<30;iteration++)
        {
            if(BoneTransform(selected,selected.Index).Origin.DistanceSquaredTo(goal)<.00000001f)break;
            foreach(int joint in chain)
            {
                var jointPose=BoneTransform(selected,joint);Vector3 from=BoneTransform(selected,selected.Index).Origin-jointPose.Origin,to=goal-jointPose.Origin;
                if(from.LengthSquared()<.00000001f||to.LengthSquared()<.00000001f)continue;
                var change=new Quaternion(from.Normalized(),to.Normalized());
                Quaternion desired=Safe(change*jointPose.Basis.Orthonormalized().GetRotationQuaternion());
                int parent=skeleton.GetBoneParent(joint);Quaternion parentRotation=parent<0?Quaternion.Identity:BoneTransform(selected,parent).Basis.Orthonormalized().GetRotationQuaternion();
                skeleton.SetBonePoseRotation(joint,Safe(parentRotation.Inverse()*desired));
            }
        }
    }
}
