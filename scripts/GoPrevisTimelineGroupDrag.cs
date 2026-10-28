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
        sealed record GroupDragKey(KeyRef Ref,double OriginalTime,Action<double> Write);
        List<GroupDragKey>? _timelineGroupDrag;
        double _timelineGroupDelta;
        string GroupChannel(KeyRef key)=>key.Kind+"/"+key.Target+"/"+key.Channel;
        IEnumerable<GroupDragKey> AtomicTimelineKeys()
        {
            foreach(var entry in App._entries)
            {
                string target="object:"+entry.Data.Id;
                foreach(string channel in new[]{"Position","Rotation","Scale"})
                    foreach(var key in TransformLayerRuntime.Channel(entry.Data.TransformAnimation,channel))
                    {var value=key;yield return new(new("Transform",target,channel,key.Id),key.Time,t=>value.Time=t);}
                string poseTarget="pose:"+entry.Data.Id;
                foreach(var key in entry.Data.PoseCorrections.Root)
                {var value=key;yield return new(new("Pose",poseTarget,"Root",key.Id),key.Time,t=>value.Time=t);}
                foreach(var pair in entry.Data.PoseCorrections.Bones)
                    foreach(var key in pair.Value)
                    {var value=key;yield return new(new("Pose",poseTarget,pair.Key,key.Id),key.Time,t=>value.Time=t);}
                var path=entry.Data.Path;var times=ActorPathRuntime.PointTimes(path);
                for(int i=0;i<times.Length;i++)
                {
                    int point=i;yield return new(new("Path",target,"Path",i.ToString(CultureInfo.InvariantCulture)),path.Start+times[i],t=>
                    {if(path.PointTimes.Count!=times.Length)path.PointTimes=times.ToList();path.PointTimes[point]=t-path.Start;});
                }
            }
            foreach(var camera in App._cameras)
            {
                string target="camera:"+camera.Id;
                foreach(string channel in new[]{"Position","Rotation","Scale"})
                    foreach(var key in TransformLayerRuntime.Channel(camera.Animation,channel))
                    {var value=key;yield return new(new("Transform",target,channel,key.Id),key.Time,t=>value.Time=t);}
                foreach(var key in camera.LensKeys)
                {var value=key;yield return new(new("Transform",target,"Lens",key.Id),key.Time,t=>value.Time=t);}
            }
        }
        bool BeginSelectedKeyDrag(TrackRow row,double time,float x)
        {
            var pressed=RowKeyRefs(row,time).ToArray();
            if(!pressed.Any(_selectedKeys.Contains)){EndSelectedKeyDrag();return false;}
            _timelineGroupDrag=AtomicTimelineKeys().Where(k=>_selectedKeys.Contains(k.Ref)).ToList();
            _timelineGroupDelta=0;_dragX=x;_keyOriginalTime=time;_hasDragged=false;
            return _timelineGroupDrag.Count>0;
        }
        void EndSelectedKeyDrag(){_timelineGroupDrag=null;_timelineGroupDelta=0;}
        bool UpdateSelectedKeyDrag(float x)
        {
            if(_timelineGroupDrag==null)return false;
            double desired=(x-_dragX)/TrackWidth*ViewDuration;
            var selected=_timelineGroupDrag.Select(k=>new KeyGroupPoint(GroupChannel(k.Ref),k.Ref.Id,k.OriginalTime)).ToArray();
            var all=AtomicTimelineKeys().Select(k=>new KeyGroupPoint(GroupChannel(k.Ref),k.Ref.Id,
                _timelineGroupDrag.FirstOrDefault(s=>s.Ref==k.Ref)?.OriginalTime??k.OriginalTime)).ToArray();
            double lower=double.NegativeInfinity,upper=double.PositiveInfinity;
            // A path's waypoints retain their spatial order. Only unselected
            // neighbours constrain the common delta; selected neighbours move too.
            foreach(var group in _timelineGroupDrag.Where(k=>k.Ref.Kind=="Path").GroupBy(k=>k.Ref.Target))
            {
                var entry=App._entries.FirstOrDefault(e=>group.Key=="object:"+e.Data.Id);if(entry==null)continue;
                var path=entry.Data.Path;var times=ActorPathRuntime.PointTimes(path);var indices=group.Select(k=>int.Parse(k.Ref.Id,CultureInfo.InvariantCulture)).ToHashSet();
                foreach(var key in group)
                {
                    int i=int.Parse(key.Ref.Id,CultureInfo.InvariantCulture);double gap=1.0/App._fps;
                    lower=Math.Max(lower,path.Start-key.OriginalTime);
                    if(i>0&&!indices.Contains(i-1))lower=Math.Max(lower,path.Start+times[i-1]+gap-key.OriginalTime);
                    if(i+1<times.Length&&!indices.Contains(i+1))upper=Math.Min(upper,path.Start+times[i+1]-gap-key.OriginalTime);
                }
            }
            double delta=GroupKeyRetiming.ResolveDelta(selected,all,desired,App._duration,App._fps,lower,upper);
            if(Math.Abs(delta-_timelineGroupDelta)<KeyTolerance)return true;
            if(!_hasDragged){App.StopHistoryPlayback();App.PushUndo();_hasDragged=true;}
            foreach(var key in _timelineGroupDrag)key.Write(key.OriginalTime+delta);
            foreach(var entry in App._entries.Where(e=>_timelineGroupDrag.Any(k=>k.Ref.Kind=="Path"&&k.Ref.Target=="object:"+e.Data.Id)))
                if(entry.Data.Path.PointTimes.Count>0)entry.Data.Path.Duration=Math.Max(1.0/App._fps,entry.Data.Path.PointTimes[^1]);
            _timelineGroupDelta=delta;_selectedKeyTime=_keyOriginalTime+delta;
            App._propertyEditBefore=null;
            App.SetTime(Math.Clamp(_keyOriginalTime+delta,0,App._duration));QueueRedraw();
            App.Status(_timelineGroupDrag.Count+" selected keys moved together · Undo restores the gesture.");return true;
        }
    }
}
