using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Previs;

public partial class PrevisApp
{
    CameraEntry? _selectedCamera;
    bool _autoKey;
    int ActiveCameraKeyCount(){var camera=_cameras.FirstOrDefault(c=>c.Rig==_shot);return camera==null?0:camera.Animation.Position.Concat(camera.Animation.Rotation).Concat(camera.Animation.Scale).Select(k=>k.Time).Concat(camera.LensKeys.Select(k=>k.Time)).Distinct().Count();}
    readonly Dictionary<string,Button> _parameterKeyButtons=new();
    readonly Dictionary<string,Label> _parameterLabels=new();

    void HandleObjectListSelection(long index)
    {
        if(index<0)return;if(index<_entries.Count)Select(_entries[(int)index]);else if(index-_entries.Count<_cameras.Count)SelectCamera(_cameras[(int)index-_entries.Count]);
    }
    void SelectCamera(CameraEntry camera)
    {
        if(_drawingPath)CancelDrawingPath();CancelPlacement();_selected=null;SyncPoseEditorSelection();_selectedCamera=camera;SetActiveCamera(camera.Id);_refreshing=true;_name.Text=camera.Name;
        _cameraQuickControls.Visible=true;ShowInspectorSection("Object");UpdateTransformFields();int i=_cameras.IndexOf(camera);if(i>=0)_objectList.Select(_entries.Count+i);_refreshing=false;UpdateSelection();RefreshAllObjectLabels();UpdateObjectAppearanceInspector();UpdatePlacementInspector();
    }
    void EnsureCameraAnimation(CameraEntry camera)
    {
        if(camera.AnimationInitialized)return;camera.AnimationInitialized=true;
        if(camera.Animation.Position.Count+camera.Animation.Rotation.Count+camera.Animation.Scale.Count+camera.LensKeys.Count>0)return;
        foreach(var key in camera.Rig.Keys)
        {
            TransformLayerRuntime.Upsert(camera.Animation.Position,key.TimeSeconds,key.Position);
            TransformLayerRuntime.Upsert(camera.Animation.Rotation,key.TimeSeconds,new Basis(key.Rotation).GetEuler()*180f/Mathf.Pi);
            camera.LensKeys.Add(new LensKeyData{Time=key.TimeSeconds,Value=key.FocalLengthMm});
        }
    }
    void EvaluateCamera(CameraEntry camera,double time)
    {
        EnsureCameraAnimation(camera);camera.Rig.GlobalPosition=TransformLayerRuntime.EvaluateVector(camera.Animation.Position,time,camera.BasePosition);
        camera.Rig.GlobalRotationDegrees=TransformLayerRuntime.EvaluateVector(camera.Animation.Rotation,time,camera.BaseRotation,true);
        camera.Rig.Scale=TransformLayerRuntime.EvaluateVector(camera.Animation.Scale,time,camera.BaseScale);
        camera.Rig.FocalLengthMm=EvaluateLens(camera,time);
    }
    float EvaluateLens(CameraEntry camera,double time)
    {
        var keys=camera.LensKeys.OrderBy(k=>k.Time).ToList();if(keys.Count==0)return camera.BaseLens;if(time<=keys[0].Time)return keys[0].Value;
        for(int i=1;i<keys.Count;i++)if(time<=keys[i].Time){var a=keys[i-1];var b=keys[i];return Mathf.Lerp(a.Value,b.Value,(float)((time-a.Time)/Math.Max(.00001,b.Time-a.Time)));}return keys[^1].Value;
    }
    void SyncCameraFromRig(CameraEntry camera)
    {
        camera.BasePosition=camera.Rig.GlobalPosition;camera.BaseRotation=camera.Rig.GlobalRotationDegrees;camera.BaseScale=camera.Rig.Scale;camera.BaseLens=camera.Rig.FocalLengthMm;
        camera.Animation=new();camera.LensKeys.Clear();camera.AnimationInitialized=false;EnsureCameraAnimation(camera);
    }
    void ClearCameraAnimation()
    {
        PushUndo();var camera=_cameras.First(c=>c.Rig==_shot);camera.BasePosition=camera.Rig.GlobalPosition;camera.BaseRotation=camera.Rig.GlobalRotationDegrees;camera.BaseScale=camera.Rig.Scale;camera.BaseLens=camera.Rig.FocalLengthMm;camera.Animation=new();camera.LensKeys.Clear();camera.Rig.ClearKeys();camera.AnimationInitialized=true;SetTime(_time);Status("Camera animation cleared.");
    }
    Vector3 ManualValue(Entry entry,string group)
    {
        var keys=TransformLayerRuntime.Channel(entry.Data.TransformAnimation,group);var value=TransformLayerRuntime.EvaluateVector(keys,_time,group=="Scale"?Vector3.One:Vector3.Zero,group=="Rotation");
        return group switch{"Position"=>(entry.Data.Path.Enabled?Vector(entry.Data.PathOffset):Vector(entry.Data.Position))+value,"Rotation"=>Vector(entry.Data.Rotation)+value,_=>Vector(entry.Data.Scale,Vector3.One)*value};
    }
    void UpdateTransformFields()
    {
        bool old=_refreshing;_refreshing=true;
        foreach(var pair in _fields)
        {
            string group=pair.Key[..^1];int axis=pair.Key[^1]-'0';Vector3 value=group=="Scale"?Vector3.One:Vector3.Zero;
            if(_selectedCamera!=null)value=group=="Position"?_selectedCamera.Rig.GlobalPosition:group=="Rotation"?_selectedCamera.Rig.GlobalRotationDegrees:_selectedCamera.Rig.Scale;
            else if(_selected!=null)value=ManualValue(_selected,group);
            pair.Value.Value=value[axis];pair.Value.Editable=_selectedCamera!=null||_selected!=null;
        }
        foreach(var pair in _parameterKeyButtons)
        {
            var animation=_selectedCamera?.Animation??_selected?.Data.TransformAnimation;
            bool keyed=animation!=null&&TransformLayerRuntime.Channel(animation,pair.Key).Any(k=>Math.Abs(k.Time-_time)<.5/_fps);
            pair.Value.Text=keyed?"◆":"◇";pair.Value.Disabled=animation==null;
        }
        foreach(var pair in _parameterLabels){bool offset=_selectedCamera==null&&_selected?.Data.Path.Enabled==true;pair.Value.Text=pair.Key+(offset&&pair.Key!="Scale"?" offset":"")+(pair.Key=="Position"?" (m)":pair.Key=="Rotation"?" (°)":"");}
        _refreshing=old;
    }
    void StorePilotedCameraPose()
    {
        var camera=_cameras.First(c=>c.Rig==_shot);EnsureCameraAnimation(camera);
        if(_autoKey||camera.Animation.Position.Count>0)TransformLayerRuntime.Upsert(camera.Animation.Position,_time,_shot.GlobalPosition);else camera.BasePosition=_shot.GlobalPosition;
        if(_autoKey||camera.Animation.Rotation.Count>0)TransformLayerRuntime.Upsert(camera.Animation.Rotation,_time,_shot.GlobalRotationDegrees);else camera.BaseRotation=_shot.GlobalRotationDegrees;
        UpdateTransformFields();_timeline.QueueRedraw();
    }
    void EditSelectedTransform(string group,int axis,float value)
    {
        if(_refreshing||_selected==null&&_selectedCamera==null)return;
        var vector=_selectedCamera is {} camera?group=="Position"?camera.Rig.GlobalPosition:group=="Rotation"?camera.Rig.GlobalRotationDegrees:camera.Rig.Scale:ManualValue(_selected!,group);
        vector[axis]=value;PushUndo();ApplySelectedManualTransform(group,vector);
    }

