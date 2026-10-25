using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    bool _poseEditing,_poseGestureActive,_posePinFeet=true;
    string _poseBone="Head",_poseTool="Rotate",_poseSelectionId="",_poseNameEntryId="";
    PoseCorrectionAnimationData? _poseCorrectionBaseline;
    readonly Dictionary<string,StaticPoseData> _poseBaseSnapshots=new();
    readonly HashSet<string> _poseGestureBones=new(StringComparer.Ordinal);
    RiggedActor? _poseGestureActor;
    StaticPoseData? _poseGestureInitial;
    bool _poseGestureRoot;
    Button _poseKeyJoint=null!,_poseKeyAll=null!;
    VBoxContainer _posePanel=null!,_posePreciseBox=null!;
    Button _poseToolbarButton=null!,_editPoseShortcut=null!,_poseStartButton=null!,_poseMoveButton=null!,_poseRotateButton=null!;
    Label _poseEditInfo=null!,_posePositionLabel=null!;
    OptionButton _poseJointChoice=null!;
    readonly SpinBox[] _poseRotationFields=new SpinBox[3],_posePositionFields=new SpinBox[3];
    SpinBox _poseHipHeight=null!;
    CheckButton _poseKeepFeet=null!;
    LineEdit _poseSaveName=null!;
    Button _poseSaveButton=null!,_poseResetButton=null!,_poseDoneButton=null!;

    void BuildPoseInspector()
    {
        _posePanel=new VBoxContainer();_posePanel.AddThemeConstantOverride("separation",6);_inspector.AddChild(_posePanel);
        _poseEditInfo=Text("Select a human figure.",11,true);_poseEditInfo.AutowrapMode=TextServer.AutowrapMode.WordSmart;_posePanel.AddChild(_poseEditInfo);
        _poseStartButton=Button("Edit pose",StartPoseEditing);_poseStartButton.TooltipText="Edit a keyed correction above the current motion. Source clips are preserved.";_posePanel.AddChild(_poseStartButton);
        _poseJointChoice=new OptionButton{FitToLongestItem=false,ClipText=true};_poseJointChoice.ItemSelected+=i=>{if(!_refreshing&&i>=0)SelectPoseBone(_poseJointChoice.GetItemMetadata((int)i).AsString());};_posePanel.AddChild(_poseJointChoice);
        _poseMoveButton=Button("Move",()=>SetPoseTool("Move"),true);_poseRotateButton=Button("Rotate",()=>SetPoseTool("Rotate"),true);_posePanel.AddChild(Row(_poseMoveButton,_poseRotateButton));
        _poseKeyJoint=Button("◇ Key joint",()=>CapturePoseCorrectionKey(_poseBone));_poseKeyAll=Button("◆ Key pose",()=>CapturePoseCorrectionKey("All"));_posePanel.AddChild(Row(_poseKeyJoint,_poseKeyAll));
        var precise=Button("Precise values",()=>_posePreciseBox.Visible=!_posePreciseBox.Visible,true);_posePanel.AddChild(precise);_posePreciseBox=new VBoxContainer{Visible=false};_posePanel.AddChild(_posePreciseBox);
        _posePreciseBox.AddChild(Text("Rotation (local degrees)",11,true));_posePreciseBox.AddChild(PoseVectorFields(_poseRotationFields,-180,180,.1,axis=>EditPoseNumber(()=>{var value=SelectedPoseRotation();value[axis]=(float)_poseRotationFields[axis].Value;UpdatePoseBoneRotation(value);})));
        _posePositionLabel=Text("Target position (world metres)",11,true);_posePreciseBox.AddChild(_posePositionLabel);_posePreciseBox.AddChild(PoseVectorFields(_posePositionFields,-1000,1000,.001,axis=>EditPoseNumber(()=>{var value=SelectedPoseWorldPosition();value[axis]=(float)_posePositionFields[axis].Value;UpdatePoseEffectorWorld(_poseBone,value);})));
        _poseHipHeight=Spin(-1000,1000,.001,1);_poseHipHeight.ValueChanged+=v=>EditPoseNumber(()=>{var actor=(RiggedActor)_selected!.Node;var world=actor.GlobalTransform*actor.GetPoseJointPosition("Hips");world.Y=(float)v;UpdatePoseEffectorWorld("Hips",world);});_posePreciseBox.AddChild(Row(Text("Hip height (m)",11,true),_poseHipHeight));
        _poseKeepFeet=new CheckButton{Text="Keep feet in place",TooltipText="Keep both feet planted when moving the pelvis.",ButtonPressed=true};_poseKeepFeet.Toggled+=v=>_posePinFeet=v;_posePanel.AddChild(_poseKeepFeet);
        _posePanel.AddChild(new HSeparator());_poseSaveName=new LineEdit{PlaceholderText="Pose name"};_posePanel.AddChild(_poseSaveName);
        _poseSaveButton=Button("Save to My poses",()=>{try{SaveCapturedPose(string.IsNullOrWhiteSpace(_poseSaveName.Text)?DefaultPoseName():_poseSaveName.Text);}catch(Exception ex){Error(ex.Message);}});_posePanel.AddChild(_poseSaveButton);
        _poseResetButton=Button("Reset edits",ResetManualPoseEdits);_poseDoneButton=Button("Done",()=>{StopPoseEditing();ShowInspectorSection("Object");});_posePanel.AddChild(Row(_poseResetButton,_poseDoneButton));
        UpdatePoseInspector();ShowInspectorSection("Object");
    }
    HBoxContainer PoseVectorFields(SpinBox[] fields,double min,double max,double step,Action<int> change)
    {
        var row=new HBoxContainer();row.AddThemeConstantOverride("separation",3);
        for(int i=0;i<3;i++){int axis=i;var column=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};column.AddThemeConstantOverride("separation",1);column.AddChild(Text("XYZ"[i].ToString(),10,true));var field=Spin(min,max,step,0);field.CustomMinimumSize=new Vector2(70,0);fields[i]=field;field.ValueChanged+=_=>{if(!_refreshing)change(axis);};column.AddChild(field);row.AddChild(column);}return row;
    }
    static string PoseBoneLabel(string bone)=>bone switch
    {
        "Hips"=>"Pelvis","Spine"=>"Lower spine","LeftLowerArm"=>"Left forearm","RightLowerArm"=>"Right forearm","LeftUpperLeg"=>"Left thigh","RightUpperLeg"=>"Right thigh","LeftLowerLeg"=>"Left knee","RightLowerLeg"=>"Right knee",_=>System.Text.RegularExpressions.Regex.Replace(bone,"(?<!^)([A-Z])"," $1")
    };
    static bool PoseCanMove(string bone)=>bone is "Hips" or "Spine" or "Chest" or "Neck" or "Head" or "LeftShoulder" or "RightShoulder" or "LeftUpperArm" or "RightUpperArm" or "LeftLowerArm" or "RightLowerArm" or "LeftHand" or "RightHand" or "LeftUpperLeg" or "RightUpperLeg" or "LeftLowerLeg" or "RightLowerLeg" or "LeftFoot" or "RightFoot" or "LeftToe" or "RightToe";
    static string DefaultPoseTool(string bone)=>bone is "Hips" or "LeftHand" or "RightHand" or "LeftFoot" or "RightFoot"?"Move":"Rotate";
    void SetPoseTool(string tool)
    {
        if(tool is not ("Move" or "Rotate"))return;_poseTool=tool;UpdatePoseInspector();_poseViewportOverlay?.QueueRedraw();
    }
    void OpenPoseInspector(){ShowInspectorSection("Pose");UpdatePoseInspector();}
    static PoseCorrectionAnimationData CopyPoseCorrections(PoseCorrectionAnimationData data)=>JsonSerializer.Deserialize<PoseCorrectionAnimationData>(JsonSerializer.Serialize(data))!;
    void StartPoseEditing()
    {
        if(_selected?.NativeTrack!=null){StartAnimalPoseEditing();return;}
        if(_selected?.Node is not RiggedActor actor){Status("Select a human figure to edit pose corrections.");return;}
        _playing=false;_play.Text="▶";SetTime(_time);
        if(!_poseEditing||_poseSelectionId!=_selected.Data.Id)
        {
            ReleasePoseViewportDrag();EndPoseGesture();_poseEditing=true;_poseSelectionId=_selected.Data.Id;_poseCorrectionBaseline=CopyPoseCorrections(_selected.Data.PoseCorrections);
        }
        if(ProjectedActorBounds(_selected).Size.Y<260)
        {
            FrameSelected();var facing=actor.GlobalBasis*actor.AnimatedForward;facing.Y=0;
            if(facing.LengthSquared()>.00001f){_yaw=Mathf.Atan2(facing.X,facing.Z)+.45f;_pitch=.25f;UpdateOrbit();}
        }
        ShowInspectorSection("Pose");UpdatePoseInspector();UpdateSelection();RefreshUnifiedWorkflow();Status("Pose corrections · edit at the playhead to add a key. Existing motion stays active.");
    }
    void StopPoseEditing()
    {
        ReleasePoseViewportDrag();EndPoseGesture();_poseEditing=false;_poseCorrectionBaseline=null;UpdatePoseInspector();UpdateSelection();RefreshUnifiedWorkflow();
    }
    void BeginPoseGesture()
    {
        if(_selected?.NativeTrack!=null){BeginAnimalPoseGesture();return;}
        if(_poseGestureActive)return;if(!_poseEditing)StartPoseEditing();if(!_poseEditing||_selected?.Node is not RiggedActor actor)return;
        _playing=false;_play.Text="▶";PushUndo();SetTime(_time);_poseGestureActive=true;_poseGestureActor=actor;actor.BeginPoseCorrectionEdit();_poseGestureInitial=actor.GetPoseCorrectionEditSnapshot();_poseGestureBones.Clear();_poseGestureRoot=false;
    }
    void EndPoseGesture()
    {
        _animalGestureTrack=null;
        _poseGestureActor?.EndPoseCorrectionEdit();_poseGestureActor=null;_poseGestureInitial=null;_poseGestureActive=false;_poseGestureBones.Clear();_poseGestureRoot=false;UpdatePoseInspector();
    }
    void SelectPoseBone(string bone)
    {
        if(_selected?.NativeTrack is {} native){if(!native.PoseBoneNames.Contains(bone))return;if(_poseBone!=bone){_poseBone=bone;_poseTool=native.IsEndJoint(bone)||native.IsFootEffector(bone)?"Move":"Rotate";}UpdatePoseInspector();return;}
        if(_selected?.Node is not RiggedActor actor||!actor.PoseBoneNames.Contains(bone))return;
        if(_poseBone!=bone){_poseBone=bone;_poseTool=DefaultPoseTool(bone);}UpdatePoseInspector();
    }
    void UpdatePoseEffectorWorld(string bone,Vector3 world)
    {
        if(_selected?.NativeTrack is {} native){native.MovePoseJointWorld(bone,world);CommitAnimalPose();return;}
        if(_selected?.Node is not RiggedActor actor||!PoseCanMove(bone))return;
        var local=actor.GlobalTransform.AffineInverse()*world;
        if(bone=="Hips")actor.MovePosePelvis(actor.GetPoseRootOffset()+local-actor.GetPoseJointPosition("Hips"),_posePinFeet);else actor.MovePoseJoint(bone,local);
        CommitEditedPose();
    }
    void UpdatePoseBoneRotation(Vector3 degrees)
    {
        if(_selected?.NativeTrack is {} native){native.SetPoseBoneRotationDegrees(_poseBone,degrees);CommitAnimalPose();return;}
        if(_selected?.Node is not RiggedActor actor)return;actor.SetPoseBoneRotationDegrees(_poseBone,degrees);CommitEditedPose();
    }
    void CommitEditedPose()
    {
        if(_selected?.Node is not RiggedActor actor||!_poseBaseSnapshots.TryGetValue(_selected.Data.Id,out var source))return;
        var edited=actor.GetPoseCorrectionEditSnapshot();var initial=_poseGestureInitial??source;
        foreach(string bone in PoseCorrectionRuntime.ChangedBones(initial,edited))_poseGestureBones.Add(bone);
        _poseGestureRoot|=PoseCorrectionRuntime.RootChanged(initial,edited);
        PoseCorrectionRuntime.WriteDifferences(_selected.Data.PoseCorrections,source,edited,_time,_poseGestureBones,_poseGestureRoot);
        SetTime(_time);RefreshTimelineSize();UpdatePoseInspector();
    }
    void CapturePoseCorrectionKey(string channel)
    {
        if(_selected?.NativeTrack!=null){CaptureAnimalPoseCorrectionKey(channel);return;}
        if(_selected?.Node is not RiggedActor actor)return;ReleasePoseViewportDrag();EndPoseGesture();SetTime(_time);
        if(!_poseBaseSnapshots.TryGetValue(_selected.Data.Id,out var source))return;PushUndo();var displayed=actor.CaptureEvaluatedPose();
        var bones=channel=="All"?actor.PoseBoneNames:channel=="Root"?Array.Empty<string>():new[]{channel};
        PoseCorrectionRuntime.WriteDifferences(_selected.Data.PoseCorrections,source,displayed,_time,bones,false);
        if(channel is "All" or "Root")
        {
            var keys=_selected.Data.PoseCorrections.Root;if(keys.Count==0&&_time>0)TransformLayerRuntime.Upsert(keys,0,Vector3.Zero);
            TransformLayerRuntime.Upsert(keys,_time,PoseCorrectionRuntime.RootValue(displayed)-PoseCorrectionRuntime.RootValue(source));
        }
        SetTime(_time);RefreshTimelineSize();UpdatePoseInspector();Status("Pose correction key added at "+_time.ToString("0.###")+" s.");
    }
    void EditPoseNumber(Action update)
    {
        if(_refreshing||!_poseEditing||(_selected?.Node is not RiggedActor&&_selected?.NativeTrack==null))return;
        bool own=!_poseGestureActive;try{if(own)BeginPoseGesture();update();}catch(Exception ex){Error(ex.Message);}finally{if(own)EndPoseGesture();}
    }
    void ResetManualPoseEdits()
    {
        if((_selected?.Node is not RiggedActor&&_selected?.NativeTrack==null)||_poseCorrectionBaseline==null)return;ReleasePoseViewportDrag();EndPoseGesture();PushUndo();_selected!.Data.PoseCorrections=CopyPoseCorrections(_poseCorrectionBaseline);SetTime(_time);RefreshTimelineSize();
    }
    void SyncPoseEditorSelection()
    {
        if(_poseEditing&&(_selected?.Data.Id!=_poseSelectionId||(_selected?.Node is not RiggedActor&&_selected?.NativeTrack==null))){ReleasePoseViewportDrag();EndPoseGesture();_poseEditing=false;_poseCorrectionBaseline=null;}
        UpdatePoseInspector();
    }
    void RestorePoseEditorSelection(string? id)
    {
        if(id==null)return;var entry=_entries.FirstOrDefault(e=>e.Data.Id==id&&(e.Node is RiggedActor||e.NativeTrack!=null));if(entry==null)return;Select(entry);_poseEditing=true;_poseSelectionId=id;_poseCorrectionBaseline=CopyPoseCorrections(entry.Data.PoseCorrections);OpenPoseInspector();
    }
    void UpdatePoseInspector()
    {
        if(_selected?.NativeTrack!=null&&_posePanel!=null){UpdateAnimalPoseInspector();return;}
        if(_posePanel==null)return;bool old=_refreshing;_refreshing=true;
        var actor=_selected?.Node as RiggedActor;bool human=actor!=null;
        _poseHipHeight.GetParent<Control>().Visible=true;_poseKeepFeet.Visible=true;
        if(human&&!actor!.PoseBoneNames.Contains(_poseBone))_poseBone="Head";
        _poseToolbarButton.Disabled=!human;_poseToolbarButton.ButtonPressed=_poseEditing&&human;_editPoseShortcut.Visible=human&&_inspectorSection=="Object";
        _poseStartButton.Visible=human&&!_poseEditing;_poseEditInfo.Text=!human?"Select a prepared human figure.":_poseEditing?"Edits add correction keys at the playhead. Existing motion stays active.":actor!.MotionTrackClipCount>0?"Adjust this motion with an additive pose correction.":"Edit this figure's head, body, arms and legs.";
        if(human&&(_poseJointChoice.ItemCount!=actor!.PoseBoneNames.Count||Enumerable.Range(0,_poseJointChoice.ItemCount).Any(i=>_poseJointChoice.GetItemMetadata(i).AsString()!=actor.PoseBoneNames[i]))){_poseJointChoice.Clear();foreach(string bone in actor.PoseBoneNames){_poseJointChoice.AddItem(PoseBoneLabel(bone));_poseJointChoice.SetItemMetadata(_poseJointChoice.ItemCount-1,bone);}}
        Vector3 rotation=Vector3.Zero,position=Vector3.Zero;float hip=0;
        if(human)
        {
            for(int i=0;i<_poseJointChoice.ItemCount;i++)if(_poseJointChoice.GetItemMetadata(i).AsString()==_poseBone)_poseJointChoice.Select(i);
            rotation=actor!.GetPoseBoneRotationDegrees(_poseBone);position=actor.GlobalTransform*actor.GetPoseJointPosition(_poseBone);hip=(actor.GlobalTransform*actor.GetPoseJointPosition("Hips")).Y;
            if(_poseNameEntryId!=_selected!.Data.Id){_poseNameEntryId=_selected.Data.Id;_poseSaveName.Text=_selected.Data.StaticPose?.Name??_selected.Data.Name+" pose";}
        }
        bool editable=human&&_poseEditing;
        for(int i=0;i<3;i++){_poseRotationFields[i].Value=rotation[i];_poseRotationFields[i].Editable=editable;_posePositionFields[i].Value=position[i];_posePositionFields[i].Editable=editable&&PoseCanMove(_poseBone);}
        _poseHipHeight.Value=hip;_poseHipHeight.Editable=editable;_poseKeepFeet.Disabled=!editable;_poseKeepFeet.ButtonPressed=_posePinFeet;
        _poseJointChoice.Disabled=!human;_poseMoveButton.Disabled=!editable||!PoseCanMove(_poseBone);_poseRotateButton.Disabled=!editable;_poseMoveButton.ButtonPressed=EffectivePoseTool=="Move";_poseRotateButton.ButtonPressed=EffectivePoseTool=="Rotate";
        _poseKeyJoint.Disabled=!human;_poseKeyAll.Disabled=!human;_poseKeyJoint.Text=human&&PoseCorrectionTimes(_selected!,_poseBone).Any(t=>Math.Abs(t-_time)<.5/_fps)?"◆ Key joint":"◇ Key joint";_poseSaveButton.Disabled=!human;_poseResetButton.Disabled=!editable;_poseDoneButton.Disabled=!editable;_refreshing=old;
    }
    async Task RunManualPoseChecksAndQuit()
    {
        try{await RunManualPoseChecks();GetTree().Quit(0);}catch(Exception ex){GD.PushError("MANUAL_POSE_FAILED "+ex);GetTree().Quit(1);}
    }
    async Task RunPoseCorrectionChecksAndQuit()
    {
        try{await RunPoseCorrectionChecks();GetTree().Quit(0);}catch(Exception ex){GD.PushError("POSE_CORRECTION_FAILED "+ex);GetTree().Quit(1);}
    }
}
