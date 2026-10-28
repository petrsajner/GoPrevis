using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GoPrevis;

public partial class GoPrevisApp
{
    sealed record MotionClipboardData(MotionClipData Clip,bool Human,string Family,string? ModelPath,string? LibraryModelId);
    MotionClipboardData? _motionClipboard;
    static MotionClipData CopyMotionClipData(MotionClipData clip)=>JsonSerializer.Deserialize<MotionClipData>(JsonSerializer.Serialize(clip))!;

    bool CopyMotionClip(Entry source,MotionClipData clip)
    {
        if(source.Node is not RiggedActor&&source.NativeTrack==null){Status("Choose a prepared character motion clip to copy.");return false;}
        _motionClipboard=new(CopyMotionClipData(clip),source.Node is RiggedActor,source.Data.ActorFamily,source.Data.ModelPath,source.Data.LibraryModelId);
        Status("Copied motion clip: "+clip.Name+". Paste places it at the playhead.");return true;
    }
    bool MotionPasteCompatible(MotionClipboardData copy,Entry target,out string reason)
    {
        reason="";
        if(copy.Human)
        {
            if(target.Node is not RiggedActor){reason="This human motion requires a prepared human character.";return false;}
            if(string.IsNullOrWhiteSpace(copy.Clip.MotionPath)||!File.Exists(copy.Clip.MotionPath)){reason="The copied BVH source is no longer available. Restore its source file before pasting.";return false;}
        }
        else
        {
            if(target.NativeTrack==null||target.Player==null){reason="This motion requires its matching rigged animal or imported model.";return false;}
            bool sameFamily=!string.IsNullOrEmpty(copy.Family)&&copy.Family==target.Data.ActorFamily;
            bool sameModel=!string.IsNullOrEmpty(copy.LibraryModelId)&&copy.LibraryModelId==target.Data.LibraryModelId||
                !string.IsNullOrEmpty(copy.ModelPath)&&!string.IsNullOrEmpty(target.Data.ModelPath)&&Path.GetFullPath(copy.ModelPath).Equals(Path.GetFullPath(target.Data.ModelPath),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);
            if(!string.IsNullOrEmpty(copy.Family)&&!string.IsNullOrEmpty(target.Data.ActorFamily)&&copy.Family!=target.Data.ActorFamily){reason="The copied motion belongs to a different animal or model skeleton.";return false;}
            if(!sameFamily&&!sameModel){reason="The copied motion belongs to a different animal or model skeleton.";return false;}
            if(!target.Player.HasAnimation(copy.Clip.SourceAnimation??copy.Clip.Name)){reason="The selected model does not contain the copied source animation.";return false;}
        }
        var c=copy.Clip;
        if(!double.IsFinite(c.Duration)||c.Duration<=0||!double.IsFinite(c.Speed)||c.Speed<=0||!double.IsFinite(c.SourceOffset)||c.SourceOffset<0||!double.IsFinite(c.BlendIn)||c.BlendIn<0||!float.IsFinite(c.SourceScale)||c.SourceScale<=0||c.SourceScale>1000)
        {reason="The copied motion has invalid source or timing settings.";return false;}
        return true;
    }
    bool PasteMotionClip(Entry target,double start)
    {
        if(_motionClipboard==null){Status("Copy a motion clip before pasting.");return false;}
        return InsertMotionClip(_motionClipboard,target,Math.Round(Math.Max(0,start)*_fps)/_fps,false);
    }
    bool DuplicateMotionClip(Entry source,MotionClipData clip)
    {
        var snapshot=new MotionClipboardData(CopyMotionClipData(clip),source.Node is RiggedActor,source.Data.ActorFamily,source.Data.ModelPath,source.Data.LibraryModelId);
        return InsertMotionClip(snapshot,source,clip.Start+clip.Duration,true);
    }
    bool InsertMotionClip(MotionClipboardData copy,Entry target,double start,bool duplicate)
    {
        if(_exportBusy||_aiBusy)return false;
        if(!MotionPasteCompatible(copy,target,out string reason)){Status("Cannot paste motion: "+reason);return false;}
        if(!double.IsFinite(start)||start<0||start+copy.Clip.Duration>MaxSceneDuration+.000001){Status("The copied clip would exceed the 600-second shot limit. Choose an earlier playhead position.");return false;}
        var inserted=CopyMotionClipData(copy.Clip);inserted.Id=Guid.NewGuid().ToString("N");inserted.IsPlaceholder=false;
        ShiftMotionPropertyKeys(inserted,start-inserted.Start);inserted.Start=start;
        foreach(var curve in inserted.PropertyAnimation.Channels.Values)foreach(var key in curve.Keys)key.Id=Guid.NewGuid().ToString("N");
        var previous=target.Data.MotionClips;
        var candidate=(!duplicate&&previous.Count>0&&previous.All(c=>c.IsPlaceholder)?new List<MotionClipData>():previous.ToList());candidate.Add(inserted);
        var undo=_undo.ToArray();var redo=_redo.ToArray();var pending=_propertyEditBefore;string selectedClip=_selectedClipId;
        double previousDuration=_duration;string? previousMotionPath=target.Data.MotionPath,previousMotionName=target.Data.MotionName;
        string? previousObject=_selected?.Data.Id,previousCamera=_selectedCamera?.Id;
        try
        {
            StopHistoryPlayback();PushUndo();target.Data.MotionClips=candidate;RebuildTrack(target);EnsureDuration(inserted.Start+inserted.Duration);
            SelectTimelineClip(target,inserted);SetTime(_time);RefreshSequenceInspector();RefreshAnimatedProperties();_timeline.GrabFocus();
            Status((duplicate?"Duplicated after current clip: ":"Pasted at playhead: ")+inserted.Name);return true;
        }
        catch(Exception error)
        {
            target.Data.MotionClips=previous;target.Data.MotionPath=previousMotionPath;target.Data.MotionName=previousMotionName;
            try{RebuildTrack(target);}catch(Exception restoreError){GD.PushWarning("Motion playback could not be refreshed after restoring the original clip data: "+restoreError.Message);}
            _duration=previousDuration;_scrub.MaxValue=_duration;bool refreshing=_refreshing;_refreshing=true;_durationField.Value=_duration;_refreshing=refreshing;
            _selectedClipId=selectedClip;_propertyEditBefore=pending;RestoreInteractionSelection(previousObject,previousCamera);
            _undo.Clear();_redo.Clear();foreach(var state in undo.Reverse())_undo.Push(state);foreach(var state in redo.Reverse())_redo.Push(state);
            RefreshHistoryControls();RefreshSequenceInspector();RefreshTimelineSize();
            Status("Could not paste motion: "+error.Message);return false;
        }
    }

