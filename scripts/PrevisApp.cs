using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Previs;

public partial class PrevisApp : Control
{
    sealed class Entry
    {
        public ObjectData Data = new();
        public Node3D Node = null!;
        public AnimationPlayer? Player;
        public string Animation = "";
        public NativeMotionTrack? NativeTrack;
    }
    readonly List<Entry> _entries = new();
    readonly Stack<(ProjectData Document, string Path)> _undo = new(), _redo = new();
    readonly Dictionary<string, Button> _tabs = new();
    readonly Dictionary<string, SpinBox> _fields = new();
    readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    readonly Color _bg = new("101013"), _panel = new("17171b"), _line = new("2b2b32"), _text = new("e9e8ed"), _muted = new("a5a3b0"), _accent = new("b3a1ef");
    SubViewport _sceneViewport = null!, _previewViewport = null!;
    SubViewportContainer _sceneContainer = null!;
    Node3D _world = null!;
    Camera3D _editorCamera = null!, _previewCamera = null!;
    StudioCameraRig _shot = null!;
    MeshInstance3D _selectionBox = null!;
    Entry? _selected;
    VBoxContainer _library = null!, _inspector = null!;
    ItemList _objectList = null!;
    Label _status = null!, _clipInfo = null!, _timeLabel = null!, _cameraInfo = null!;
    LineEdit _name = null!;
    HSlider _scrub = null!;
    Button _play = null!, _pilotButton = null!;
    CheckButton _loop = null!, _inPlace = null!;
    SpinBox _speed = null!, _sourceScale = null!, _lens = null!;
    SpinBox _durationField = null!;
    OptionButton _fpsChoice = null!;
    CameraFrameOverlay _framing = null!;
    OptionButton _animationChoice = null!;
    TimelineView _timeline = null!;
    FileDialog _fileDialog = null!;
    AcceptDialog _message = null!;
    Action<string>? _fileAction;
    bool _refreshing, _playing, _pilot, _rightDown, _middleDown, _dragging, _moveTool = true;
    double _time, _duration = 8;
    int _fps = 24;
    int _loadingVersion=4;
    Vector3 _orbitTarget = new(0, .9f, 0);
    float _yaw = .65f, _pitch = .42f, _distance = 12;
    Vector3 _dragGrabOffset;
    string _category = "People", _projectPath = "";

