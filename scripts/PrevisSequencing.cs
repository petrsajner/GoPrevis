using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Previs;

public partial class PrevisApp
{
    sealed class CameraEntry
    {
        public string Id=Guid.NewGuid().ToString("N");
        public string Name="Camera";
        public StudioCameraRig Rig=null!;
    }
    readonly List<CameraEntry> _cameras=new();
    string ActiveCameraId=>_cameras.FirstOrDefault(c=>c.Rig==_shot)?.Id??"";
    string ActiveCameraName=>_cameras.FirstOrDefault(c=>c.Rig==_shot)?.Name??"Camera";
    OptionButton _cameraChoice=null!,_clipChoice=null!;
    StudioLighting _lighting=null!;
    SpinBox _clipStart=null!,_clipLength=null!,_clipOffset=null!,_clipBlend=null!,_pathStart=null!,_pathDuration=null!;
    CheckButton _usePath=null!,_facePath=null!;
    Label _pathInfo=null!;
    Label _motionHeader=null!,_pathHeader=null!;
    HBoxContainer _inspectorTabs=null!;
    OptionButton _pathKeyChoice=null!;
    SpinBox _pathKeyTime=null!;
    int _selectedPathPoint;
    string _inspectorSection="Object";
    Button _drawPathButton=null!;
    string _selectedClipId="";
    bool _drawingPath;
    Entry? _pathEntry;
    ActorPathData? _pathBackup;
    MeshInstance3D? _pathLines;
    MotionClipData? SelectedClip=>_selected?.Data.MotionClips.FirstOrDefault(c=>c.Id==_selectedClipId);