    /// <summary>Finds a phase-preserving IN boundary using the integrated source clock.</summary>
    static double EarliestMotionTrimStart(MotionClipData clip,double originalStart,double baseOffset)
    {
        // An animated source offset can change independently of the playback
        // clock. Extending its pre-roll would require remapping the entire offset
        // curve, so preserve it rather than silently clamp individual key values.
        if(clip.PropertyAnimation.Channels.TryGetValue("SourceOffset",out var offset)&&PropertyCurveRuntime.HasKeys(offset))return originalStart;
        double available=Math.Max(0,baseOffset);
        if(!clip.PropertyAnimation.Channels.TryGetValue("Speed",out var rate)||!PropertyCurveRuntime.HasKeys(rate))
            return Math.Max(0,originalStart-available/Math.Max(.000001,clip.Speed));
        double Used(double from)=>PropertyCurveRuntime.Integrate(rate,from,originalStart,clip.Speed,0,double.PositiveInfinity);
        if(Used(0)<=available)return 0;
        double low=0,high=originalStart;
        // Integrated nonnegative speed is monotone. Keep the valid side of the
        // boundary so IN never consumes more source pre-roll than is available.
        for(int i=0;i<60;i++){double mid=(low+high)*.5;if(Used(mid)>available)low=mid;else high=mid;}
        return high;
    }

