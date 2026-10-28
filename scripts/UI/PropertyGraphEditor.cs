using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GoPrevis;

/// <summary>One in-app key clipboard shared by dope sheet and both graph modes.</summary>
public static class PropertyKeyClipboard
{
    public sealed record Key(string Channel, string Path, double Offset, ScalarKeyData Data);
    public static readonly List<Key> Items = new();
}

/// <summary>
/// Property value and signed-velocity graph. Keys and handles edit the same
/// serialized curves used by playback; the speed display is their derivative.
/// </summary>
public partial class PropertyGraphEditor : Control
{
    public List<AnimationChannelBinding> Channels { get; set; } = new();
    public double Duration { get; set; } = 8;
    public double Time { get; set; }
    public int Fps { get; set; } = 24;
    public bool SpeedMode { get; set; }
    public Action? BeginEdit;
    public Action? Changed;
    public Action<double>? Scrub;
    public Action? SelectionChanged;

    readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    readonly Dictionary<string, KeySnapshot> _original = new(StringComparer.Ordinal);
    readonly Dictionary<string, (double In, double Out, bool Last)> _originalSpeeds = new(StringComparer.Ordinal);
    string _selectionSignature = "";
    double _timeFrom, _timeTo = 8, _low = -1, _high = 1;
    Vector2 _down, _last;
    bool _gesture;
    enum DragMode { None, Keys, InHandle, OutHandle, Marquee, Pan, Scrub }
    DragMode _drag;
    string _activeIdentity = "", _activeKeyId = "";
    Rect2 _marquee;
    PopupMenu? _menu;
    bool _speedPreviously;
    double _lastTime = double.NaN;
    readonly Color _background = new("17181f"), _grid = new("30323c"),
        _muted = new("8f96a8"), _foreground = new("dfe3ed"), _highlight = new("f0c67c");
    static readonly string[] Presets = { "Linear", "Bezier", "Ease in/out", "Stop", "Hold", "Auto", "Flat" };
    static List<PropertyKeyClipboard.Key> Clipboard => PropertyKeyClipboard.Items;

    sealed record KeySnapshot(double Time, double Value, string Interpolation,
        double InSlope, double OutSlope, double InWeight, double OutWeight, bool BrokenTangents);
    sealed record KeyRef(AnimationChannelBinding Binding, ScalarAnimationCurve Curve, ScalarKeyData Key)
    {
        public string Identity => Binding.Identity + "/" + Key.Id;
    }