    CameraEntry CreateCamera(string name,CameraTrackData? data,bool activate)
    {
        var item=new CameraEntry {Name=name,Id=data?.Id??Guid.NewGuid().ToString("N"),Rig=new StudioCameraRig()};
        _world.AddChild(item.Rig);item.Rig.ShotCamera.Current=false;
        if(data!=null)
        {
            item.Rig.GlobalPosition=Vector(data.Position,new Vector3(6,3,7));item.Rig.GlobalRotationDegrees=Vector(data.Rotation);item.Rig.FocalLengthMm=data.FocalLength;
            foreach(var k in data.Keys)if(k.Quaternion.Length==4)item.Rig.Keys.Add(new ShotCameraKey(k.Time,Vector(k.Position),new Quaternion(k.Quaternion[0],k.Quaternion[1],k.Quaternion[2],k.Quaternion[3]).Normalized(),k.FocalLength));
            item.Rig.Keys.Sort((a,b)=>a.TimeSeconds.CompareTo(b.TimeSeconds));
        }
        var label=new Label3D {Text=name,Position=new Vector3(0,.45f,0),FontSize=28,PixelSize=.0025f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,Layers=2,NoDepthTest=false};item.Rig.AddChild(label);
        _cameras.Add(item);if(activate||_shot==null)_shot=item.Rig;RefreshCameraChoices();return item;
    }
    void RefreshCameraChoices()
    {
        if(_cameraChoice==null)return;bool old=_refreshing;_refreshing=true;_cameraChoice.Clear();
        for(int i=0;i<_cameras.Count;i++){_cameraChoice.AddItem(_cameras[i].Name);if(_cameras[i].Rig==_shot)_cameraChoice.Select(i);}
        _refreshing=old;RefreshTimelineSize();
    }
    void SetActiveCamera(string id)
    {
        var camera=_cameras.FirstOrDefault(c=>c.Id==id);if(camera==null)return;
        _shot=camera.Rig;RefreshCameraChoices();if(_previewCamera!=null){SetTime(_time);if(_pilot)_editorCamera.GlobalTransform=_shot.GlobalTransform;UpdatePreview();}
    }
    void AddCameraFromView(bool duplicate)
    {
        PushUndo();string name="Camera "+NextCameraName();CameraTrackData? copy=duplicate?SnapshotCamera(_cameras.First(c=>c.Rig==_shot)):null;
        if(copy!=null){copy.Id=Guid.NewGuid().ToString("N");copy.Name=name;}
        var item=CreateCamera(name,copy,true);if(copy==null){item.Rig.GlobalTransform=_editorCamera.GlobalTransform;item.Rig.CaptureKey(_time);}
        SetActiveCamera(item.Id);if(_category=="Cameras")ShowCategory("Cameras");Status(name+" added. Each camera keeps its own keys and lens.");
    }
    string NextCameraName(){int n=_cameras.Count;string value;do{value=n<26?((char)('A'+n)).ToString():(n+1).ToString();n++;}while(_cameras.Any(c=>c.Name=="Camera "+value));return value;}
    void RemoveActiveCamera()
    {
        if(_cameras.Count<=1){Status("A scene must keep at least one camera.");return;}PushUndo();var item=_cameras.First(c=>c.Rig==_shot);_cameras.Remove(item);_world.RemoveChild(item.Rig);item.Rig.QueueFree();SetActiveCamera(_cameras[0].Id);if(_category=="Cameras")ShowCategory("Cameras");Status(item.Name+" removed.");
    }
    void ClearCameras(){foreach(var c in _cameras){_world.RemoveChild(c.Rig);c.Rig.QueueFree();}_cameras.Clear();}
    CameraTrackData SnapshotCamera(CameraEntry item)=>new(){Id=item.Id,Name=item.Name,Position=Array3(item.Rig.GlobalPosition),Rotation=Array3(item.Rig.GlobalRotationDegrees),FocalLength=item.Rig.FocalLengthMm,Keys=item.Rig.Keys.Select(k=>new CameraData{Time=k.TimeSeconds,Position=Array3(k.Position),Quaternion=new[]{k.Rotation.X,k.Rotation.Y,k.Rotation.Z,k.Rotation.W},FocalLength=k.FocalLengthMm}).ToList()};
    void BuildCameraLibrary()
    {
        foreach(var camera in _cameras){var captured=camera;_library.AddChild(Button(camera.Name+" · "+camera.Rig.Keys.Count+" keys",()=>SetActiveCamera(captured.Id)));}
        _library.AddChild(Row(Button("Add camera",()=>AddCameraFromView(false)),Button("Duplicate",()=>AddCameraFromView(true))));
        var rename=new LineEdit {Text=ActiveCameraName,PlaceholderText="Camera name",TooltipText="Press Enter to rename the active camera"};
        rename.TextSubmitted+=value=>{if(string.IsNullOrWhiteSpace(value))return;PushUndo();var entry=_cameras.First(c=>c.Rig==_shot);entry.Name=value.Trim();foreach(var label in entry.Rig.GetChildren().OfType<Label3D>())label.Text=entry.Name;RefreshCameraChoices();UpdatePreview();ShowCategory("Cameras");};_library.AddChild(rename);
        _library.AddChild(Text("FRAMING",12,true));foreach(var p in new[]{"Wide shot","Medium shot","Close-up","High angle","Current view"}){string choice=p;_library.AddChild(Button(p,()=>ApplyShot(choice)));}
        _library.AddChild(Button("Clear camera keys",()=>{PushUndo();_shot.ClearKeys();_timeline.QueueRedraw();}));
    }