    /// <summary>Moves the clip's scene-time parameter curves with its body, preserving their relative timing.</summary>
    static void ShiftMotionPropertyKeys(MotionClipData clip,double delta)
    {
        if(Math.Abs(delta)<1e-12)return;
        foreach(var curve in clip.PropertyAnimation.Channels.Values)
        {
            if(curve.Keys.Count==0)continue;
            var original=PropertyCurveRuntime.Copy(curve);original.Enabled=true;var ordered=PropertyCurveRuntime.OrderedKeys(original);
            double cut=-delta;
            ScalarKeyData? before=ordered.LastOrDefault(k=>k.Time<cut),after=ordered.FirstOrDefault(k=>k.Time>=cut);
            foreach(var key in curve.Keys)key.Time+=delta;
            curve.Keys.RemoveAll(k=>k.Time<0);
            if(before==null)continue;
            if(after!=null&&Math.Abs(after.Time-cut)<1e-10)continue;
            var boundary=PropertyCurveRuntime.Upsert(curve,0,PropertyCurveRuntime.Evaluate(original,cut,before.Value));
            boundary.Interpolation=before.Interpolation;boundary.InSlope=boundary.OutSlope=PropertyCurveRuntime.Velocity(original,cut);
            if(after==null||curve.Discrete||before.Interpolation=="Hold")continue;
            if(before.Interpolation=="Stop")
            {
                // A clipped quintic cannot be represented by one cubic. Adaptive
                // Hermite segments preserve its values, velocities and source-clock integral.
                var end=curve.Keys.Single(k=>k.Id==after.Id);AppendStoppedCurvePrefix(curve,original,cut,after.Time,delta,boundary,end,0);continue;
            }
            if(before.Interpolation is not("Bezier" or "Curved" or "Auto" or "Flat" or "Ease in/out"))continue;
            SplitMotionBezierPrefix(curve,before,after,cut,delta,boundary);
        }
    }
    readonly record struct MotionCurvePoint(double Time,double Value)
    {public MotionCurvePoint Mix(MotionCurvePoint other,double t)=>new(Time+(other.Time-Time)*t,Value+(other.Value-Value)*t);}
    static void SplitMotionBezierPrefix(ScalarAnimationCurve curve,ScalarKeyData a,ScalarKeyData b,double cut,double delta,ScalarKeyData boundary)
    {
        double dt=b.Time-a.Time;bool ease=a.Interpolation=="Ease in/out",flat=a.Interpolation=="Flat";
        double ow=ease?1.0/3:Math.Clamp(a.OutWeight,0,1),iw=ease?1.0/3:Math.Clamp(b.InWeight,0,1);
        var p0=new MotionCurvePoint(a.Time,a.Value);var p1=new MotionCurvePoint(a.Time+dt*ow,a.Value+(ease||flat?0:a.OutSlope)*dt*ow);
        var p2=new MotionCurvePoint(b.Time-dt*iw,b.Value-(ease||flat?0:b.InSlope)*dt*iw);var p3=new MotionCurvePoint(b.Time,b.Value);
        double lo=0,hi=1;
        for(int i=0;i<52;i++){double t=(lo+hi)*.5;var q0=p0.Mix(p1,t);var q1=p1.Mix(p2,t);var q2=p2.Mix(p3,t);if(q0.Mix(q1,t).Mix(q1.Mix(q2,t),t).Time<cut)lo=t;else hi=t;}
        double u=(lo+hi)*.5;var right2=p2.Mix(p3,u);var middle=p1.Mix(p2,u).Mix(right2,u);
        double remaining=p3.Time-cut;boundary.Interpolation="Bezier";boundary.OutWeight=(middle.Time-cut)/remaining;
        boundary.OutSlope=Math.Abs(middle.Time-cut)<1e-12?0:(middle.Value-boundary.Value)/(middle.Time-cut);
        var end=curve.Keys.Single(k=>k.Id==b.Id);end.InWeight=(p3.Time-right2.Time)/remaining;
        end.InSlope=Math.Abs(p3.Time-right2.Time)<1e-12?0:(p3.Value-right2.Value)/(p3.Time-right2.Time);
    }
    static void AppendStoppedCurvePrefix(ScalarAnimationCurve curve,ScalarAnimationCurve original,double from,double to,double delta,ScalarKeyData first,ScalarKeyData last,int depth)
    {
        double dt=to-from;first.Interpolation="Bezier";first.OutWeight=last.InWeight=1.0/3;
        first.OutSlope=PropertyCurveRuntime.Velocity(original,from);last.InSlope=PropertyCurveRuntime.OrderedKeys(original).Any(k=>Math.Abs(k.Time-to)<1e-10)?0:PropertyCurveRuntime.Velocity(original,to);
        var candidate=new ScalarAnimationCurve{Keys=new(){PropertyCurveRuntime.Copy(first),PropertyCurveRuntime.Copy(last)}};
        double error=0,scale=1+Math.Max(Math.Abs(first.Value),Math.Abs(last.Value));
        foreach(double f in new[]{.25,.5,.75})error=Math.Max(error,Math.Abs(PropertyCurveRuntime.Evaluate(candidate,from+delta+dt*f,0)-PropertyCurveRuntime.Evaluate(original,from+dt*f,0)));
        if(error<1e-9*scale||depth>=14)return;
        double mid=(from+to)*.5;var centre=PropertyCurveRuntime.Upsert(curve,mid+delta,PropertyCurveRuntime.Evaluate(original,mid,0));
        AppendStoppedCurvePrefix(curve,original,from,mid,delta,first,centre,depth+1);AppendStoppedCurvePrefix(curve,original,mid,to,delta,centre,last,depth+1);
    }

