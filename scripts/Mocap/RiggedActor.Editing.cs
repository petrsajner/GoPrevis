using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Previs;

/// <summary>Static rig editing in actor-local metres; scene transforms remain a separate layer.</summary>
public partial class RiggedActor
{
    IReadOnlyList<string>? _poseBoneNames;
    public IReadOnlyList<string> PoseBoneNames
    {
        get { EnsureRig(); return _poseBoneNames ??= Array.AsReadOnly(_bones.Select(bone=>bone.Name).ToArray()); }
    }

    public Vector3 GetPoseBoneRotationDegrees(string boneName)
    {
        int index=EditableBoneIndex(boneName);
        return new Basis(_skeleton!.GetBonePoseRotation(index)).GetEuler()*(180f/Mathf.Pi);
    }

    public void SetPoseBoneRotationDegrees(string boneName,Vector3 degrees)
    {
        int index=EditableBoneIndex(boneName);ValidateEditVector(degrees,360000,"Bone rotation");
        var pose=EditablePoseSnapshot();
        SetEditingRotation(pose,index,Quaternion.FromEuler(degrees*(Mathf.Pi/180f)));
        CommitEditedPose(pose);
    }

    /// <summary>Current joint position before the outer actor translation, rotation and scale.</summary>
    public Vector3 GetPoseJointPosition(string boneName)=>PreparedBoneTransform(EditableBoneIndex(boneName)).Origin;

    /// <summary>Joint orientation in actor-local space, with its parent rotations included.</summary>
    public Quaternion GetPoseJointGlobalRotation(string boneName)=>PreparedBoneTransform(EditableBoneIndex(boneName)).Basis.GetRotationQuaternion().Normalized();

    public string? GetPoseBoneParentName(string boneName)
    {
        int parent=_bones[EditableBoneIndex(boneName)].Parent;
        return parent<0?null:_bones[parent].Name;
    }

    /// <summary>Current hip offset relative to rest, including any currently evaluated source travel.</summary>
    public Vector3 GetPoseRootOffset()
    {
        EnsureRig();return _skeleton!.GetBonePosePosition(0)-_bones[0].Offset;
    }

    public void SetPoseRootOffset(Vector3 offset)=>MovePosePelvis(offset,false);

    /// <summary>
    /// Moves the hips to an absolute pose offset. Feet can remain pinned in actor-local
    /// space; excessive pelvis travel is limited to the connected reachable region.
    /// </summary>
    public void MovePosePelvis(Vector3 offset,bool keepFeet=true)
    {
        ValidateEditVector(offset,1000,"Pelvis position");var pose=EditablePoseSnapshot();
        var original=EditingTransforms(pose);Vector3 previous=new(pose.RootPosition[0],pose.RootPosition[1],pose.RootPosition[2]);
        int left=EditableBoneIndex("LeftFoot"),right=EditableBoneIndex("RightFoot");
        Vector3 leftTarget=original[left].Origin,rightTarget=original[right].Origin;
        Vector3 displacement=offset-previous;
        if(keepFeet)
        {
            float fraction=1;
            foreach(int foot in new[]{left,right})
            {
                int lower=_bones[foot].Parent,upper=_bones[lower].Parent;
                float first=_bones[lower].Offset.Length(),second=_bones[foot].Offset.Length();
                fraction=Math.Min(fraction,ReachablePelvisFraction(original[upper].Origin-original[foot].Origin,displacement,Math.Abs(first-second),first+second));
            }
            offset=previous+displacement*fraction;
        }
        pose.RootPosition=new[]{offset.X,offset.Y,offset.Z};
        if(keepFeet)
        {
            SolvePoseLimb(pose,"LeftFoot",leftTarget);
            SolvePoseLimb(pose,"RightFoot",rightTarget);
        }
        CommitEditedPose(pose);
    }

    /// <summary>
    /// Analytic two-bone hand/foot IK. Unreachable targets stop at the limb's reach;
    /// bone lengths and the end joint's orientation are preserved.
    /// </summary>
    public Vector3 MovePoseEndEffector(string boneName,Vector3 targetActorLocal)
    {
        ValidateEditVector(targetActorLocal,1000,"Limb target");
        if(boneName is not ("LeftHand" or "RightHand" or "LeftFoot" or "RightFoot"))
            throw new ArgumentException("Choose a hand or foot to move with inverse kinematics.",nameof(boneName));
        var pose=EditablePoseSnapshot();Vector3 actual=SolvePoseLimb(pose,boneName,targetActorLocal);
        CommitEditedPose(pose);return actual;
    }

