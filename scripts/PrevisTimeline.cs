using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Previs;

public partial class PrevisApp
{
    /// <summary>Compact object tracks with grouped XYZ transform channels.</summary>
    partial class TimelineView : Control
    {
        public PrevisApp App = null!;
        private const float Left = 180, Top = 24, RowHeight = 24;
        private const double KeyTolerance = .00001;
        private readonly HashSet<string> _expanded = new();
        private Entry? _dragEntry, _dragPathEntry, _dragKeyEntry;
        private CameraEntry? _dragKeyCamera;
        private MotionClipData? _dragClip;
        private int _dragPathIndex;
        private bool _trim, _scrubbing, _hasDragged, _dragKey;
        private double _start, _length, _pathKeyStart, _keyOriginalTime, _lastKeyTime;
        private float _dragX;
        private string _dragKeyChannel = "All";
        private string _selectedKeyTarget = "", _selectedKeyChannel = "";
        private double _selectedKeyTime = double.NaN;
        private float TrackWidth => Math.Max(1, Size.X - Left - 18);
        private float X(double seconds) => Left + TrackWidth * (float)(seconds / App._duration);
        private double Seconds(float x) => Math.Clamp((x - Left) / TrackWidth * App._duration, 0, App._duration);
        private double Snap(double seconds) => Math.Clamp(Math.Round(seconds * App._fps) / App._fps, 0, App._duration);

        private sealed record TrackRow(string Kind, string Label, Entry? Object = null,
            CameraEntry? Camera = null, string Channel = "All", bool Child = false)
        {
            public string Target => Camera != null ? "camera:" + Camera.Id : "object:" + Object!.Data.Id;
        }

        private List<TrackRow> Rows
        {
            get
            {
                var rows = new List<TrackRow>();
                foreach (CameraEntry camera in App._cameras)
                {
                    rows.Add(new TrackRow("Transform", camera.Name, Camera: camera));
                    if (_expanded.Contains("camera:" + camera.Id))
                        foreach (string channel in new[] { "Position", "Rotation", "Scale", "Lens" })
                            rows.Add(new TrackRow("Transform", channel == "Lens" ? "Focal length" : channel,
                                Camera: camera, Channel: channel, Child: true));
                }
                foreach (Entry entry in App._entries)
                {
                    bool motion = entry.Data.MotionClips.Count > 0 || entry.Node is RiggedActor || entry.Player != null;
                    if (motion) rows.Add(new TrackRow("Motion", entry.Data.Name + " · Motion", Object: entry));
                    if (entry.Data.Path.Points.Count > 0)
                        rows.Add(new TrackRow("Path", entry.Data.Name + " · Path", Object: entry));
                    rows.Add(new TrackRow("Transform", motion ? "Transform" : entry.Data.Name,
                        Object: entry, Child: false));
                    if (_expanded.Contains("object:" + entry.Data.Id))
                        foreach (string channel in new[] { "Position", "Rotation", "Scale" })
                            rows.Add(new TrackRow("Transform", channel, Object: entry, Channel: channel, Child: true));
                }
                return rows;
            }
        }

        public void UpdateMinimumHeight()
        {
            if (App == null) return;
            CustomMinimumSize = new Vector2(0, Top + Rows.Count * RowHeight + 14);
            QueueRedraw();
        }
        public void ExpandObject(string id)
        { _expanded.Add("object:"+id);UpdateMinimumHeight();ScrollGroupIntoView("object:"+id); }
        void ScrollGroupIntoView(string target)
        {
            Callable.From(()=>{if(GetParent() is not ScrollContainer scroll)return;var rows=Rows;int start=rows.FindIndex(r=>r.Target==target&&r.Kind=="Transform"&&!r.Child);if(start<0)return;int count=rows[start].Camera!=null?5:4;scroll.ScrollVertical=(int)Math.Max(0,Top+(start+count)*RowHeight-scroll.Size.Y);}).CallDeferred();
        }

        public override void _Ready()
        {
            TooltipText = "Expand Transform to see Position, Rotation and Scale. Drag diamonds to retime; right-click removes a key. Double-click an empty parameter lane to add a key.";
            UpdateMinimumHeight();
        }