    public override void _Ready()
    {
        TranslationServer.SetLocale("en");
        var window=GetWindow();float dpiScale=Math.Clamp(Math.Max(DisplayServer.ScreenGetScale(),DisplayServer.ScreenGetDpi()/96f),1,2.5f);window.ContentScaleMode=Window.ContentScaleModeEnum.CanvasItems;window.ContentScaleSize=Vector2I.Zero;window.ContentScaleFactor=dpiScale;
        DisplayServer.WindowSetTitle("Previs 0.5.0 — Film shot planning");
        BuildTheme(); BuildUi(); BuildWorld(); StarterScene();
        if (OS.GetCmdlineUserArgs().Contains("--pose-test")) _ = RunPoseChecksAndQuit();
        else if (OS.GetCmdlineUserArgs().Contains("--content-test")) _ = RunContentChecksAndQuit();
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-test")) _ = RunSmokeChecks();
        else {var open=OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--open=",StringComparison.Ordinal));if(open!=null){try{LoadProjectFromPath(open[7..]);if(_selected?.Data.Path.Points.Count>0)ShowInspectorSection("Path");}catch(Exception ex){Error(ex.Message);}}}
    }

    async Task RunContentChecksAndQuit()
    {
        try{await RunContentLibraryChecks();GetTree().Quit(0);}
        catch(Exception ex){GD.PushError("CONTENT_LIBRARY_FAILED "+ex);GetTree().Quit(1);}
    }

    void BuildTheme()
    {
        var theme = new Theme { DefaultFontSize = 13 };
        theme.SetColor("font_color", "Label", _text);
        theme.SetColor("font_color", "Button", _text);
        theme.SetColor("font_hover_color", "Button", Colors.White);
        theme.SetColor("font_pressed_color", "Button", Colors.White);
        theme.SetStylebox("normal", "Button", Style(new Color("222228"), 6));
        theme.SetStylebox("hover", "Button", Style(new Color("303039"), 6));
        theme.SetStylebox("pressed", "Button", Style(new Color("383045"), 6, new Color("706185")));
        theme.SetStylebox("focus", "Button", Style(Colors.Transparent, 6, _accent));
        theme.SetStylebox("normal", "LineEdit", Style(new Color("25252b"), 5));
        theme.SetColor("font_color", "LineEdit", _text);
        theme.SetStylebox("panel", "PopupMenu", Style(_panel, 6, _line));
        theme.SetStylebox("panel", "AcceptDialog", Style(_panel, 8, _line));
        theme.SetStylebox("panel", "ItemList", Style(_panel, 4));
        theme.SetStylebox("selected", "ItemList", Style(new Color("3d334e"), 4));
        theme.SetConstant("separation", "VBoxContainer", 8);
        theme.SetConstant("separation", "HBoxContainer", 8);
        Theme = theme;
        var background = new ColorRect { Color = _bg, MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(background);
    }

    StyleBoxFlat Style(Color fill, int radius = 0, Color? border = null)
    {
        var s = new StyleBoxFlat { BgColor = fill, CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            ContentMarginLeft = 9, ContentMarginRight = 9, ContentMarginTop = 6, ContentMarginBottom = 6 };
        if (border.HasValue) { s.BorderColor = border.Value; s.SetBorderWidthAll(1); }
        return s;
    }
    Label Text(string value, int size = 13, bool muted = false)
    {
        var l = new Label { Text = value }; l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", muted ? _muted : _text); return l;
    }
    Button Button(string text, Action action, bool toggle = false)
    {
        var b = new Button { Text = text, ToggleMode = toggle, FocusMode = FocusModeEnum.None };
        b.Pressed += action; return b;
    }
    PanelContainer Panel(Control child, Color? color = null)
    {
        var p = new PanelContainer(); p.AddThemeStyleboxOverride("panel", Style(color ?? _panel, 0, _line)); p.AddChild(child); return p;
    }
    HBoxContainer Row(params Control[] items)
    { var row = new HBoxContainer(); foreach (var c in items) row.AddChild(c); return row; }
    Control Space() => new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };

    void BuildUi() => BuildResponsiveUi();

    SpinBox Spin(double min, double max, double step, double value)
        => new() { MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(70, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };

    void BuildInspector()
    {
        _inspectorTabs=Row(Button("Object",()=>ShowInspectorSection("Object")),Button("Motion",()=>ShowInspectorSection("Motion")),Button("Path",()=>ShowInspectorSection("Path")));_inspector.AddChild(_inspectorTabs);
        _inspector.AddChild(Text("PROPERTIES", 12, true));
        _name = new LineEdit { PlaceholderText = "Select an object" }; _name.TextSubmitted += value => { if(string.IsNullOrWhiteSpace(value))return;PushUndo();if(_selectedCamera!=null){_selectedCamera.Name=value.Trim();foreach(var label in _selectedCamera.Rig.GetChildren().OfType<Label3D>())label.Text=_selectedCamera.Name;RefreshCameraChoices();}else if(_selected!=null)_selected.Data.Name=value.Trim();RefreshObjects(); }; _inspector.AddChild(_name);
        foreach (string group in new[] { "Position", "Rotation", "Scale" })
        {
            string captureGroup=group;var key=Button("◇",()=>CaptureSelectedTransformKey(captureGroup));key.TooltipText="Key all XYZ axes of "+group;_parameterKeyButtons[group]=key;
            var parameterLabel=Text(group + (group == "Position" ? " (m)" : group == "Rotation" ? " (°)" : ""), 12, true);_parameterLabels[group]=parameterLabel;_inspector.AddChild(Row(parameterLabel,Space(),key));
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 3);
            for (int i = 0; i < 3; i++)
            {
                int axis = i; var field = Spin(group == "Scale" ? .05 : -1000, 1000, group == "Rotation" ? 1 : .05, group == "Scale" ? 1 : 0);
                field.TooltipText = $"{group} {"XYZ"[i]}"; field.CustomMinimumSize = new Vector2(76, 0); _fields[group + i] = field;
                field.ValueChanged += value => ChangeTransform(group, axis, (float)value); row.AddChild(field);
            }
            _inspector.AddChild(row);
        }
        var autoKey=new CheckButton{Text="Auto key"};autoKey.Toggled+=v=>_autoKey=v;_inspector.AddChild(Row(Button("◆ Key transform",()=>CaptureSelectedTransformKey("All")),autoKey));
        BuildSequenceInspector();
    }

    void BuildWorld()
    {
        _world = new Node3D { Name = "World" }; _sceneViewport.AddChild(_world);
        _lighting=new StudioLighting();_world.AddChild(_lighting);_world.AddChild(new WorldEnvironment { Environment=_lighting.Environment });
        var floor = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(100, 100) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("4a4a50"), Roughness = 1 } }; _world.AddChild(floor);
        var grid = new ImmediateMesh(); grid.SurfaceBegin(Mesh.PrimitiveType.Lines);
        for (int i = -30; i <= 30; i++) { grid.SurfaceAddVertex(new Vector3(i, .012f, -30)); grid.SurfaceAddVertex(new Vector3(i, .012f, 30)); grid.SurfaceAddVertex(new Vector3(-30, .012f, i)); grid.SurfaceAddVertex(new Vector3(30, .012f, i)); } grid.SurfaceEnd();
        _world.AddChild(new MeshInstance3D { Mesh = grid, Layers = 2, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("414147"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } });
        _editorCamera = new Camera3D { CullMask = 3, Far = 500, Near = .05f, Current = true, Fov = 50 }; _world.AddChild(_editorCamera); ResetEditorView();
        CreateCamera("Camera A",null,true); _editorCamera.MakeCurrent();
        _previewViewport.World3D = _sceneViewport.World3D;
        _previewCamera = new Camera3D { CullMask = 1, Near = .05f, Far = 500, Current = true, KeepAspect = Camera3D.KeepAspectEnum.Width }; _previewViewport.AddChild(_previewCamera);
        _selectionBox = new MeshInstance3D { Layers = 2 }; _world.AddChild(_selectionBox);
    }

    void StarterScene()
    {
        AddObject("actor", new Vector3(-1.5f, 0, .5f), "Alex");
        string sample = SamplePath("samples/synthetic-walk-gesture.bvh");
        if (File.Exists(sample)) { try { LoadMotion(_entries[0], sample, .01f); } catch (Exception ex) { Status("Sample motion could not load: " + ex.Message); } }
        AddObject("table", new Vector3(1, 0, -1.8f)); AddObject("chair", new Vector3(1, 0, -.5f));
        AddObject("chair", new Vector3(1, 0, -3)); _entries[^1].Node.RotationDegrees = new Vector3(0, 180, 0);_entries[^1].Data.Rotation=new float[]{0,180,0};
        AddObject("car", new Vector3(4, 0, -4)); AddObject("wall", new Vector3(-2, 0, -4)); AddObject("tree", new Vector3(-4, 0, -2));
        AddFox(new Vector3(2.7f,0,1.3f),true);
        _shot.SetPreset("Dolly in", _duration);var a=ActiveCameraId;
        var b=CreateCamera("Camera B",null,false);b.Rig.SetPose(new Vector3(-1.5f,1.65f,4),new Vector3(-1.5f,1,0));b.Rig.CaptureKey(0);
        var c=CreateCamera("Camera C",null,false);c.Rig.SetPose(new Vector3(-6,3.5f,-5),new Vector3(0,1,0));c.Rig.CaptureKey(0);SetActiveCamera(a);
        Select(_entries[0]); ShowCategory("People"); SetTime(0); _undo.Clear(); _redo.Clear();
        Status("Neutral studio ready · choose a camera, append motion clips, or draw a character path.");
    }

    Entry AddObject(string kind, Vector3 position, string? name = null)
    {
        Node3D node = kind == "actor" ? new RiggedActor() : PropFactory.Create(kind);
        var data = new ObjectData { Kind = kind, Name = name ?? (kind == "actor" ? "Actor" : PropFactory.DisplayName(kind)),Position=Array3(position),ActorFamily=kind=="actor"?"human":"" };
        var entry = new Entry { Node = node, Data = data }; node.Name = "Object_" + data.Id; node.SetMeta("entry_id", data.Id);
        _world.AddChild(node); node.Position = position; _entries.Add(entry); RefreshObjects(); return entry;
    }
    void AddFromLibrary(string kind) { PushUndo(); var item = AddObject(kind, new Vector3(0, 0, 0)); Select(item); Status("Added " + item.Data.Name + ". Drag it with the Move tool or edit Position."); }

    void ShowCategory(string category)
    {
        _category = category; foreach (var p in _tabs) p.Value.ButtonPressed = p.Key == category;
        foreach (Node c in _library.GetChildren()) { _library.RemoveChild(c); c.QueueFree(); }
        _library.AddChild(Text(category.ToUpperInvariant(), 12, true));
        if(category=="Cameras"){BuildCameraLibrary();return;}
        if(category=="Motions"){BuildMotionLibrary();return;}
        if(category=="Poses"){BuildPoseLibrary();return;}
        if(category=="Environment"){var light=new CheckButton {Text="Directional light",ButtonPressed=_lighting.DirectionalEnabled};light.Toggled+=v=>{PushUndo();_lighting.DirectionalEnabled=v;Status(v?"Optional directional light enabled.":"Neutral studio · no directional light.");};_library.AddChild(light);_library.AddChild(Text("Neutral studio is the default.",11,true));}
        if (category == "Cameras" || category == "Camera moves")
        {
            if (category == "Cameras")
            {
                foreach (var p in new[] { "Wide shot", "Medium shot", "Close-up", "High angle", "Current view" }) { string choice = p; _library.AddChild(Button(p, () => ApplyShot(choice))); }
            }
            else foreach (string preset in new[] { "Static", "Dolly in", "Truck right", "Crane up", "Orbit" }) { string p = preset; _library.AddChild(Button(p, () => { PushUndo(); _shot.SetPreset(p, _duration);SyncCameraFromRig(_cameras.First(c=>c.Rig==_shot));SetTime(0);SelectCamera(_cameras.First(c=>c.Rig==_shot)); Status(p + " camera move applied."); })); }
            _library.AddChild(Button("Add camera key", CaptureCameraKey)); _library.AddChild(Button("Clear camera keys",ClearCameraAnimation)); return;
        }
        if (category == "Scene") { _library.AddChild(Text("Select an object below.\nDrag with Move.\nF frames the selection.\nK keys the transform.", 12, true)); return; }
        BuildModelLibrary(category);
    }

    void Select(Entry? entry)
    {
        _selectedCamera=null;_selected = entry; _refreshing = true;_cameraQuickControls.Visible=false;
        _name.Text = entry?.Data.Name ?? "";
        foreach (var p in _fields) { string group = p.Key[..^1]; int axis = p.Key[^1] - '0'; Vector3? value = entry==null?null:group == "Position" ? Vector(entry.Data.Position) : group == "Rotation" ? Vector(entry.Data.Rotation) : entry.Node.Scale; p.Value.Value = value.HasValue ? value.Value[axis] : group == "Scale" ? 1 : 0; p.Value.Editable = entry != null; }
        _loop.ButtonPressed = entry?.Data.Loop ?? true; _inPlace.ButtonPressed = entry?.Data.InPlace ?? false; _speed.Value = entry?.Data.Speed ?? 1; _sourceScale.Value = entry?.Data.SourceScale ?? .01f;
        _animationChoice.Clear(); _animationChoice.Visible = entry?.Player != null;
        if (entry?.Player is { } player) { foreach (string animation in player.GetAnimationList()) if (animation != "RESET") _animationChoice.AddItem(animation); for(int i=0;i<_animationChoice.ItemCount;i++)if(_animationChoice.GetItemText(i)==entry.Animation)_animationChoice.Select(i); }
        _clipInfo.Text = entry?.Node is RiggedActor actor ? (actor.DurationSeconds <= 0 ? "Prepared human rig · no motion loaded." : $"{entry.Data.MotionName ?? actor.MotionName}\n{actor.DurationSeconds:0.00} s · {actor.SourceFps:0.##} fps · {actor.JointCount} joints\n{actor.ImportSummary}") : entry?.Player != null ? "Imported model · native animation clips" : "Static blockout prop";
        if (entry != null) { int index = _entries.IndexOf(entry); if (index >= 0) _objectList.Select(index); }
        _refreshing = false; ShowInspectorSection("Object");RefreshSequenceInspector();UpdateTransformFields();UpdateSelection();DrawPaths();
    }
    void RefreshObjects() { _objectList.Clear(); foreach (var e in _entries) _objectList.AddItem(e.Data.Name);foreach(var c in _cameras)_objectList.AddItem("[Camera] "+c.Name); if (_selected != null && _entries.Contains(_selected)) _objectList.Select(_entries.IndexOf(_selected));else if(_selectedCamera!=null&&_cameras.Contains(_selectedCamera))_objectList.Select(_entries.Count+_cameras.IndexOf(_selectedCamera)); }
    void ChangeTransform(string group, int axis, float value)
    {
        EditSelectedTransform(group,axis,value);
    }
    void DuplicateSelected()
    {
        if(_selectedCamera!=null){AddCameraFromView(true);return;}
        if (_selected == null) return; PushUndo(); var d = SnapshotObject(_selected); d.Id = Guid.NewGuid().ToString("N"); d.Name += " copy"; d.Position[0] += .75f;foreach(var p in d.Path.Points)p[0]+=.75f; var e = RestoreObject(d, ""); Select(e);SetTime(_time);
    }
    void DeleteSelected()
    {
        if(_selectedCamera!=null){RemoveActiveCamera();return;}
        if (_selected == null) return;if(_drawingPath)CancelDrawingPath(); PushUndo(); var e = _selected; _selected = null; _entries.Remove(e); _world.RemoveChild(e.Node); e.Node.QueueFree(); RefreshObjects(); Select(null);RefreshTimelineSize(); Status("Object deleted.");
    }

    void ImportMotion() => ChooseFile("Import BVH motion capture", FileDialog.FileModeEnum.OpenFile, new[] { "*.bvh ; BVH motion capture" }, path =>
    {
        Entry? target = _selected?.Node is RiggedActor ? _selected : null;
        if (target == null) { PushUndo(); target = AddObject("actor", Vector3.Zero); } else PushUndo();
        AppendMotion(target, path, (float)_sourceScale.Value); Select(target);ShowInspectorSection("Motion"); SetTime(SelectedClip?.Start??0); Status("Appended " + Path.GetFileName(path) + " · overlap clips to blend.");
    });
    void ApplySample(string path)
    {
        path = path.StartsWith("res://") ? SamplePath(path[6..]) : path;
        float scale = Path.GetFileName(path).StartsWith("CMU_", StringComparison.OrdinalIgnoreCase) ? .055f : .01f;
        try { PushUndo(); Entry target = _selected?.Node is RiggedActor ? _selected : AddObject("actor", Vector3.Zero); AppendMotion(target, path, scale); Select(target);ShowInspectorSection("Motion"); SetTime(SelectedClip?.Start??0); Status("Appended " + Path.GetFileName(path)); } catch (Exception ex) { Error(ex.Message); }
    }
    static string SamplePath(string relative)
    {
        string resource = "res://" + relative;
        string path = ProjectSettings.GlobalizePath(resource);
        if (File.Exists(path)) return path;
        string cached = ProjectSettings.GlobalizePath("user://" + relative); Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
        if (Godot.FileAccess.FileExists(resource)) File.WriteAllBytes(cached, Godot.FileAccess.GetFileAsBytes(resource));
        return cached;
    }
    void LoadMotion(Entry target, string path, float scale)
    {
        var source=BvhMotion.FromFile(path);var clip=new MotionClipData{MotionPath=Path.GetFullPath(path),Name=Path.GetFileNameWithoutExtension(path),Duration=source.DurationSeconds,SourceScale=scale,BlendIn=0};((RiggedActor)target.Node).SetMotionClips(new[]{clip});target.Data.MotionClips=new(){clip};_selectedClipId=clip.Id;target.Data.MotionPath=clip.MotionPath;target.Data.MotionName=clip.Name;target.Data.SourceScale=scale;
    }
    void ReloadMotionScale(float scale)
    {
        if (_refreshing || _selected?.Node is not RiggedActor || string.IsNullOrEmpty(_selected.Data.MotionPath)) return;
        try { LoadMotion(_selected, _selected.Data.MotionPath, scale); SetTime(_time); } catch (Exception ex) { Error(ex.Message); }
    }

    void ImportModel() => ChooseFile("Import prepared 3D model", FileDialog.FileModeEnum.OpenFile, new[] { "*.glb,*.gltf,*.fbx ; Animated 3D models" }, path =>
    { PushUndo(); var data = new ObjectData { Kind = "imported", Name = Path.GetFileNameWithoutExtension(path), ModelPath = path }; var entry = RestoreObject(data, "");InitializeNativeClip(entry);Select(entry);SetTime(_time); Status("Imported model."); });
    Entry ImportModelData(ObjectData data)
    {
        string path = data.ModelPath ?? throw new InvalidDataException("The model path is missing.");
        bool fbx = Path.GetExtension(path).Equals(".fbx", StringComparison.OrdinalIgnoreCase);
        GltfDocument document = fbx ? new FbxDocument() : new GltfDocument(); GltfState state = fbx ? new FbxState() : new GltfState();
        var result = document.AppendFromFile(path, state); if (result != Godot.Error.Ok) throw new InvalidDataException("The model could not be imported: " + result);
        var model = document.GenerateScene(state) ?? throw new InvalidDataException("No scene was found in the model.");
        var root = new Node3D(); root.SetMeta("entry_id", data.Id); _world.AddChild(root); root.AddChild(model);
        if(model is Node3D model3d)
        {
            float units=data.LibraryModelId==null&&data.ActorFamily=="fox"?MotionCatalog.FoxModelScale:data.ModelUnitScale;
            if(!float.IsFinite(units)||units<=0)throw new InvalidDataException("The model unit scale is invalid.");
            model3d.Scale*=units;
            if(data.ModelGrounded){var bounds=PropFactory.CalculateBounds(root);model3d.Position-=Vector3.Up*bounds.Position.Y;}
        }
        foreach (var mesh in Descendants(model).OfType<MeshInstance3D>()) if (mesh.Mesh != null) { mesh.CreateTrimeshCollision(); foreach (Node child in mesh.GetChildren()) if (child is StaticBody3D body) body.SetMeta("scene_root", root); }
        var player = Descendants(model).OfType<AnimationPlayer>().FirstOrDefault(); string animation = data.ModelAnimation != null && player?.HasAnimation(data.ModelAnimation)==true ? data.ModelAnimation : player?.GetAnimationList().FirstOrDefault(a => a != "RESET") ?? "";
        if (player != null && animation != "") { player.Set("callback_mode_process", 2); player.Play(animation); }
        var entry = new Entry { Data = data, Node = root, Player = player, Animation = animation,NativeTrack=player!=null&&Descendants(model).OfType<Skeleton3D>().Any(s=>s.GetBoneCount()>0)?new NativeMotionTrack(model,player):null }; _entries.Add(entry);return entry;
    }
    static IEnumerable<Node> Descendants(Node root) { yield return root; foreach (Node child in root.GetChildren()) foreach (Node nested in Descendants(child)) yield return nested; }

    void CaptureCameraKey() { var camera=_cameras.First(c=>c.Rig==_shot);_selected=null;_selectedCamera=camera;CaptureSelectedTransformKey("All");SelectCamera(camera); }
    void UseCurrentView() { PushUndo();var camera=_cameras.First(c=>c.Rig==_shot);EnsureCameraAnimation(camera);TransformLayerRuntime.Upsert(camera.Animation.Position,_time,_editorCamera.GlobalPosition);TransformLayerRuntime.Upsert(camera.Animation.Rotation,_time,_editorCamera.GlobalRotationDegrees);SetTime(_time);SelectCamera(camera); }
    void TogglePilot() { _pilot = !_pilot; _editorCamera.CullMask=_pilot?1u:3u; _pilotButton.ButtonPressed = _pilot; _pilotButton.Text = _pilot ? "Exit camera view" : "Enter camera view"; if (_pilot) _editorCamera.GlobalTransform = _shot.GlobalTransform; else { _editorCamera.KeepAspect=Camera3D.KeepAspectEnum.Height; _editorCamera.Fov=50; ResetEditorView(); } UpdatePreview(); Status(_pilot ? "Camera view: RMB look + WASD/QE move. Add key at the desired frame." : "Working perspective view."); }
    void ApplyShot(string shot)
    {
        PushUndo(); Vector3 target = (_selected?.Node.Position ?? Vector3.Zero) + Vector3.Up;
        _shot.ClearKeys(); if (shot == "Current view") _shot.GlobalTransform = _editorCamera.GlobalTransform;
        else { Vector3 offset = shot switch { "Medium shot" => new Vector3(0, .3f, 3.8f), "Close-up" => new Vector3(0, .55f, 1.8f), "High angle" => new Vector3(3, 6, 4), _ => new Vector3(6, 2.5f, 7) }; _shot.SetPose(target + offset, target); }
        _shot.CaptureKey(_time);SyncCameraFromRig(_cameras.First(c=>c.Rig==_shot)); if(_pilot)_editorCamera.GlobalTransform=_shot.GlobalTransform; UpdatePreview(); _timeline.QueueRedraw(); Status(shot + " framing applied to "+ActiveCameraName+".");
    }
    void ResetEditorView() { _orbitTarget = new Vector3(0, .9f, 0); _yaw = .65f; _pitch = .42f; _distance = 12; UpdateOrbit(); }
    void UpdateOrbit() { if (_editorCamera == null) return; _editorCamera.Position = _orbitTarget + new Vector3(Mathf.Sin(_yaw) * Mathf.Cos(_pitch), Mathf.Sin(_pitch), Mathf.Cos(_yaw) * Mathf.Cos(_pitch)) * _distance; _editorCamera.LookAt(_orbitTarget); }
    void FrameSelected() { if(_selectedCamera!=null){if(_pilot)TogglePilot();_orbitTarget=_selectedCamera.Rig.GlobalPosition;_distance=4;UpdateOrbit();return;}if (_selected == null) return; if (_pilot) TogglePilot(); var bounds=_selected.Node is RiggedActor actor?actor.GetPreparedPoseBounds():PropFactory.CalculateBounds(_selected.Node);_orbitTarget=_selected.Node.GlobalTransform*bounds.GetCenter();_distance=Math.Max(2.5f,(bounds.Size*_selected.Node.Scale).Length()*1.65f);UpdateOrbit(); }
    void UpdatePreview() { if (_previewCamera == null || _shot == null) return; _previewCamera.GlobalTransform = _shot.ShotCamera.GlobalTransform; _previewCamera.Fov = _shot.ShotCamera.Fov; _cameraInfo.Text = $"{ActiveCameraName} · {_shot.FocalLengthMm:0} mm · 16:9 · {ActiveCameraKeyCount()} keys"; foreach(var camera in _cameras)camera.Rig.Visible=camera.Rig.GlobalPosition.DistanceTo(_editorCamera.GlobalPosition)>.55f;if(_pilot){_editorCamera.KeepAspect=Camera3D.KeepAspectEnum.Width;float aspect=_sceneViewport.Size.X/(float)Math.Max(1,_sceneViewport.Size.Y);_editorCamera.Fov=Mathf.RadToDeg(2*Mathf.Atan(Mathf.Tan(Mathf.DegToRad(_shot.ShotCamera.Fov)*.5f)*Math.Max(1,aspect/(16f/9))));} _framing?.QueueRedraw(); }

    public override void _Process(double delta)
    {
        UpdateCompactChrome();
        if (_playing) { double next = _time + delta; if (next >= _duration) { next = _duration; _playing = false; _play.Text = "▶ Play"; } SetTime(next); }
        if (_rightDown)
        {
            Vector3 direction = Vector3.Zero;
            if (Input.IsPhysicalKeyPressed(Key.W)) direction -= _editorCamera.GlobalBasis.Z;
            if (Input.IsPhysicalKeyPressed(Key.S)) direction += _editorCamera.GlobalBasis.Z;
            if (Input.IsPhysicalKeyPressed(Key.A)) direction -= _editorCamera.GlobalBasis.X;
            if (Input.IsPhysicalKeyPressed(Key.D)) direction += _editorCamera.GlobalBasis.X;
            if (Input.IsPhysicalKeyPressed(Key.Q)) direction -= Vector3.Up; if (Input.IsPhysicalKeyPressed(Key.E)) direction += Vector3.Up;
            if (direction.LengthSquared() > 0) { Vector3 move = direction.Normalized() * (float)delta * (Input.IsPhysicalKeyPressed(Key.Shift) ? 10 : 3); _editorCamera.Position += move; _orbitTarget += move; if (_pilot) {_shot.GlobalTransform = _editorCamera.GlobalTransform;StorePilotedCameraPose();} }
        }
        UpdatePreview();
    }
    void SetTime(double seconds)
    {
        _time = Math.Clamp(seconds, 0, _duration);
        foreach (var e in _entries)
        {
            if (e.Node is RiggedActor actor) actor.EvaluateTimeline(_time,e.Data.Path.Enabled&&e.Data.Path.Points.Count>=2);
            else if(e.NativeTrack!=null)e.NativeTrack.Evaluate(_time,e.Data.Path.Enabled&&e.Data.Path.Points.Count>=2);
            else if (e.Player != null && e.Animation != "") { var anim = e.Player.GetAnimation(e.Animation); double t = _time * e.Data.Speed; if (anim.Length > 0) t = e.Data.Loop ? t % anim.Length : Math.Min(t, anim.Length); e.Player.Seek(t, true); e.Player.Advance(0); }
            ApplyEntryPath(e);
        }
        foreach(var camera in _cameras)EvaluateCamera(camera,_time); if(_pilot)_editorCamera.GlobalTransform = _shot.GlobalTransform;
        bool previous = _refreshing; _refreshing = true; _scrub.Value = _time; _lens.Value = _shot.FocalLengthMm; _refreshing = previous;
        int frame = (int)Math.Floor(_time * _fps + .00001); _timeLabel.Text = $"{frame / _fps / 60:00}:{frame / _fps % 60:00}:{frame % _fps:00}"; _timeline.QueueRedraw();UpdateTransformFields(); UpdateSelection(); UpdatePreview();DrawPaths();
    }
    void ViewportInput(InputEvent input)
    {
        if(HandlePathInput(input))return;
        if (input is InputEventMouseButton b)
        {
            if (b.ButtonIndex == MouseButton.Right) {_rightDown = b.Pressed;if(b.Pressed&&_pilot){PushUndo();_playing=false;_play.Text="▶";}}
            if (b.ButtonIndex == MouseButton.Middle) _middleDown = b.Pressed;
            if (b.ButtonIndex == MouseButton.WheelUp && b.Pressed) { _distance = Math.Max(.7f, _distance * .88f); if (_pilot) {PushUndo(); _editorCamera.Position -= _editorCamera.Basis.Z * .35f; _shot.GlobalTransform = _editorCamera.GlobalTransform;StorePilotedCameraPose(); } else UpdateOrbit(); }
            if (b.ButtonIndex == MouseButton.WheelDown && b.Pressed) { _distance = Math.Min(100, _distance / .88f); if (_pilot) {PushUndo(); _editorCamera.Position += _editorCamera.Basis.Z * .35f; _shot.GlobalTransform = _editorCamera.GlobalTransform;StorePilotedCameraPose(); } else UpdateOrbit(); }
            if (b.ButtonIndex == MouseButton.Left)
            {
                if (b.Pressed) {var camera=PickCamera(b.Position);if(camera!=null){SelectCamera(camera);return;} var picked = Pick(b.Position); Select(picked); if (picked != null && _moveTool) { PushUndo(); _dragging = true;var origin=_editorCamera.ProjectRayOrigin(b.Position);var ray=_editorCamera.ProjectRayNormal(b.Position);_dragGrabOffset=Math.Abs(ray.Y)>.001f?picked.Node.Position-(origin+ray*((picked.Node.Position.Y-origin.Y)/ray.Y)):Vector3.Zero; } }
                else { _dragging = false; if (_selected != null) Select(_selected); }
            }
        }
        if (input is InputEventMouseMotion m)
        {
            if (_rightDown) { if (_pilot) { _editorCamera.RotateY(-m.Relative.X * .004f); _editorCamera.RotateObjectLocal(Vector3.Right, -m.Relative.Y * .004f); _shot.GlobalTransform = _editorCamera.GlobalTransform;StorePilotedCameraPose(); } else { _yaw -= m.Relative.X * .006f; _pitch = Math.Clamp(_pitch + m.Relative.Y * .006f, -.15f, 1.45f); UpdateOrbit(); } }
            if (_middleDown) { Vector3 shift=(-_editorCamera.Basis.X * m.Relative.X + _editorCamera.Basis.Y * m.Relative.Y) * _distance * .0015f; if(_pilot){_editorCamera.Position+=shift;_shot.GlobalTransform=_editorCamera.GlobalTransform;StorePilotedCameraPose();}else{_orbitTarget+=shift;UpdateOrbit();} }
            if (_dragging && _selected != null) { var origin = _editorCamera.ProjectRayOrigin(m.Position); var ray = _editorCamera.ProjectRayNormal(m.Position); if (Math.Abs(ray.Y) > .001f) { float t = (_selected.Node.Position.Y - origin.Y) / ray.Y; if (t > 0) { Vector3 p = origin + ray * t+_dragGrabOffset; if (Input.IsPhysicalKeyPressed(Key.Ctrl)) { p.X = Mathf.Snapped(p.X, .25f); p.Z = Mathf.Snapped(p.Z, .25f); } MoveEntryBase(_selected,Vector(_selected.Data.Position)+p-_selected.Node.Position); UpdateSelection(); } } }
        }
    }
    Entry? Pick(Vector2 point)
    {
        var origin = _editorCamera.ProjectRayOrigin(point); var ray = _editorCamera.ProjectRayNormal(point);
        var hit = _world.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(origin, origin + ray * 500, 1));
        if (hit.Count == 0) return null; Node? n = hit["collider"].AsGodotObject() as Node;
        while (n != null) { if (n.HasMeta("scene_root") && n.GetMeta("scene_root").AsGodotObject() is Node3D r) return _entries.FirstOrDefault(e => e.Node == r); var e = _entries.FirstOrDefault(e => e.Node == n); if (e != null) return e; n = n.GetParent(); } return null;
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (input is not InputEventKey key || !key.Pressed || key.Echo) return;
        if(_drawingPath&&key.Keycode==Key.Escape){CancelDrawingPath();return;}if(_drawingPath&&key.Keycode==Key.Enter){FinishDrawingPath();return;}
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return;
        if(key.Keycode==Key.K){CaptureSelectedTransformKey("All");return;}
        if (key.CtrlPressed && key.Keycode == Key.S) SaveProject(); else if (key.CtrlPressed && key.Keycode == Key.O) OpenProject(); else if (key.CtrlPressed && key.Keycode == Key.Z) Undo(); else if (key.CtrlPressed && key.Keycode == Key.Y) Redo(); else if (key.CtrlPressed && key.Keycode == Key.D) DuplicateSelected(); else if (key.Keycode == Key.Delete) DeleteSelected(); else if (key.Keycode == Key.F) FrameSelected(); else if (key.Keycode == Key.Space) { _playing = !_playing; _play.Text = _playing ? "Ⅱ Pause" : "▶ Play"; }
    }
    public override void _Input(InputEvent input)
    {
        if(input is InputEventMouseButton { Pressed: false } button) { if(button.ButtonIndex==MouseButton.Right)_rightDown=false; if(button.ButtonIndex==MouseButton.Middle)_middleDown=false; if(button.ButtonIndex==MouseButton.Left)_dragging=false; }
    }
    void UpdateSelection()
    {
        if (_selectionBox == null) return; _selectionBox.Visible = _selected != null; if (_selected == null) return;
        var bounds = _selected.Node is RiggedActor actor ? actor.GetPreparedPoseBounds() : PropFactory.CalculateBounds(_selected.Node);
        var mesh = new ImmediateMesh(); mesh.SurfaceBegin(Mesh.PrimitiveType.Lines); Vector3[] corners = Enumerable.Range(0, 8).Select(i => bounds.Position + new Vector3((i & 1) != 0 ? bounds.Size.X : 0, (i & 2) != 0 ? bounds.Size.Y : 0, (i & 4) != 0 ? bounds.Size.Z : 0)).ToArray();
        foreach (int i in Enumerable.Range(0, 8)) foreach (int bit in new[] { 1, 2, 4 }) if ((i & bit) == 0) { mesh.SurfaceAddVertex(corners[i]); mesh.SurfaceAddVertex(corners[i | bit]); } mesh.SurfaceEnd();
        _selectionBox.Mesh = mesh; _selectionBox.MaterialOverride = new StandardMaterial3D { AlbedoColor = _accent, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }; _selectionBox.GlobalTransform = _selected.Node.GlobalTransform;
    }

    void ChooseFile(string title, FileDialog.FileModeEnum mode, string[] filters, Action<string> action)
    { _playing = false; _play.Text = "▶ Play"; _fileAction = action; _fileDialog.Title = title; _fileDialog.FileMode = mode; _fileDialog.Filters = filters; _fileDialog.PopupCentered(new Vector2I(900, 600)); }
    void Status(string text) { if (_status != null) _status.Text = text; }
    void Error(string message) { Status(message); _message.Title = "Previs — action could not be completed"; _message.DialogText = message; _message.PopupCentered(new Vector2I(640, 230)); GD.PushWarning(message); }
    void NewScene() { if(_drawingPath)CancelDrawingPath();PushUndo(); ClearObjects(); ClearCameras();CreateCamera("Camera A",null,true);_shot.ClearKeys();_lighting.DirectionalEnabled=false; _projectPath = ""; SetTime(0); Status("New neutral scene. Add an actor or prop from the library."); }
    void ClearObjects() { foreach (var e in _entries) { _world.RemoveChild(e.Node); e.Node.QueueFree(); } _entries.Clear(); _selected = null; RefreshObjects(); Select(null); }
    void PushUndo() { if (_world == null) return; _undo.Push((Snapshot(),_projectPath)); _redo.Clear(); }
    void Undo() { if (_undo.Count == 0) return; _redo.Push((Snapshot(),_projectPath)); var previous=_undo.Pop();Restore(previous.Document, "");_projectPath=previous.Path; Status("Undo."); }
    void Redo() { if (_redo.Count == 0) return; _undo.Push((Snapshot(),_projectPath)); var next=_redo.Pop();Restore(next.Document, "");_projectPath=next.Path; Status("Redo."); }
    static float[] Array3(Vector3 v) => new[] { v.X, v.Y, v.Z };
    static Vector3 Vector(float[] v, Vector3 fallback = default) => v.Length == 3 && v.All(float.IsFinite) ? new Vector3(v[0], v[1], v[2]) : fallback;
    ObjectData SnapshotObject(Entry e) {var d=JsonSerializer.Deserialize<ObjectData>(JsonSerializer.Serialize(e.Data))!;d.ModelAnimation=e.Animation;return d;}
    ProjectData Snapshot() => new() { Fps = _fps, Duration = _duration, Objects = _entries.Select(SnapshotObject).ToList(), CameraKeys = _shot.Keys.Select(k => new CameraData { Time = k.TimeSeconds, Position = Array3(k.Position), Quaternion = new[] { k.Rotation.X, k.Rotation.Y, k.Rotation.Z, k.Rotation.W }, FocalLength = k.FocalLengthMm }).ToList(), ShotPosition = Array3(_shot.GlobalPosition), ShotRotation = Array3(_shot.GlobalRotationDegrees), FocalLength = _shot.FocalLengthMm,Cameras=_cameras.Select(SnapshotCamera).ToList(),ActiveCameraId=ActiveCameraId,DirectionalLightEnabled=_lighting.DirectionalEnabled };
    Entry RestoreObject(ObjectData data, string baseDirectory)
    {
        var copy = JsonSerializer.Deserialize<ObjectData>(JsonSerializer.Serialize(data))!;
        if (!string.IsNullOrEmpty(copy.MotionPath) && !Path.IsPathRooted(copy.MotionPath)) copy.MotionPath = Path.GetFullPath(Path.Combine(baseDirectory, copy.MotionPath));
        if (!string.IsNullOrEmpty(copy.ModelPath) && !Path.IsPathRooted(copy.ModelPath)) copy.ModelPath = Path.GetFullPath(Path.Combine(baseDirectory, copy.ModelPath));
        foreach(var clip in copy.MotionClips)if(!Path.IsPathRooted(clip.MotionPath))clip.MotionPath=Path.GetFullPath(Path.Combine(baseDirectory,clip.MotionPath));
        Entry e = copy.Kind == "imported" ? ImportModelData(copy) : AddObject(copy.Kind, Vector(copy.Position), copy.Name);
        e.Data = copy;if(e.Node is RiggedActor&&copy.ActorFamily=="")copy.ActorFamily="human"; e.Node.SetMeta("entry_id", copy.Id); e.Node.Position = Vector(copy.Position); e.Node.RotationDegrees = Vector(copy.Rotation); e.Node.Scale = Vector(copy.Scale, Vector3.One);
        if(e.Node is RiggedActor human){ApplyHumanAppearance(human,copy.LibraryModelId);human.SetStaticPose(copy.StaticPose);if(copy.MotionClips.Count>0)RebuildTrack(e);else if(!string.IsNullOrEmpty(copy.MotionPath)){string? label=copy.MotionName;LoadMotion(e,copy.MotionPath,copy.SourceScale);var clip=copy.MotionClips[0];clip.Speed=copy.Speed;clip.Loop=copy.Loop;clip.InPlace=copy.InPlace;clip.Duration/=Math.Max(.1,copy.Speed);if(!string.IsNullOrEmpty(label))clip.Name=copy.MotionName=label;RebuildTrack(e);}}else if(e.NativeTrack!=null){if(copy.MotionClips.Count>0)RebuildTrack(e);else if(_loadingVersion<3)InitializeNativeClip(e);else e.NativeTrack.SetClips(Array.Empty<MotionClipData>());}RefreshObjects();RefreshTimelineSize();return e;
    }
    void Restore(ProjectData data, string baseDirectory)
    {
        if ((data.Version <1||data.Version>4) || data.Objects.Count > 500 || data.CameraKeys.Count > 10000||data.Cameras.Count>100) throw new InvalidDataException("Unsupported project version or project size.");
        _loadingVersion=data.Version;ClearObjects(); _fps = Math.Clamp(data.Fps, 1, 120); _duration = Math.Clamp(data.Duration, 1, MaxSceneDuration); _scrub.MaxValue = _duration; _scrub.Step = 1.0 / _fps; _refreshing=true;_durationField.Value=_duration;for(int i=0;i<_fpsChoice.ItemCount;i++)if(_fpsChoice.GetItemId(i)==_fps)_fpsChoice.Select(i);_refreshing=false;
        foreach (var obj in data.Objects) RestoreObject(obj, baseDirectory);
        ClearCameras();if(data.Cameras.Count>0){foreach(var camera in data.Cameras)CreateCamera(camera.Name,camera,false);SetActiveCamera(data.Cameras.Any(c=>c.Id==data.ActiveCameraId)?data.ActiveCameraId:data.Cameras[0].Id);}else{var legacy=new CameraTrackData{Name="Camera A",Position=data.ShotPosition.Length==3?data.ShotPosition:new float[]{6,3,7},Rotation=data.ShotRotation,FocalLength=data.FocalLength,Keys=data.CameraKeys};CreateCamera("Camera A",legacy,true);}
        _lighting.DirectionalEnabled=data.DirectionalLightEnabled;RefreshCameraChoices();RefreshTimelineSize();
        _loadingVersion=4;SetTime(_time); Select(_entries.FirstOrDefault());
    }
    void SaveProject()
    {
        if (_projectPath == "") ChooseFile("Save Previs project", FileDialog.FileModeEnum.SaveFile, new[] { "*.previs ; Previs project" }, SaveTo);
        else { try { SaveTo(_projectPath); } catch (Exception ex) { Error(ex.Message); } }
    }
    void SaveTo(string path)
    {
        if (!Path.HasExtension(path)) path += ".previs"; var document = Snapshot(); string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string assetFolder = Path.GetFileNameWithoutExtension(path) + ".assets"; string assetDirectory = Path.Combine(baseDirectory, assetFolder); Directory.CreateDirectory(assetDirectory);
        foreach (var item in document.Objects)
        {
            foreach(var clip in item.MotionClips){string target=Path.Combine(assetDirectory,item.Id+"_"+clip.Id+Path.GetExtension(clip.MotionPath));if(!Path.GetFullPath(clip.MotionPath).Equals(Path.GetFullPath(target),StringComparison.OrdinalIgnoreCase))File.Copy(clip.MotionPath,target,true);clip.MotionPath=Path.Combine(assetFolder,Path.GetFileName(target)).Replace('\\','/');}
            if(item.MotionClips.Count>0)item.MotionPath=null;
            foreach (bool motion in new[] { true, false }) { string? source = motion ? item.MotionPath : item.ModelPath; if (string.IsNullOrEmpty(source)) continue;
                if (Path.GetExtension(source).Equals(".gltf", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Save imported models as self-contained GLB or FBX. External glTF dependencies are not bundled in this prototype.");
                string fileName = item.Id + (motion ? "_motion" : "_model") + Path.GetExtension(source); string target = Path.Combine(assetDirectory, fileName); if (!Path.GetFullPath(source).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) File.Copy(source, target, true);
                if (motion) item.MotionPath = Path.Combine(assetFolder, fileName).Replace('\\','/'); else item.ModelPath = Path.Combine(assetFolder, fileName).Replace('\\','/'); }
        }
        string temporary = path + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(document, _json));if(File.Exists(path))File.Copy(path,path+".bak",true); File.Move(temporary, path, true); _projectPath = path; Status("Saved cameras, motion clips, paths and assets: " + Path.GetFileName(path));
    }
    void OpenProject() => ChooseFile("Open Previs project", FileDialog.FileModeEnum.OpenFile, new[] { "*.previs ; Previs project" },LoadProjectFromPath);
    void LoadProjectFromPath(string path)
    {
        var document = JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(path)) ?? throw new InvalidDataException("The project is empty."); var before = Snapshot();
        try { Restore(document, Path.GetDirectoryName(path)!); PushUndoSnapshot(before); _projectPath = path; Status("Opened " + Path.GetFileName(path)); } catch { Restore(before, ""); throw; }
    }
    void PushUndoSnapshot(ProjectData document) { _undo.Push((document,_projectPath)); _redo.Clear(); }
    void ExportFrame() => ChooseFile("Export "+ActiveCameraName+" frame (1920 × 1080)", FileDialog.FileModeEnum.SaveFile, new[] { "*.png ; PNG image" }, path => { _ = ExportFrameSafe(path); });
    async Task ExportFrameSafe(string path) { try { await RenderFrame(path); Status("Exported clean 1920 × 1080 camera frame and shot metadata."); } catch(Exception ex) { Error(ex.Message); } }
    async Task RenderFrame(string path)
    {
        if (!path.EndsWith(".png",StringComparison.OrdinalIgnoreCase))path+=".png";
        var view = new SubViewport { Size = new Vector2I(1920,1080), World3D = _sceneViewport.World3D, RenderTargetUpdateMode = SubViewport.UpdateMode.Once, Msaa3D = Viewport.Msaa.Msaa4X };
        AddChild(view); var camera = new Camera3D { Current=true,CullMask=1,Near=.05f,Far=500,KeepAspect=Camera3D.KeepAspectEnum.Width,Fov=_shot.ShotCamera.Fov };view.AddChild(camera);camera.GlobalTransform=_shot.ShotCamera.GlobalTransform;
        try { for(int i=0;i<2;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);var image=view.GetTexture().GetImage();var error=image.SavePng(path);if(error!=Godot.Error.Ok)throw new IOException("The frame could not be saved: "+error); File.WriteAllText(Path.ChangeExtension(path,".json"),JsonSerializer.Serialize(new {application="Previs",version="0.5.0",frame=(int)Math.Round(_time*_fps),timeSeconds=_time,fps=_fps,width=1920,height=1080,activeCameraId=ActiveCameraId,camera=Snapshot()},_json)); }
        finally { view.QueueFree(); }
    }

    async Task RunSmokeChecks()
    {
        try
        {
            for (int i=0;i<5;i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(BvhChecks.Run() >= 14, "BVH parser checks");
            Require(_entries.Count >= 7, "Starter scene objects");
            Require(_cameras.Count==3&&_cameras.Select(c=>c.Id).Distinct().Count()==3,"Three independent cameras");
            Require(!_lighting.DirectionalEnabled&&!Descendants(_world).OfType<DirectionalLight3D>().Any(l=>l.Visible),"Neutral default without directional light");
            _lighting.DirectionalEnabled=true;_lighting.DirectionalEnabled=false;Require(Descendants(_world).OfType<DirectionalLight3D>().Count()==1&&!_lighting.DirectionalEnabled,"Optional directional light toggles without accumulation");
            Require(ActorPathProbe.RunChecks()>1000,"Arc-length character path checks");
            var actor = (RiggedActor)_entries[0].Node; Require(actor.DurationSeconds > 0, "BVH sample loaded");
            actor.ApplyTime(.5, true, false, 1); var sk = Descendants(actor).OfType<Skeleton3D>().First(); int boneCount = sk.GetBoneCount(); var pose = sk.GetBonePoseRotation(Math.Min(5, boneCount-1)); actor.ApplyTime(2, true, false, 1); actor.ApplyTime(.5,true,false,1); Require(pose.IsEqualApprox(sk.GetBonePoseRotation(Math.Min(5,boneCount-1))), "Deterministic BVH scrub");
            var real = SamplePath("samples/real/CMU_02_01_Walk.bvh"); actor.LoadBvh(real,.055f); actor.ApplyTime(.7,false); Require(actor.JointCount>20&&actor.RootMotionOffset.Length()>.01f,"Real CMU import and root motion"); actor.LoadBvh(SamplePath("samples/synthetic-walk-gesture.bvh"),.01f);
            _shot.Evaluate(1); var cameraPose = _shot.GlobalTransform; _shot.Evaluate(5); _shot.Evaluate(1); Require(cameraPose.IsEqualApprox(_shot.GlobalTransform), "Deterministic camera scrub");
            TogglePilot();SetTime(1);var pilotStart=_editorCamera.GlobalTransform;SetTime(5);Require(!pilotStart.IsEqualApprox(_editorCamera.GlobalTransform)&&_editorCamera.GlobalTransform.IsEqualApprox(_shot.GlobalTransform),"Pilot playback follows camera keys");Require(_editorCamera.CullMask==1,"Pilot excludes editor helpers");TogglePilot();
            var originalCamera=ActiveCameraId;SetActiveCamera(_cameras[1].Id);var bPose=_shot.GlobalTransform;SetTime(2);Require(bPose.IsEqualApprox(_shot.GlobalTransform),"Static Camera B independent of Camera A dolly");SetActiveCamera(originalCamera);
            var trackEntry=_entries[0];AppendMotion(trackEntry,SamplePath("samples/real/CMU_02_01_Walk.bvh"),.055f);var second=trackEntry.Data.MotionClips[1];Require(second.Start<trackEntry.Data.MotionClips[0].Duration&&second.BlendIn>0,"Sequential motion clips overlap for crossfade");
            trackEntry.Data.Path=new ActorPathData{Enabled=true,Duration=8,Points=new(){new float[]{-2,0,1},new float[]{0,0,2},new float[]{3,0,0}},OrientToPath=true};SetTime(4);var pathPosition=trackEntry.Node.Position;var pathRotation=trackEntry.Node.Quaternion;Require(pathPosition.IsEqualApprox(ActorPathRuntime.Evaluate(trackEntry.Data.Path,4).Position),"Drawn path controls actor position");Require(Math.Abs(((RiggedActor)trackEntry.Node).RootMotionOffset.X)<.0001&&Math.Abs(((RiggedActor)trackEntry.Node).RootMotionOffset.Z)<.0001,"Path suppresses duplicate mocap translation");SetTime(7);SetTime(4);Require(pathPosition.IsEqualApprox(trackEntry.Node.Position)&&Math.Abs(pathRotation.Dot(trackEntry.Node.Quaternion))>.99999,"Blended path playback deterministic after reverse seek");
            SetPathKeyTime(trackEntry,1,2,false);SetTime(2);Require(trackEntry.Node.Position.DistanceTo(Vector(trackEntry.Data.Path.Points[1]))<.001f,"Path key arrives at its curve point at the edited time");SetPathKeyTime(trackEntry,1,3,false);SetTime(2);Require(trackEntry.Node.Position.DistanceTo(Vector(trackEntry.Data.Path.Points[1]))>.05f,"Moving a path key changes travel timing");SetTime(3);Require(trackEntry.Node.Position.DistanceTo(Vector(trackEntry.Data.Path.Points[1]))<.001f,"Retimed path key hits the same spatial point");
            AddCameraFromView(false);Require(_cameras.Count==4&&!_shot.Visible,"Added camera from current view does not occlude working view");RemoveActiveCamera();Require(_cameras.Count==3,"Removing camera retains other tracks");
            var before = Snapshot(); var extra = AddObject("box",new Vector3(2,0,2)); Select(extra); Require(_selected==extra,"Object selection"); Restore(before, ""); Require(_entries.Count==before.Objects.Count,"Scene restore");
            string output = ProjectSettings.GlobalizePath("res://output/smoke"); Directory.CreateDirectory(output); SaveTo(Path.Combine(output,"roundtrip.previs")); var data = JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(_projectPath))!; Restore(data,output); Require(_entries.Count==before.Objects.Count,"Project roundtrip");
            Require(data.Version==4&&_cameras.Count==3&&_entries[0].Data.MotionClips.Count==2&&_entries[0].Data.Path.Points.Count==3,"Version 4 roundtrip retains all cameras clips and path");
            Require(_entries[0].Data.Path.PointTimes.Count==3&&Math.Abs(_entries[0].Data.Path.PointTimes[1]-3)<.001,"Edited path key times survive save and reopen");
            var savedV2=Snapshot();var legacy=JsonSerializer.Deserialize<ProjectData>(JsonSerializer.Serialize(savedV2))!;legacy.Version=1;legacy.Cameras.Clear();legacy.ActiveCameraId="";foreach(var obj in legacy.Objects){if(obj.MotionClips.Count>0){var clip=obj.MotionClips[0];obj.MotionPath=clip.MotionPath;obj.MotionName=clip.Name;obj.SourceScale=clip.SourceScale;obj.MotionClips.Clear();}obj.Path=new();}Restore(legacy,"");Require(_cameras.Count==1&&_entries[0].Data.MotionClips.Count==1,"Version 1 scene migration");Restore(savedV2,"");
            var deletionEntry=_entries[0];deletionEntry.Data.MotionClips.Clear();RebuildTrack(deletionEntry);var deleted=Snapshot();Restore(deleted,"");Require(_entries[0].Data.MotionClips.Count==0,"Removing last clip survives restore");Restore(savedV2,"");
            RunLayerWorkflowChecks();await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);var dropTarget=_entries.First(e=>e.Node is RiggedActor);var dropBounds=ProjectedActorBounds(dropTarget);Require(PickMotionTarget(dropBounds.GetCenter())==dropTarget,"Motion drops resolve the displayed rig rather than creating a duplicate");
            Require(new Vector2(_sceneViewport.Size.X,_sceneViewport.Size.Y).DistanceTo(_sceneContainer.Size)<2,"Viewport input and render coordinates align under DPI scaling");
            var beforeDrop=Snapshot();int countBeforeDrop=_entries.Count;int clipsBeforeDrop=dropTarget.Data.MotionClips.Count;var payload=new Godot.Collections.Dictionary{{"previs_type","motion"},{"id","human-cmu-walk"}};var dropSurface=(SceneDropViewport)_sceneContainer;Require(dropSurface._CanDropData(dropBounds.GetCenter(),payload),"Motion library drag accepted for matching actor");dropSurface._DropData(dropBounds.GetCenter(),payload);Require(_entries.Count==countBeforeDrop&&dropTarget.Data.MotionClips.Count==clipsBeforeDrop+1,"Dropping a motion adds a clip to existing actor");Restore(beforeDrop,"");SaveTo(Path.Combine(output,"layered-scene.previs"));
            ShowCategory("Motions");_timeline.ExpandObject(_entries[0].Data.Id);
            SetTime(1.25); for(int i=0;i<4;i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (DisplayServer.GetName() != "headless") { await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw); GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,"ui.png")); await RenderFrame(Path.Combine(output,"camera.png")); Require(Image.LoadFromFile(Path.Combine(output,"camera.png")).GetWidth()==1920,"Full HD camera render"); }
            GD.Print("PREVIS_SMOKE_OK objects="+_entries.Count+" bones="+boneCount+" keys="+_shot.Keys.Count); GetTree().Quit(0);
        }
        catch(Exception ex) { GD.PushError("PREVIS_SMOKE_FAILED "+ex); GetTree().Quit(1); }
    }
    static void Require(bool condition,string message) { if(!condition)throw new Exception(message); GD.Print("PASS "+message); }

    partial class CameraFrameOverlay : Control
    {
        public PrevisApp App = null!;
        public override void _Draw(){if(App==null||!App._pilot)return;float w=Size.X,h=Size.Y,target=16f/9;if(w/h<target){float bar=(h-w/target)/2;DrawRect(new Rect2(0,0,w,bar),Colors.Black);DrawRect(new Rect2(0,h-bar,w,bar),Colors.Black);}else{float bar=(w-h*target)/2;DrawRect(new Rect2(0,0,bar,h),Colors.Black);DrawRect(new Rect2(w-bar,0,bar,h),Colors.Black);}DrawString(ThemeDB.FallbackFont,new Vector2(12,20),App.ActiveCameraName.ToUpperInvariant()+" · 16:9",fontSize:12,modulate:Colors.White);}
    }

}
