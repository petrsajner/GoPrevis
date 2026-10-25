using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GenPrevis;

public partial class GenPrevisApp
{
    /// <summary>Compact object tracks with grouped XYZ transform channels.</summary>
    partial class TimelineView : Control
    {
        public GenPrevisApp App = null!;
        private const float Left = 180, Top = 24, RowHeight = 24;
        private const double KeyTolerance = .00001;
        private readonly HashSet<string> _expanded = new();
        private Entry? _dragEntry, _dragPathEntry, _dragKeyEntry;
        private CameraEntry? _dragKeyCamera;
        private MotionClipData? _dragClip;
        private readonly Dictionary<string,double> _clipBlendSnapshot=new();
        private int _dragPathIndex;
        private enum ClipDrag { Move, In, Out }
        private ClipDrag _clipDrag;
        private bool _scrubbing, _hasDragged, _dragKey, _dragPoseKey;
        private double _start, _length, _offset, _pathKeyStart, _keyOriginalTime, _lastKeyTime;
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
            public string Target => Kind=="Pose"?"pose:"+Object!.Data.Id:Camera != null ? "camera:" + Camera.Id : "object:" + Object!.Data.Id;
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
                    bool motion = entry.Data.MotionClips.Count > 0 || entry.NativeTrack == null && entry.Player != null;
                    if (motion) rows.Add(new TrackRow("Motion", entry.Data.Name + " · Motion", Object: entry));
                    if (entry.Data.Path.Points.Count > 0)
                        rows.Add(new TrackRow("Path", entry.Data.Name + " · Path", Object: entry));
                    rows.Add(new TrackRow("Transform", motion ? "Transform" : entry.Data.Name,
                        Object: entry, Child: false));
                    if (_expanded.Contains("object:" + entry.Data.Id))
                        foreach (string channel in new[] { "Position", "Rotation", "Scale" })
                            rows.Add(new TrackRow("Transform", channel, Object: entry, Channel: channel, Child: true));
                    if(entry.Node is RiggedActor||entry.NativeTrack!=null)
                    {
                        rows.Add(new TrackRow("Pose","Pose corrections",Object:entry));
                        if(_expanded.Contains("pose:"+entry.Data.Id))
                        {
                            var data=entry.Data.PoseCorrections;
                            if(data.Root.Count>0)rows.Add(new TrackRow("Pose","Pelvis offset",Object:entry,Channel:"Root",Child:true));
                            foreach(string bone in (entry.Node is RiggedActor human?human.PoseBoneNames:entry.NativeTrack!.PoseBoneNames).Concat(data.Bones.Keys).Distinct())
                                if(data.Bones.TryGetValue(bone,out var keys)&&keys.Count>0)
                                    rows.Add(new TrackRow("Pose",PoseBoneLabel(bone),Object:entry,Channel:bone,Child:true));
                        }
                    }
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
        public void ExpandPoseCorrections(string id)
        { _expanded.Add("pose:"+id);UpdateMinimumHeight();ScrollGroupIntoView("pose:"+id); }
        internal Rect2 PoseCorrectionRowRect(string entryId,string channel="All")
        {
            int row=Rows.FindIndex(r=>r.Kind=="Pose"&&r.Object?.Data.Id==entryId&&r.Channel==channel);
            return row<0?new Rect2():new Rect2(Left,Top+row*RowHeight+2,TrackWidth,19);
        }
        void ScrollGroupIntoView(string target)
        {
            Callable.From(()=>{if(GetParent() is not ScrollContainer scroll)return;var rows=Rows;int start=rows.FindIndex(r=>r.Target==target&&(r.Kind=="Transform"||r.Kind=="Pose")&&!r.Child);if(start<0)return;int count=rows[start].Kind=="Pose"?rows.Skip(start).TakeWhile(r=>r.Target==target).Count():rows[start].Camera!=null?5:4;scroll.ScrollVertical=(int)Math.Max(0,Top+(start+count)*RowHeight-scroll.Size.Y);}).CallDeferred();
        }

