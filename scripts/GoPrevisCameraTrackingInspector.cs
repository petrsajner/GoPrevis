using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GoPrevis;

public partial class GoPrevisApp
{
    VBoxContainer? _trackingPanel,_trackingContent;
    Button? _trackingHeader;
    Button _trackingMatch=null!,_trackingResetAngle=null!;
    CheckButton _trackingFollow=null!,_trackingLook=null!,_trackingLocal=null!;
    OptionButton _trackingFollowTarget=null!,_trackingLookTarget=null!;
    SpinBox _trackingSide=null!,_trackingHeight=null!,_trackingDistance=null!,_trackingFollowSmooth=null!,_trackingLookSmooth=null!;
    readonly SpinBox[] _trackingAim=new SpinBox[3];
    Label _trackingNote=null!;
    bool _refreshingTracking,_trackingExpanded;
    string _lastTrackingTargetId="";
    string _trackingInspectorCameraId="";
    readonly Dictionary<OptionButton,string[]> _trackingTargetItems=new();

    void BuildCameraTrackingInspector()
    {
        _trackingPanel=new VBoxContainer();_trackingPanel.AddThemeConstantOverride("separation",4);_inspector.AddChild(_trackingPanel);
        _trackingHeader=Button("▸ Follow / Look at · Off",()=>{_trackingExpanded=!_trackingExpanded;RefreshCameraTrackingInspector();},true);_trackingHeader.Alignment=HorizontalAlignment.Left;_trackingHeader.ClipText=true;_trackingPanel.AddChild(_trackingHeader);
        _trackingContent=new VBoxContainer();_trackingContent.AddThemeConstantOverride("separation",5);_trackingPanel.AddChild(_trackingContent);
        _trackingFollow=new CheckButton{Text="Follow"};_trackingFollowTarget=new OptionButton{FitToLongestItem=false,ClipText=true,SizeFlagsHorizontal=SizeFlags.ExpandFill};_trackingContent.AddChild(Row(_trackingFollow,_trackingFollowTarget));
        _trackingSide=Spin(-1000,1000,.001,0);_trackingHeight=Spin(-1000,1000,.001,3);_trackingDistance=Spin(-1000,1000,.001,6);
        void Field(string label,Control value)=>_trackingContent.AddChild(Row(Text(label,11,true),value));
        Field("Side (m)",_trackingSide);Field("Height (m)",_trackingHeight);Field("Distance (m)",_trackingDistance);
        _trackingDistance.TooltipText="Positive distance is behind the target; negative distance is in front of it.";
        _trackingFollowSmooth=Spin(0,5,.01,0);_trackingFollowSmooth.TooltipText="Follow lag in seconds. Zero follows exactly; larger values ease the camera behind movement.";Field("Follow smoothness (s)",_trackingFollowSmooth);
        _trackingLook=new CheckButton{Text="Look at"};_trackingLookTarget=new OptionButton{FitToLongestItem=false,ClipText=true,SizeFlagsHorizontal=SizeFlags.ExpandFill};_trackingContent.AddChild(Row(_trackingLook,_trackingLookTarget));
        _trackingContent.AddChild(Text("Aim offset (m) · X / Y / Z",11,true));var aim=new HBoxContainer();aim.AddThemeConstantOverride("separation",3);
        for(int i=0;i<3;i++){int axis=i;_trackingAim[i]=Spin(-1000,1000,.001,i==1?.8:0);_trackingAim[i].CustomMinimumSize=new Vector2(62,0);_trackingAim[i].ValueChanged+=value=>EditCameraTracking(data=>data.LookAtOffset[axis]=(float)value);aim.AddChild(_trackingAim[i]);}_trackingContent.AddChild(aim);
        _trackingLookSmooth=Spin(0,5,.01,0);Field("Look smoothness (s)",_trackingLookSmooth);
        _trackingLocal=new CheckButton{Text="Target-relative offsets",TooltipText="Offsets rotate with target heading and stay in metres. Turn off for fixed world axes."};_trackingContent.AddChild(_trackingLocal);
        _trackingMatch=Button("Match current view",MatchTrackingCurrentView);_trackingResetAngle=Button("Reset angle",()=>EditCameraTracking(data=>data.RotationOffset=new float[3]));_trackingContent.AddChild(Row(_trackingMatch,_trackingResetAngle));
        _trackingNote=Text("Follow controls position; Look at controls rotation. Existing camera keys are kept and resume when their constraint is off. Use offsets or the viewport to adjust the live camera.",10,true);_trackingNote.AutowrapMode=TextServer.AutowrapMode.WordSmart;_trackingContent.AddChild(_trackingNote);
        _trackingFollow.Toggled+=value=>EditCameraTracking(data=>{data.FollowEnabled=value;if(value&&string.IsNullOrEmpty(data.FollowTargetId))data.FollowTargetId=DefaultTrackingTarget();});
        _trackingLook.Toggled+=value=>EditCameraTracking(data=>{data.LookAtEnabled=value;if(value&&string.IsNullOrEmpty(data.LookAtTargetId))data.LookAtTargetId=!string.IsNullOrEmpty(data.FollowTargetId)?data.FollowTargetId:DefaultTrackingTarget();});
        _trackingFollowTarget.ItemSelected+=index=>EditCameraTracking(data=>data.FollowTargetId=_trackingFollowTarget.GetItemMetadata((int)index).AsString());
        _trackingLookTarget.ItemSelected+=index=>EditCameraTracking(data=>data.LookAtTargetId=_trackingLookTarget.GetItemMetadata((int)index).AsString());
        _trackingSide.ValueChanged+=value=>EditCameraTracking(data=>data.FollowOffset[0]=(float)value);_trackingHeight.ValueChanged+=value=>EditCameraTracking(data=>data.FollowOffset[1]=(float)value);_trackingDistance.ValueChanged+=value=>EditCameraTracking(data=>data.FollowOffset[2]=(float)value);
        _trackingFollowSmooth.ValueChanged+=value=>EditCameraTracking(data=>data.FollowSmoothness=value);_trackingLookSmooth.ValueChanged+=value=>EditCameraTracking(data=>data.LookAtSmoothness=value);
        _trackingLocal.Toggled+=value=>EditCameraTracking(data=>data.TargetRelativeOffsets=value);
        RefreshCameraTrackingInspector();
    }
    string DefaultTrackingTarget()=>_entries.Any(entry=>entry.Data.Id==_lastTrackingTargetId)?_lastTrackingTargetId:_entries.FirstOrDefault()?.Data.Id??"";
    void EditCameraTracking(Action<CameraTrackingData> edit)
    {
        if(_refreshing||_refreshingTracking||_selectedCamera==null)return;var next=_selectedCamera.Tracking.ValidatedCopy();edit(next);next=next.ValidatedCopy();PushUndo();_selectedCamera.Tracking=next;if(next.FollowEnabled)_selectedCamera.Movement.Mode="None";RefreshCameraMovementInspector();
        SetTime(_time);RefreshCameraTrackingInspector();Status("Camera Follow / Look at updated. Target motion is evaluated live.");
    }
    void MatchTrackingCurrentView()
    {
        if(_selectedCamera is not {} camera)return;PushUndo();var position=_editorCamera.GlobalPosition;var rotation=_editorCamera.GlobalRotationDegrees;
        bool changed=TryEditConstrainedCamera(camera,"Position",position)|TryEditConstrainedCamera(camera,"Rotation",rotation);
        if(!changed){Status("Enable Follow or Look at and choose a target first.");return;}SetTime(_time);RefreshCameraTrackingInspector();
    }
    void RefreshCameraTrackingInspector()
    {
        if(_trackingPanel==null)return;var camera=_selectedCamera;_trackingPanel.Visible=camera!=null&&_inspectorSection=="Object";if(camera==null)return;
        if(_trackingInspectorCameraId!=camera.Id){_trackingInspectorCameraId=camera.Id;_trackingExpanded=camera.Tracking.FollowEnabled||camera.Tracking.LookAtEnabled;}
        bool previous=_refreshingTracking;_refreshingTracking=true;
        var data=camera.Tracking;
        string Target(string id)=>_entries.FirstOrDefault(entry=>entry.Data.Id==id)?.Data.Name??"Missing target";
        string description=data.FollowEnabled||data.LookAtEnabled?string.Join(" / ",new[]{data.FollowEnabled?Target(data.FollowTargetId):null,data.LookAtEnabled?Target(data.LookAtTargetId):null}.Where(value=>value!=null).Distinct()):"Off";
        _trackingHeader!.Text=(_trackingExpanded?"▾ ":"▸ ")+"Follow / Look at · "+description;_trackingHeader.ButtonPressed=_trackingExpanded;_trackingContent!.Visible=_trackingExpanded;
        void Targets(OptionButton option,string id)
        {
            bool missing=!string.IsNullOrEmpty(id)&&!_entries.Any(entry=>entry.Data.Id==id);
            var items=new string[2*(1+_entries.Count+(missing?1:0))];items[0]="";items[1]="Choose object…";
            for(int i=0;i<_entries.Count;i++){items[2*(i+1)]=_entries[i].Data.Id;items[2*(i+1)+1]=_entries[i].Data.Name;}
            if(missing){items[^2]=id;items[^1]="Missing target";}
            if(!_trackingTargetItems.TryGetValue(option,out var previousItems)||!items.SequenceEqual(previousItems))
            {
                option.Clear();for(int i=0;i<items.Length;i+=2){option.AddItem(items[i+1]);option.SetItemMetadata(i/2,items[i]);}
                _trackingTargetItems[option]=items;
            }
            int selected=0;for(int i=0;i<items.Length;i+=2)if(items[i]==id){selected=i/2;break;}
            if(option.Selected!=selected)option.Select(selected);
        }
        Targets(_trackingFollowTarget,data.FollowTargetId);Targets(_trackingLookTarget,data.LookAtTargetId);
        _trackingFollow.ButtonPressed=data.FollowEnabled;_trackingLook.ButtonPressed=data.LookAtEnabled;_trackingLocal.ButtonPressed=data.TargetRelativeOffsets;
        _trackingSide.Value=data.FollowOffset[0];_trackingHeight.Value=data.FollowOffset[1];_trackingDistance.Value=data.FollowOffset[2];_trackingFollowSmooth.Value=data.FollowSmoothness;
        _trackingDistance.TooltipText=data.TargetRelativeOffsets?"Positive distance is behind the target; negative distance is in front of it.":"World-space Z offset: positive moves toward +Z and negative toward -Z, independently of target heading.";
        for(int i=0;i<3;i++)_trackingAim[i].Value=data.LookAtOffset[i];_trackingLookSmooth.Value=data.LookAtSmoothness;
        _trackingSide.Editable=_trackingHeight.Editable=_trackingDistance.Editable=_trackingFollowSmooth.Editable=data.FollowEnabled;
        foreach(var field in _trackingAim)field.Editable=data.LookAtEnabled;_trackingLookSmooth.Editable=data.LookAtEnabled;
        _trackingMatch.Disabled=!CameraFollowActive(camera)&&!CameraLookAtActive(camera);_trackingResetAngle.Disabled=!data.LookAtEnabled;
        _refreshingTracking=previous;
    }
}
