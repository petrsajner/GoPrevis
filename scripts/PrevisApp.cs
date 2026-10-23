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
    Vector3 _orbitTarget = new(0, .9f, 0);
    float _yaw = .65f, _pitch = .42f, _distance = 12;
    string _category = "People", _projectPath = "";

    public override void _Ready()
    {
        TranslationServer.SetLocale("en");
        DisplayServer.WindowSetTitle("Previs 0.1.0 — Film shot planning");
        BuildTheme(); BuildUi(); BuildWorld(); StarterScene();
        if (OS.GetCmdlineUserArgs().Contains("--smoke-test")) _ = RunSmokeChecks();
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

    void BuildUi()
    {
        var root = new VBoxContainer { OffsetLeft = 0, OffsetTop = 0 };
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); root.AddThemeConstantOverride("separation", 1); AddChild(root);
        var brand = Text("PREVIS", 16);
        var top = Row(brand, Text("0.1.0 prototype", 12, true), Space(), Button("New scene", NewScene), Button("Open", OpenProject), Button("Save", SaveProject), Button("Undo", Undo), Button("Redo", Redo), Button("Export frame", ExportFrame));
        top.CustomMinimumSize = new Vector2(0, 41); root.AddChild(Panel(top));
        var middle = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; middle.AddThemeConstantOverride("separation", 1); root.AddChild(middle);
        var rail = new VBoxContainer { CustomMinimumSize = new Vector2(76, 0) };
        foreach (var category in new[] { "Scene", "People", "Animals", "Props", "Environment", "Cameras", "Camera moves", "Motions" })
        { string c = category; var b = Button(category.Replace("Camera moves", "Camera\nmoves"), () => ShowCategory(c), true); b.CustomMinimumSize = new Vector2(68, 49); rail.AddChild(b); _tabs[c] = b; }
        middle.AddChild(Panel(rail));
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(208, 0) };
        left.AddChild(Text("LIBRARY", 12, true));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _library = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; scroll.AddChild(_library); left.AddChild(scroll);
        left.AddChild(Button("Import mocap…", ImportMotion)); left.AddChild(Button("Import 3D model…", ImportModel));
        left.AddChild(Text("IN THIS SCENE", 12, true));
        _objectList = new ItemList { CustomMinimumSize = new Vector2(0, 156), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _objectList.ItemSelected += i => Select(_entries[(int)i]); left.AddChild(_objectList);
        left.AddChild(Row(Button("Duplicate", DuplicateSelected), Button("Delete", DeleteSelected)));
        middle.AddChild(Panel(left));
        var center = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        center.AddChild(Panel(Row(Button("Select", () => _moveTool = false), Button("Move", () => _moveTool = true), Button("Frame selected", FrameSelected), Space(), Button("Perspective", () => { if(_pilot)TogglePilot();else ResetEditorView(); }))));
        _sceneContainer = new SubViewportContainer { Stretch = true, SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Stop };
        _sceneViewport = new SubViewport { Size = new Vector2I(800, 500), RenderTargetUpdateMode = SubViewport.UpdateMode.Always, HandleInputLocally = true, Msaa3D = Viewport.Msaa.Msaa4X };
        _sceneContainer.AddChild(_sceneViewport); _sceneContainer.GuiInput += ViewportInput;
        center.AddChild(_sceneContainer);
        _framing = new CameraFrameOverlay { App = this, MouseFilter = MouseFilterEnum.Ignore }; _framing.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); _sceneContainer.AddChild(_framing);
        center.AddChild(Text("LMB select / move  ·  RMB orbit  ·  MMB pan  ·  Wheel zoom  ·  RMB + WASD/QE fly  ·  F frame", 11, true));
        middle.AddChild(center);
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(272, 0) }; right.AddChild(Text("CAMERA PREVIEW", 12, true));
        var preview = new SubViewportContainer { Stretch = true, CustomMinimumSize = new Vector2(264, 148), MouseFilter = MouseFilterEnum.Ignore };
        _previewViewport = new SubViewport { Size = new Vector2I(528, 296), RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Msaa4X };
        preview.AddChild(_previewViewport); right.AddChild(preview);
        _cameraInfo = Text("Camera A · 35 mm · 16:9", 12, true); right.AddChild(_cameraInfo);
        _pilotButton = Button("Enter camera view", TogglePilot, true); right.AddChild(_pilotButton);
        right.AddChild(Row(Button("Use current view", UseCurrentView), Button("Add key", CaptureCameraKey)));
        var lensRow = Row(Text("Lens (mm)", 12), Space()); _lens = Spin(14, 200, 1, 35); lensRow.AddChild(_lens); right.AddChild(lensRow);
        _lens.ValueChanged += value => { if (!_refreshing && _shot != null) { _shot.FocalLengthMm = (float)value; UpdatePreview(); } };
        right.AddChild(new HSeparator());
        var inspectorScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _inspector = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; inspectorScroll.AddChild(_inspector); right.AddChild(inspectorScroll);
        BuildInspector(); middle.AddChild(Panel(right));
        var transport = Row(); _play = Button("▶ Play", () => { _playing = !_playing; _play.Text = _playing ? "Ⅱ Pause" : "▶ Play"; }); transport.AddChild(_play);
        transport.AddChild(Button("Start", () => SetTime(0))); _timeLabel = Text("00:00:00", 14); transport.AddChild(_timeLabel);
        _scrub = new HSlider { MinValue = 0, MaxValue = _duration, Step = 1.0 / _fps, SizeFlagsHorizontal = SizeFlags.ExpandFill }; _scrub.ValueChanged += v => { if (!_refreshing) SetTime(v); }; transport.AddChild(_scrub);
        transport.AddChild(Text("Duration", 12, true)); _durationField = Spin(1, 120, 1, _duration); _durationField.CustomMinimumSize = new Vector2(80,0); _durationField.SizeFlagsHorizontal=SizeFlags.ShrinkEnd; _durationField.ValueChanged += v => { if(_refreshing)return; _duration = v; _scrub.MaxValue = v; SetTime(Math.Min(_time,v)); }; transport.AddChild(_durationField);
        transport.AddChild(Text("fps", 12, true)); _fpsChoice = new OptionButton(); foreach (int f in new[] { 24, 25, 30, 60 }) _fpsChoice.AddItem(f.ToString(), f); _fpsChoice.ItemSelected += i => { if(_refreshing)return; _fps = _fpsChoice.GetItemId((int)i); _scrub.Step = 1.0 / _fps; SetTime(_time); }; transport.AddChild(_fpsChoice);
        root.AddChild(Panel(transport));
        _timeline = new TimelineView { CustomMinimumSize = new Vector2(0, 104), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _timeline.App = this; root.AddChild(Panel(_timeline));
        _status = Text("Ready", 11, true); root.AddChild(Panel(_status));
        _fileDialog = new FileDialog { Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = false };
        _fileDialog.FileSelected += path => { try { _fileAction?.Invoke(path); } catch (Exception ex) { Error(ex.Message); } }; AddChild(_fileDialog);
        _message = new AcceptDialog { Title = "Previs" }; AddChild(_message);
    }

    SpinBox Spin(double min, double max, double step, double value)
        => new() { MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(70, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };

    void BuildInspector()
    {
        _inspector.AddChild(Text("PROPERTIES", 12, true));
        _name = new LineEdit { PlaceholderText = "Select an object" }; _name.TextSubmitted += value => { if (_selected == null) return; PushUndo(); _selected.Data.Name = value.Trim(); RefreshObjects(); }; _inspector.AddChild(_name);
        foreach (string group in new[] { "Position", "Rotation", "Scale" })
        {
            _inspector.AddChild(Text(group + (group == "Position" ? " (m)" : group == "Rotation" ? " (°)" : ""), 12, true));
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 3);
            for (int i = 0; i < 3; i++)
            {
                int axis = i; var field = Spin(group == "Scale" ? .05 : -1000, 1000, group == "Rotation" ? 1 : .05, group == "Scale" ? 1 : 0);
                field.TooltipText = $"{group} {"XYZ"[i]}"; field.CustomMinimumSize = new Vector2(76, 0); _fields[group + i] = field;
                field.ValueChanged += value => ChangeTransform(group, axis, (float)value); row.AddChild(field);
            }
            _inspector.AddChild(row);
        }
        _inspector.AddChild(new HSeparator()); _inspector.AddChild(Text("MOTION CLIP", 12, true));
        _clipInfo = Text("Select a prepared actor to import BVH.", 12, true); _clipInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart; _inspector.AddChild(_clipInfo);
        _animationChoice = new OptionButton(); _animationChoice.ItemSelected += index => { if (_selected?.Player is { } player) { _selected.Animation = _animationChoice.GetItemText((int)index); player.Play(_selected.Animation); SetTime(_time); } }; _inspector.AddChild(_animationChoice);
        _inspector.AddChild(Button("Import BVH to selected actor…", ImportMotion));
        _inspector.AddChild(Text("Source units → metres", 11, true));
        _sourceScale = Spin(.0001, 10, .001, .01); _sourceScale.ValueChanged += value => ReloadMotionScale((float)value); _inspector.AddChild(_sourceScale);
        var speedRow = Row(Text("Speed", 12), Space()); _speed = Spin(.1, 4, .1, 1); speedRow.AddChild(_speed); _inspector.AddChild(speedRow);
        _speed.ValueChanged += v => { if (!_refreshing && _selected != null) { _selected.Data.Speed = v; SetTime(_time); } };
        _loop = new CheckButton { Text = "Loop", ButtonPressed = true }; _inPlace = new CheckButton { Text = "In-place (no forward motion)" };
        _loop.Toggled += v => { if (!_refreshing && _selected != null) { _selected.Data.Loop = v; SetTime(_time); } };
        _inPlace.Toggled += v => { if (!_refreshing && _selected != null) { _selected.Data.InPlace = v; SetTime(_time); } };
        _inspector.AddChild(_loop); _inspector.AddChild(_inPlace);
    }

    void BuildWorld()
    {
        _world = new Node3D { Name = "World" }; _sceneViewport.AddChild(_world);
        var env = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color("252529"), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color("c1c4d3"), AmbientLightEnergy = .65f, TonemapMode = Godot.Environment.ToneMapper.Filmic };
        _world.AddChild(new WorldEnvironment { Environment = env });
        _world.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-48, -28, 0), LightEnergy = 1.3f, ShadowEnabled = true, DirectionalShadowMaxDistance = 60 });
        var floor = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(100, 100) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("28282c"), Roughness = 1 } }; _world.AddChild(floor);
        var grid = new ImmediateMesh(); grid.SurfaceBegin(Mesh.PrimitiveType.Lines);
        for (int i = -30; i <= 30; i++) { grid.SurfaceAddVertex(new Vector3(i, .012f, -30)); grid.SurfaceAddVertex(new Vector3(i, .012f, 30)); grid.SurfaceAddVertex(new Vector3(-30, .012f, i)); grid.SurfaceAddVertex(new Vector3(30, .012f, i)); } grid.SurfaceEnd();
        _world.AddChild(new MeshInstance3D { Mesh = grid, Layers = 2, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("414147"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } });
        _editorCamera = new Camera3D { CullMask = 3, Far = 500, Near = .05f, Current = true, Fov = 50 }; _world.AddChild(_editorCamera); ResetEditorView();
        _shot = new StudioCameraRig(); _world.AddChild(_shot); _shot.ResetShot(); _shot.ShotCamera.Current = false; _editorCamera.MakeCurrent();
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
        AddObject("chair", new Vector3(1, 0, -3)); _entries[^1].Node.RotationDegrees = new Vector3(0, 180, 0);
        AddObject("car", new Vector3(4, 0, -4)); AddObject("wall", new Vector3(-2, 0, -4)); AddObject("tree", new Vector3(-4, 0, -2));
        _shot.SetPreset("Dolly in", _duration); Select(_entries[0]); ShowCategory("People"); SetTime(0); _undo.Clear(); _redo.Clear();
        Status("Ready — synthetic demo motion. Add objects, import BVH, or pilot Camera A and capture keys.");
    }

    Entry AddObject(string kind, Vector3 position, string? name = null)
    {
        Node3D node = kind == "actor" ? new RiggedActor() : PropFactory.Create(kind);
        var data = new ObjectData { Kind = kind, Name = name ?? (kind == "actor" ? "Actor" : PropFactory.DisplayName(kind)) };
        var entry = new Entry { Node = node, Data = data }; node.Name = "Object_" + data.Id; node.SetMeta("entry_id", data.Id);
        _world.AddChild(node); node.Position = position; _entries.Add(entry); RefreshObjects(); return entry;
    }
    void AddFromLibrary(string kind) { PushUndo(); var item = AddObject(kind, new Vector3(0, 0, 0)); Select(item); Status("Added " + item.Data.Name + ". Drag it with the Move tool or edit Position."); }

    void ShowCategory(string category)
    {
        _category = category; foreach (var p in _tabs) p.Value.ButtonPressed = p.Key == category;
        foreach (Node c in _library.GetChildren()) { _library.RemoveChild(c); c.QueueFree(); }
        _library.AddChild(Text(category.ToUpperInvariant(), 12, true));
        if (category == "Cameras" || category == "Camera moves")
        {
            if (category == "Cameras")
            {
                foreach (var p in new[] { "Wide shot", "Medium shot", "Close-up", "High angle", "Current view" }) { string choice = p; _library.AddChild(Button(p, () => ApplyShot(choice))); }
            }
            else foreach (string preset in new[] { "Static", "Dolly in", "Truck right", "Crane up", "Orbit" }) { string p = preset; _library.AddChild(Button(p, () => { PushUndo(); _shot.SetPreset(p, _duration); SetTime(0); Status(p + " camera move applied. Keys are editable by recapturing a frame."); })); }
            _library.AddChild(Button("Add camera key", CaptureCameraKey)); _library.AddChild(Button("Clear camera keys", () => { PushUndo(); _shot.ClearKeys(); _timeline.QueueRedraw(); })); return;
        }
        if (category == "Motions")
        {
            var notice = Text("BVH imports are applied to the selected prepared human rig. Imported FBX/GLB models play their own animation.", 12, true); notice.AutowrapMode = TextServer.AutowrapMode.WordSmart; _library.AddChild(notice);
            _library.AddChild(Button("Import BVH…", ImportMotion));
            _library.AddChild(Button("Synthetic walk / gesture", () => ApplySample("res://samples/synthetic-walk-gesture.bvh")));
            string realSample = SamplePath("samples/real/CMU_02_01_Walk.bvh");
            if (File.Exists(realSample)) _library.AddChild(Button("CMU · Captured walk", () => ApplySample(realSample)));
            return;
        }
        if (category == "Scene") { _library.AddChild(Text("Select an object below.\nDrag with Move.\nF frames the selection.\nCtrl+D duplicates.\nDelete removes it.", 12, true)); return; }
        var kinds = category switch { "People" => new[] { "actor" }, "Animals" => new[] { "dog" }, "Props" => new[] { "table", "chair", "car", "box", "sphere", "cylinder" }, _ => new[] { "wall", "floor", "tree", "box" } };
        var cards = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill }; _library.AddChild(cards);
        foreach (string kind in kinds)
        {
            string k = kind; var card = Button("", () => AddFromLibrary(k)); card.CustomMinimumSize = new Vector2(94, 118); card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var content = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); content.OffsetLeft = 4; content.OffsetTop = 4; content.OffsetRight = -4; content.OffsetBottom = -4; card.AddChild(content);
            var thumb = new SubViewport { Size = new Vector2I(176, 144), OwnWorld3D = true, TransparentBg = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Once }; card.AddChild(thumb);
            var thumbWorld = new Node3D(); thumb.AddChild(thumbWorld);
            thumbWorld.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color("25252b"), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightEnergy = .8f } });
            thumbWorld.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, -35, 0), LightEnergy = 1.4f });
            Node3D model = kind == "actor" ? new RiggedActor() : PropFactory.Create(kind); thumbWorld.AddChild(model);
            Aabb bounds = kind == "actor" ? new Aabb(new Vector3(-.5f, 0, -.3f), new Vector3(1, 1.8f, .6f)) : PropFactory.CalculateBounds(model);
            var camera = new Camera3D { Current = true, Fov = 40 }; thumbWorld.AddChild(camera); var target = bounds.GetCenter(); float radius = Math.Max(1.2f, bounds.Size.Length() * 1.15f); var cameraPosition = target + new Vector3(radius * .7f, radius * .5f, radius); camera.Transform = Transform3D.Identity.LookingAt(target - cameraPosition, Vector3.Up); camera.Position = cameraPosition;
            content.AddChild(new TextureRect { Texture = thumb.GetTexture(), CustomMinimumSize = new Vector2(84, 78), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ExpandFill });
            var title = Text(kind == "actor" ? "Human actor" : PropFactory.DisplayName(kind), 11); title.HorizontalAlignment = HorizontalAlignment.Center; title.AutowrapMode = TextServer.AutowrapMode.WordSmart; title.MouseFilter = MouseFilterEnum.Ignore; content.AddChild(title); cards.AddChild(card);
        }
    }

    void Select(Entry? entry)
    {
        _selected = entry; _refreshing = true;
        _name.Text = entry?.Data.Name ?? "";
        foreach (var p in _fields) { string group = p.Key[..^1]; int axis = p.Key[^1] - '0'; var value = group == "Position" ? entry?.Node.Position : group == "Rotation" ? entry?.Node.RotationDegrees : entry?.Node.Scale; p.Value.Value = value.HasValue ? value.Value[axis] : group == "Scale" ? 1 : 0; p.Value.Editable = entry != null; }
        _loop.ButtonPressed = entry?.Data.Loop ?? true; _inPlace.ButtonPressed = entry?.Data.InPlace ?? false; _speed.Value = entry?.Data.Speed ?? 1; _sourceScale.Value = entry?.Data.SourceScale ?? .01f;
        _animationChoice.Clear(); _animationChoice.Visible = entry?.Player != null;
        if (entry?.Player is { } player) { foreach (string animation in player.GetAnimationList()) if (animation != "RESET") _animationChoice.AddItem(animation); for(int i=0;i<_animationChoice.ItemCount;i++)if(_animationChoice.GetItemText(i)==entry.Animation)_animationChoice.Select(i); }
        _clipInfo.Text = entry?.Node is RiggedActor actor ? (actor.DurationSeconds <= 0 ? "Prepared human rig · no motion loaded." : $"{entry.Data.MotionName ?? actor.MotionName}\n{actor.DurationSeconds:0.00} s · {actor.SourceFps:0.##} fps · {actor.JointCount} joints\n{actor.ImportSummary}") : entry?.Player != null ? "Imported model · native animation clips" : "Static blockout prop";
        if (entry != null) { int index = _entries.IndexOf(entry); if (index >= 0) _objectList.Select(index); }
        _refreshing = false; UpdateSelection();
    }
    void RefreshObjects() { _objectList.Clear(); foreach (var e in _entries) _objectList.AddItem(e.Data.Name); if (_selected != null && _entries.Contains(_selected)) _objectList.Select(_entries.IndexOf(_selected)); }
    void ChangeTransform(string group, int axis, float value)
    {
        if (_refreshing || _selected == null) return; PushUndo(); var n = _selected.Node;
        Vector3 vector = group == "Position" ? n.Position : group == "Rotation" ? n.RotationDegrees : n.Scale; vector[axis] = value;
        if (group == "Position") n.Position = vector; else if (group == "Rotation") n.RotationDegrees = vector; else n.Scale = vector; UpdateSelection();
    }
    void DuplicateSelected()
    {
        if (_selected == null) return; PushUndo(); var d = SnapshotObject(_selected); d.Id = Guid.NewGuid().ToString("N"); d.Name += " copy"; d.Position[0] += .75f; var e = RestoreObject(d, ""); Select(e);
    }
    void DeleteSelected()
    {
        if (_selected == null) return; PushUndo(); var e = _selected; _selected = null; _entries.Remove(e); _world.RemoveChild(e.Node); e.Node.QueueFree(); RefreshObjects(); Select(null); Status("Object deleted.");
    }

    void ImportMotion() => ChooseFile("Import BVH motion capture", FileDialog.FileModeEnum.OpenFile, new[] { "*.bvh ; BVH motion capture" }, path =>
    {
        Entry? target = _selected?.Node is RiggedActor ? _selected : null;
        if (target == null) { PushUndo(); target = AddObject("actor", Vector3.Zero); } else PushUndo();
        LoadMotion(target, path, (float)_sourceScale.Value); Select(target); SetTime(0); Status("Imported " + Path.GetFileName(path) + " — " + ((RiggedActor)target.Node).ImportSummary);
    });
    void ApplySample(string path)
    {
        path = path.StartsWith("res://") ? SamplePath(path[6..]) : path;
        float scale = Path.GetFileName(path).StartsWith("CMU_", StringComparison.OrdinalIgnoreCase) ? .055f : .01f;
        try { PushUndo(); Entry target = _selected?.Node is RiggedActor ? _selected : AddObject("actor", Vector3.Zero); LoadMotion(target, path, scale); Select(target); SetTime(0); Status("Loaded " + Path.GetFileName(path)); } catch (Exception ex) { Error(ex.Message); }
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
        ((RiggedActor)target.Node).LoadBvh(path, scale); target.Data.MotionPath = Path.GetFullPath(path); target.Data.MotionName = Path.GetFileNameWithoutExtension(path); target.Data.SourceScale = scale;
    }
    void ReloadMotionScale(float scale)
    {
        if (_refreshing || _selected?.Node is not RiggedActor || string.IsNullOrEmpty(_selected.Data.MotionPath)) return;
        try { LoadMotion(_selected, _selected.Data.MotionPath, scale); SetTime(_time); } catch (Exception ex) { Error(ex.Message); }
    }

    void ImportModel() => ChooseFile("Import prepared 3D model", FileDialog.FileModeEnum.OpenFile, new[] { "*.glb,*.gltf,*.fbx ; Animated 3D models" }, path =>
    { PushUndo(); var data = new ObjectData { Kind = "imported", Name = Path.GetFileNameWithoutExtension(path), ModelPath = path }; var entry = RestoreObject(data, ""); Select(entry); Status("Imported model. Its original rig and animation are preserved."); });
    Entry ImportModelData(ObjectData data)
    {
        string path = data.ModelPath ?? throw new InvalidDataException("The model path is missing.");
        bool fbx = Path.GetExtension(path).Equals(".fbx", StringComparison.OrdinalIgnoreCase);
        GltfDocument document = fbx ? new FbxDocument() : new GltfDocument(); GltfState state = fbx ? new FbxState() : new GltfState();
        var result = document.AppendFromFile(path, state); if (result != Godot.Error.Ok) throw new InvalidDataException("The model could not be imported: " + result);
        var model = document.GenerateScene(state) ?? throw new InvalidDataException("No scene was found in the model.");
        var root = new Node3D(); root.SetMeta("entry_id", data.Id); _world.AddChild(root); root.AddChild(model);
        foreach (var mesh in Descendants(model).OfType<MeshInstance3D>()) if (mesh.Mesh != null) { mesh.CreateTrimeshCollision(); foreach (Node child in mesh.GetChildren()) if (child is StaticBody3D body) body.SetMeta("scene_root", root); }
        var player = Descendants(model).OfType<AnimationPlayer>().FirstOrDefault(); string animation = data.ModelAnimation != null && player?.HasAnimation(data.ModelAnimation)==true ? data.ModelAnimation : player?.GetAnimationList().FirstOrDefault(a => a != "RESET") ?? "";
        if (player != null && animation != "") { player.Set("callback_mode_process", 2); player.Play(animation); }
        var entry = new Entry { Data = data, Node = root, Player = player, Animation = animation }; _entries.Add(entry); return entry;
    }
    static IEnumerable<Node> Descendants(Node root) { yield return root; foreach (Node child in root.GetChildren()) foreach (Node nested in Descendants(child)) yield return nested; }

    void CaptureCameraKey() { PushUndo(); _shot.CaptureKey(_time); _timeline.QueueRedraw(); Status($"Camera key captured at {_time:0.000} s. {_shot.Keys.Count} key(s)."); }
    void UseCurrentView() { PushUndo(); _shot.GlobalTransform = _editorCamera.GlobalTransform; UpdatePreview(); Status("Camera A now matches the working view. Add a key to keep this pose."); }
    void TogglePilot() { _pilot = !_pilot; _editorCamera.CullMask=_pilot?1u:3u; _pilotButton.ButtonPressed = _pilot; _pilotButton.Text = _pilot ? "Exit camera view" : "Enter camera view"; if (_pilot) _editorCamera.GlobalTransform = _shot.GlobalTransform; else { _editorCamera.KeepAspect=Camera3D.KeepAspectEnum.Height; _editorCamera.Fov=50; ResetEditorView(); } UpdatePreview(); Status(_pilot ? "Camera view: RMB look + WASD/QE move. Add key at the desired frame." : "Working perspective view."); }
    void ApplyShot(string shot)
    {
        PushUndo(); Vector3 target = (_selected?.Node.Position ?? Vector3.Zero) + Vector3.Up;
        _shot.ClearKeys(); if (shot == "Current view") _shot.GlobalTransform = _editorCamera.GlobalTransform;
        else { Vector3 offset = shot switch { "Medium shot" => new Vector3(0, .3f, 3.8f), "Close-up" => new Vector3(0, .55f, 1.8f), "High angle" => new Vector3(3, 6, 4), _ => new Vector3(6, 2.5f, 7) }; _shot.SetPose(target + offset, target); }
        _shot.CaptureKey(_time); if(_pilot)_editorCamera.GlobalTransform=_shot.GlobalTransform; UpdatePreview(); _timeline.QueueRedraw(); Status(shot + " framing applied to Camera A.");
    }
    void ResetEditorView() { _orbitTarget = new Vector3(0, .9f, 0); _yaw = .65f; _pitch = .42f; _distance = 12; UpdateOrbit(); }
    void UpdateOrbit() { if (_editorCamera == null) return; _editorCamera.Position = _orbitTarget + new Vector3(Mathf.Sin(_yaw) * Mathf.Cos(_pitch), Mathf.Sin(_pitch), Mathf.Cos(_yaw) * Mathf.Cos(_pitch)) * _distance; _editorCamera.LookAt(_orbitTarget); }
    void FrameSelected() { if (_selected == null) return; if (_pilot) TogglePilot(); _orbitTarget = _selected.Node.Position + Vector3.Up + (_selected.Node is RiggedActor actor ? actor.RootMotionOffset : Vector3.Zero); _distance = 5; UpdateOrbit(); }
    void UpdatePreview() { if (_previewCamera == null || _shot == null) return; _previewCamera.GlobalTransform = _shot.ShotCamera.GlobalTransform; _previewCamera.Fov = _shot.ShotCamera.Fov; _cameraInfo.Text = $"Camera A · {_shot.FocalLengthMm:0} mm · 16:9 · {_shot.Keys.Count} keys"; if(_pilot){_editorCamera.KeepAspect=Camera3D.KeepAspectEnum.Width;float aspect=_sceneViewport.Size.X/(float)Math.Max(1,_sceneViewport.Size.Y);_editorCamera.Fov=Mathf.RadToDeg(2*Mathf.Atan(Mathf.Tan(Mathf.DegToRad(_shot.ShotCamera.Fov)*.5f)*Math.Max(1,aspect/(16f/9))));} _framing?.QueueRedraw(); }

    public override void _Process(double delta)
    {
        if (_playing) { double next = _time + delta; if (next >= _duration) { next = _duration; _playing = false; _play.Text = "▶ Play"; } SetTime(next); }
        if (_rightDown)
        {
            Vector3 direction = Vector3.Zero;
            if (Input.IsPhysicalKeyPressed(Key.W)) direction -= _editorCamera.GlobalBasis.Z;
            if (Input.IsPhysicalKeyPressed(Key.S)) direction += _editorCamera.GlobalBasis.Z;
            if (Input.IsPhysicalKeyPressed(Key.A)) direction -= _editorCamera.GlobalBasis.X;
            if (Input.IsPhysicalKeyPressed(Key.D)) direction += _editorCamera.GlobalBasis.X;
            if (Input.IsPhysicalKeyPressed(Key.Q)) direction -= Vector3.Up; if (Input.IsPhysicalKeyPressed(Key.E)) direction += Vector3.Up;
            if (direction.LengthSquared() > 0) { Vector3 move = direction.Normalized() * (float)delta * (Input.IsPhysicalKeyPressed(Key.Shift) ? 10 : 3); _editorCamera.Position += move; _orbitTarget += move; if (_pilot) _shot.GlobalTransform = _editorCamera.GlobalTransform; }
        }
        UpdatePreview();
    }
    void SetTime(double seconds)
    {
        _time = Math.Clamp(seconds, 0, _duration);
        foreach (var e in _entries)
        {
            if (e.Node is RiggedActor actor) actor.ApplyTime(_time, e.Data.Loop, e.Data.InPlace, e.Data.Speed);
            else if (e.Player != null && e.Animation != "") { var anim = e.Player.GetAnimation(e.Animation); double t = _time * e.Data.Speed; if (anim.Length > 0) t = e.Data.Loop ? t % anim.Length : Math.Min(t, anim.Length); e.Player.Seek(t, true); e.Player.Advance(0); }
        }
        _shot.Evaluate(_time); if(_pilot)_editorCamera.GlobalTransform = _shot.GlobalTransform;
        bool previous = _refreshing; _refreshing = true; _scrub.Value = _time; _lens.Value = _shot.FocalLengthMm; _refreshing = previous;
        int frame = (int)Math.Floor(_time * _fps + .00001); _timeLabel.Text = $"{frame / _fps / 60:00}:{frame / _fps % 60:00}:{frame % _fps:00}"; _timeline.QueueRedraw(); UpdateSelection(); UpdatePreview();
    }
    void ViewportInput(InputEvent input)
    {
        if (input is InputEventMouseButton b)
        {
            if (b.ButtonIndex == MouseButton.Right) _rightDown = b.Pressed;
            if (b.ButtonIndex == MouseButton.Middle) _middleDown = b.Pressed;
            if (b.ButtonIndex == MouseButton.WheelUp && b.Pressed) { _distance = Math.Max(.7f, _distance * .88f); if (_pilot) { _editorCamera.Position -= _editorCamera.Basis.Z * .35f; _shot.GlobalTransform = _editorCamera.GlobalTransform; } else UpdateOrbit(); }
            if (b.ButtonIndex == MouseButton.WheelDown && b.Pressed) { _distance = Math.Min(100, _distance / .88f); if (_pilot) { _editorCamera.Position += _editorCamera.Basis.Z * .35f; _shot.GlobalTransform = _editorCamera.GlobalTransform; } else UpdateOrbit(); }
            if (b.ButtonIndex == MouseButton.Left)
            {
                if (b.Pressed) { var picked = Pick(b.Position); Select(picked); if (picked != null && _moveTool) { PushUndo(); _dragging = true; } }
                else { _dragging = false; if (_selected != null) Select(_selected); }
            }
        }
        if (input is InputEventMouseMotion m)
        {
            if (_rightDown) { if (_pilot) { _editorCamera.RotateY(-m.Relative.X * .004f); _editorCamera.RotateObjectLocal(Vector3.Right, -m.Relative.Y * .004f); _shot.GlobalTransform = _editorCamera.GlobalTransform; } else { _yaw -= m.Relative.X * .006f; _pitch = Math.Clamp(_pitch + m.Relative.Y * .006f, -.15f, 1.45f); UpdateOrbit(); } }
            if (_middleDown) { Vector3 shift=(-_editorCamera.Basis.X * m.Relative.X + _editorCamera.Basis.Y * m.Relative.Y) * _distance * .0015f; if(_pilot){_editorCamera.Position+=shift;_shot.GlobalTransform=_editorCamera.GlobalTransform;}else{_orbitTarget+=shift;UpdateOrbit();} }
            if (_dragging && _selected != null) { var origin = _editorCamera.ProjectRayOrigin(m.Position); var ray = _editorCamera.ProjectRayNormal(m.Position); if (Math.Abs(ray.Y) > .001f) { float t = (_selected.Node.Position.Y - origin.Y) / ray.Y; if (t > 0) { Vector3 p = origin + ray * t; if (Input.IsPhysicalKeyPressed(Key.Ctrl)) { p.X = Mathf.Snapped(p.X, .25f); p.Z = Mathf.Snapped(p.Z, .25f); } _selected.Node.Position = p; UpdateSelection(); } } }
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
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return;
        if (key.CtrlPressed && key.Keycode == Key.S) SaveProject(); else if (key.CtrlPressed && key.Keycode == Key.O) OpenProject(); else if (key.CtrlPressed && key.Keycode == Key.Z) Undo(); else if (key.CtrlPressed && key.Keycode == Key.Y) Redo(); else if (key.CtrlPressed && key.Keycode == Key.D) DuplicateSelected(); else if (key.Keycode == Key.Delete) DeleteSelected(); else if (key.Keycode == Key.F) FrameSelected(); else if (key.Keycode == Key.Space) { _playing = !_playing; _play.Text = _playing ? "Ⅱ Pause" : "▶ Play"; }
    }
    public override void _Input(InputEvent input)
    {
        if(input is InputEventMouseButton { Pressed: false } button) { if(button.ButtonIndex==MouseButton.Right)_rightDown=false; if(button.ButtonIndex==MouseButton.Middle)_middleDown=false; if(button.ButtonIndex==MouseButton.Left)_dragging=false; }
    }
    void UpdateSelection()
    {
        if (_selectionBox == null) return; _selectionBox.Visible = _selected != null; if (_selected == null) return;
        var bounds = _selected.Node is RiggedActor actor ? new Aabb(new Vector3(-.4f, 0, -.3f) + actor.RootMotionOffset, new Vector3(.8f, 1.9f, .6f)) : PropFactory.CalculateBounds(_selected.Node);
        var mesh = new ImmediateMesh(); mesh.SurfaceBegin(Mesh.PrimitiveType.Lines); Vector3[] corners = Enumerable.Range(0, 8).Select(i => bounds.Position + new Vector3((i & 1) != 0 ? bounds.Size.X : 0, (i & 2) != 0 ? bounds.Size.Y : 0, (i & 4) != 0 ? bounds.Size.Z : 0)).ToArray();
        foreach (int i in Enumerable.Range(0, 8)) foreach (int bit in new[] { 1, 2, 4 }) if ((i & bit) == 0) { mesh.SurfaceAddVertex(corners[i]); mesh.SurfaceAddVertex(corners[i | bit]); } mesh.SurfaceEnd();
        _selectionBox.Mesh = mesh; _selectionBox.MaterialOverride = new StandardMaterial3D { AlbedoColor = _accent, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }; _selectionBox.GlobalTransform = _selected.Node.GlobalTransform;
    }

    void ChooseFile(string title, FileDialog.FileModeEnum mode, string[] filters, Action<string> action)
    { _playing = false; _play.Text = "▶ Play"; _fileAction = action; _fileDialog.Title = title; _fileDialog.FileMode = mode; _fileDialog.Filters = filters; _fileDialog.PopupCentered(new Vector2I(900, 600)); }
    void Status(string text) { if (_status != null) _status.Text = text; }
    void Error(string message) { Status(message); _message.Title = "Previs — action could not be completed"; _message.DialogText = message; _message.PopupCentered(new Vector2I(640, 230)); GD.PushWarning(message); }
    void NewScene() { PushUndo(); ClearObjects(); _shot.ResetShot(); _shot.ClearKeys(); _projectPath = ""; SetTime(0); Status("New scene. Add an actor or prop from the library."); }
    void ClearObjects() { foreach (var e in _entries) { _world.RemoveChild(e.Node); e.Node.QueueFree(); } _entries.Clear(); _selected = null; RefreshObjects(); Select(null); }
    void PushUndo() { if (_world == null) return; _undo.Push((Snapshot(),_projectPath)); _redo.Clear(); }
    void Undo() { if (_undo.Count == 0) return; _redo.Push((Snapshot(),_projectPath)); var previous=_undo.Pop();Restore(previous.Document, "");_projectPath=previous.Path; Status("Undo."); }
    void Redo() { if (_redo.Count == 0) return; _undo.Push((Snapshot(),_projectPath)); var next=_redo.Pop();Restore(next.Document, "");_projectPath=next.Path; Status("Redo."); }
    static float[] Array3(Vector3 v) => new[] { v.X, v.Y, v.Z };
    static Vector3 Vector(float[] v, Vector3 fallback = default) => v.Length == 3 && v.All(float.IsFinite) ? new Vector3(v[0], v[1], v[2]) : fallback;
    ObjectData SnapshotObject(Entry e) => new() { Id = e.Data.Id, Kind = e.Data.Kind, Name = e.Data.Name, Position = Array3(e.Node.Position), Rotation = Array3(e.Node.RotationDegrees), Scale = Array3(e.Node.Scale), MotionPath = e.Data.MotionPath, MotionName = e.Data.MotionName, ModelPath = e.Data.ModelPath, ModelAnimation=e.Animation, SourceScale = e.Data.SourceScale, Speed = e.Data.Speed, Loop = e.Data.Loop, InPlace = e.Data.InPlace };
    ProjectData Snapshot() => new() { Fps = _fps, Duration = _duration, Objects = _entries.Select(SnapshotObject).ToList(), CameraKeys = _shot.Keys.Select(k => new CameraData { Time = k.TimeSeconds, Position = Array3(k.Position), Quaternion = new[] { k.Rotation.X, k.Rotation.Y, k.Rotation.Z, k.Rotation.W }, FocalLength = k.FocalLengthMm }).ToList(), ShotPosition = Array3(_shot.GlobalPosition), ShotRotation = Array3(_shot.GlobalRotationDegrees), FocalLength = _shot.FocalLengthMm };
    Entry RestoreObject(ObjectData data, string baseDirectory)
    {
        var copy = JsonSerializer.Deserialize<ObjectData>(JsonSerializer.Serialize(data))!;
        if (!string.IsNullOrEmpty(copy.MotionPath) && !Path.IsPathRooted(copy.MotionPath)) copy.MotionPath = Path.GetFullPath(Path.Combine(baseDirectory, copy.MotionPath));
        if (!string.IsNullOrEmpty(copy.ModelPath) && !Path.IsPathRooted(copy.ModelPath)) copy.ModelPath = Path.GetFullPath(Path.Combine(baseDirectory, copy.ModelPath));
        Entry e = copy.Kind == "imported" ? ImportModelData(copy) : AddObject(copy.Kind, Vector(copy.Position), copy.Name);
        e.Data = copy; e.Node.SetMeta("entry_id", copy.Id); e.Node.Position = Vector(copy.Position); e.Node.RotationDegrees = Vector(copy.Rotation); e.Node.Scale = Vector(copy.Scale, Vector3.One);
        if (e.Node is RiggedActor && !string.IsNullOrEmpty(copy.MotionPath)) { string? label = copy.MotionName; LoadMotion(e, copy.MotionPath, copy.SourceScale); if(!string.IsNullOrEmpty(label))copy.MotionName=label; } RefreshObjects(); return e;
    }
    void Restore(ProjectData data, string baseDirectory)
    {
        if (data.Version != 1 || data.Objects.Count > 500 || data.CameraKeys.Count > 10000) throw new InvalidDataException("Unsupported project version or project size.");
        ClearObjects(); _fps = Math.Clamp(data.Fps, 1, 120); _duration = Math.Clamp(data.Duration, 1, 120); _scrub.MaxValue = _duration; _scrub.Step = 1.0 / _fps; _refreshing=true;_durationField.Value=_duration;for(int i=0;i<_fpsChoice.ItemCount;i++)if(_fpsChoice.GetItemId(i)==_fps)_fpsChoice.Select(i);_refreshing=false;
        foreach (var obj in data.Objects) RestoreObject(obj, baseDirectory);
        _shot.ClearKeys(); foreach (var k in data.CameraKeys) { if (k.Quaternion.Length != 4) continue; _shot.Keys.Add(new ShotCameraKey(k.Time, Vector(k.Position), new Quaternion(k.Quaternion[0], k.Quaternion[1], k.Quaternion[2], k.Quaternion[3]).Normalized(), k.FocalLength)); }
        _shot.Keys.Sort((a,b) => a.TimeSeconds.CompareTo(b.TimeSeconds)); _shot.GlobalPosition = Vector(data.ShotPosition, new Vector3(6,3,7)); _shot.GlobalRotationDegrees = Vector(data.ShotRotation); _shot.FocalLengthMm = data.FocalLength;
        SetTime(_time); Select(_entries.FirstOrDefault());
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
            foreach (bool motion in new[] { true, false }) { string? source = motion ? item.MotionPath : item.ModelPath; if (string.IsNullOrEmpty(source)) continue;
                if (Path.GetExtension(source).Equals(".gltf", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Save imported models as self-contained GLB or FBX. External glTF dependencies are not bundled in this prototype.");
                string fileName = item.Id + (motion ? "_motion" : "_model") + Path.GetExtension(source); string target = Path.Combine(assetDirectory, fileName); if (!Path.GetFullPath(source).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) File.Copy(source, target, true);
                if (motion) item.MotionPath = Path.Combine(assetFolder, fileName).Replace('\\','/'); else item.ModelPath = Path.Combine(assetFolder, fileName).Replace('\\','/'); }
        }
        string temporary = path + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(document, _json)); File.Move(temporary, path, true); _projectPath = path; Status("Saved project and its motion/model assets: " + Path.GetFileName(path));
    }
    void OpenProject() => ChooseFile("Open Previs project", FileDialog.FileModeEnum.OpenFile, new[] { "*.previs ; Previs project" }, path =>
    {
        var document = JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(path)) ?? throw new InvalidDataException("The project is empty."); var before = Snapshot();
        try { Restore(document, Path.GetDirectoryName(path)!); PushUndoSnapshot(before); _projectPath = path; Status("Opened " + Path.GetFileName(path)); } catch { Restore(before, ""); throw; }
    });
    void PushUndoSnapshot(ProjectData document) { _undo.Push((document,_projectPath)); _redo.Clear(); }
    void ExportFrame() => ChooseFile("Export Camera A frame (1920 × 1080)", FileDialog.FileModeEnum.SaveFile, new[] { "*.png ; PNG image" }, path => { _ = ExportFrameSafe(path); });
    async Task ExportFrameSafe(string path) { try { await RenderFrame(path); Status("Exported clean 1920 × 1080 camera frame and shot metadata."); } catch(Exception ex) { Error(ex.Message); } }
    async Task RenderFrame(string path)
    {
        if (!path.EndsWith(".png",StringComparison.OrdinalIgnoreCase))path+=".png";
        var view = new SubViewport { Size = new Vector2I(1920,1080), World3D = _sceneViewport.World3D, RenderTargetUpdateMode = SubViewport.UpdateMode.Once, Msaa3D = Viewport.Msaa.Msaa4X };
        AddChild(view); var camera = new Camera3D { Current=true,CullMask=1,Near=.05f,Far=500,KeepAspect=Camera3D.KeepAspectEnum.Width,Fov=_shot.ShotCamera.Fov };view.AddChild(camera);camera.GlobalTransform=_shot.ShotCamera.GlobalTransform;
        try { for(int i=0;i<2;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);var image=view.GetTexture().GetImage();var error=image.SavePng(path);if(error!=Godot.Error.Ok)throw new IOException("The frame could not be saved: "+error); File.WriteAllText(Path.ChangeExtension(path,".json"),JsonSerializer.Serialize(new {application="Previs",version="0.1.0",frame=(int)Math.Round(_time*_fps),timeSeconds=_time,fps=_fps,width=1920,height=1080,camera=Snapshot()},_json)); }
        finally { view.QueueFree(); }
    }

    async Task RunSmokeChecks()
    {
        try
        {
            for (int i=0;i<5;i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(BvhChecks.Run() >= 14, "BVH parser checks");
            Require(_entries.Count >= 7, "Starter scene objects");
            var actor = (RiggedActor)_entries[0].Node; Require(actor.DurationSeconds > 0, "BVH sample loaded");
            actor.ApplyTime(.5, true, false, 1); var sk = Descendants(actor).OfType<Skeleton3D>().First(); int boneCount = sk.GetBoneCount(); var pose = sk.GetBonePoseRotation(Math.Min(5, boneCount-1)); actor.ApplyTime(2, true, false, 1); actor.ApplyTime(.5,true,false,1); Require(pose.IsEqualApprox(sk.GetBonePoseRotation(Math.Min(5,boneCount-1))), "Deterministic BVH scrub");
            var real = SamplePath("samples/real/CMU_02_01_Walk.bvh"); actor.LoadBvh(real,.055f); actor.ApplyTime(.7,false); Require(actor.JointCount>20&&actor.RootMotionOffset.Length()>.01f,"Real CMU import and root motion"); actor.LoadBvh(SamplePath("samples/synthetic-walk-gesture.bvh"),.01f);
            _shot.Evaluate(1); var cameraPose = _shot.GlobalTransform; _shot.Evaluate(5); _shot.Evaluate(1); Require(cameraPose.IsEqualApprox(_shot.GlobalTransform), "Deterministic camera scrub");
            TogglePilot();SetTime(1);var pilotStart=_editorCamera.GlobalTransform;SetTime(5);Require(!pilotStart.IsEqualApprox(_editorCamera.GlobalTransform)&&_editorCamera.GlobalTransform.IsEqualApprox(_shot.GlobalTransform),"Pilot playback follows camera keys");Require(_editorCamera.CullMask==1,"Pilot excludes editor helpers");TogglePilot();
            var before = Snapshot(); var extra = AddObject("box",new Vector3(2,0,2)); Select(extra); Require(_selected==extra,"Object selection"); Restore(before, ""); Require(_entries.Count==before.Objects.Count,"Scene restore");
            string output = ProjectSettings.GlobalizePath("res://output/smoke"); Directory.CreateDirectory(output); SaveTo(Path.Combine(output,"roundtrip.previs")); var data = JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(_projectPath))!; Restore(data,output); Require(_entries.Count==before.Objects.Count,"Project roundtrip");
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
        public override void _Draw(){if(App==null||!App._pilot)return;float w=Size.X,h=Size.Y,target=16f/9;if(w/h<target){float bar=(h-w/target)/2;DrawRect(new Rect2(0,0,w,bar),Colors.Black);DrawRect(new Rect2(0,h-bar,w,bar),Colors.Black);}else{float bar=(w-h*target)/2;DrawRect(new Rect2(0,0,bar,h),Colors.Black);DrawRect(new Rect2(w-bar,0,bar,h),Colors.Black);}DrawString(ThemeDB.FallbackFont,new Vector2(12,20),"CAMERA A · 16:9",fontSize:12,modulate:Colors.White);}
    }

    partial class TimelineView : Control
    {
        public PrevisApp App = null!;
        public override void _Draw()
        {
            if(App==null||App._shot==null)return; var font=ThemeDB.FallbackFont; float left=140,width=Math.Max(1,Size.X-left-12); double duration=App._duration;
            DrawString(font,new Vector2(8,27),"Camera A",fontSize:12,modulate:App._text); DrawString(font,new Vector2(8,60),"Motion clips",fontSize:12,modulate:App._text);
            for(int i=0;i<=8;i++){float x=left+width*i/8;DrawLine(new Vector2(x,13),new Vector2(x,90),App._line);DrawString(font,new Vector2(x,102),(duration*i/8).ToString("0.#",CultureInfo.InvariantCulture)+"s",fontSize:11,modulate:App._muted);}
            DrawRect(new Rect2(left,12,width,22),new Color("292431"));
            foreach(var key in App._shot.Keys){float x=left+width*(float)(key.TimeSeconds/duration);DrawColoredPolygon(new[]{new Vector2(x,15),new Vector2(x+6,23),new Vector2(x,31),new Vector2(x-6,23)},App._accent);}
            float clipY=46;foreach(var e in App._entries.Where(e=>e.Node is RiggedActor||e.Player!=null).Take(2)){double len=e.Node is RiggedActor a?a.DurationSeconds:duration;DrawRect(new Rect2(left,clipY,(float)Math.Min(width,width*len/duration),19),new Color("3d354e"));DrawString(font,new Vector2(left+7,clipY+14),e.Data.Name+" · "+(e.Data.MotionName??(e.Node is RiggedActor ra?ra.MotionName:e.Animation)),fontSize:11,modulate:App._text);clipY+=22;}
            float head=left+width*(float)(App._time/duration);DrawLine(new Vector2(head,0),new Vector2(head,91),new Color("e0d6ff"),2);
        }
        public override void _GuiInput(InputEvent input){if(input is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.Left} b&&b.Position.X>=140)App.SetTime((b.Position.X-140)/Math.Max(1,Size.X-152)*App._duration);}
    }
}