    void BuildSequenceInspector()
    {
        _motionHeader=Text("MOTION CLIPS",12,true);_inspector.AddChild(_motionHeader);
        _clipInfo=Text("Select a human actor to append motion clips.",11,true);_clipInfo.AutowrapMode=TextServer.AutowrapMode.WordSmart;_inspector.AddChild(_clipInfo);
        _clipChoice=new OptionButton{FitToLongestItem=false,ClipText=true};_clipChoice.ItemSelected+=i=>{if(_refreshing||_selected==null||i<0||i>=_selected.Data.MotionClips.Count)return;_selectedClipId=_selected.Data.MotionClips[(int)i].Id;RefreshSequenceInspector();_timeline.QueueRedraw();};_inspector.AddChild(_clipChoice);
        _inspector.AddChild(Row(Button("Append BVH…",ImportMotion),Button("Remove clip",RemoveSelectedClip)));
        void Field(string name,SpinBox field){_inspector.AddChild(Row(Text(name,11,true),field));}
        _clipStart=Spin(0,120,1.0/_fps,0);Field("Start (s)",_clipStart);
        _clipLength=Spin(.04,120,1.0/_fps,3);Field("Duration (s)",_clipLength);
        _clipOffset=Spin(0,3600,1.0/_fps,0);Field("Source offset (s)",_clipOffset);
        _clipBlend=Spin(0,30,.05,.3);Field("Blend in (s)",_clipBlend);
        _speed=Spin(.1,4,.1,1);Field("Speed",_speed);
        _sourceScale=Spin(.0001,10,.001,.01);Field("Source metres/unit",_sourceScale);
        _clipStart.ValueChanged+=v=>EditClip(c=>c.Start=v);_clipLength.ValueChanged+=v=>EditClip(c=>c.Duration=v);_clipOffset.ValueChanged+=v=>EditClip(c=>c.SourceOffset=v);
        _clipBlend.ValueChanged+=v=>EditClip(c=>{c.BlendIn=v;var prev=_selected!.Data.MotionClips.Where(p=>p.Id!=c.Id&&p.Start<=c.Start).OrderBy(p=>p.Start).LastOrDefault();if(prev!=null)c.Start=Math.Max(0,prev.Start+prev.Duration-Math.Min(v,Math.Min(prev.Duration,c.Duration)));});
        _speed.ValueChanged+=v=>{if(_selected?.Node is RiggedActor)EditClip(c=>c.Speed=v);else if(!_refreshing&&_selected!=null){_selected.Data.Speed=v;SetTime(_time);}};
        _sourceScale.ValueChanged+=v=>EditClip(c=>c.SourceScale=(float)v);
        _loop=new CheckButton {Text="Loop source"};_inPlace=new CheckButton {Text="In-place clip"};_loop.Toggled+=v=>{if(_selected?.Node is RiggedActor)EditClip(c=>c.Loop=v);else if(!_refreshing&&_selected!=null){_selected.Data.Loop=v;SetTime(_time);}};_inPlace.Toggled+=v=>EditClip(c=>c.InPlace=v);_inspector.AddChild(_loop);_inspector.AddChild(_inPlace);
        _animationChoice=new OptionButton();_animationChoice.ItemSelected+=i=>{if(_refreshing||_selected?.Player==null)return;PushUndo();_selected.Animation=_animationChoice.GetItemText((int)i);_selected.Player.Play(_selected.Animation);SetTime(_time);};_inspector.AddChild(_animationChoice);
        _pathHeader=Text("CHARACTER PATH",12,true);_inspector.AddChild(_pathHeader);
        _pathInfo=Text("No path drawn.",11,true);_pathInfo.AutowrapMode=TextServer.AutowrapMode.WordSmart;_inspector.AddChild(_pathInfo);
        _usePath=new CheckButton{Text="Move along path"};_usePath.Toggled+=v=>{if(_refreshing||_selected==null)return;PushUndo();_selected.Data.Path.Enabled=v;SetTime(_time);RefreshSequenceInspector();};_inspector.AddChild(_usePath);
        _inspector.AddChild(Row(Button("Draw / replace",StartDrawingPath),Button("Clear",ClearSelectedPath)));
        _pathStart=Spin(0,120,1.0/_fps,0);Field("Path start (s)",_pathStart);_pathDuration=Spin(.1,120,.1,8);Field("Travel time (s)",_pathDuration);
        _pathStart.ValueChanged+=v=>EditPath(p=>p.Start=v);_pathDuration.ValueChanged+=v=>EditPath(p=>{var times=ActorPathRuntime.PointTimes(p);double old=Math.Max(.0001,p.Duration);p.PointTimes=times.Select(t=>t*v/old).ToList();p.Duration=v;});
        _facePath=new CheckButton{Text="Face travel direction"};_facePath.Toggled+=v=>EditPath(p=>p.OrientToPath=v);_inspector.AddChild(_facePath);
        _inspector.AddChild(Text("PATH KEYFRAMES",12,true));_pathKeyChoice=new OptionButton{FitToLongestItem=false,ClipText=true};_pathKeyChoice.ItemSelected+=i=>{if(!_refreshing&&_selected!=null)SelectPathKey(_selected,(int)i);};_inspector.AddChild(_pathKeyChoice);
        _pathKeyTime=Spin(0,120,1.0/_fps,0);Field("Arrival time (s)",_pathKeyTime);_pathKeyTime.ValueChanged+=v=>{if(!_refreshing&&_selected!=null)SetPathKeyTime(_selected,_selectedPathPoint,v,true);};
        _inspector.AddChild(Text("Drag the diamond keys on the Path track to change arrival times.",11,true));
        ShowInspectorSection("Object");
    }
    void ShowInspectorSection(string section)
    {
        _inspectorSection=section;string current="Object";
        foreach(var node in _inspector.GetChildren().OfType<Control>()){if(node==_inspectorTabs){node.Visible=true;continue;}if(node==_motionHeader)current="Motion";if(node==_pathHeader)current="Path";node.Visible=current==section;}
        if(_animationChoice!=null)_animationChoice.Visible=section=="Motion"&&_selected?.Player!=null;
        if(_clipChoice!=null)_clipChoice.Visible=section=="Motion"&&_selected?.Node is RiggedActor;
    }
    void RefreshSequenceInspector()
    {
        if(_clipChoice==null)return;bool old=_refreshing;_refreshing=true;var entry=_selected;bool human=entry?.Node is RiggedActor;var clips=entry?.Data.MotionClips;
        _clipChoice.Clear();if(clips!=null)for(int i=0;i<clips.Count;i++){var c=clips[i];_clipChoice.AddItem($"{i+1}. {c.Name}");if(c.Id==_selectedClipId)_clipChoice.Select(i);}
        if(clips?.Count>0&&clips.All(c=>c.Id!=_selectedClipId)){_selectedClipId=clips[0].Id;_clipChoice.Select(0);}
        var clip=SelectedClip;_clipChoice.Visible=human;_animationChoice.Visible=entry?.Player!=null;
        _clipStart.Value=clip?.Start??0;_clipLength.Value=clip?.Duration??3;_clipOffset.Value=clip?.SourceOffset??0;_clipBlend.Value=clip?.BlendIn??0;_sourceScale.Value=clip?.SourceScale??entry?.Data.SourceScale??.01f;
        _speed.Value=clip?.Speed??entry?.Data.Speed??1;_loop.ButtonPressed=clip?.Loop??entry?.Data.Loop??true;_inPlace.ButtonPressed=clip?.InPlace??false;
        foreach(var f in new[]{_clipStart,_clipLength,_clipOffset,_clipBlend,_sourceScale})f.Editable=clip!=null;
        _clipInfo.Text=human?$"{clips!.Count} clip(s) · overlap to crossfade\n"+(clip==null?"Append a BVH motion to begin.":$"{clip.Name}\n{clip.Start:0.##}–{clip.Start+clip.Duration:0.##} s · blend {clip.BlendIn:0.##} s") : entry?.Player!=null?"Imported rig · native animation":"Static blockout prop";
        var path=entry?.Data.Path;_pathInfo.Text=path?.Points.Count>0?$"{path.Points.Count} points · {ActorPathRuntime.Length(path):0.00} m\nPath controls travel; mocap controls body motion.":"No path drawn. Click Draw, place ground points, then Finish path.";
        _usePath.ButtonPressed=path?.Enabled??false;_facePath.ButtonPressed=path?.OrientToPath??true;_pathStart.Value=path?.Start??0;_pathDuration.Value=path?.Duration??_duration;
        _pathKeyChoice.Clear();if(path!=null){var times=ActorPathRuntime.PointTimes(path);_selectedPathPoint=Math.Clamp(_selectedPathPoint,0,Math.Max(0,path.Points.Count-1));for(int i=0;i<times.Length;i++)_pathKeyChoice.AddItem($"Point {i+1} · {path.Start+times[i]:0.##} s");if(times.Length>0){_pathKeyChoice.Select(_selectedPathPoint);_pathKeyTime.Value=path.Start+times[_selectedPathPoint];}}_pathKeyTime.Editable=path?.Points.Count>1;
        _usePath.Disabled=entry==null||path?.Points.Count<2;_drawPathButton.ButtonPressed=_drawingPath;
        _refreshing=old;
        ShowInspectorSection(_inspectorSection);
    }
    void EditClip(Action<MotionClipData> edit)
    {
        if(_refreshing||_selected?.Node is not RiggedActor||SelectedClip==null)return;PushUndo();var before=JsonSerializer.Deserialize<List<MotionClipData>>(JsonSerializer.Serialize(_selected.Data.MotionClips))!;
        try{edit(SelectedClip);RebuildTrack(_selected);EnsureDuration(_selected.Data.MotionClips.Max(c=>c.Start+c.Duration));SetTime(_time);RefreshSequenceInspector();}catch(Exception ex){_selected.Data.MotionClips=before;RebuildTrack(_selected);Error(ex.Message);RefreshSequenceInspector();}
    }
    void RemoveSelectedClip(){if(_selected==null||SelectedClip==null)return;PushUndo();_selected.Data.MotionClips.Remove(SelectedClip);_selectedClipId="";RebuildTrack(_selected);SetTime(_time);RefreshSequenceInspector();RefreshTimelineSize();}
    void RebuildTrack(Entry entry){if(entry.Node is RiggedActor actor)actor.SetMotionClips(entry.Data.MotionClips);entry.Data.MotionPath=entry.Data.MotionClips.FirstOrDefault()?.MotionPath;entry.Data.MotionName=entry.Data.MotionClips.FirstOrDefault()?.Name;RefreshTimelineSize();}
    void AppendMotion(Entry entry,string path,float scale)
    {
        var source=BvhMotion.FromFile(path);var previous=entry.Data.MotionClips.OrderBy(c=>c.Start).LastOrDefault();double duration=source.DurationSeconds;
        double blend=previous==null?0:Math.Min(.3,Math.Min(previous.Duration,duration)*.25);
        var clip=new MotionClipData{MotionPath=System.IO.Path.GetFullPath(path),Name=System.IO.Path.GetFileNameWithoutExtension(path),Duration=duration,Start=previous==null?Math.Max(0,_time):Math.Max(0,previous.Start+previous.Duration-blend),BlendIn=blend,SourceScale=scale};
        var candidate=new List<MotionClipData>(entry.Data.MotionClips){clip};((RiggedActor)entry.Node).SetMotionClips(candidate);entry.Data.MotionClips=candidate;entry.Data.MotionPath=clip.MotionPath;entry.Data.MotionName=clip.Name;entry.Data.SourceScale=scale;_selectedClipId=clip.Id;EnsureDuration(clip.Start+clip.Duration);RefreshTimelineSize();
    }
    void EnsureDuration(double end){if(end<=_duration)return;_duration=Math.Min(120,Math.Ceiling(end));_scrub.MaxValue=_duration;bool old=_refreshing;_refreshing=true;_durationField.Value=_duration;_refreshing=old;}
    void SelectTimelineClip(Entry entry,MotionClipData clip){_selectedClipId=clip.Id;Select(entry);ShowInspectorSection("Motion");RefreshSequenceInspector();}
    void RefreshTimelineSize(){if(_timeline!=null){int actors=_entries.Count(e=>e.Node is RiggedActor||e.Player!=null);int paths=_entries.Count(e=>(e.Node is RiggedActor||e.Player!=null)&&e.Data.Path.Points.Count>0);_timeline.CustomMinimumSize=new Vector2(0,32+(_cameras.Count+actors+paths)*30+24);_timeline.QueueRedraw();}}
    void SelectPathKey(Entry entry,int index)
    {
        Select(entry);_selectedPathPoint=Math.Clamp(index,0,Math.Max(0,entry.Data.Path.Points.Count-1));ShowInspectorSection("Path");var times=ActorPathRuntime.PointTimes(entry.Data.Path);if(times.Length>0)SetTime(entry.Data.Path.Start+times[_selectedPathPoint]);RefreshSequenceInspector();Status($"Path point {_selectedPathPoint+1} · drag its diamond to change arrival time.");
    }
    void SetPathKeyTime(Entry entry,int index,double absoluteTime,bool pushUndo)
    {
        var path=entry.Data.Path;var times=ActorPathRuntime.PointTimes(path);if(index<0||index>=times.Length)return;if(pushUndo)PushUndo();double step=1.0/_fps;
        double low=index==0?0:times[index-1]+step;double high=index==times.Length-1?Math.Max(low,_duration-path.Start):times[index+1]-step;if(high<low)high=low;
        times[index]=Math.Clamp(Math.Round((absoluteTime-path.Start)*_fps)/_fps,low,high);path.PointTimes=times.ToList();if(index==times.Length-1)path.Duration=Math.Max(step,times[^1]);_selectedPathPoint=index;SetTime(path.Start+times[index]);RefreshSequenceInspector();
    }

