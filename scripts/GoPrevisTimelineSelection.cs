using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GoPrevis;

public partial class GoPrevisApp
{
    partial class TimelineView
    {
        // Atomic IDs deduplicate summary diamonds and expanded parameter rows.
        sealed record KeyRef(string Kind,string Target,string Channel,string Id);
        readonly HashSet<KeyRef> _selectedKeys=new();
        HashSet<KeyRef> _marqueeBase=new();
        bool _marquee;
        Vector2 _marqueeFrom,_marqueeTo;
        internal int SelectedKeyCount=>_selectedKeys.Count;
        internal Rect2 KeyRowRect(string kind,string id,string channel="All")
        {
            int row=Rows.FindIndex(r=>r.Kind==kind&&(r.Camera?.Id??r.Object?.Data.Id)==id&&r.Channel==channel);
            return row<0?new():new Rect2(Left,Top+row*RowHeight,TrackWidth,RowHeight);
        }
        internal void ClearKeySelection(){EndSelectedKeyDrag();_selectedKeys.Clear();_marquee=false;ClearItemSelection();QueueRedraw();}

        IEnumerable<KeyRef> RowKeyRefs(TrackRow row,double time)
        {
            if(row.Kind=="Path")
            {
                var path=row.Object!.Data.Path;var times=ActorPathRuntime.PointTimes(path);
                for(int i=0;i<times.Length;i++)if(Math.Abs(path.Start+times[i]-time)<KeyTolerance)
                    yield return new("Path",row.Target,"Path",i.ToString(CultureInfo.InvariantCulture));
            }
            else if(row.Kind=="Pose")
            {
                var data=row.Object!.Data.PoseCorrections;
                if(row.Channel is "All" or "Root")foreach(var key in data.Root.Where(k=>Math.Abs(k.Time-time)<KeyTolerance))yield return new("Pose",row.Target,"Root",key.Id);
                foreach(var pair in data.Bones.Where(pair=>row.Channel=="All"||pair.Key==row.Channel))
                    foreach(var key in pair.Value.Where(k=>Math.Abs(k.Time-time)<KeyTolerance))yield return new("Pose",row.Target,pair.Key,key.Id);
            }
            else if(row.Kind=="Transform")
            {
                var animation=Animation(row);
                foreach(string channel in new[]{"Position","Rotation","Scale"}.Where(c=>row.Channel=="All"||row.Channel==c))
                    foreach(var key in TransformLayerRuntime.Channel(animation,channel).Where(k=>Math.Abs(k.Time-time)<KeyTolerance))yield return new("Transform",row.Target,channel,key.Id);
                if(row.Camera!=null&&row.Channel is "All" or "Lens")
                    foreach(var key in row.Camera.LensKeys.Where(k=>Math.Abs(k.Time-time)<KeyTolerance))yield return new("Transform",row.Target,"Lens",key.Id);
            }
        }
        IEnumerable<double> RowTimes(TrackRow row)=>row.Kind=="Path"
            ?ActorPathRuntime.PointTimes(row.Object!.Data.Path).Select(t=>t+row.Object.Data.Path.Start):row.Kind is "Transform" or "Pose"?KeyTimes(row):Array.Empty<double>();
        bool IsTimelineKeySelected(TrackRow row,double time)=>RowKeyRefs(row,time).Any(key=>_selectedKeys.Contains(key)||_selectedItem is {} item&&item.Kind=="Track"+row.Kind&&item.Target==(row.Camera?.Id??row.Object?.Data.Id)&&(item.Channel=="All"||item.Channel==key.Channel));
        void SelectTimelineKey(TrackRow row,double time,bool add)
        {
            ClearItemSelection();
            var keys=RowKeyRefs(row,time).ToArray();
            // A normal press inside the current selection starts a group drag.
            // Only pressing an unselected key replaces the current selection.
            if(!add&&keys.Any(_selectedKeys.Contains)){QueueRedraw();return;}
            if(!add)_selectedKeys.Clear();
            bool remove=add&&keys.Length>0&&keys.All(_selectedKeys.Contains);
            foreach(var key in keys){if(remove)_selectedKeys.Remove(key);else _selectedKeys.Add(key);}
            QueueRedraw();
        }
        bool HitTimelineItem(List<TrackRow> rows,int index,float x)
        {
            if(index<0||index>=rows.Count)return false;var row=rows[index];
            return row.Kind=="Motion"?row.Object!.Data.MotionClips.Any(c=>x>=X(c.Start)&&x<=X(c.Start+c.Duration))||row.Object.NativeTrack==null&&row.Object.Player!=null&&row.Object.Animation!=""
                :HitCameraMove(row,x)||HitPathSpan(row,x)||RowTimes(row).Any(time=>Math.Abs(x-X(time))<9);
        }
        void BeginMarquee(InputEventMouseButton button)
        {
            ClearItemSelection();
            _marquee=true;_marqueeFrom=_marqueeTo=button.Position;
            _marqueeBase=button.CtrlPressed||button.ShiftPressed?new(_selectedKeys):new();
            _selectedKeys.Clear();_selectedKeys.UnionWith(_marqueeBase);
            App._playing=false;App._play.Text="▶";App.SetTime(Snap(Seconds(button.Position.X)));QueueRedraw();
        }
        Rect2 MarqueeRect=>new(_marqueeFrom.Min(_marqueeTo),(_marqueeTo-_marqueeFrom).Abs());
        void UpdateMarquee(Vector2 position)
        {
            _marqueeTo=position;_selectedKeys.Clear();_selectedKeys.UnionWith(_marqueeBase);
            if(position.DistanceTo(_marqueeFrom)>3)
            {
                var box=MarqueeRect;var rows=Rows;
                for(int index=0;index<rows.Count;index++)
                    foreach(double time in RowTimes(rows[index]))
                        if(box.HasPoint(new Vector2(X(time),Top+index*RowHeight+11)))_selectedKeys.UnionWith(RowKeyRefs(rows[index],time));
            }
            App.Status(_selectedKeys.Count+" parameter key(s) selected · Delete to remove · Ctrl/Shift to add.");QueueRedraw();
        }
        void DrawMarquee()
        {
            if(!_marquee||_marqueeFrom.DistanceTo(_marqueeTo)<3)return;
            var box=MarqueeRect.Intersection(new Rect2(Left,Top,TrackWidth,Math.Max(1,Size.Y-Top)));
            DrawRect(box,new Color(.7f,.61f,.94f,.14f));DrawRect(box,new Color("c4b0f4"),false,1);
        }
        internal bool DeleteSelectedKeys()
        {
            var existing=Rows.SelectMany(row=>RowTimes(row).SelectMany(time=>RowKeyRefs(row,time))).ToHashSet();
            _selectedKeys.IntersectWith(existing);if(_selectedKeys.Count==0)return false;
            App.StopHistoryPlayback();App.PushUndo();int count=_selectedKeys.Count;
            foreach(var group in _selectedKeys.GroupBy(key=>(key.Kind,key.Target)))
            {
                var entry=App._entries.FirstOrDefault(e=>group.Key.Target.EndsWith(":"+e.Data.Id,StringComparison.Ordinal));
                var camera=App._cameras.FirstOrDefault(c=>group.Key.Target=="camera:"+c.Id);
                if(group.Key.Kind=="Path"&&entry!=null)
                {
                    var path=entry.Data.Path;path.PointTimes=ActorPathRuntime.PointTimes(path).ToList();
                    foreach(int index in group.Select(k=>int.Parse(k.Id,CultureInfo.InvariantCulture)).Distinct().OrderDescending())
                    {path.Points.RemoveAt(index);path.PointTimes.RemoveAt(index);if(index<path.PointInterpolation.Count)path.PointInterpolation.RemoveAt(index);}
                    if(path.Points.Count<2)path.Enabled=false;
                    if(path.PointTimes.Count>0)path.Duration=Math.Max(1.0/App._fps,path.PointTimes[^1]);App._selectedPathPoint=0;
                }
                else foreach(var channel in group.GroupBy(k=>k.Channel))
                {
                    var ids=channel.Select(k=>k.Id).ToHashSet();
                    if(group.Key.Kind=="Pose"&&entry!=null)
                    {
                        var data=entry.Data.PoseCorrections;
                        if(channel.Key=="Root")data.Root.RemoveAll(k=>ids.Contains(k.Id));
                        else if(data.Bones.TryGetValue(channel.Key,out var keys)){keys.RemoveAll(k=>ids.Contains(k.Id));if(keys.Count==0)data.Bones.Remove(channel.Key);}
                    }
                    else if(camera!=null||entry!=null)
                    {
                        if(channel.Key=="Lens")camera?.LensKeys.RemoveAll(k=>ids.Contains(k.Id));
                        else TransformLayerRuntime.Channel(camera?.Animation??entry!.Data.TransformAnimation,channel.Key).RemoveAll(k=>ids.Contains(k.Id));
                    }
                }
            }
            ClearKeySelection();App.SetTime(App._time);App.RefreshSequenceInspector();UpdateMinimumHeight();
            GrabFocus();App.Status("Deleted "+count+" parameter key(s). Undo restores the deleted keys.");return true;
        }
    }
}