    void CaptureSelectedTransformKey(string group)
    {
        if(_selected==null&&_selectedCamera==null){Status("Select an object or camera to add a key.");return;}PushUndo();
        foreach(string channel in group=="All"?new[]{"Position","Rotation","Scale"}:group=="Lens"?Array.Empty<string>():new[]{group})
        {
            if(_selectedCamera is {} camera){EnsureCameraAnimation(camera);var value=channel=="Position"?camera.Rig.GlobalPosition:channel=="Rotation"?camera.Rig.GlobalRotationDegrees:camera.Rig.Scale;TransformLayerRuntime.Upsert(TransformLayerRuntime.Channel(camera.Animation,channel),_time,value);}
            else if(_selected is {} entry){var list=TransformLayerRuntime.Channel(entry.Data.TransformAnimation,channel);var correction=TransformLayerRuntime.EvaluateVector(list,_time,channel=="Scale"?Vector3.One:Vector3.Zero,channel=="Rotation");TransformLayerRuntime.Upsert(list,_time,correction);}
        }
        if(_selectedCamera is {} cam&&(group=="All"||group=="Lens"))UpsertLens(cam,_time,cam.Rig.FocalLengthMm);
        _timeline.QueueRedraw();UpdateTransformFields();Status(group+" key added at "+_time.ToString("0.###")+" s.");
    }
    void UpsertLens(CameraEntry camera,double time,float value){var existing=camera.LensKeys.FirstOrDefault(k=>Math.Abs(k.Time-time)<.00001);if(existing==null)camera.LensKeys.Add(new LensKeyData{Time=time,Value=value});else existing.Value=value;}
    void ChangeCameraLens(double value)
    {
        if(_refreshing||_shot==null)return;var camera=_cameras.FirstOrDefault(c=>c.Rig==_shot);if(camera==null)return;PushUndo();EnsureCameraAnimation(camera);if(_autoKey||camera.LensKeys.Count>0)UpsertLens(camera,_time,(float)value);else camera.BaseLens=(float)value;SetTime(_time);
    }
    void SelectTransformTimeline(Entry? entry,CameraEntry? camera,string channel,double time)
    {if(camera!=null)SelectCamera(camera);else Select(entry);SetTime(time);ShowInspectorSection("Object");}
    void RetimeTransform(Entry? entry,CameraEntry? camera,string channel,double from,double to)
    {
        var animation=camera?.Animation??entry?.Data.TransformAnimation;if(animation==null)return;
        foreach(string name in channel=="All"?new[]{"Position","Rotation","Scale"}:channel=="Lens"?Array.Empty<string>():new[]{channel})
        {var list=TransformLayerRuntime.Channel(animation,name);var key=list.FirstOrDefault(k=>Math.Abs(k.Time-from)<.00001);if(key!=null){list.RemoveAll(k=>k!=key&&Math.Abs(k.Time-to)<.00001);key.Time=to;}}
        if(camera!=null&&(channel=="Lens"||channel=="All")){var key=camera.LensKeys.FirstOrDefault(k=>Math.Abs(k.Time-from)<.00001);if(key!=null){camera.LensKeys.RemoveAll(k=>k!=key&&Math.Abs(k.Time-to)<.00001);key.Time=to;}}
        SetTime(to);
    }
    void RemoveTransformKey(Entry? entry,CameraEntry? camera,string channel,double time)
    {
        var animation=camera?.Animation??entry?.Data.TransformAnimation;if(animation==null)return;PushUndo();foreach(string name in channel=="All"?new[]{"Position","Rotation","Scale"}:channel=="Lens"?Array.Empty<string>():new[]{channel})TransformLayerRuntime.RemoveAtTime(TransformLayerRuntime.Channel(animation,name),time);
        if(camera!=null&&(channel=="Lens"||channel=="All"))camera.LensKeys.RemoveAll(k=>Math.Abs(k.Time-time)<.00001);SetTime(_time);
    }
}