    void EditPath(Action<ActorPathData> edit){if(_refreshing||_selected==null)return;PushUndo();edit(_selected.Data.Path);SetTime(_time);RefreshSequenceInspector();}
    void StartDrawingPath()
    {
        if(_drawingPath){FinishDrawingPath();return;}
        if(_selected?.Node is not RiggedActor&&_selected?.Player==null){_drawPathButton.ButtonPressed=false;Status("Select a character before drawing a path.");return;}
        if(_pilot)TogglePilot();_playing=false;_play.Text="▶ Play";PushUndo();_pathEntry=_selected;_pathBackup=JsonSerializer.Deserialize<ActorPathData>(JsonSerializer.Serialize(_selected.Data.Path));_selected.Data.Path=new ActorPathData{Duration=_duration};_drawingPath=true;_drawPathButton.ButtonPressed=true;ShowInspectorSection("Path");DrawPaths();Status("Draw path · click ground points · Enter / Finish path to apply · Esc to cancel.");
    }
    void FinishDrawingPath()
    {
        if(!_drawingPath)return;_drawingPath=false;_drawPathButton.ButtonPressed=false;
        if(_pathEntry!=null){if(_pathEntry.Data.Path.Points.Count>=2){_pathEntry.Data.Path.Enabled=true;_pathEntry.Data.Path.PointTimes=ActorPathRuntime.PointTimes(_pathEntry.Data.Path).ToList();Status("Path applied. Drag its timeline keys to change arrival times.");}else{_pathEntry.Data.Path=_pathBackup??new();Status("A path needs at least two points; the previous path was kept.");}}
        _pathEntry=null;_pathBackup=null;RefreshTimelineSize();SetTime(_time);ShowInspectorSection("Path");RefreshSequenceInspector();
    }
    void CancelDrawingPath(){if(!_drawingPath)return;if(_pathEntry!=null)_pathEntry.Data.Path=_pathBackup??new();_drawingPath=false;_drawPathButton.ButtonPressed=false;_pathEntry=null;_pathBackup=null;SetTime(_time);RefreshSequenceInspector();Status("Path drawing cancelled.");}
    void ClearSelectedPath(){if(_selected==null)return;PushUndo();_selected.Data.Path=new ActorPathData{Duration=_duration};SetTime(_time);RefreshSequenceInspector();}
    bool HandlePathInput(InputEvent input)
    {
        if(!_drawingPath)return false;
        if(input is InputEventMouseButton{ButtonIndex:MouseButton.Left,Pressed:false})return true;
        if(input is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.Left} button)
        {
            Vector3 origin=_editorCamera.ProjectRayOrigin(button.Position),ray=_editorCamera.ProjectRayNormal(button.Position);if(Math.Abs(ray.Y)>.0001f){float t=-origin.Y/ray.Y;if(t>0&&_pathEntry!=null){var p=origin+ray*t;p.Y=0;if(Input.IsPhysicalKeyPressed(Key.Ctrl)){p.X=Mathf.Snapped(p.X,.25f);p.Z=Mathf.Snapped(p.Z,.25f);}_pathEntry.Data.Path.Points.Add(Array3(p));RefreshTimelineSize();DrawPaths();RefreshSequenceInspector();if(button.DoubleClick&&_pathEntry.Data.Path.Points.Count>=2)FinishDrawingPath();}}return true;
        }
        return false;
    }
    void DrawPaths()
    {
        if(_world==null)return;if(_pathLines==null){_pathLines=new MeshInstance3D{Layers=2,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,MaterialOverride=new StandardMaterial3D{AlbedoColor=new Color("b4a1f1"),ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,CullMode=BaseMaterial3D.CullModeEnum.Disabled}};_world.AddChild(_pathLines);}
        var mesh=new ImmediateMesh();bool any=false;
        foreach(var entry in _entries.Where(e=>e.Data.Path.Points.Count>0&&(e==_selected||e==_pathEntry)))
        {
            var points=ActorPathRuntime.SamplePolyline(entry.Data.Path,100);if(!any){mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);any=true;}for(int i=1;i<points.Length;i++){var a=points[i-1]+Vector3.Up*.035f;var b=points[i]+Vector3.Up*.035f;var side=(b-a).Cross(Vector3.Up).Normalized()*.027f;foreach(var p in new[]{a-side,a+side,b+side,a-side,b+side,b-side})mesh.SurfaceAddVertex(p);}
            foreach(var raw in entry.Data.Path.Points){var p=Vector(raw)+Vector3.Up*.04f;float s=.1f;foreach(var v in new[]{p+new Vector3(-s,0,-s),p+new Vector3(s,0,-s),p+new Vector3(s,0,s),p+new Vector3(-s,0,-s),p+new Vector3(s,0,s),p+new Vector3(-s,0,s)})mesh.SurfaceAddVertex(v);}
        }
        if(any)mesh.SurfaceEnd();_pathLines.Mesh=mesh;
    }
    void ApplyEntryPath(Entry entry)
    {
        var path=entry.Data.Path;entry.Node.Position=Vector(entry.Data.Position);entry.Node.RotationDegrees=Vector(entry.Data.Rotation);
        if(!path.Enabled||path.Points.Count<2)return;var sample=ActorPathRuntime.Evaluate(path,_time);entry.Node.Position=sample.Position;
        if(path.OrientToPath){var forward=entry.Node is RiggedActor actor?actor.AnimatedForward:Vector3.Forward;float yaw=Mathf.Atan2(sample.Tangent.X,sample.Tangent.Z)-Mathf.Atan2(forward.X,forward.Z);entry.Node.Rotation=new Vector3(Mathf.DegToRad(Vector(entry.Data.Rotation).X),yaw,Mathf.DegToRad(Vector(entry.Data.Rotation).Z));}
    }
    void MoveEntryBase(Entry entry,Vector3 position)
    {
        Vector3 delta=position-Vector(entry.Data.Position);entry.Data.Position=Array3(position);if(entry.Data.Path.Points.Count>0)foreach(var p in entry.Data.Path.Points){p[0]+=delta.X;p[1]+=delta.Y;p[2]+=delta.Z;}SetTime(_time);
    }
}
