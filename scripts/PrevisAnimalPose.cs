using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Previs;

public partial class PrevisApp
{
    NativeMotionTrack? _animalGestureTrack;
    StaticPoseData? _animalGestureInitial;
    Vector3 SelectedPoseRotation()=>_selected?.NativeTrack is {} native?native.GetPoseBoneRotationDegrees(_poseBone):((RiggedActor)_selected!.Node).GetPoseBoneRotationDegrees(_poseBone);
    Vector3 SelectedPoseWorldPosition()=>_selected?.NativeTrack is {} native?native.GetPoseJointWorld(_poseBone):_selected!.Node.GlobalTransform*((RiggedActor)_selected.Node).GetPoseJointPosition(_poseBone);
    void StartAnimalPoseEditing()
    {
        if(_selected?.NativeTrack is not {} native)return;
        _playing=false;_play.Text="▶";SetTime(_time);
        if(!_poseEditing||_poseSelectionId!=_selected.Data.Id)
        {
            ReleasePoseViewportDrag();EndPoseGesture();_poseEditing=true;_poseSelectionId=_selected.Data.Id;_poseCorrectionBaseline=CopyPoseCorrections(_selected.Data.PoseCorrections);
            _poseBone=native.DefaultPoseBone;_poseTool=native.IsEndJoint(_poseBone)?"Move":"Rotate";
        }
        FrameSelected();ShowInspectorSection("Pose");UpdatePoseInspector();UpdateSelection();RefreshUnifiedWorkflow();
        Status("Animal pose · drag a joint to move its IK chain, or rotate it. Corrections stay above motion.");
    }
    void BeginAnimalPoseGesture()
    {
        if(_poseGestureActive)return;if(!_poseEditing)StartPoseEditing();
        if(!_poseEditing||_selected?.NativeTrack is not {} native)return;
        _playing=false;_play.Text="▶";PushUndo();SetTime(_time);_poseGestureActive=true;_animalGestureTrack=native;_animalGestureInitial=native.CaptureEvaluatedPose();_poseGestureBones.Clear();_poseGestureRoot=false;
    }
    void CommitAnimalPose()
    {
        if(_selected?.NativeTrack is not {} native)return;
        var source=native.CaptureSourcePose();var edited=native.CaptureEvaluatedPose();var before=_animalGestureInitial??source;
        foreach(string bone in PoseCorrectionRuntime.ChangedBones(before,edited))_poseGestureBones.Add(bone);
        _poseGestureRoot|=PoseCorrectionRuntime.RootChanged(before,edited);
        PoseCorrectionRuntime.WriteDifferences(_selected.Data.PoseCorrections,source,edited,_time,_poseGestureBones,_poseGestureRoot);
        SetTime(_time);RefreshTimelineSize();UpdatePoseInspector();
    }
    void CaptureAnimalPoseCorrectionKey(string channel)
    {
        if(_selected?.NativeTrack is not {} native)return;
        ReleasePoseViewportDrag();EndPoseGesture();SetTime(_time);PushUndo();
        PoseCorrectionRuntime.WriteDifferences(_selected.Data.PoseCorrections,native.CaptureSourcePose(),native.CaptureEvaluatedPose(),_time,
            channel=="All"?native.PoseBoneNames:channel=="Root"?Array.Empty<string>():new[]{channel},channel is "All" or "Root");
        SetTime(_time);RefreshTimelineSize();UpdatePoseInspector();Status("Animal pose correction key added.");
    }
    void UpdateAnimalPoseInspector()
    {
        var native=_selected!.NativeTrack!;bool old=_refreshing;_refreshing=true;
        if(!native.PoseBoneNames.Contains(_poseBone))_poseBone=native.DefaultPoseBone;
        _poseToolbarButton.Disabled=false;_poseToolbarButton.ButtonPressed=_poseEditing;_editPoseShortcut.Visible=_inspectorSection=="Object";_poseStartButton.Visible=!_poseEditing;
        _poseEditInfo.Text=_poseEditing?"Drag joints to move their ancestor IK chain. Move / Rotate or Shift changes tool. Edits key corrections above native motion.":"Edit this animal's head, spine, tail and limbs on its original rig.";
        if(_poseJointChoice.ItemCount!=native.PoseBoneNames.Count||Enumerable.Range(0,_poseJointChoice.ItemCount).Any(i=>_poseJointChoice.GetItemMetadata(i).AsString()!=native.PoseBoneNames[i]))
        {
            _poseJointChoice.Clear();foreach(string bone in native.PoseBoneNames){_poseJointChoice.AddItem(bone);_poseJointChoice.SetItemMetadata(_poseJointChoice.ItemCount-1,bone);}
        }
        for(int i=0;i<_poseJointChoice.ItemCount;i++)if(_poseJointChoice.GetItemMetadata(i).AsString()==_poseBone)_poseJointChoice.Select(i);
        Vector3 rotation=native.GetPoseBoneRotationDegrees(_poseBone),position=native.GetPoseJointWorld(_poseBone);
        for(int i=0;i<3;i++){_poseRotationFields[i].Value=rotation[i];_poseRotationFields[i].Editable=_poseEditing;_posePositionFields[i].Value=position[i];_posePositionFields[i].Editable=_poseEditing;}
        _poseHipHeight.GetParent<Control>().Visible=false;_poseKeepFeet.Visible=false;_poseJointChoice.Disabled=false;
        _poseMoveButton.Disabled=!_poseEditing;_poseRotateButton.Disabled=!_poseEditing;_poseMoveButton.ButtonPressed=EffectivePoseTool=="Move";_poseRotateButton.ButtonPressed=EffectivePoseTool=="Rotate";
        _poseKeyJoint.Disabled=false;_poseKeyAll.Disabled=false;_poseKeyJoint.Text=PoseCorrectionTimes(_selected,_poseBone).Any(t=>Math.Abs(t-_time)<.5/_fps)?"◆ Key joint":"◇ Key joint";
        if(_poseNameEntryId!=_selected.Data.Id){_poseNameEntryId=_selected.Data.Id;_poseSaveName.Text=_selected.Data.StaticPose?.Name??_selected.Data.Name+" pose";}
        _poseSaveButton.Disabled=string.IsNullOrWhiteSpace(_selected.Data.LibraryModelId);_poseSaveButton.TooltipText=_poseSaveButton.Disabled?"Saving requires a prepared library animal rig.":"Save this pose for this animal model.";
        _poseResetButton.Disabled=!_poseEditing;_poseDoneButton.Disabled=!_poseEditing;_refreshing=old;
    }
    void DrawAnimalPoseViewport(Control canvas,NativeMotionTrack native)
    {
        var points=new Dictionary<string,Vector2>();
        foreach(string name in native.PoseBoneNames)if(PoseViewportProject(native.GetPoseJointWorld(name),out var point))points[name]=point;
        foreach(var pair in points)
        {
            string? parent=native.ParentPoseBone(pair.Key);if(parent!=null&&points.TryGetValue(parent,out var start))canvas.DrawLine(start,pair.Value,new Color(.76f,.72f,.93f,.55f),1.4f,true);
            bool selected=pair.Key==_poseBone,hover=pair.Key==_poseViewportHoverBone;
            canvas.DrawCircle(pair.Value,selected?7:hover?6:4.5f,new Color("15151c"));canvas.DrawCircle(pair.Value,selected?5:3.2f,selected?new Color("f2d291"):hover?Colors.White:new Color("c9bff0"));
        }
        Vector3 origin=native.GetPoseJointWorld(_poseBone);if(!PoseViewportProject(origin,out var center))return;float length=PoseViewportLength(origin);bool move=EffectivePoseTool=="Move";
        if(move)
        {
            for(int axis=0;axis<3;axis++)if(PoseViewportProject(origin+PoseViewportAxisVector(axis)*length,out var end)&&(end-center).Length()>12)
            {
                var direction=(end-center).Normalized();var side=new Vector2(-direction.Y,direction.X);var color=PoseViewportAxisColors[axis];canvas.DrawLine(center+direction*10,end,color,2.2f,true);
                canvas.DrawColoredPolygon(new[]{end,end-direction*10+side*4,end-direction*10-side*4},color);
            }
        }
        else
        {
            for(int axis=0;axis<3;axis++){var ring=PoseViewportRing(origin,native.GetPoseJointWorldBasis(_poseBone),axis,length*.83f);for(int i=1;i<ring.Length;i++)if(ring[i-1].HasValue&&ring[i].HasValue)canvas.DrawLine(ring[i-1]!.Value,ring[i]!.Value,PoseViewportAxisColors[axis],1.5f,true);}
        }
        canvas.DrawString(ThemeDB.FallbackFont,center+new Vector2(12,-15),_poseBone,fontSize:12,modulate:new Color("f2dfb7"));
        canvas.DrawString(ThemeDB.FallbackFont,new Vector2(12,22),"ANIMAL POSE · "+_poseBone,fontSize:12,modulate:new Color("e1d7f7"));
        canvas.DrawString(ThemeDB.FallbackFont,new Vector2(12,41),(move?"MOVE · ancestor IK":"ROTATE · local joint")+" · Shift: alternate · Alt: fine · Ctrl: snap",fontSize:11,modulate:new Color("b7b2c4"));
    }
    int AnimalPoseHitAxis(NativeMotionTrack native,Vector2 point,bool move)
    {
        Vector3 origin=native.GetPoseJointWorld(_poseBone);if(!PoseViewportProject(origin,out var center)||point.DistanceTo(center)<12)return -1;
        float best=7,length=PoseViewportLength(origin);int hit=-1;
        for(int axis=0;axis<3;axis++)
        {
            float distance=float.PositiveInfinity;
            if(move){if(PoseViewportProject(origin+PoseViewportAxisVector(axis)*length,out var end)&&(end-center).Length()>12)distance=PoseViewportSegmentDistance(point,center+(end-center).Normalized()*12,end);}
            else {var ring=PoseViewportRing(origin,native.GetPoseJointWorldBasis(_poseBone),axis,length*.83f);for(int i=1;i<ring.Length;i++)if(ring[i-1].HasValue&&ring[i].HasValue)distance=Math.Min(distance,PoseViewportSegmentDistance(point,ring[i-1]!.Value,ring[i]!.Value));}
            if(distance<best){hit=axis;best=distance;}
        }
        return hit;
    }
    string? AnimalPoseHitJoint(NativeMotionTrack native,Vector2 point)
    {
        float nearest=12;string? hit=null;
        foreach(string bone in native.PoseBoneNames)if(PoseViewportProject(native.GetPoseJointWorld(bone),out var projected))
        {float distance=point.DistanceTo(projected);if(distance<nearest){nearest=distance;hit=bone;}}
        return hit;
    }
    bool HandleAnimalPoseViewportInput(InputEvent input,NativeMotionTrack native)
    {
        if(input is InputEventMouseButton{ButtonIndex:MouseButton.Left} button)
        {
            if(!button.Pressed){ReleasePoseViewportDrag();return true;}
            ReleasePoseViewportDrag();int axis=AnimalPoseHitAxis(native,button.Position,EffectivePoseTool=="Move");string? bone=axis>=0?_poseBone:AnimalPoseHitJoint(native,button.Position);if(bone==null)return true;
            if(bone!=_poseBone)SelectPoseBone(bone);
            _poseViewportActorId=_selected!.Data.Id;_poseViewportDragBone=bone;_poseViewportDragMouse=button.Position;_poseViewportAxis=axis;bool move=EffectivePoseTool=="Move";
            _poseViewportDrag=move?(axis<0?PoseViewportDrag.ScreenMove:PoseViewportDrag.AxisMove):(axis<0?PoseViewportDrag.FreeRotate:PoseViewportDrag.AxisRotate);return true;
        }
        if(input is InputEventMouseButton{ButtonIndex:MouseButton.Right or MouseButton.Middle,Pressed:true}){ReleasePoseViewportDrag();return false;}
        if(input is not InputEventMouseMotion motion)return false;
        _poseViewportHoverBone=AnimalPoseHitJoint(native,motion.Position)??"";
        if(!PoseViewportDragging)return false;if(_selected!.Data.Id!=_poseViewportActorId){ReleasePoseViewportDrag();return true;}
        if(!_poseViewportGestureStarted)
        {
            if(motion.Position.DistanceTo(_poseViewportDragMouse)<2)return true;BeginPoseGesture();_poseViewportGestureStarted=true;
            _poseViewportOrigin=native.GetPoseJointWorld(_poseViewportDragBone);_poseViewportStartRotation=native.GetPoseBoneRotation(_poseViewportDragBone);_poseViewportHandleLength=PoseViewportLength(_poseViewportOrigin);
            _poseViewportPlaneNormal=_editorCamera.GlobalBasis.Z.Normalized();_poseViewportPlaneStartValid=PoseViewportPlanePoint(_poseViewportDragMouse,_poseViewportOrigin,_poseViewportPlaneNormal,out _poseViewportPlaneStart);
            _poseViewportAxisWorld=_poseViewportDrag==PoseViewportDrag.AxisRotate?(native.GetPoseJointWorldBasis(_poseViewportDragBone)*PoseViewportAxisVector(_poseViewportAxis)).Normalized():PoseViewportAxisVector(_poseViewportAxis);
            _poseViewportAxisStartValid=PoseViewportRayAxis(_poseViewportDragMouse,_poseViewportOrigin,_poseViewportAxisWorld,out _poseViewportAxisStart);
            _poseViewportRotateStartValid=PoseViewportPlanePoint(_poseViewportDragMouse,_poseViewportOrigin,_poseViewportAxisWorld,out var start)&&(start-_poseViewportOrigin).LengthSquared()>.000001f;_poseViewportRotateStart=_poseViewportRotateStartValid?(start-_poseViewportOrigin).Normalized():Vector3.Right;
        }
        try
        {
            float fine=motion.AltPressed ? .2f : 1;
            if(_poseViewportDrag is PoseViewportDrag.ScreenMove or PoseViewportDrag.AxisMove)
            {
                Vector3 target=_poseViewportOrigin;
                if(_poseViewportDrag==PoseViewportDrag.ScreenMove){if(!_poseViewportPlaneStartValid||!PoseViewportPlanePoint(motion.Position,_poseViewportOrigin,_poseViewportPlaneNormal,out var point))return true;target+=(point-_poseViewportPlaneStart)*fine;}
                else
                {
                    float amount;if(_poseViewportAxisStartValid&&PoseViewportRayAxis(motion.Position,_poseViewportOrigin,_poseViewportAxisWorld,out var value))amount=value-_poseViewportAxisStart;
                    else{if(!PoseViewportProject(_poseViewportOrigin,out var a)||!PoseViewportProject(_poseViewportOrigin+_poseViewportAxisWorld*_poseViewportHandleLength,out var b)||(b-a).Length()<1)return true;amount=(motion.Position-_poseViewportDragMouse).Dot((b-a).Normalized())/(b-a).Length()*_poseViewportHandleLength;}
                    amount*=fine;if(motion.CtrlPressed)amount=Mathf.Snapped(amount,.01f);target+=_poseViewportAxisWorld*amount;
                }
                UpdatePoseEffectorWorld(_poseViewportDragBone,target);
            }
            else
            {
                Quaternion rotation;
                if(_poseViewportDrag==PoseViewportDrag.FreeRotate){Vector2 delta=(motion.Position-_poseViewportDragMouse)*.45f*fine;if(motion.CtrlPressed)delta=new Vector2(Mathf.Snapped(delta.X,5),Mathf.Snapped(delta.Y,5));rotation=_poseViewportStartRotation*new Quaternion(Vector3.Up,Mathf.DegToRad(delta.X))*new Quaternion(Vector3.Right,Mathf.DegToRad(-delta.Y));}
                else
                {
                    float angle;if(_poseViewportRotateStartValid&&PoseViewportPlanePoint(motion.Position,_poseViewportOrigin,_poseViewportAxisWorld,out var point)&&(point-_poseViewportOrigin).LengthSquared()>.000001f){Vector3 current=(point-_poseViewportOrigin).Normalized();angle=Mathf.Atan2(_poseViewportAxisWorld.Dot(_poseViewportRotateStart.Cross(current)),_poseViewportRotateStart.Dot(current));}else angle=(motion.Position-_poseViewportDragMouse).Dot(new Vector2(1,-1))*.012f;
                    angle*=fine;if(motion.CtrlPressed)angle=Mathf.Snapped(angle,Mathf.DegToRad(15));rotation=_poseViewportStartRotation*new Quaternion(PoseViewportAxisVector(_poseViewportAxis),angle);
                }
                UpdatePoseBoneRotation(new Basis(rotation.Normalized()).GetEuler()*180f/Mathf.Pi);
            }
        }
        catch(Exception error){ReleasePoseViewportDrag();Error(error.Message);}
        return true;
    }
}
