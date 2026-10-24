using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Previs;

public partial class PrevisApp
{
    bool _poseEditing,_poseGestureActive,_posePinFeet=true;
    string _poseBone="Head",_poseTool="Rotate",_poseSelectionId="",_poseNameEntryId="";
    StaticPoseData? _poseEditBaseline;
    VBoxContainer _posePanel=null!,_posePreciseBox=null!;
    Button _poseToolbarButton=null!,_poseInspectorTab=null!,_editPoseShortcut=null!,_poseStartButton=null!,_poseMoveButton=null!,_poseRotateButton=null!;
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
        _posePanel.AddChild(Text("BODY POSE",12,true));
        _poseEditInfo=Text("Select a human figure.",11,true);_poseEditInfo.AutowrapMode=TextServer.AutowrapMode.WordSmart;_posePanel.AddChild(_poseEditInfo);
        _poseStartButton=Button("Edit pose",StartPoseEditing);_poseStartButton.TooltipText="Freeze the current frame for manual editing. Undo restores motion clips.";_posePanel.AddChild(_poseStartButton);
        _poseJointChoice=new OptionButton{FitToLongestItem=false,ClipText=true};_poseJointChoice.ItemSelected+=i=>{if(!_refreshing&&i>=0)SelectPoseBone(_poseJointChoice.GetItemMetadata((int)i).AsString());};_posePanel.AddChild(_poseJointChoice);
        _poseMoveButton=Button("Move",()=>{_poseTool="Move";UpdatePoseInspector();},true);_poseRotateButton=Button("Rotate",()=>{_poseTool="Rotate";UpdatePoseInspector();},true);_posePanel.AddChild(Row(_poseMoveButton,_poseRotateButton));
        var precise=Button("Precise values",()=>_posePreciseBox.Visible=!_posePreciseBox.Visible,true);_posePanel.AddChild(precise);_posePreciseBox=new VBoxContainer{Visible=false};_posePanel.AddChild(_posePreciseBox);
        _posePreciseBox.AddChild(Text("Rotation (local degrees)",11,true));_posePreciseBox.AddChild(PoseVectorFields(_poseRotationFields,-180,180,1,axis=>EditPoseNumber(()=>{var value=((RiggedActor)_selected!.Node).GetPoseBoneRotationDegrees(_poseBone);value[axis]=(float)_poseRotationFields[axis].Value;UpdatePoseBoneRotation(value);})));
        _posePositionLabel=Text("Target position (world metres)",11,true);_posePreciseBox.AddChild(_posePositionLabel);_posePreciseBox.AddChild(PoseVectorFields(_posePositionFields,-1000,1000,.001,axis=>EditPoseNumber(()=>{var actor=(RiggedActor)_selected!.Node;var value=actor.GlobalTransform*actor.GetPoseJointPosition(_poseBone);value[axis]=(float)_posePositionFields[axis].Value;UpdatePoseEffectorWorld(_poseBone,value);})));
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
    static bool PoseCanMove(string bone)=>bone is "Hips" or "LeftHand" or "RightHand" or "LeftFoot" or "RightFoot";
    void OpenPoseInspector(){ShowInspectorSection("Pose");UpdatePoseInspector();}
    void StartPoseEditing()
    {
        if(_selected?.Node is not RiggedActor actor){Status("Select a prepared human figure to edit its body pose.");return;}
        _playing=false;_play.Text="▶";
        if(!_poseEditing||_poseSelectionId!=_selected.Data.Id)
        {
            ReleasePoseViewportDrag();_poseGestureActive=false;
            _poseEditing=true;_poseSelectionId=_selected.Data.Id;_poseEditBaseline=actor.GetStaticPoseSnapshot()??actor.BakeCurrentPose(_selected.Data.Name+" pose");
        }
        if(actor.MotionTrackClipCount>0){PushUndo();FreezeFigureForPoseEditing(_selected);_poseEditBaseline=actor.GetStaticPoseSnapshot();}
        if(ProjectedActorBounds(_selected).Size.Y<260)
        {
            FrameSelected();var facing=actor.GlobalBasis*actor.AnimatedForward;facing.Y=0;
            if(facing.LengthSquared()>.00001f){_yaw=Mathf.Atan2(facing.X,facing.Z)+.45f;_pitch=.25f;UpdateOrbit();}
        }
        ShowInspectorSection("Pose");UpdatePoseInspector();UpdateSelection();Status("Edit pose · grab a hand or foot to move the whole limb.");
    }
    void StopPoseEditing()
    {
        ReleasePoseViewportDrag();_poseGestureActive=false;_poseEditing=false;_poseEditBaseline=null;UpdatePoseInspector();UpdateSelection();
    }
    void FreezeFigureForPoseEditing(Entry entry)
    {
        var actor=(RiggedActor)entry.Node;var beforeHip=actor.GlobalTransform*actor.GetPoseJointPosition("Hips");var beforeRotation=entry.Node.RotationDegrees;
        var frozen=actor.BakeCurrentPose(entry.Data.Name+" pose");actor.SetStaticPose(frozen);entry.Data.StaticPose=actor.GetStaticPoseSnapshot();entry.Data.MotionClips.Clear();entry.Data.MotionPath=null;entry.Data.MotionName=null;_selectedClipId="";RebuildTrack(entry);SetTime(_time);
        var rotationDelta=beforeRotation-entry.Node.RotationDegrees;for(int i=0;i<3;i++)rotationDelta[i]=Mathf.Wrap(rotationDelta[i],-180,180);entry.Data.Rotation=Array3(Vector(entry.Data.Rotation)+rotationDelta);SetTime(_time);
        var delta=beforeHip-actor.GlobalTransform*actor.GetPoseJointPosition("Hips");var parent=entry.Node.GetParentNode3D();if(parent!=null)delta=parent.GlobalBasis.Inverse()*delta;
        if(entry.Data.Path.Enabled)entry.Data.PathOffset=Array3(Vector(entry.Data.PathOffset)+delta);else entry.Data.Position=Array3(Vector(entry.Data.Position)+delta);SetTime(_time);RefreshSequenceInspector();
    }
    void BeginPoseGesture()
    {
        if(_poseGestureActive)return;if(!_poseEditing)StartPoseEditing();if(!_poseEditing||_selected?.Node is not RiggedActor actor)return;
        _playing=false;_play.Text="▶";PushUndo();_poseGestureActive=true;
        if(actor.MotionTrackClipCount>0)FreezeFigureForPoseEditing(_selected);
        if(!actor.HasStaticPose){actor.SetStaticPose(actor.BakeCurrentPose(_selected.Data.Name+" pose"));_selected.Data.StaticPose=actor.GetStaticPoseSnapshot();}
    }
    void EndPoseGesture(){_poseGestureActive=false;UpdatePoseInspector();}
    void SelectPoseBone(string bone)
    {
        if(_selected?.Node is not RiggedActor actor||!actor.PoseBoneNames.Contains(bone))return;_poseBone=bone;_poseTool=PoseCanMove(bone)?"Move":"Rotate";UpdatePoseInspector();
    }
    void UpdatePoseEffectorWorld(string bone,Vector3 world)
    {
        if(_selected?.Node is not RiggedActor actor||!PoseCanMove(bone))return;
        var local=actor.GlobalTransform.AffineInverse()*world;
        if(bone=="Hips")actor.MovePosePelvis(actor.GetPoseRootOffset()+local-actor.GetPoseJointPosition("Hips"),_posePinFeet);else actor.MovePoseEndEffector(bone,local);
        CommitEditedPose();
    }
    void UpdatePoseBoneRotation(Vector3 degrees)
    {
        if(_selected?.Node is not RiggedActor actor)return;actor.SetPoseBoneRotationDegrees(_poseBone,degrees);CommitEditedPose();
    }
    void CommitEditedPose()
    {
        if(_selected?.Node is not RiggedActor actor)return;var rotation=_selected.Node.RotationDegrees;_selected.Data.StaticPose=actor.GetStaticPoseSnapshot();SetTime(_time);
        // Pose edits stay above path facing: turning the pelvis must not be
        // cancelled by a new path heading or rotate the outer object instead.
        if(_selected.Data.Path.Enabled&&_selected.Data.Path.OrientToPath)
        {
            var delta=rotation-_selected.Node.RotationDegrees;for(int i=0;i<3;i++)delta[i]=Mathf.Wrap(delta[i],-180,180);
            if(delta.LengthSquared()>.000001f){_selected.Data.Rotation=Array3(Vector(_selected.Data.Rotation)+delta);SetTime(_time);}
        }
        UpdatePoseInspector();
    }
    void EditPoseNumber(Action update)
    {
        if(_refreshing||!_poseEditing||_selected?.Node is not RiggedActor)return;
        bool own=!_poseGestureActive;try{if(own)BeginPoseGesture();update();}catch(Exception ex){Error(ex.Message);}finally{if(own)EndPoseGesture();}
    }
    void ResetManualPoseEdits()
    {
        if(_selected?.Node is not RiggedActor actor||_poseEditBaseline==null)return;BeginPoseGesture();actor.SetStaticPose(_poseEditBaseline);CommitEditedPose();EndPoseGesture();
    }
    void SyncPoseEditorSelection()
    {
        if(_poseEditing&&(_selected?.Data.Id!=_poseSelectionId||_selected?.Node is not RiggedActor)){ReleasePoseViewportDrag();_poseEditing=false;_poseGestureActive=false;_poseEditBaseline=null;}
        UpdatePoseInspector();
    }
    void RestorePoseEditorSelection(string? id)
    {
        if(id==null)return;var entry=_entries.FirstOrDefault(e=>e.Data.Id==id&&e.Node is RiggedActor);if(entry==null)return;Select(entry);_poseEditing=true;_poseSelectionId=id;_poseEditBaseline=((RiggedActor)entry.Node).GetStaticPoseSnapshot()??((RiggedActor)entry.Node).BakeCurrentPose(entry.Data.Name+" pose");OpenPoseInspector();
    }
    void UpdatePoseInspector()
    {
        if(_posePanel==null)return;bool old=_refreshing;_refreshing=true;
        var actor=_selected?.Node as RiggedActor;bool human=actor!=null;
        _poseToolbarButton.Disabled=!human;_poseToolbarButton.ButtonPressed=_poseEditing&&human;_poseInspectorTab.Visible=human;_editPoseShortcut.Visible=human&&_inspectorSection=="Object";
        _poseStartButton.Visible=human&&!_poseEditing;_poseEditInfo.Text=!human?"Select a prepared human figure.":_poseEditing?"Drag a hand or foot: the whole limb follows. Drag the head to turn it.":actor!.MotionTrackClipCount>0?"Edit pose freezes this frame. Undo restores its motion clips.":"Edit this figure's head, body, arms and legs.";
        if(human&&_poseJointChoice.ItemCount!=actor!.PoseBoneNames.Count){_poseJointChoice.Clear();foreach(string bone in actor.PoseBoneNames){_poseJointChoice.AddItem(PoseBoneLabel(bone));_poseJointChoice.SetItemMetadata(_poseJointChoice.ItemCount-1,bone);}}
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
        _poseJointChoice.Disabled=!human;_poseMoveButton.Disabled=!editable||!PoseCanMove(_poseBone);_poseRotateButton.Disabled=!editable;_poseMoveButton.ButtonPressed=_poseTool=="Move";_poseRotateButton.ButtonPressed=_poseTool=="Rotate";
        _poseSaveButton.Disabled=!human;_poseResetButton.Disabled=!editable;_poseDoneButton.Disabled=!editable;_refreshing=old;
    }
    async Task RunManualPoseChecksAndQuit()
    {
        try{await RunManualPoseChecks();GetTree().Quit(0);}catch(Exception ex){GD.PushError("MANUAL_POSE_FAILED "+ex);GetTree().Quit(1);}
    }
}