    bool HandleMotionClipboardShortcut(InputEventKey key)
        =>_animationWorkspaceMode=="Clips"&&_timeline.HasFocus()&&_timeline.HandleMotionClipboardShortcut(key);

    partial class TimelineView
    {
        (Entry? Entry,MotionClipData? Clip) MotionClipboardSelection()
        {
            var entry=App._entries.FirstOrDefault(e=>e.Data.Id==_selectedItem?.Target)??App._selected;
            var clip=_selectedItem?.Kind=="MotionClip"?entry?.Data.MotionClips.FirstOrDefault(c=>c.Id==_selectedItem.Id):null;
            return(entry,clip);
        }
        internal bool HandleMotionClipboardShortcut(InputEventKey key)
        {
            if(!key.CtrlPressed||key.Keycode is not(Key.C or Key.V or Key.D))return false;
            var (entry,clip)=MotionClipboardSelection();if(entry==null)return false;
            if(key.Keycode==Key.C&&clip!=null)App.CopyMotionClip(entry,clip);
            else if(key.Keycode==Key.D&&clip!=null)App.DuplicateMotionClip(entry,clip);
            else if(key.Keycode==Key.V&&App._motionClipboard!=null)App.PasteMotionClip(entry,App._time);
            else return false;
            GetViewport().SetInputAsHandled();return true;
        }
        void AddMotionClipboardMenuItems(PopupMenu menu)
        {
            var (entry,clip)=MotionClipboardSelection();bool motion=entry!=null&&(entry.Node is RiggedActor||entry.NativeTrack!=null);
            if(!motion)return;
            if(clip!=null){menu.AddItem("Duplicate",1);menu.AddItem("Copy",2);}
            menu.AddItem("Paste",3);menu.SetItemDisabled(menu.GetItemIndex(3),App._motionClipboard==null);menu.AddSeparator();
        }
        void InvokeMotionClipboardAction(int id)
        {
            if(id==0){DeleteSelectedItems();return;}var (entry,clip)=MotionClipboardSelection();if(entry==null)return;
            if(id==1&&clip!=null)App.DuplicateMotionClip(entry,clip);else if(id==2&&clip!=null)App.CopyMotionClip(entry,clip);else if(id==3)App.PasteMotionClip(entry,App._time);
        }
        internal void InvokeMotionClipboardMenu(int id)=>_itemMenu!.EmitSignal(PopupMenu.SignalName.IdPressed,id);
        internal void HideMotionClipboardMenu()=>_itemMenu?.Hide();
        internal string[] MotionClipboardMenuLabels()=>_itemMenu==null?Array.Empty<string>():Enumerable.Range(0,_itemMenu.ItemCount).Select(i=>_itemMenu.GetItemText(i)).ToArray();
    }
}