    public int SelectedKeyCount => SelectedKeys().Count();
    public double? SelectedTime => SelectedKeys().FirstOrDefault()?.Key.Time;
    public double? SelectedValue => SelectedKeys().FirstOrDefault()?.Key.Value;
    public double? SelectedSpeed => SelectedKeys().FirstOrDefault() is { } key ? KeySpeed(key) : null;
    internal Vector2 KeyScreenPoint(string channelIdentity, string keyId)
    {
        var key = AllKeys().FirstOrDefault(k => k.Binding.Identity == channelIdentity && k.Key.Id == keyId);
        return key == null ? new Vector2(float.NaN, float.NaN) : Point(key.Key.Time, SpeedMode ? KeySpeed(key) : key.Key.Value);
    }
    internal Vector2 TangentScreenPoint(string channelIdentity, string keyId, bool incoming)
    {
        var key = AllKeys().FirstOrDefault(k => k.Binding.Identity == channelIdentity && k.Key.Id == keyId);
        return key == null ? new Vector2(float.NaN, float.NaN) : HandlePoint(key, incoming);
    }
    Rect2 Plot => new(new Vector2(65, 34), new Vector2(Math.Max(30, Size.X - 82), Math.Max(30, Size.Y - 67)));

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(280, 160);
        TooltipText = "Value / speed graph. Drag keys or tangent handles. Double-click adds a key. Ctrl-click toggles selection. Drag empty space to select. Wheel zooms; Ctrl: values only; Shift: time only. Middle-drag pans. F fits. Delete removes selected keys.";
    }

    public override void _Process(double delta)
    {
        string signature = string.Join("|", Channels.Select(c => c.Identity));
        if (signature != _selectionSignature || _speedPreviously != SpeedMode)
        {
            _selectionSignature = signature;
            _speedPreviously = SpeedMode;
            _selected.RemoveWhere(id => !AllKeys().Any(k => k.Identity == id));
            FitCurves();
        }
        if (_lastTime != Time) { _lastTime = Time; QueueRedraw(); }
    }

    IEnumerable<KeyRef> AllKeys()
    {
        foreach (var channel in Channels)
        {
            var curve = channel.Curve();
            if (curve == null) continue;
            foreach (var key in curve.Keys)
                if (double.IsFinite(key.Time) && double.IsFinite(key.Value))
                    yield return new KeyRef(channel, curve, key);
        }
    }
    IEnumerable<KeyRef> SelectedKeys() => AllKeys().Where(k => _selected.Contains(k.Identity));
    KeyRef? ActiveKey() => AllKeys().FirstOrDefault(k => k.Binding.Identity == _activeIdentity && k.Key.Id == _activeKeyId);
    static ScalarKeyData[] Ordered(ScalarAnimationCurve curve) => curve.Keys.Where(k => double.IsFinite(k.Time) && double.IsFinite(k.Value)).OrderBy(k => k.Time).ToArray();
    static double IntervalBefore(KeyRef key)
    { var previous = Ordered(key.Curve).LastOrDefault(k => k.Time < key.Key.Time - 1e-8); return previous == null ? 0 : key.Key.Time - previous.Time; }
    static double IntervalAfter(KeyRef key)
    { var next = Ordered(key.Curve).FirstOrDefault(k => k.Time > key.Key.Time + 1e-8); return next == null ? 0 : next.Time - key.Key.Time; }
    static double KeySpeed(KeyRef key)
    {
        double after = IntervalAfter(key);
        return PropertyCurveRuntime.Velocity(key.Curve,
            after > 0 ? key.Key.Time + Math.Min(after * .0001, .00001) : key.Key.Time - .00001);
    }
    static double HandleSlope(KeyRef key, bool incoming)
    {
        if (incoming)
        {
            var previous = Ordered(key.Curve).LastOrDefault(k => k.Time < key.Key.Time - 1e-8);
            if (previous == null) return 0;
            if (previous.Interpolation == "Bezier") return key.Key.InSlope;
            return PropertyCurveRuntime.Velocity(key.Curve, key.Key.Time - Math.Min((key.Key.Time - previous.Time) * .0001, .00001));
        }
        double duration = IntervalAfter(key);
        if (duration <= 0) return 0;
        if (key.Key.Interpolation == "Bezier") return key.Key.OutSlope;
        return PropertyCurveRuntime.Velocity(key.Curve, key.Key.Time + Math.Min(duration * .0001, .00001));
    }
    Vector2 Point(double time, double value)
    {
        var p = Plot;
        return p.Position + new Vector2((float)((time - _timeFrom) / Math.Max(1e-9, _timeTo - _timeFrom)),
            (float)(1 - (value - _low) / Math.Max(1e-9, _high - _low))) * p.Size;
    }
    double Seconds(float x) => _timeFrom + (x - Plot.Position.X) / Plot.Size.X * (_timeTo - _timeFrom);
    double Value(float y) => _high - (y - Plot.Position.Y) / Plot.Size.Y * (_high - _low);
    double Snap(double time) => Math.Clamp(Math.Round(time * Math.Max(1, Fps)) / Math.Max(1, Fps), 0, Math.Max(0, Duration));
    static string Number(double value)
    {
        if (Math.Abs(value) is > 99999 or > 0 and < .001) return value.ToString("0.##e+0", CultureInfo.InvariantCulture);
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
    static double ClampValue(AnimationChannelBinding binding, double value)
    {
        if (!double.IsFinite(value)) return binding.Read();
        double next = double.IsFinite(binding.Minimum) && double.IsFinite(binding.Maximum) && binding.Maximum > binding.Minimum
            ? Math.Clamp(value, binding.Minimum, binding.Maximum) : value;
        return binding.Discrete ? Math.Round(next) : next;
    }

    public void RefreshView(bool fit = false)
    {
        if (fit) FitCurves();
        QueueRedraw();
    }
    public void FitCurves()
    {
        _timeFrom = 0;
        _timeTo = Math.Max(.1, Duration);
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach (var channel in Channels)
        {
            var curve = channel.Curve();
            for (int i = 0; i <= 192; i++)
            {
                double t = _timeTo * i / 192.0;
                double v = SpeedMode ? PropertyCurveRuntime.Velocity(curve, t)
                    : PropertyCurveRuntime.Evaluate(curve, t, channel.Read());
                if (!double.IsFinite(v)) continue;
                min = Math.Min(min, v); max = Math.Max(max, v);
            }
            if (!SpeedMode && curve != null)
                foreach (var key in curve.Keys) { min = Math.Min(min, key.Value); max = Math.Max(max, key.Value); }
        }
        if (!double.IsFinite(min) || !double.IsFinite(max)) { min = -1; max = 1; }
        if (SpeedMode) { min = Math.Min(0, min); max = Math.Max(0, max); }
        double span = max - min;
        if (span < .000001) span = Math.Max(1, Math.Abs(max) * .2);
        _low = min - span * .16;
        _high = max + span * .16;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        DrawRect(new Rect2(Vector2.Zero, Size), _background);
        var plot = Plot;
        DrawRect(plot, new Color("1d1f28"));
        for (int i = 0; i <= 6; i++)
        {
            float fraction = i / 6f;
            float x = plot.Position.X + plot.Size.X * fraction;
            float y = plot.Position.Y + plot.Size.Y * fraction;
            DrawLine(new Vector2(x, plot.Position.Y), new Vector2(x, plot.End.Y), _grid);
            DrawLine(new Vector2(plot.Position.X, y), new Vector2(plot.End.X, y), _grid);
            DrawString(font, new Vector2(x - 8, plot.End.Y + 17), Number(_timeFrom + (_timeTo - _timeFrom) * fraction) + "s", fontSize: 10, modulate: _muted);
            DrawString(font, new Vector2(3, y + 4), Number(_high - (_high - _low) * fraction), fontSize: 10, modulate: _muted);
        }
        if (_low <= 0 && _high >= 0)
            DrawLine(Point(_timeFrom, 0), Point(_timeTo, 0), new Color("596175"));
        DrawString(font, new Vector2(8, 18), SpeedMode ? "SPEED  ·  signed units / second" : "VALUE", fontSize: 11, modulate: _foreground);
        string legend = Channels.Count == 0 ? "Select parameters in the property tree" : string.Join("   ", Channels.Take(3).Select(c => c.TargetName + " · " + c.Label + (c.Unit == "" ? "" : " (" + c.Unit + ")")));
        int limit = Math.Max(10, (int)((Size.X - 220) / 6));
        if (legend.Length > limit) legend = legend[..Math.Max(0, limit - 1)] + "…";
        DrawString(font, new Vector2(205, 18), legend, fontSize: 10, modulate: _muted);
        foreach (var channel in Channels)
        {
            var curve = channel.Curve();
            Color color = channel.Color;
            bool hasKeys = curve != null && curve.Keys.Count > 0;
            if (!hasKeys) color.A = .35f;
            Vector2? previous = null;
            int samples = Math.Clamp((int)plot.Size.X, 96, 1400);
            for (int i = 0; i <= samples; i++)
            {
                double t = _timeFrom + (_timeTo - _timeFrom) * i / samples;
                double value = SpeedMode ? PropertyCurveRuntime.Velocity(curve, t)
                    : PropertyCurveRuntime.Evaluate(curve, t, channel.Read());
                var at = Point(t, value);
                if (double.IsFinite(value) && at.Y >= plot.Position.Y - 1 && at.Y <= plot.End.Y + 1)
                {
                    if (previous is { } from) DrawLine(from, at, color, 1.7f, true);
                    previous = at;
                }
                else previous = null;
            }
        }
        foreach (var key in AllKeys())
        {
            var at = Point(key.Key.Time, SpeedMode ? KeySpeed(key) : key.Key.Value);
            if (!plot.Grow(7).HasPoint(at)) continue;
            bool selected = _selected.Contains(key.Identity);
            DrawDiamond(at, selected ? _highlight : key.Binding.Color, selected ? 5 : 4);
            if (selected) DrawHandles(key);
        }
        float playhead = Point(Time, 0).X;
        if (playhead >= plot.Position.X && playhead <= plot.End.X)
        {
            DrawLine(new Vector2(playhead, plot.Position.Y - 6), new Vector2(playhead, plot.End.Y), new Color("d9c8ff"), 1.5f);
            DrawColoredPolygon(new[] { new Vector2(playhead - 4, plot.Position.Y - 7), new Vector2(playhead + 4, plot.Position.Y - 7), new Vector2(playhead, plot.Position.Y) }, new Color("d9c8ff"));
        }
        if (_drag == DragMode.Marquee)
        {
            DrawRect(_marquee, new Color(.5f, .4f, .9f, .12f));
            DrawRect(_marquee, new Color("ae91ef"), false);
        }
        string hint = SelectedKeyCount == 0 ? "Double-click to add a key · F: fit · Wheel: zoom · Middle-drag: pan"
            : SelectedKeyCount + " selected · Drag keys / handles · Delete · Right-click: interpolation";
        DrawString(font, new Vector2(8, Size.Y - 4), hint, fontSize: 10, modulate: _muted);
    }
    void DrawDiamond(Vector2 p, Color color, float radius)
        => DrawColoredPolygon(new[] { p + new Vector2(0, -radius), p + new Vector2(radius, 0), p + new Vector2(0, radius), p + new Vector2(-radius, 0) }, color);

    Vector2 HandlePoint(KeyRef key, bool incoming)
    {
        double interval = incoming ? IntervalBefore(key) : IntervalAfter(key);
        double weight = incoming ? key.Key.InWeight : key.Key.OutWeight;
        double slope = SpeedMode ? HandleSlope(key, incoming) : incoming ? key.Key.InSlope : key.Key.OutSlope;
        if (SpeedMode)
        {
            // These markers are editing handles beside the exact key time, not
            // samples of the velocity curve. A short leader shows their anchor.
            return Point(key.Key.Time, slope) + new Vector2(incoming ? -10 : 10, 0);
        }
        double distance = interval * Math.Clamp(weight, .001, .999);
        return Point(key.Key.Time + (incoming ? -distance : distance), key.Key.Value + (incoming ? -1 : 1) * slope * distance);
    }
    bool HasHandle(KeyRef key, bool incoming)
    {
        if (key.Binding.Discrete || key.Curve.Discrete) return false;
        if (incoming)
        {
            var previous = Ordered(key.Curve).LastOrDefault(k => k.Time < key.Key.Time - 1e-8);
            return previous != null && (previous.Interpolation == "Bezier" || SpeedMode);
        }
        return IntervalAfter(key) > 0 && (key.Key.Interpolation == "Bezier" || SpeedMode);
    }
    void DrawHandles(KeyRef key)
    {
        foreach (bool incoming in new[] { true, false })
        {
            if (!HasHandle(key, incoming)) continue;
            var point = HandlePoint(key, incoming);
            if (!Plot.Grow(8).HasPoint(point)) continue;
            var anchor = Point(key.Key.Time, SpeedMode ? HandleSlope(key, incoming) : key.Key.Value);
            DrawLine(anchor, point, new Color("a58a61"), 1, true);
            DrawCircle(point, 4, _highlight);
            DrawCircle(point, 2, _background);
        }
    }

    KeyRef? HitKey(Vector2 at) => AllKeys().OrderBy(k => at.DistanceSquaredTo(Point(k.Key.Time, SpeedMode ? KeySpeed(k) : k.Key.Value)))
        .FirstOrDefault(k => at.DistanceTo(Point(k.Key.Time, SpeedMode ? KeySpeed(k) : k.Key.Value)) < 9);
    (KeyRef Key, bool Incoming)? HitHandle(Vector2 at)
    {
        foreach (var key in SelectedKeys())
            foreach (bool incoming in new[] { true, false })
                if (HasHandle(key, incoming) && at.DistanceTo(HandlePoint(key, incoming)) < 8) return (key, incoming);
        return null;
    }
    void RememberKeys()
    {
        _original.Clear();
        _originalSpeeds.Clear();
        foreach (var key in AllKeys())
        {
            _original[key.Identity] = new(key.Key.Time, key.Key.Value, key.Key.Interpolation,
                key.Key.InSlope, key.Key.OutSlope, key.Key.InWeight, key.Key.OutWeight, key.Key.BrokenTangents);
            _originalSpeeds[key.Identity] = (HandleSlope(key, true), HandleSlope(key, false), IntervalAfter(key) <= 0);
        }
    }
    void StartMutation()
    {
        if (_gesture) return;
        BeginEdit?.Invoke(); MaterializeSelectedCurves(); _gesture = true;
    }
    void MaterializeSelectedCurves()
    {
        foreach (var channel in Channels)
            if (_selected.Any(id => id.StartsWith(channel.Identity + "/", StringComparison.Ordinal))) channel.EnsureCurve();
    }
    void NotifyChanged()
    { Changed?.Invoke(); SelectionChanged?.Invoke(); QueueRedraw(); }

    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.CtrlPressed && key.Keycode == Key.C) { CopySelectedKeys(); AcceptEvent(); }
            else if (key.CtrlPressed && key.Keycode == Key.V) { PasteKeys(); AcceptEvent(); }
            else if (key.CtrlPressed && key.Keycode == Key.D) { DuplicateSelectedKeys(); AcceptEvent(); }
            else if (key.Keycode == Key.Delete) { DeleteSelectedKeys(); AcceptEvent(); }
            else if (key.Keycode == Key.F) { FitCurves(); AcceptEvent(); }
            else if (key.Keycode == Key.Escape) { if (_gesture) MergeCollisions(); _drag = DragMode.None; _gesture = false; QueueRedraw(); AcceptEvent(); }
            return;
        }
        if (input is not InputEventMouseButton { Pressed: true } mouse) return;
        GrabFocus();
        if (mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            Zoom(mouse.Position, mouse.ButtonIndex == MouseButton.WheelUp ? .8 : 1.25, !mouse.CtrlPressed, !mouse.ShiftPressed);
            AcceptEvent(); return;
        }
        if (mouse.ButtonIndex == MouseButton.Middle)
        { _drag = DragMode.Pan; _down = _last = mouse.Position; AcceptEvent(); return; }
        if (mouse.ButtonIndex == MouseButton.Right)
        {
            var hit = HitKey(mouse.Position);
            if (hit != null && !_selected.Contains(hit.Identity)) { _selected.Clear(); _selected.Add(hit.Identity); SelectionChanged?.Invoke(); }
            ShowMenu(mouse.Position); QueueRedraw(); AcceptEvent(); return;
        }
        if (mouse.ButtonIndex != MouseButton.Left) return;
        _gesture = false; _down = _last = mouse.Position;
        if (mouse.Position.Y < Plot.Position.Y)
        { _drag = DragMode.Scrub; Scrub?.Invoke(Snap(Seconds(mouse.Position.X))); AcceptEvent(); return; }
        var handle = HitHandle(mouse.Position);
        if (handle is { } selectedHandle)
        {
            _activeIdentity = selectedHandle.Key.Binding.Identity; _activeKeyId = selectedHandle.Key.Key.Id;
            _drag = selectedHandle.Incoming ? DragMode.InHandle : DragMode.OutHandle;
            RememberKeys(); AcceptEvent(); return;
        }
        var picked = HitKey(mouse.Position);
        if (picked != null)
        {
            if (mouse.CtrlPressed)
            {
                if (!_selected.Add(picked.Identity)) _selected.Remove(picked.Identity);
            }
            else if (!_selected.Contains(picked.Identity) && !mouse.ShiftPressed) { _selected.Clear(); _selected.Add(picked.Identity); }
            else _selected.Add(picked.Identity);
            _activeIdentity = picked.Binding.Identity; _activeKeyId = picked.Key.Id;
            _drag = _selected.Contains(picked.Identity) ? DragMode.Keys : DragMode.None;
            RememberKeys(); SelectionChanged?.Invoke(); QueueRedraw(); AcceptEvent(); return;
        }
        if (mouse.DoubleClick && Plot.HasPoint(mouse.Position) && Channels.Count > 0)
        {
            var binding = Channels[0]; double time = Snap(Seconds(mouse.Position.X));
            double value = SpeedMode ? PropertyCurveRuntime.Evaluate(binding.Curve(), time, binding.Read()) : ClampValue(binding, Value(mouse.Position.Y));
            BeginEdit?.Invoke(); var curve = binding.EnsureCurve(); var created = PropertyCurveRuntime.Upsert(curve, time, value);
            if (SpeedMode && !binding.Discrete) { created.Interpolation = "Bezier"; created.InSlope = created.OutSlope = Value(mouse.Position.Y); }
            _selected.Clear(); _selected.Add(binding.Identity + "/" + created.Id); NotifyChanged(); AcceptEvent(); return;
        }
        if (!mouse.CtrlPressed && !mouse.ShiftPressed) _selected.Clear();
        _drag = DragMode.Marquee; _marquee = new Rect2(mouse.Position, Vector2.Zero);
        SelectionChanged?.Invoke(); QueueRedraw(); AcceptEvent();
    }

    public override void _Input(InputEvent input)
    {
        if (_drag == DragMode.None) return;
        if (input is InputEventMouseMotion motion)
        {
            UpdateDrag(GetGlobalTransformWithCanvas().AffineInverse() * motion.Position, motion.AltPressed);
            GetViewport().SetInputAsHandled();
        }
        else if (input is InputEventMouseButton { Pressed: false } button &&
                 (button.ButtonIndex == MouseButton.Left || button.ButtonIndex == MouseButton.Middle))
        {
            if (_drag == DragMode.Marquee)
                foreach (var key in AllKeys())
                    if (_marquee.HasPoint(Point(key.Key.Time, SpeedMode ? KeySpeed(key) : key.Key.Value))) _selected.Add(key.Identity);
            if (_gesture) MergeCollisions();
            _drag = DragMode.None; _gesture = false; SelectionChanged?.Invoke(); QueueRedraw();
            GetViewport().SetInputAsHandled();
        }
    }
    void UpdateDrag(Vector2 at, bool fine)
    {
        if (_drag == DragMode.Pan)
        {
            var change = at - _last;
            double panTime = -change.X / Plot.Size.X * (_timeTo - _timeFrom), panValue = change.Y / Plot.Size.Y * (_high - _low);
            _timeFrom += panTime; _timeTo += panTime; _low += panValue; _high += panValue; _last = at; QueueRedraw(); return;
        }
        if (_drag == DragMode.Scrub) { Scrub?.Invoke(Snap(Seconds(at.X))); QueueRedraw(); return; }
        if (_drag == DragMode.Marquee)
        { _marquee = new Rect2(new Vector2(Math.Min(_down.X, at.X), Math.Min(_down.Y, at.Y)), new Vector2(Math.Abs(at.X - _down.X), Math.Abs(at.Y - _down.Y))); QueueRedraw(); return; }
        if (!_gesture && at.DistanceTo(_down) < 2) return;
        StartMutation();
        var active = ActiveKey(); if (active == null) return;
        if (_drag is DragMode.InHandle or DragMode.OutHandle)
        {
            bool incoming = _drag == DragMode.InHandle;
            SetHandle(active, incoming, at, fine); NotifyChanged(); return;
        }
        var keys = SelectedKeys().ToArray();
        if (keys.Length == 0) return;
        double multiplier = fine ? .1 : 1;
        double dt = (Seconds(at.X) - Seconds(_down.X)) * multiplier;
        double min = keys.Min(k => _original[k.Identity].Time), max = keys.Max(k => _original[k.Identity].Time);
        dt = Math.Clamp(dt, -min, Math.Max(-min, Duration - max));
        dt = Math.Round(dt * Math.Max(1, Fps)) / Math.Max(1, Fps);
        double dv = (Value(at.Y) - Value(_down.Y)) * multiplier;
        foreach (var key in keys)
        {
            var original = _original[key.Identity]; key.Key.Time = Math.Clamp(original.Time + dt, 0, Duration);
            if (!SpeedMode) key.Key.Value = ClampValue(key.Binding, original.Value + dv);
            else if (!key.Binding.Discrete)
            {
                var speeds = _originalSpeeds[key.Identity];
                bool outgoing = !speeds.Last;
                double slope = (outgoing ? speeds.Out : speeds.In) + dv;
                SetSlope(key, !outgoing, slope);
            }
            key.Curve.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
        }
        NotifyChanged();
    }
    void SetSlope(KeyRef key, bool incoming, double slope)
    {
        if (!double.IsFinite(slope)) return;
        if (incoming)
        {
            key.Key.InSlope = slope;
            var previous = Ordered(key.Curve).LastOrDefault(k => k.Time < key.Key.Time - 1e-8);
            if (previous != null) previous.Interpolation = "Bezier";
        }
        else { key.Key.OutSlope = slope; key.Key.Interpolation = "Bezier"; }
        if (!key.Key.BrokenTangents)
        {
            key.Key.InSlope = key.Key.OutSlope = slope;
            if (IntervalAfter(key) > 0) key.Key.Interpolation = "Bezier";
            var previous = Ordered(key.Curve).LastOrDefault(k => k.Time < key.Key.Time - 1e-8);
            if (previous != null) previous.Interpolation = "Bezier";
        }
    }
    void SetHandle(KeyRef key, bool incoming, Vector2 at, bool fine)
    {
        double interval = incoming ? IntervalBefore(key) : IntervalAfter(key);
        if (interval <= 0) return;
        if (SpeedMode)
        {
            double handleSpeed = Value(at.Y);
            if (fine && _originalSpeeds.TryGetValue(key.Identity, out var original))
            { double initial = incoming ? original.In : original.Out; handleSpeed = initial + (handleSpeed - initial) * .1; }
            SetSlope(key, incoming, handleSpeed); return;
        }
        double distance = incoming ? key.Key.Time - Seconds(at.X) : Seconds(at.X) - key.Key.Time;
        var neighbor = incoming ? Ordered(key.Curve).LastOrDefault(k => k.Time < key.Key.Time - 1e-8)
            : Ordered(key.Curve).FirstOrDefault(k => k.Time > key.Key.Time + 1e-8);
        double other = neighbor == null ? 1.0 / 3 : incoming ? neighbor.OutWeight : neighbor.InWeight;
        distance = Math.Clamp(distance, interval * .005, interval * Math.Max(.005, .999 - Math.Clamp(other, 0, .99)));
        double slope = incoming ? (key.Key.Value - Value(at.Y)) / distance : (Value(at.Y) - key.Key.Value) / distance;
        if (incoming) key.Key.InWeight = distance / interval; else key.Key.OutWeight = distance / interval;
        SetSlope(key, incoming, slope);
    }
    void MergeCollisions()
    {
        foreach (var selected in SelectedKeys().ToArray())
            selected.Curve.Keys.RemoveAll(other => other.Id != selected.Key.Id && !_selected.Contains(selected.Binding.Identity + "/" + other.Id) && Math.Abs(other.Time - selected.Key.Time) < .000001);
        NotifyChanged();
    }
    void Zoom(Vector2 at, double factor, bool horizontal, bool vertical)
    {
        if (horizontal)
        {
            double pivot = Seconds(at.X), span = Math.Clamp((_timeTo - _timeFrom) * factor, 2.0 / Math.Max(1, Fps), Math.Max(10, Duration * 8));
            double fraction = (pivot - _timeFrom) / (_timeTo - _timeFrom);
            _timeFrom = pivot - span * fraction; _timeTo = _timeFrom + span;
        }
        if (vertical)
        {
            double pivot = Value(at.Y), span = Math.Clamp((_high - _low) * factor, .00001, 1e12);
            double fraction = (pivot - _low) / (_high - _low);
            _low = pivot - span * fraction; _high = _low + span;
        }
        QueueRedraw();
    }

    public bool DeleteSelectedKeys()
    {
        var keys = SelectedKeys().ToArray(); if (keys.Length == 0) return false;
        BeginEdit?.Invoke(); MaterializeSelectedCurves();
        foreach (var key in SelectedKeys().ToArray()) PropertyCurveRuntime.Remove(key.Curve, key.Key.Id);
        _selected.Clear(); NotifyChanged(); return true;
    }
    public void SetPreset(string preset)
    {
        var keys = SelectedKeys().ToArray(); if (keys.Length == 0) return;
        BeginEdit?.Invoke(); MaterializeSelectedCurves();
        foreach (var key in SelectedKeys().ToArray()) PropertyCurveRuntime.ApplyPreset(key.Curve, key.Key.Id, preset);
        NotifyChanged();
    }
    public void SetSelectedTime(double time)
    {
        var keys = SelectedKeys().ToArray(); if (keys.Length == 0 || !double.IsFinite(time)) return;
        double delta = Snap(time) - keys[0].Key.Time;
        delta = Math.Clamp(delta, -keys.Min(k => k.Key.Time), Math.Max(0, Duration - keys.Max(k => k.Key.Time)));
        BeginEdit?.Invoke(); MaterializeSelectedCurves(); foreach (var key in SelectedKeys().ToArray()) key.Key.Time += delta;
        MergeCollisions();
    }
    public void SetSelectedValue(double value)
    {
        var keys = SelectedKeys().ToArray(); if (keys.Length == 0 || !double.IsFinite(value)) return;
        BeginEdit?.Invoke(); MaterializeSelectedCurves(); foreach (var key in SelectedKeys().ToArray()) key.Key.Value = ClampValue(key.Binding, value); NotifyChanged();
    }
    public void SetSelectedSpeed(double value)
    {
        var keys = SelectedKeys().ToArray(); if (keys.Length == 0 || !double.IsFinite(value)) return;
        BeginEdit?.Invoke(); MaterializeSelectedCurves();
        foreach (var key in SelectedKeys().ToArray()) if (!key.Binding.Discrete) SetSlope(key, IntervalAfter(key) <= 0, value);
        NotifyChanged();
    }
    public void CopySelectedKeys()
    {
        var selected = SelectedKeys().ToArray(); if (selected.Length == 0) return;
        Clipboard.Clear();
        double first = selected.Min(k => k.Key.Time);
        foreach (var key in selected) Clipboard.Add(new(key.Binding.Identity, key.Binding.Path, key.Key.Time - first, PropertyCurveRuntime.Copy(key.Key)));
    }
    public void PasteKeys() => PasteClipboard(Time);
    public void DuplicateSelectedKeys()
    {
        var selected = SelectedKeys().ToArray(); if (selected.Length == 0) return;
        double offset = 1.0 / Math.Max(1, Fps);
        if (selected.Max(k => k.Key.Time) + offset > Duration + .000001) return;
        CopySelectedKeys(); PasteClipboard(selected.Min(k => k.Key.Time) + offset);
    }
    void PasteClipboard(double atTime)
    {
        if (Clipboard.Count == 0 || Channels.Count == 0) return;
        bool singleSource = Clipboard.Select(k => k.Channel).Distinct().Count() == 1;
        var destinations = Clipboard.Select(item => (Item: item,
            Target: Channels.FirstOrDefault(c => c.Identity == item.Channel)
                ?? (singleSource && Channels.Count == 1 ? Channels[0] : Channels.FirstOrDefault(c => c.Path == item.Path))))
            .Where(p => p.Target != null).ToArray();
        if (destinations.Length == 0) return;
        double origin = destinations.Min(p => p.Item.Data.Time), span = destinations.Max(p => p.Item.Data.Time) - origin;
        double start = Math.Clamp(Snap(atTime), 0, Math.Max(0, Duration - span));
        BeginEdit?.Invoke(); _selected.Clear();
        foreach (var pair in destinations)
        {
            var source = pair.Item.Data; var target = pair.Target!; var curve = target.EnsureCurve();
            double time = Snap(start + source.Time - origin);
            var pasted = PropertyCurveRuntime.Upsert(curve, time, ClampValue(target, source.Value));
            pasted.Interpolation = source.Interpolation; pasted.InSlope = source.InSlope; pasted.OutSlope = source.OutSlope;
            pasted.InWeight = source.InWeight; pasted.OutWeight = source.OutWeight; pasted.BrokenTangents = source.BrokenTangents;
            _selected.Add(target.Identity + "/" + pasted.Id);
        }
        NotifyChanged();
    }
    void ShowMenu(Vector2 point)
    {
        if (_menu == null)
        {
            _menu = new PopupMenu(); AddChild(_menu);
            _menu.IdPressed += id =>
            {
                if (id >= 0 && id < Presets.Length) SetPreset(Presets[id]);
                else if (id == 20) DeleteSelectedKeys();
                else if (id == 21) FitCurves();
                else if (id is 10 or 11)
                {
                    var keys = SelectedKeys().ToArray(); if (keys.Length == 0) return;
                    BeginEdit?.Invoke(); MaterializeSelectedCurves(); foreach (var key in SelectedKeys().ToArray())
                    {
                        key.Key.BrokenTangents = id == 10;
                        if (id == 11)
                        {
                            double slope = IntervalAfter(key) <= 0 ? key.Key.InSlope : IntervalBefore(key) <= 0
                                ? key.Key.OutSlope : (key.Key.InSlope + key.Key.OutSlope) * .5;
                            SetSlope(key, IntervalAfter(key) <= 0, slope);
                        }
                    }
                    NotifyChanged();
                }
            };
        }
        _menu.Clear(); bool empty = SelectedKeyCount == 0;
        for (int i = 0; i < Presets.Length; i++) { _menu.AddItem(Presets[i], i); _menu.SetItemDisabled(_menu.ItemCount - 1, empty); }
        _menu.AddSeparator(); _menu.AddItem("Break tangents", 10); _menu.AddItem("Unify tangents", 11);
        _menu.SetItemDisabled(_menu.ItemCount - 1, empty); _menu.SetItemDisabled(_menu.ItemCount - 2, empty);
        _menu.AddSeparator(); _menu.AddItem("Delete selected keys", 20); _menu.SetItemDisabled(_menu.ItemCount - 1, empty);
        _menu.AddItem("Fit curves", 21);
        _menu.Position = (Vector2I)(GetViewport().GuiEmbedSubwindows ? GetGlobalTransformWithCanvas() * point : GetScreenTransform() * point);
        _menu.Popup();
    }
}
