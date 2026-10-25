using Godot;
using System;
using System.Linq;

namespace GoPrevis;

public partial class RiggedActor
{
    StaticPoseData? _editingPoseOverride;
    Vector3 _editingPoseFacing=Vector3.Forward;
    StaticPoseData? _lastCorrectionSource,_lastCorrectionResult;
    Vector3 _lastCorrectionFacing=Vector3.Forward;
    public bool PoseCorrectionEditActive=>_editingPoseOverride!=null;

    /// <summary>The actual evaluated skeleton, including source travel and displayed corrections.</summary>
    public StaticPoseData CaptureEvaluatedPose(string name="Evaluated pose")
    {
        EnsureRig();var root=_skeleton!.GetBonePosePosition(0)-_bones[0].Offset;
        var forward=_faceMarker==null?Vector3.Forward:new Vector3(_faceMarker.Position.X,0,_faceMarker.Position.Z);
        if(forward.LengthSquared()<.000001f)forward=Vector3.Forward;forward=forward.Normalized();
        var result=new StaticPoseData{Name=name,RootPosition=new[]{root.X,root.Y,root.Z},Forward=new[]{forward.X,0f,forward.Z}};
        for(int i=0;i<_bones.Count;i++)result.BoneRotations[_bones[i].Name]=PoseCorrectionRuntime.Values(_skeleton.GetBonePoseRotation(i).Normalized());
        return result;
    }

    /// <summary>
    /// Applies additive corrections after EvaluateTimeline. Passing its freshly captured source
    /// snapshot makes this operation explicitly independent of every earlier evaluation.
    /// Neither static pose data nor source motion clips are changed.
    /// </summary>
    public void ApplyPoseCorrections(PoseCorrectionAnimationData? data,double time,StaticPoseData? sourcePose=null)
    {
        EnsureRig();var current=sourcePose??CaptureEvaluatedPose();var facing=AnimatedForward;
        if(sourcePose==null&&_lastCorrectionSource!=null&&_lastCorrectionResult!=null&&SameCorrectionPose(current,_lastCorrectionResult))
        {current=_lastCorrectionSource;facing=_lastCorrectionFacing;}
        if(!PoseCorrectionRuntime.HasKeys(data))
        {
            if(sourcePose==null&&_lastCorrectionResult!=null&&SameCorrectionPose(CaptureEvaluatedPose(),_lastCorrectionResult))ApplyEvaluatedPoseSnapshot(current,facing);
            _lastCorrectionSource=null;_lastCorrectionResult=null;return;
        }
        var result=CloneStaticPose(current);var root=PoseCorrectionRuntime.RootValue(result)+PoseCorrectionRuntime.EvaluateRoot(data,time);result.RootPosition=new[]{root.X,root.Y,root.Z};
        if(data?.Bones!=null)foreach(var track in data.Bones)
        {
            if(FindBone(track.Key)<0)continue;
            var rotation=(PoseCorrectionRuntime.ReadBone(current,track.Key)*PoseCorrectionRuntime.EvaluateRotation(track.Value,time)).Normalized();result.BoneRotations[track.Key]=PoseCorrectionRuntime.Values(rotation);
        }
        ApplyEvaluatedPoseSnapshot(result,facing);
        _lastCorrectionSource=CloneStaticPose(current);_lastCorrectionResult=CloneStaticPose(result);_lastCorrectionFacing=facing;
    }

    /// <summary>Stages an edit of the currently displayed pose without disabling its motion source.</summary>
    public void BeginPoseCorrectionEdit()
    {
        EnsureRig();_editingPoseOverride=CaptureEvaluatedPose("Edited pose");_editingPoseFacing=AnimatedForward;
    }
    public StaticPoseData GetPoseCorrectionEditSnapshot()=>_editingPoseOverride==null?CaptureEvaluatedPose("Edited pose"):CloneStaticPose(_editingPoseOverride);
    public StaticPoseData? EndPoseCorrectionEdit()
    {
        var result=_editingPoseOverride==null?null:CloneStaticPose(_editingPoseOverride);_editingPoseOverride=null;return result;
    }

    void ApplyEvaluatedPoseSnapshot(StaticPoseData pose,Vector3 sourceFacing)
    {
        var root=PoseCorrectionRuntime.RootValue(pose);if(!root.IsFinite())throw new FormatException("The evaluated pose root must remain finite.");
        for(int i=0;i<_bones.Count;i++)
        {
            var definition=_bones[i];var local=PoseCorrectionRuntime.ReadBone(pose,definition.Name);var parent=definition.Parent<0?Quaternion.Identity:_targetRotations[definition.Parent];
            var position=definition.Offset+(definition.Parent<0?root:Vector3.Zero);_targetRotations[i]=(parent*local).Normalized();
            _targetPositions[i]=definition.Parent<0?position:_targetPositions[definition.Parent]+parent*position;
            _skeleton!.SetBonePosePosition(i,position);_skeleton.SetBonePoseRotation(i,local);_skeleton.SetBonePoseScale(i,Vector3.One);
        }
        RootMotionOffset=root;AnimatedForward=sourceFacing;ResetStaticFaceMarker(new Vector3(pose.Forward[0],0,pose.Forward[2]));FitStaticPosePicker();
    }
    static bool SameCorrectionPose(StaticPoseData first,StaticPoseData second)
        =>!PoseCorrectionRuntime.RootChanged(first,second)&&first.BoneRotations.Count==second.BoneRotations.Count&&PoseCorrectionRuntime.ChangedBones(first,second).Count==0;
}