        public override void _Input(InputEvent input)
        {
            if (_dragClip == null && _dragPathEntry == null && !_dragKey && !_scrubbing) return;
            if (input is InputEventMouseMotion)
            {
                UpdatePointer(GetLocalMousePosition().X);
                GetViewport().SetInputAsHandled();
            }
            else if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
            {
                EndDrag(); GetViewport().SetInputAsHandled();
            }
        }

        private void EndDrag()
        {
            _dragClip = null; _dragEntry = null; _dragPathEntry = null;
            _dragKeyEntry = null; _dragKeyCamera = null; _dragKey = false;
            _scrubbing = false; _hasDragged = false;
        }

        private bool BeginMutation(float x)
        {
            if (!_hasDragged && Math.Abs(x - _dragX) < 3) return false;
            if (!_hasDragged) { App.PushUndo(); _hasDragged = true; }
            return true;
        }

        private void UpdatePointer(float x)
        {
            if (_dragKey)
            {
                if (!BeginMutation(x)) return;
                double time = Snap(_keyOriginalTime + (x - _dragX) / TrackWidth * App._duration);
                if (Math.Abs(time - _lastKeyTime) < KeyTolerance) return;
                App.RetimeTransform(_dragKeyEntry, _dragKeyCamera, _dragKeyChannel, _lastKeyTime, time);
                _lastKeyTime = time; _selectedKeyTime = time;
                App.SetTime(time); QueueRedraw();
            }
            else if (_dragPathEntry != null)
            {
                if (!BeginMutation(x)) return;
                App.SetPathKeyTime(_dragPathEntry, _dragPathIndex,
                    _pathKeyStart + (x - _dragX) / TrackWidth * App._duration, false);
            }
            else if (_dragClip != null && _dragEntry != null)
            {
                if (!BeginMutation(x)) return;
                double change = (x - _dragX) / TrackWidth * App._duration;
                if (_trim)
                    _dragClip.Duration = Math.Clamp(Snap(_length + change), 1.0 / App._fps,
                        Math.Max(1.0 / App._fps, App._duration - _dragClip.Start));
                else
                {
                    _dragClip.Start = Math.Clamp(Snap(_start + change), 0, Math.Max(0, App._duration - _dragClip.Duration));
                    var previous = _dragEntry.Data.MotionClips.Where(c => c != _dragClip && c.Start <= _dragClip.Start)
                        .OrderBy(c => c.Start).LastOrDefault();
                    _dragClip.BlendIn = previous == null ? 0 : Math.Max(0,
                        Math.Min(_dragClip.Duration, previous.Start + previous.Duration - _dragClip.Start));
                }
                try { App.RebuildTrack(_dragEntry); App.SetTime(App._time); App.RefreshSequenceInspector(); }
                catch (Exception ex) { App.Status(ex.Message); }
            }
            else if (_scrubbing) App.SetTime(Snap(Seconds(x)));
        }

        private TransformAnimationData Animation(TrackRow row) => row.Camera?.Animation ?? row.Object!.Data.TransformAnimation;
        private double[] KeyTimes(TrackRow row)
        {
            TransformAnimationData animation = Animation(row);
            IEnumerable<double> times = row.Channel switch
            {
                "Position" => animation.Position.Select(k => k.Time),
                "Rotation" => animation.Rotation.Select(k => k.Time),
                "Scale" => animation.Scale.Select(k => k.Time),
                "Lens" => row.Camera?.LensKeys.Select(k => k.Time) ?? Array.Empty<double>(),
                _ => animation.Position.Concat(animation.Rotation).Concat(animation.Scale).Select(k => k.Time)
                    .Concat(row.Camera?.LensKeys.Select(k => k.Time) ?? Array.Empty<double>())
            };
            return times.Where(t => double.IsFinite(t) && t >= 0 && t <= App._duration)
                .Distinct().OrderBy(t => t).ToArray();
        }

