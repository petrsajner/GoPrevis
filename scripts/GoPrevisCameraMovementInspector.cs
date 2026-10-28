using Godot;
using System;
using System.Linq;

namespace GoPrevis;

public partial class GoPrevisApp
{
    VBoxContainer? _movementPanel,_movementContent;
    Button _movementHeader=null!;
    OptionButton _movementMode=null!,_movementTarget=null!,_movementEasing=null!,_movementPoint=null!,_movementCurve=null!;
    CheckButton _movementAim=null!,_movementAimPath=null!;
    SpinBox _movementRadius=null!,_movementAngle=null!,_movementSweep=null!,_movementHeight=null!,_movementEndHeight=null!,_movementStart=null!,_movementDuration=null!,_movementPointTime=null!;
    readonly SpinBox[] _movementPointPosition=new SpinBox[3];
    Control[] _orbitFields=Array.Empty<Control>();
    VBoxContainer _cameraPathFields=null!;
    bool _movementExpanded,_refreshingMovement;
    string[] _movementTargets=Array.Empty<string>();
    void BuildCameraMovementInspector()
    {
        _movementPanel=new VBoxContainer();_inspector.AddChild(_movementPanel);
        _movementHeader=Button("▸ Camera movement · Off",()=>{_movementExpanded=!_movementExpanded;RefreshCameraMovementInspector();},true);_movementHeader.Alignment=HorizontalAlignment.Left;_movementPanel.AddChild(_movementHeader);
        _movementContent=new VBoxContainer();_movementPanel.AddChild(_movementContent);
        _movementMode=new OptionButton();foreach(string mode in new[]{"None","Orbit","Path"})_movementMode.AddItem(mode);_movementContent.AddChild(_movementMode);
        _movementMode.ItemSelected+=index=>EditCameraMovement(data=>
        {
            data.Mode=_movementMode.GetItemText((int)index);
            if(data.Mode=="Orbit"&&_selectedCamera is {} camera)
            {
                data.TargetId=data.TargetId==""?DefaultTrackingTarget():data.TargetId;var center=CameraMovementCenter(camera,_time);var offset=camera.Rig.GlobalPosition-center;
                data.Radius=Math.Max(.5f,new Vector2(offset.X,offset.Z).Length());data.StartAngleDegrees=Mathf.RadToDeg(Mathf.Atan2(offset.X,offset.Z));data.StartHeight=data.EndHeight=offset.Y;
            }
            if(data.Mode=="Path"&&data.Path.Points.Count<2&&_selectedCamera is {} pathCamera)
            {var p=pathCamera.Rig.GlobalPosition;data.Path=new(){Enabled=true,Duration=_duration,Points=new(){Array3(p),Array3(p-pathCamera.Rig.GlobalBasis.Z*2)},PointTimes=new(){0,_duration}};}
        });
        _movementTarget=new OptionButton{FitToLongestItem=false,ClipText=true};_movementContent.AddChild(_movementTarget);_movementTarget.ItemSelected+=index=>EditCameraMovement(data=>data.TargetId=_movementTarget.GetItemMetadata((int)index).AsString());
        _movementAim=new CheckButton{Text="Aim at target"};_movementAim.Toggled+=value=>EditCameraMovement(data=>data.AimAtTarget=value);_movementContent.AddChild(_movementAim);
        _movementAimPath=new CheckButton{Text="Animated aim path"};_movementAimPath.Toggled+=value=>EditCameraMovement(data=>{data.AimPathEnabled=value;if(value)data.AimAtTarget=true;if(value&&data.AimPath.Points.Count<2){var center=CameraMovementCenter(_selectedCamera!,_time);data.AimPath=new(){Enabled=true,Start=data.Start,Duration=data.Duration,Points=new(){Array3(center),Array3(center)},PointTimes=new(){0,data.Duration}};}});_movementContent.AddChild(_movementAimPath);
        SpinBox Field(string label,double min,double max,double step,double value,Action<CameraMovementData,double> edit)
        {var spin=Spin(min,max,step,value);string property=label switch {"Start (s)"=>"Start","Duration (s)"=>"Duration","Radius (m)"=>"Radius","Start angle (°)"=>"StartAngleDegrees","Sweep (°)"=>"OrbitDegrees","Height above aim (m)"=>"StartHeight","End height (m)"=>"EndHeight",_=>""};_movementContent.AddChild(property==""?Row(Text(label,11,true),Space(),spin):Row(Text(label,11,true),Space(),spin,AnimatedParameterKey("Movement."+property)));spin.ValueChanged+=number=>EditCameraMovement(data=>edit(data,number));return spin;}
        _movementStart=Field("Start (s)",0,600,.01,0,(d,v)=>d.Start=v);_movementDuration=Field("Duration (s)",.01,600,.01,8,(d,v)=>d.Duration=v);
        _movementRadius=Field("Radius (m)",.1,2000,.01,8,(d,v)=>d.Radius=(float)v);_movementAngle=Field("Start angle (°)",-36000,36000,.1,0,(d,v)=>d.StartAngleDegrees=(float)v);_movementSweep=Field("Sweep (°)",-3600,3600,.1,360,(d,v)=>d.OrbitDegrees=(float)v);
        _movementHeight=Field("Height above aim (m)",-1000,1000,.01,2,(d,v)=>d.StartHeight=(float)v);_movementEndHeight=Field("End height (m)",-1000,1000,.01,2,(d,v)=>d.EndHeight=(float)v);
        _orbitFields=new[]{_movementRadius.GetParent<Control>(),_movementAngle.GetParent<Control>(),_movementSweep.GetParent<Control>(),_movementHeight.GetParent<Control>(),_movementEndHeight.GetParent<Control>()};
        _movementEasing=new OptionButton();foreach(string mode in KeyInterpolation.Modes)_movementEasing.AddItem(mode);_movementEasing.ItemSelected+=index=>EditCameraMovement(data=>data.Easing=_movementEasing.GetItemText((int)index));_movementContent.AddChild(_movementEasing);
        _cameraPathFields=new VBoxContainer();_movementContent.AddChild(_cameraPathFields);_movementCurve=new OptionButton();_movementCurve.AddItem("Camera position path");_movementCurve.AddItem("Camera aim path");_movementCurve.ItemSelected+=_=>RefreshCameraMovementInspector();_cameraPathFields.AddChild(_movementCurve);_movementPoint=new OptionButton();_cameraPathFields.AddChild(_movementPoint);_movementPoint.ItemSelected+=_=>RefreshCameraMovementInspector();
        _movementPointTime=Spin(0,600,.01,0);_cameraPathFields.AddChild(Row(Text("Arrival (s)",11,true),_movementPointTime));_movementPointTime.ValueChanged+=value=>EditCameraMovement(data=>{int i=_movementPoint.Selected;if(i>=0&&i<SelectedCameraCurve(data).PointTimes.Count)SelectedCameraCurve(data).PointTimes[i]=value-SelectedCameraCurve(data).Start;});
        var xyz=new HBoxContainer();for(int axis=0;axis<3;axis++){int a=axis;var field=_movementPointPosition[axis]=Spin(-1000,1000,.01,0);field.CustomMinimumSize=new Vector2(60,0);xyz.AddChild(field);field.ValueChanged+=value=>EditCameraMovement(data=>{int i=_movementPoint.Selected;if(i>=0&&i<SelectedCameraCurve(data).Points.Count)SelectedCameraCurve(data).Points[i][a]=(float)value;});}_cameraPathFields.AddChild(xyz);
        var note=Text("Orbit is an exact curve, without sampled transform keys. Shift transforms adjust the complete move. Disable movement to edit retained camera keys.",10,true);note.AutowrapMode=TextServer.AutowrapMode.WordSmart;_movementContent.AddChild(note);
    }
    void EditCameraMovement(Action<CameraMovementData> edit)
    {
        if(_refreshing||_refreshingMovement||_selectedCamera is not {} camera)return;
        try
        {
            var next=camera.Movement.ValidatedCopy();double oldDuration=next.Duration;edit(next);
            if(next.Start!=camera.Movement.Start||next.Duration!=oldDuration)
                foreach(var curve in new[]{next.Path,next.AimPath})
                {curve.Start=next.Start;curve.Duration=next.Duration;for(int i=0;i<curve.PointTimes.Count;i++)curve.PointTimes[i]*=next.Duration/oldDuration;}
            next=next.ValidatedCopy();PushUndo();camera.Movement=next;if(next.Mode!="None"){camera.Tracking.FollowEnabled=false;if(next.AimAtTarget)camera.Tracking.LookAtEnabled=false;}
            EnsureDuration(next.Start+next.Duration);SetTime(_time);RefreshCameraMovementInspector();RefreshCameraTrackingInspector();
        }
        catch(ArgumentException error){Status(error.Message);RefreshCameraMovementInspector();}
    }
    ActorPathData SelectedCameraCurve(CameraMovementData data)=>_movementCurve.Selected==1?data.AimPath:data.Path;
    void RefreshCameraMovementInspector()
    {
        if(_movementPanel==null)return;_movementPanel.Visible=_inspectorSection=="Object"&&_selectedCamera!=null;if(!_movementPanel.Visible)return;
        _refreshingMovement=true;var data=_selectedCamera!.Movement;
        _movementHeader.Text=(_movementExpanded?"▾ ":"▸ ")+"Camera movement · "+(data.Mode=="None"?"Off":data.Mode);_movementContent!.Visible=_movementExpanded;
        _movementMode.Select(data.Mode=="Orbit"?1:data.Mode=="Path"?2:0);
        var targets=new[]{"","World aim point"}.Concat(_entries.SelectMany(e=>new[]{e.Data.Id,e.Data.Name})).ToArray();
        if(!targets.SequenceEqual(_movementTargets)){_movementTargets=targets;_movementTarget.Clear();for(int i=0;i<targets.Length;i+=2){_movementTarget.AddItem(targets[i+1]);_movementTarget.SetItemMetadata(i/2,targets[i]);}}
        for(int i=0;i<_movementTarget.ItemCount;i++)if(_movementTarget.GetItemMetadata(i).AsString()==data.TargetId)_movementTarget.Select(i);
        _movementAim.ButtonPressed=data.AimAtTarget;_movementAimPath.ButtonPressed=data.AimPathEnabled;_movementStart.Value=data.Start;_movementDuration.Value=data.Duration;_movementRadius.Value=data.Radius;_movementAngle.Value=data.StartAngleDegrees;_movementSweep.Value=data.OrbitDegrees;_movementHeight.Value=data.StartHeight;_movementEndHeight.Value=data.EndHeight;
        _movementEasing.Select(Array.IndexOf(KeyInterpolation.Modes,data.Easing));foreach(var field in _orbitFields)field.Visible=data.Mode=="Orbit";_cameraPathFields.Visible=data.Mode=="Path"||data.AimPathEnabled;
        var selectedCurve=SelectedCameraCurve(data);int selected=Math.Clamp(_movementPoint.Selected,0,Math.Max(0,selectedCurve.Points.Count-1));if(_movementPoint.ItemCount!=selectedCurve.Points.Count){_movementPoint.Clear();for(int i=0;i<selectedCurve.Points.Count;i++)_movementPoint.AddItem("Point "+(i+1));}if(selectedCurve.Points.Count>0){_movementPoint.Select(selected);_movementPointTime.Value=selectedCurve.Start+selectedCurve.PointTimes[selected];for(int i=0;i<3;i++)_movementPointPosition[i].Value=selectedCurve.Points[selected][i];}
        _refreshingMovement=false;
    }
    void DrawCameraTrajectory(ImmediateMesh mesh,ref bool any)
    {
        if(_exportBusy||_selectedCamera is not {} camera||!CameraMovementActive(camera))return;
        Vector3 before=CameraBasePosition(camera,camera.Movement.Start);
        for(int i=1;i<=64;i++)
        {
            var p=CameraBasePosition(camera,camera.Movement.Start+camera.Movement.Duration*i/64);var tangent=p-before;var side=tangent.Cross(Vector3.Up);if(side.LengthSquared()<.000001f)side=tangent.Cross(Vector3.Right);side=side.Normalized()*.025f;
            if(!any){mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);any=true;}foreach(var vertex in new[]{before-side,before+side,p+side,before-side,p+side,p-side})mesh.SurfaceAddVertex(vertex);before=p;
        }
    }
}