        public override void _Ready()
        {
            FocusMode=FocusModeEnum.All;
            TooltipText = "Drag empty track space to box-select keys. Ctrl/Shift adds to the selection; Delete removes selected keys. Drag the ruler to scrub. Clip edges trim IN/OUT.";
            UpdateMinimumHeight();
        }

        public override void _Input(InputEvent input)
        {
            if(_marquee&&input is InputEventMouseMotion){UpdateMarquee(GetLocalMousePosition());GetViewport().SetInputAsHandled();return;}
            if (_dragClip == null && _dragPathEntry == null && !_dragKey && !_scrubbing&&!_marquee) return;
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
            _marquee=false;QueueRedraw();
            _dragClip = null; _dragEntry = null; _dragPathEntry = null;
            _dragKeyEntry = null; _dragKeyCamera = null; _dragKey = false; _dragPoseKey=false;
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
                if(_dragPoseKey&&_dragKeyEntry!=null)App.RetimePoseCorrection(_dragKeyEntry,_dragKeyChannel,_lastKeyTime,time);
                else App.RetimeTransform(_dragKeyEntry, _dragKeyCamera, _dragKeyChannel, _lastKeyTime, time);
                _lastKeyTime = time; _selectedKeyTime = time;
                SelectTimelineKey(new TrackRow(_dragPoseKey?"Pose":"Transform","",_dragKeyEntry,_dragKeyCamera,_dragKeyChannel),time,false);
                App.SelectKeyInterpolation(_dragPoseKey?"Pose":"Transform",_dragKeyEntry,_dragKeyCamera,_dragKeyChannel,time);
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
                _dragClip.IsPlaceholder=false;double change = (x - _dragX) / TrackWidth * App._duration;
                if (_clipDrag==ClipDrag.In)
                {
                    double minimum=Math.Min(1.0/App._fps,_length),outPoint=_start+_length;
                    double low=Math.Max(0,_start-_offset/Math.Max(.000001,_dragClip.Speed));
                    double start=Math.Clamp(Snap(_start+change),low,Math.Max(low,outPoint-minimum));
                    _dragClip.Start=start;_dragClip.Duration=outPoint-start;
                    _dragClip.SourceOffset=Math.Max(0,_offset+(start-_start)*_dragClip.Speed);
                    UpdateClipBlend(_dragEntry,_dragClip,true);
                }
                else if (_clipDrag==ClipDrag.Out)
                {
                    _dragClip.Duration = Math.Clamp(Snap(_length + change), 1.0 / App._fps,
                        Math.Max(1.0 / App._fps, App._duration - _dragClip.Start));
                    UpdateClipBlend(_dragEntry,_dragClip,true);
                }
                else
                {
                    _dragClip.Start = Math.Clamp(Snap(_start + change), 0, Math.Max(0, App._duration - _dragClip.Duration));
                    UpdateClipBlend(_dragEntry,_dragClip);
                }
                try { App.RebuildTrack(_dragEntry); App.SetTime(App._time); App.RefreshSequenceInspector(); }
                catch (Exception ex) { App.Status(ex.Message); }
            }
            else if (_scrubbing) App.SetTime(Snap(Seconds(x)));
        }

        private void UpdateClipBlend(Entry entry,MotionClipData clip,bool retainFade=false)
        {
            var previous=entry.Data.MotionClips.Where(c=>c!=clip&&c.Start<=clip.Start).OrderBy(c=>c.Start).LastOrDefault();
            double overlap=previous==null?0:Math.Max(0,Math.Min(clip.Duration,previous.Start+previous.Duration-clip.Start));
            clip.BlendIn=retainFade?Math.Min(_clipBlendSnapshot.GetValueOrDefault(clip.Id,clip.BlendIn),overlap):overlap;
            // Trimming the outgoing end also changes the following overlap.
            var next=entry.Data.MotionClips.Where(c=>c!=clip&&c.Start>=clip.Start).OrderBy(c=>c.Start).FirstOrDefault();
            if(next!=null)next.BlendIn=Math.Max(0,Math.Min(_clipBlendSnapshot.GetValueOrDefault(next.Id,next.BlendIn),Math.Min(next.Duration,clip.Start+clip.Duration-next.Start)));
        }