        private bool Selected(TrackRow row) => row.Object != null
            ? App._selected == row.Object
            : row.Camera?.Rig == App._shot && App._selected == null;

        private static string Fit(string text, float pixels, float average = 6.2f)
        {
            int chars = Math.Max(0, (int)(pixels / average));
            return text.Length <= chars ? text : chars > 2 ? text[..(chars - 1)] + "…" : "";
        }

        private void Diamond(float x, float y, Color color, float radius = 4.5f)
            => DrawColoredPolygon(new[] { new Vector2(x, y - radius - 1), new Vector2(x + radius, y),
                new Vector2(x, y + radius + 1), new Vector2(x - radius, y) }, color);

        public override void _Draw()
        {
            if (App == null || App._shot == null) return;
            var rows = Rows; var font = ThemeDB.FallbackFont;
            float end = Top + rows.Count * RowHeight;
            int ticks = Size.X < 900 ? 4 : 8;
            for (int i = 0; i <= ticks; i++)
            {
                float x = Left + TrackWidth * i / ticks;
                DrawLine(new Vector2(x, Top - 6), new Vector2(x, end), App._line);
                string label = (App._duration * i / ticks).ToString("0.##", CultureInfo.InvariantCulture) + " s";
                DrawString(font, new Vector2(Math.Min(x, Size.X - 34), 15), label, fontSize: 11, modulate: App._muted);
            }
            for (int index = 0; index < rows.Count; index++)
            {
                TrackRow row = rows[index]; float y = Top + index * RowHeight;
                bool selected = Selected(row);
                Color text = row.Kind == "Path" ? new Color("9ed8df") : row.Child ? App._muted : App._text;
                DrawRect(new Rect2(0, y, Left - 3, RowHeight - 1), selected ? new Color("30293c") : new Color("1b1b20"));
                float labelX = row.Child ? 29 : row.Kind == "Transform" ? 24 : 8;
                if (row.Kind == "Transform" && !row.Child)
                {
                    bool expanded = _expanded.Contains(row.Target);
                    Vector2[] arrow = expanded
                        ? new[] { new Vector2(8, y + 8), new Vector2(16, y + 8), new Vector2(12, y + 13) }
                        : new[] { new Vector2(9, y + 6), new Vector2(9, y + 14), new Vector2(15, y + 10) };
                    DrawColoredPolygon(arrow, App._muted);
                }
                DrawString(font, new Vector2(labelX, y + 16), Fit(row.Label, Left - labelX - 7), fontSize: 11, modulate: text);
                if (row.Kind == "Transform") DrawTransformRow(row, y, selected);
                else if (row.Kind == "Path") DrawPathRow(row.Object!, y, selected);
                else DrawMotionRow(row.Object!, y, selected);
            }
            float head = X(App._time);
            DrawLine(new Vector2(head, Top - 5), new Vector2(head, end), new Color("e8dcff"), 1.5f);
            DrawColoredPolygon(new[] { new Vector2(head - 4, Top - 6), new Vector2(head + 4, Top - 6), new Vector2(head, Top) }, new Color("e8dcff"));
        }

        private void DrawTransformRow(TrackRow row, float y, bool selected)
        {
            DrawRect(new Rect2(Left, y + 2, TrackWidth, RowHeight - 5), row.Child ? new Color("202027") : new Color("28232f"));
            double[] times = KeyTimes(row);
            foreach (double time in times)
            {
                bool chosen = _selectedKeyTarget == row.Target && Math.Abs(_selectedKeyTime - time) < KeyTolerance &&
                    (_selectedKeyChannel == row.Channel || row.Channel == "All");
                Diamond(X(time), y + 11, chosen ? new Color("f0e6ff") : selected ? App._accent : new Color("897c9b"));
            }
            if (times.Length == 0 && row.Child)
                DrawString(ThemeDB.FallbackFont, new Vector2(Left + 7, y + 16), "Double-click to add key", fontSize: 11, modulate: new Color("77727e"));
        }