    /// <summary>
    /// Moves any prepared joint while keeping the rig's authored bone lengths.
    /// IK runs upwards toward the chain root. Children retain their local poses
    /// and follow the moved joint; a hand or foot is never pinned by an elbow/knee drag.
    /// </summary>
    public Vector3 MovePoseJoint(string boneName,Vector3 targetActorLocal)
    {
        ValidateEditVector(targetActorLocal,1000,"Joint target");int joint=EditableBoneIndex(boneName);
        if(boneName is "LeftHand" or "RightHand" or "LeftFoot" or "RightFoot")return MovePoseEndEffector(boneName,targetActorLocal);
        var pose=EditablePoseSnapshot();var original=EditingTransforms(pose);
        if(boneName=="Hips")
        {
            Vector3 offset=new Vector3(pose.RootPosition[0],pose.RootPosition[1],pose.RootPosition[2])+targetActorLocal-original[joint].Origin;
            pose.RootPosition=new[]{offset.X,offset.Y,offset.Z};
        }
        else if(boneName is "LeftLowerArm" or "RightLowerArm")
        {
            // Clavicle -> shoulder -> elbow is a genuine upward two-bone chain.
            // Undo the solver's end-orientation compensation: the elbow's local
            // pose and every descendant must follow its parents instead of holding the hand still.
            var local=PoseCorrectionRuntime.ReadBone(pose,boneName);SolvePoseLimb(pose,boneName,targetActorLocal);SetEditingRotation(pose,joint,local);
        }
        else
        {
            int parent=_bones[joint].Parent;
            if(parent<0)throw new InvalidOperationException("This joint has no movable parent.");
            Vector3 current=original[joint].Origin-original[parent].Origin,desired=targetActorLocal-original[parent].Origin;
            if(desired.LengthSquared()<.000000001f)desired=current;
            Quaternion rotatedParent=(EditingAlign(current,desired)*original[parent].Basis.GetRotationQuaternion()).Normalized();
            int ancestor=_bones[parent].Parent;
            Quaternion ancestorRotation=ancestor<0?Quaternion.Identity:original[ancestor].Basis.GetRotationQuaternion().Normalized();
            SetEditingRotation(pose,parent,(ancestorRotation.Inverse()*rotatedParent).Normalized());
            // Deliberately keep this joint and its descendants' LOCAL rotations.
            // This includes knees: the hip turns, and the shin/foot follow rigidly.
        }
        Vector3 actual=EditingTransforms(pose)[joint].Origin;CommitEditedPose(pose);return actual;
    }

    StaticPoseData EditablePoseSnapshot()
    {
        EnsureRig();
        if(_editingPoseOverride!=null)return CloneStaticPose(_editingPoseOverride);
        if(_motionTrack.Length!=0)throw new InvalidOperationException("Begin a pose-correction edit before changing an animated joint.");
        return _staticPose==null?BakeCurrentPose("Edited pose"):CloneStaticPose(_staticPose);
    }

    void CommitEditedPose(StaticPoseData pose)
    {
        // SetStaticPose validates and clones before mutating the current playable state.
        pose.CatalogId=null;
        if(_editingPoseOverride!=null){_editingPoseOverride=CopyValidatedStaticPose(pose);ApplyEvaluatedPoseSnapshot(_editingPoseOverride,_editingPoseFacing);}
        else SetStaticPose(pose);
    }

    int EditableBoneIndex(string name)
    {
        EnsureRig();int index=FindBone(name);
        if(index<0)throw new ArgumentException("The prepared human rig has no bone named '"+name+"'.",nameof(name));
        return index;
    }

    static void ValidateEditVector(Vector3 value,float limit,string label)
    {
        if(!value.IsFinite()||Math.Abs(value.X)>limit||Math.Abs(value.Y)>limit||Math.Abs(value.Z)>limit)
            throw new ArgumentOutOfRangeException(label,label+" must contain finite values within the editing range.");
    }

    Transform3D[] EditingTransforms(StaticPoseData pose)
    {
        var result=new Transform3D[_bones.Count];var root=new Vector3(pose.RootPosition[0],pose.RootPosition[1],pose.RootPosition[2]);
        for(int index=0;index<_bones.Count;index++)
        {
            Quaternion rotation=Quaternion.Identity;
            if(pose.BoneRotations.TryGetValue(_bones[index].Name,out float[]? values))rotation=new Quaternion(values[0],values[1],values[2],values[3]).Normalized();
            var local=new Transform3D(new Basis(rotation),_bones[index].Offset+(_bones[index].Parent<0?root:Vector3.Zero));
            result[index]=_bones[index].Parent<0?local:result[_bones[index].Parent]*local;
        }
        return result;
    }

    void SetEditingRotation(StaticPoseData pose,int index,Quaternion rotation)
    {
        rotation=rotation.Normalized();pose.BoneRotations[_bones[index].Name]=new[]{rotation.X,rotation.Y,rotation.Z,rotation.W};
    }