        internal Rect2 MotionClipRect(string entryId,string clipId)
        {
            var rows=Rows;int row=rows.FindIndex(r=>r.Kind=="Motion"&&r.Object?.Data.Id==entryId);
            var clip=rows.ElementAtOrDefault(row)?.Object?.Data.MotionClips.FirstOrDefault(c=>c.Id==clipId);
            return clip==null?new Rect2():new Rect2(X(clip.Start),Top+row*RowHeight+2,Math.Max(2,X(clip.Start+clip.Duration)-X(clip.Start)),19);
        }

        private MotionClipData? SelectedClipEdge(Entry entry,float x)
        {
            if(App._selected!=entry)return null;
            var selected=entry.Data.MotionClips.FirstOrDefault(c=>c.Id==App._selectedClipId);
            return selected!=null&&x>=X(selected.Start)&&x<=X(selected.Start+selected.Duration)&&Math.Min(x-X(selected.Start),X(selected.Start+selected.Duration)-x)<8?selected:null;
        }

        public override string _GetTooltip(Vector2 atPosition)
        {
            int index=(int)Math.Floor((atPosition.Y-Top)/RowHeight);var rows=Rows;
            if(index<0||index>=rows.Count)return TooltipText;
            if(rows[index].Kind=="Pose")return "Pose corrections are added above motion. Expand to see keyed joints. Drag a diamond to retime it; right-click removes it. Double-click to add a key.";
            if(rows[index].Kind!="Motion")return TooltipText;
            var entry=rows[index].Object!;var clip=SelectedClipEdge(entry,atPosition.X)??entry.Data.MotionClips.OrderByDescending(c=>c.Start).FirstOrDefault(c=>atPosition.X>=X(c.Start)&&atPosition.X<=X(c.Start+c.Duration));
            if(clip==null)return TooltipText;
            float left=atPosition.X-X(clip.Start),right=X(clip.Start+clip.Duration)-atPosition.X;
            string action=left<8&&left<=right?"Trim IN · drag to choose a later or earlier source start":right<8?"Trim OUT · drag to change the clip end":"Drag to move this clip";
            return $"{clip.Name}\n{action}\nTimeline {clip.Start:0.###}–{clip.Start+clip.Duration:0.###} s\nSource IN {clip.SourceOffset:0.###} s · OUT {clip.SourceOffset+clip.Duration*clip.Speed:0.###} s";
        }