        private void DrawMotionRow(Entry actor, float y, bool selected)
        {
            var font = ThemeDB.FallbackFont;
            var clips = actor.Data.MotionClips.OrderBy(c => c.Start).ToList();
            if (clips.Count == 0 && actor.Player != null)
            {
                DrawRect(new Rect2(Left, y + 2, TrackWidth, 19), new Color("3c3548"));
                DrawString(font, new Vector2(Left + 7, y + 16), Fit(actor.Animation, TrackWidth - 14), fontSize: 11, modulate: App._text); return;
            }
            foreach (MotionClipData clip in clips.Where(c => c.Start < App._duration))
            {
                float start = X(clip.Start), finish = X(Math.Min(App._duration, clip.Start + clip.Duration));
                bool chosen = selected && clip.Id == App._selectedClipId;
                var rect = new Rect2(start, y + 2, Math.Max(2, finish - start), 19);
                DrawRect(rect, chosen ? new Color("67507f") : new Color("43364f"));
                if (chosen) DrawRect(rect, App._accent, false, 1);
                var previous = clips.Where(c => c != clip && c.Start <= clip.Start).LastOrDefault();
                double overlap = previous == null ? 0 : Math.Min(clip.BlendIn, previous.Start + previous.Duration - clip.Start);
                if (overlap > 0)
                {
                    float blendEnd = Math.Min(finish, X(clip.Start + overlap));
                    DrawRect(new Rect2(start, y + 2, blendEnd - start, 19), new Color("8a72a6"));
                    DrawLine(new Vector2(start, y + 20), new Vector2(blendEnd, y + 3), new Color("c6afe5"));
                }
                DrawLine(new Vector2(finish - 3, y + 5), new Vector2(finish - 3, y + 18), new Color("bdaec8"), 2);
                DrawString(font, new Vector2(start + 7, y + 16), Fit(clip.Name, rect.Size.X - 18), fontSize: 11, modulate: App._text);
            }
        }

        private void DrawPathRow(Entry actor, float y, bool selected)
        {
            double[] times = ActorPathRuntime.PointTimes(actor.Data.Path);
            if (times.Length > 0)
            {
                float begin = X(Math.Clamp(actor.Data.Path.Start + times[0], 0, App._duration));
                float finish = X(Math.Clamp(actor.Data.Path.Start + times[^1], 0, App._duration));
                DrawRect(new Rect2(begin, y + 9, Math.Max(2, finish - begin), 4), new Color("3b6570"));
            }
            for (int i = 0; i < times.Length; i++)
            {
                double absoluteTime = actor.Data.Path.Start + times[i];
                if (absoluteTime < 0 || absoluteTime > App._duration) continue;
                float x = X(absoluteTime);
                Color color = actor.Data.Path.Enabled ? selected && i == App._selectedPathPoint
                    ? new Color("c2f5fc") : new Color("78c4d1") : new Color("62757a");
                Diamond(x, y + 11, color);
                if (times.Length < 12) DrawString(ThemeDB.FallbackFont, new Vector2(Math.Min(x + 7, Size.X - 24), y + 16), "P" + (i + 1), fontSize: 11, modulate: App._text);
            }
        }