    Vector3 SolvePoseLimb(StaticPoseData pose,string effectorName,Vector3 target,Vector3? bendTarget=null)
    {
        int end=EditableBoneIndex(effectorName),middle=_bones[end].Parent,start=_bones[middle].Parent;
        var fk=EditingTransforms(pose);Vector3 origin=fk[start].Origin;
        Quaternion endOrientation=fk[end].Basis.GetRotationQuaternion().Normalized();
        float first=_bones[middle].Offset.Length(),second=_bones[end].Offset.Length();
        Vector3 delta=target-origin;float requested=delta.Length();
        Vector3 direction=requested>.000001f?delta/requested:(fk[end].Origin-origin).Normalized();
        if(direction.LengthSquared()<.000001f)direction=Vector3.Down;
        float distance=Math.Clamp(requested,Math.Abs(first-second)+.000001f,first+second-.000001f);
        Vector3 clampedTarget=origin+direction*distance;

        // Retain the current bend as the pole, projected into the new target plane.
        Vector3 bend=(bendTarget??fk[middle].Origin)-origin;
        bend-=direction*bend.Dot(direction);
        if(bend.LengthSquared()<.0000000001f)
        {
            Vector3 preferred=fk[0].Basis*(effectorName.EndsWith("Foot",StringComparison.Ordinal)?Vector3.Forward:Vector3.Down);
            bend=preferred-direction*preferred.Dot(direction);
        }
        if(bend.LengthSquared()<.0000000001f)
        {
            Vector3 preferred=fk[0].Basis*Vector3.Right;bend=preferred-direction*preferred.Dot(direction);
        }
        if(bend.LengthSquared()<.0000000001f){Vector3 preferred=Math.Abs(direction.Y)<.9f?Vector3.Up:Vector3.Right;bend=preferred-direction*preferred.Dot(direction);}
        bend=bend.Normalized();
        float along=(first*first-second*second+distance*distance)/(2*distance);
        float height=Mathf.Sqrt(Math.Max(0,first*first-along*along));
        Vector3 elbow=origin+direction*along+bend*height;

        Quaternion oldUpper=fk[start].Basis.GetRotationQuaternion().Normalized();
        Quaternion upper=(EditingAlign(fk[middle].Origin-origin,elbow-origin)*oldUpper).Normalized();
        Quaternion parent=_bones[start].Parent<0?Quaternion.Identity:fk[_bones[start].Parent].Basis.GetRotationQuaternion().Normalized();
        SetEditingRotation(pose,start,(parent.Inverse()*upper).Normalized());
        var rotated=EditingTransforms(pose);
        Quaternion lower=(EditingAlign(rotated[end].Origin-rotated[middle].Origin,clampedTarget-elbow)*rotated[middle].Basis.GetRotationQuaternion()).Normalized();
        SetEditingRotation(pose,middle,(upper.Inverse()*lower).Normalized());
        SetEditingRotation(pose,end,(lower.Inverse()*endOrientation).Normalized());
        return EditingTransforms(pose)[end].Origin;
    }

    static Quaternion EditingAlign(Vector3 from,Vector3 to)
    {
        from=from.Normalized();to=to.Normalized();float dot=Math.Clamp(from.Dot(to),-1,1);
        if(dot>1-.0000001f)return Quaternion.Identity;
        if(dot< -1+.0000001f)
        {
            Vector3 reference=Math.Abs(from.Y)<.9f?Vector3.Up:Vector3.Right;
            return new Quaternion(from.Cross(reference).Normalized(),Mathf.Pi);
        }
        Vector3 cross=from.Cross(to);return new Quaternion(cross.X,cross.Y,cross.Z,1+dot).Normalized();
    }

    static float ReachablePelvisFraction(Vector3 fromFoot,Vector3 displacement,float minimum,float maximum)
    {
        double a=displacement.LengthSquared();if(a<1e-14)return 1;
        double b=2*fromFoot.Dot(displacement),c=fromFoot.LengthSquared(),limit=1;
        // The outside of the outer sphere is unreachable. The feasible segment
        // starts at t=0, so its positive exit is the maximum allowed translation.
        double outer=b*b-4*a*(c-maximum*maximum);
        if(outer>=0)limit=Math.Clamp((-b+Math.Sqrt(Math.Max(0,outer)))/(2*a),0,1);
        else if(c>maximum*maximum+1e-7)return 0;
        // Avoid crossing the inner unreachable sphere when folding a limb fully.
        double inner=b*b-4*a*(c-minimum*minimum);
        if(inner>0)
        {
            double enter=(-b-Math.Sqrt(inner))/(2*a),leave=(-b+Math.Sqrt(inner))/(2*a);
            if(enter>=0&&enter<limit&&leave>0)limit=enter;
        }
        return (float)Math.Clamp(limit,0,1);
    }
}