        private TransformAnimationData Animation(TrackRow row) => row.Camera?.Animation ?? row.Object!.Data.TransformAnimation;
        private double[] KeyTimes(TrackRow row)
        {
            if(row.Kind=="Pose")return App.PoseCorrectionTimes(row.Object!,row.Channel).Where(t=>double.IsFinite(t)&&t>=0&&t<=App._duration).Distinct().OrderBy(t=>t).ToArray();
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
                Color text = row.Kind == "Pose" ? new Color("dec99b") : row.Kind == "Path" ? new Color("9ed8df") : row.Child ? App._muted : App._text;
                DrawRect(new Rect2(0, y, Left - 3, RowHeight - 1), selected ? new Color("30293c") : new Color("1b1b20"));
                float labelX = row.Child ? 29 : row.Kind is "Transform" or "Pose" ? 24 : 8;
                if ((row.Kind is "Transform" or "Pose") && !row.Child)
                {
                    bool expanded = _expanded.Contains(row.Target);
                    Vector2[] arrow = expanded
                        ? new[] { new Vector2(8, y + 8), new Vector2(16, y + 8), new Vector2(12, y + 13) }
                        : new[] { new Vector2(9, y + 6), new Vector2(9, y + 14), new Vector2(15, y + 10) };
                    DrawColoredPolygon(arrow, App._muted);
                }
                DrawString(font, new Vector2(labelX, y + 16), Fit(row.Label, Left - labelX - 7), fontSize: 11, modulate: text);
                if(row.Kind=="Pose")DrawPoseCorrectionRow(row,y,selected);
                else if (row.Kind == "Transform") DrawTransformRow(row, y, selected);
                else if (row.Kind == "Path") DrawPathRow(row.Object!, y, selected);
                else DrawMotionRow(row.Object!, y, selected);
            }
            float head = X(App._time);
            DrawLine(new Vector2(head, Top - 5), new Vector2(head, end), new Color("e8dcff"), 1.5f);
            DrawColoredPolygon(new[] { new Vector2(head - 4, Top - 6), new Vector2(head + 4, Top - 6), new Vector2(head, Top) }, new Color("e8dcff"));
            DrawMarquee();
        }

        private void DrawTransformRow(TrackRow row, float y, bool selected)
        {
            DrawRect(new Rect2(Left, y + 2, TrackWidth, RowHeight - 5), row.Child ? new Color("202027") : new Color("28232f"));
            double[] times = KeyTimes(row);
            foreach (double time in times)
            {
                bool chosen = IsTimelineKeySelected(row,time);
                Diamond(X(time), y + 11, chosen ? new Color("f0e6ff") : selected ? App._accent : new Color("897c9b"));
            }
            if (times.Length == 0 && row.Child)
                DrawString(ThemeDB.FallbackFont, new Vector2(Left + 7, y + 16), "Double-click to add key", fontSize: 11, modulate: new Color("77727e"));
        }

        private void DrawPoseCorrectionRow(TrackRow row,float y,bool selected)
        {
            DrawRect(new Rect2(Left,y+2,TrackWidth,RowHeight-5),row.Child?new Color("28251f"):new Color("302a21"));
            var times=KeyTimes(row);
            foreach(double time in times)
            {
                bool chosen=IsTimelineKeySelected(row,time);
                Diamond(X(time),y+11,chosen?new Color("fff0cd"):selected?new Color("e7c787"):new Color("b49b70"));
            }
            if(times.Length==0&&!row.Child)DrawString(ThemeDB.FallbackFont,new Vector2(Left+7,y+16),"Double-click to key the selected joint",fontSize:11,modulate:new Color("9d8f76"));
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
                DrawLine(new Vector2(start + 3, y + 5), new Vector2(start + 3, y + 18), new Color("d3c2df"), 2);
                DrawLine(new Vector2(finish - 3, y + 5), new Vector2(finish - 3, y + 18), new Color("d3c2df"), 2);
                DrawString(font, new Vector2(start + 9, y + 16), Fit(clip.Name+(clip.SourceOffset>1.0/App._fps?$" · IN {clip.SourceOffset:0.##} s":""), rect.Size.X - 24), fontSize: 11, modulate: App._text);
            }
            // Selected trim handles remain visible and reachable when an
            // overlapping incoming clip is drawn above the outgoing clip.
            var selectedClip=selected?clips.FirstOrDefault(c=>c.Id==App._selectedClipId):null;
            if(selectedClip!=null)
            {
                float begin=X(selectedClip.Start),end=X(Math.Min(App._duration,selectedClip.Start+selectedClip.Duration));
                DrawRect(new Rect2(begin,y+2,Math.Max(2,end-begin),19),App._accent,false,1);
                DrawLine(new Vector2(begin+3,y+5),new Vector2(begin+3,y+18),new Color("f0e0ff"),2);
                DrawLine(new Vector2(end-3,y+5),new Vector2(end-3,y+18),new Color("f0e0ff"),2);
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
                Diamond(x, y + 11, IsTimelineKeySelected(new TrackRow("Path","",Object:actor),absoluteTime)?new Color("ffdc62"):color);
                if (times.Length < 12) DrawString(ThemeDB.FallbackFont, new Vector2(Math.Min(x + 7, Size.X - 24), y + 16), "P" + (i + 1), fontSize: 11, modulate: App._text);
            }
        }

        public override void _GuiInput(InputEvent input)
        {
            if(_marquee&&input is InputEventMouseMotion marqueeMotion){UpdateMarquee(marqueeMotion.Position);AcceptEvent();return;}
            if (input is InputEventMouseButton button && button.ButtonIndex is MouseButton.Left or MouseButton.Right)
            {
                if (!button.Pressed) { if (button.ButtonIndex == MouseButton.Left) EndDrag(); return; }
                GrabFocus();
                int index = (int)Math.Floor((button.Position.Y - Top) / RowHeight);
                var rows = Rows;
                if(button.ButtonIndex==MouseButton.Left&&!button.DoubleClick&&button.Position.X>=Left&&button.Position.Y>=Top&&!HitTimelineItem(rows,index,button.Position.X))
                {BeginMarquee(button);AcceptEvent();return;}
                if (index < 0 || index >= rows.Count)
                {
                    if (button.ButtonIndex == MouseButton.Left && button.Position.X >= Left) { _scrubbing = true; App.SetTime(Snap(Seconds(button.Position.X))); AcceptEvent(); }
                    return;
                }
                TrackRow row = rows[index];
                if (row.Kind == "Transform") { TransformInput(row, button); return; }
                if(row.Kind=="Pose"){PoseCorrectionInput(row,button);return;}
                if (button.ButtonIndex != MouseButton.Left) return;
                Entry actor = row.Object!;
                if (row.Kind == "Path")
                {
                    double[] times = ActorPathRuntime.PointTimes(actor.Data.Path); int point = -1; float nearest = 9;
                    for (int i = 0; i < times.Length; i++) { float d = Math.Abs(button.Position.X - X(actor.Data.Path.Start + times[i])); if (d < nearest) { nearest = d; point = i; } }
                    if (point >= 0 && button.Position.X >= Left - 8)
                    {
                        SelectTimelineKey(row,actor.Data.Path.Start+times[point],button.CtrlPressed||button.ShiftPressed);
                        App.SelectPathKey(actor, point); _dragPathEntry = actor; _dragPathIndex = point; _dragX = button.Position.X;
                        _pathKeyStart = actor.Data.Path.Start + times[point]; _hasDragged = false; AcceptEvent(); return;
                    }
                    App.Select(actor); App.ShowInspectorSection("Path");
                    if (button.Position.X >= Left) { _scrubbing = true; App.SetTime(Snap(Seconds(button.Position.X))); }
                    AcceptEvent(); return;
                }
                if (button.Position.X < Left) { App.Select(actor); AcceptEvent(); return; }
                var clip = SelectedClipEdge(actor,button.Position.X)??actor.Data.MotionClips.OrderByDescending(c => c.Start)
                    .FirstOrDefault(c => button.Position.X >= X(c.Start) && button.Position.X <= X(c.Start + c.Duration));
                if (clip != null)
                {
                    App._playing = false; App._play.Text = "▶ Play"; App.SelectTimelineClip(actor, clip);
                    _hasDragged = false; _dragEntry = actor; _dragClip = clip; _dragX = button.Position.X;
                    _start = clip.Start; _length = clip.Duration; _offset=clip.SourceOffset;
                    _clipBlendSnapshot.Clear();foreach(var source in actor.Data.MotionClips)_clipBlendSnapshot[source.Id]=source.BlendIn;
                    float fromStart=button.Position.X-X(clip.Start),fromEnd=X(clip.Start+clip.Duration)-button.Position.X;
                    _clipDrag=fromStart<8&&fromStart<=fromEnd?ClipDrag.In:fromEnd<8?ClipDrag.Out:ClipDrag.Move;
                    AcceptEvent(); return;
                }
                _scrubbing = true; App.SetTime(Snap(Seconds(button.Position.X))); AcceptEvent();
            }
            else if (input is InputEventMouseMotion motion && (_dragClip != null || _dragPathEntry != null || _dragKey || _scrubbing))
            { UpdatePointer(motion.Position.X); AcceptEvent(); }
        }

        private void PoseCorrectionInput(TrackRow row,InputEventMouseButton button)
        {
            if(button.Position.X<Left)
            {
                if(button.ButtonIndex==MouseButton.Left)
                {
                    if(!row.Child&&button.Position.X<25){if(!_expanded.Add(row.Target))_expanded.Remove(row.Target);UpdateMinimumHeight();if(_expanded.Contains(row.Target))ScrollGroupIntoView(row.Target);}
                    App.SelectPoseCorrectionTimeline(row.Object!,row.Channel,App._time);AcceptEvent();
                }
                return;
            }
            double nearest=double.NaN;float distance=8;
            foreach(double time in KeyTimes(row)){float d=Math.Abs(button.Position.X-X(time));if(d<distance){nearest=time;distance=d;}}
            if(double.IsFinite(nearest))
            {
                if(button.ButtonIndex==MouseButton.Right){App.RemovePoseCorrectionKey(row.Object!,row.Channel,nearest);QueueRedraw();AcceptEvent();return;}
                App._playing=false;App._play.Text="▶";App.SelectPoseCorrectionTimeline(row.Object!,row.Channel,nearest);App.SelectKeyInterpolation("Pose",row.Object,null,row.Channel,nearest);
                SelectTimelineKey(row,nearest,button.CtrlPressed||button.ShiftPressed);
                _selectedKeyTarget=row.Target;_selectedKeyChannel=row.Channel;_selectedKeyTime=nearest;
                _dragKey=true;_dragPoseKey=true;_dragKeyEntry=row.Object;_dragKeyCamera=null;_dragKeyChannel=row.Channel;_dragX=button.Position.X;_keyOriginalTime=_lastKeyTime=nearest;_hasDragged=false;AcceptEvent();return;
            }
            if(button.ButtonIndex!=MouseButton.Left)return;
            App.SelectPoseCorrectionTimeline(row.Object!,row.Channel,Snap(Seconds(button.Position.X)));
            if(button.DoubleClick){App.CapturePoseCorrectionKey(row.Channel);App.SelectKeyInterpolation("Pose",row.Object,null,row.Channel,App._time);_selectedKeyTarget=row.Target;_selectedKeyChannel=row.Channel;_selectedKeyTime=App._time;SelectTimelineKey(row,App._time,false);UpdateMinimumHeight();}
            else _scrubbing=true;
            AcceptEvent();QueueRedraw();
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
                SelectTimelineKey(row,nearest,button.CtrlPressed||button.ShiftPressed);
                _selectedKeyTarget = row.Target; _selectedKeyChannel = row.Channel; _selectedKeyTime = nearest;
                _dragKey = true; _dragPoseKey=false; _dragKeyEntry = row.Object; _dragKeyCamera = row.Camera; _dragKeyChannel = row.Channel;
                _dragX = button.Position.X; _keyOriginalTime = _lastKeyTime = nearest; _hasDragged = false; AcceptEvent(); return;
            }
            if (button.ButtonIndex == MouseButton.Right) return;
            if (row.Camera != null) App.SelectCamera(row.Camera); else App.Select(row.Object);
            App.SetTime(Snap(Seconds(button.Position.X)));
            if (button.DoubleClick)
            {
                App.CaptureSelectedTransformKey(row.Channel);
                _selectedKeyTarget = row.Target; _selectedKeyChannel = row.Channel; _selectedKeyTime = App._time;
                SelectTimelineKey(row,App._time,false);
            }
            else _scrubbing = true;
            AcceptEvent(); QueueRedraw();
        }
    }
}