        public override void _GuiInput(InputEvent input)
        {
            if (input is InputEventMouseButton button && button.ButtonIndex is MouseButton.Left or MouseButton.Right)
            {
                if (!button.Pressed) { if (button.ButtonIndex == MouseButton.Left) EndDrag(); return; }
                int index = (int)Math.Floor((button.Position.Y - Top) / RowHeight);
                var rows = Rows;
                if (index < 0 || index >= rows.Count)
                {
                    if (button.ButtonIndex == MouseButton.Left && button.Position.X >= Left) { _scrubbing = true; App.SetTime(Snap(Seconds(button.Position.X))); AcceptEvent(); }
                    return;
                }
                TrackRow row = rows[index];
                if (row.Kind == "Transform") { TransformInput(row, button); return; }
                if (button.ButtonIndex != MouseButton.Left) return;
                Entry actor = row.Object!;
                if (row.Kind == "Path")
                {
                    double[] times = ActorPathRuntime.PointTimes(actor.Data.Path); int point = -1; float nearest = 9;
                    for (int i = 0; i < times.Length; i++) { float d = Math.Abs(button.Position.X - X(actor.Data.Path.Start + times[i])); if (d < nearest) { nearest = d; point = i; } }
                    if (point >= 0 && button.Position.X >= Left - 8)
                    {
                        App.SelectPathKey(actor, point); _dragPathEntry = actor; _dragPathIndex = point; _dragX = button.Position.X;
                        _pathKeyStart = actor.Data.Path.Start + times[point]; _hasDragged = false; AcceptEvent(); return;
                    }
                    App.Select(actor); App.ShowInspectorSection("Path");
                    if (button.Position.X >= Left) { _scrubbing = true; App.SetTime(Snap(Seconds(button.Position.X))); }
                    AcceptEvent(); return;
                }
                if (button.Position.X < Left) { App.Select(actor); AcceptEvent(); return; }
                var clip = actor.Data.MotionClips.OrderByDescending(c => c.Start)
                    .FirstOrDefault(c => button.Position.X >= X(c.Start) && button.Position.X <= X(c.Start + c.Duration));
                if (clip != null)
                {
                    App._playing = false; App._play.Text = "▶ Play"; App.SelectTimelineClip(actor, clip);
                    _hasDragged = false; _dragEntry = actor; _dragClip = clip; _dragX = button.Position.X;
                    _start = clip.Start; _length = clip.Duration; _trim = X(clip.Start + clip.Duration) - button.Position.X < 8;
                    AcceptEvent(); return;
                }
                _scrubbing = true; App.SetTime(Snap(Seconds(button.Position.X))); AcceptEvent();
            }
            else if (input is InputEventMouseMotion motion && (_dragClip != null || _dragPathEntry != null || _dragKey || _scrubbing))
            { UpdatePointer(motion.Position.X); AcceptEvent(); }
        }

        private void TransformInput(TrackRow row, InputEventMouseButton button)
        {
            if (button.Position.X < Left)
            {
                if (button.ButtonIndex == MouseButton.Left)
                {
                    if (!row.Child && button.Position.X < 25)
                    { if (!_expanded.Add(row.Target)) _expanded.Remove(row.Target); UpdateMinimumHeight();if(_expanded.Contains(row.Target))ScrollGroupIntoView(row.Target); }
                    if (row.Camera != null) App.SelectCamera(row.Camera); else App.Select(row.Object);
                    AcceptEvent();
                }
                return;
            }
            double[] times = KeyTimes(row);
            double nearest = double.NaN; float distance = 8;
            foreach (double time in times) { float d = Math.Abs(button.Position.X - X(time)); if (d < distance) { distance = d; nearest = time; } }
            if (double.IsFinite(nearest))
            {
                if (button.ButtonIndex == MouseButton.Right)
                { App.RemoveTransformKey(row.Object, row.Camera, row.Channel, nearest); QueueRedraw(); AcceptEvent(); return; }
                App._playing = false; App._play.Text = "▶ Play";
                App.SelectTransformTimeline(row.Object, row.Camera, row.Channel, nearest);
                _selectedKeyTarget = row.Target; _selectedKeyChannel = row.Channel; _selectedKeyTime = nearest;
                _dragKey = true; _dragKeyEntry = row.Object; _dragKeyCamera = row.Camera; _dragKeyChannel = row.Channel;
                _dragX = button.Position.X; _keyOriginalTime = _lastKeyTime = nearest; _hasDragged = false; AcceptEvent(); return;
            }
            if (button.ButtonIndex == MouseButton.Right) return;
            if (row.Camera != null) App.SelectCamera(row.Camera); else App.Select(row.Object);
            App.SetTime(Snap(Seconds(button.Position.X)));
            if (button.DoubleClick)
            {
                App.CaptureSelectedTransformKey(row.Channel);
                _selectedKeyTarget = row.Target; _selectedKeyChannel = row.Channel; _selectedKeyTime = App._time;
            }
            else _scrubbing = true;
            AcceptEvent(); QueueRedraw();
        }
    }
}
