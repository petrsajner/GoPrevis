using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GoPrevis;

public partial class GoPrevisApp
{
    double MotionSourceLength(Entry entry,MotionClipData clip)
        =>entry.Node is RiggedActor actor?actor.MotionSourceDuration(clip):entry.NativeTrack is {} native?native.MotionSourceDuration(clip.SourceAnimation??clip.Name)
            :entry.Player?.HasAnimation(clip.SourceAnimation??clip.Name)==true?entry.Player.GetAnimation(clip.SourceAnimation??clip.Name).Length:0;
    readonly record struct MotionDataRange(double SourceLength,double Start,double End,bool VariableTiming,bool EndPaused);
    MotionDataRange MotionAvailableData(Entry entry,MotionClipData clip)
    {
        double length=MotionSourceLength(entry,clip),speed=Math.Max(.000001,clip.Speed);
        double atStart=MotionClipClock.SourceTime(clip.PropertyAnimation,clip.Start,clip.Start,clip.Duration,clip.SourceOffset,clip.Speed);
        double atEnd=MotionClipClock.SourceTime(clip.PropertyAnimation,clip.Start+clip.Duration,clip.Start,clip.Duration,clip.SourceOffset,clip.Speed);
        bool variable=MotionHasAnimatedSourceTime(clip);
        double step=Math.Min(.0001,Math.Max(.000001,clip.Duration/1000));
        double startRate=variable?(MotionClipClock.SourceTime(clip.PropertyAnimation,clip.Start+step,clip.Start,clip.Duration,clip.SourceOffset,clip.Speed)-atStart)/step:speed;
        double endRate=variable?(atEnd-MotionClipClock.SourceTime(clip.PropertyAnimation,clip.Start+clip.Duration-step,clip.Start,clip.Duration,clip.SourceOffset,clip.Speed))/step:speed;
        // Outside the active edit, extend its boundary rates. With an animated
        // remap the true source duration stays authoritative and the visual tails
        // are explicitly marked as a boundary-rate projection.
        double begin=startRate<=1e-6?clip.Start:clip.Start-atStart/startRate;
        double end=variable?clip.Start+clip.Duration+(length-atEnd)/Math.Max(.000001,endRate):clip.Start+(length-clip.SourceOffset)/speed;
        if(!double.IsFinite(begin))begin=clip.Start;
        if(!double.IsFinite(end)||endRate<=1e-6)end=clip.Start+clip.Duration;
        return new(length,begin,Math.Max(begin,end),variable,endRate<=1e-6);
    }
    static bool MotionHasAnimatedSourceTime(MotionClipData clip)
        =>clip.PropertyAnimation.Channels.Any(p=>(p.Key is "Speed" or "SourceOffset")&&PropertyCurveRuntime.HasKeys(p.Value));
    Button? _motionSourceViewButton;
    void BuildMotionSourceViewButton(HBoxContainer toolbar)
    {
        _motionSourceViewButton=Button("Fit source",()=>_timeline.FitMotionSourceRange());
        _motionSourceViewButton.TooltipText="Fit the complete recorded source, including unused grey handles. Press again for the shot range.";
        _motionSourceViewButton.Visible=false;toolbar.AddChild(_motionSourceViewButton);
    }
    partial class TimelineView
    {
        double _motionViewStart,_motionViewEnd;
        double ViewStart=>_motionViewEnd>_motionViewStart?_motionViewStart:0;
        double ViewEnd=>_motionViewEnd>_motionViewStart?_motionViewEnd:App._duration;
        double ViewDuration=>Math.Max(1.0/App._fps,ViewEnd-ViewStart);
        internal void FitMotionSourceRange()
        {
            if(_motionViewEnd>_motionViewStart){_motionViewStart=_motionViewEnd=0;if(App._motionSourceViewButton!=null)App._motionSourceViewButton.Text="Fit source";UpdateMinimumHeight();return;}
            var entry=App._selected;var clip=entry?.Data.MotionClips.FirstOrDefault(c=>c.Id==App._selectedClipId);
            if(entry==null||clip==null){App.Status("Select a motion clip to fit its complete source.");return;}
            var data=App.MotionAvailableData(entry,clip);if(data.SourceLength<=0)return;
            _motionViewStart=Math.Min(0,Math.Max(-MaxSceneDuration,data.Start));
            _motionViewEnd=Math.Max(App._duration,Math.Min(MaxSceneDuration,Math.Max(data.End,clip.Start+clip.Duration)));
            if(App._motionSourceViewButton!=null)App._motionSourceViewButton.Text="Shot range";
            UpdateMinimumHeight();App.Status("Source "+data.SourceLength.ToString("0.###")+" s · grey: unused source · color: active clip · Fit source returns to shot range.");
        }
        internal Rect2 MotionSourceRect(string entryId,string clipId)
        {
            var entry=App._entries.FirstOrDefault(e=>e.Data.Id==entryId);var clip=entry?.Data.MotionClips.FirstOrDefault(c=>c.Id==clipId);
            int row=Rows.FindIndex(r=>r.Kind=="Motion"&&r.Object?.Data.Id==entryId);if(entry==null||clip==null||row<0)return new();
            var data=App.MotionAvailableData(entry,clip);double left=Math.Max(ViewStart,data.Start),right=Math.Min(ViewEnd,data.End);
            return new Rect2(X(left),Top+row*RowHeight+2,Math.Max(0,X(right)-X(left)),19);
        }
        void DrawMotionSourceHandles(Entry entry,List<MotionClipData> clips,float y)
        {
            foreach(var clip in clips)
            {
                var data=App.MotionAvailableData(entry,clip);if(data.SourceLength<=0)continue;
                double left=Math.Max(ViewStart,data.Start),right=Math.Min(ViewEnd,data.End);if(right<=left)continue;
                var r=new Rect2(X(left),y+2,Math.Max(1,X(right)-X(left)),19);
                DrawRect(r,new Color("30323a"));DrawRect(r,new Color("626574"),false,1);
                if(data.Start<ViewStart)DrawLine(new(Left+1,y+5),new(Left+6,y+10),new Color("a6a7b1"));
                if(data.End>ViewEnd)DrawLine(new(Size.X-24,y+5),new(Size.X-19,y+10),new Color("a6a7b1"));
            }
        }
        void DrawMotionSourceStatus(Entry entry,MotionClipData clip,Rect2 r,float y)
        {
            double length=App.MotionSourceLength(entry,clip);if(length<=0)return;
            bool loops=MotionClipClock.Flag(clip.PropertyAnimation,"Loop",App._time,clip.Loop);
            bool variable=MotionHasAnimatedSourceTime(clip);
            if(loops&&!variable)
            {
                double step=length/Math.Max(.000001,clip.Speed),begin=clip.Start-clip.SourceOffset/Math.Max(.000001,clip.Speed);
                int count=0;for(double t=begin+step;t<clip.Start+clip.Duration&&count<128;t+=step,count++)
                    if(t>clip.Start&&t>=ViewStart&&t<=ViewEnd)DrawLine(new(X(t),y+4),new(X(t),y+19),new Color(.8f,.75f,.9f,.45f));
            }
            else if(!loops&&!variable)
            {
                double end=clip.Start+(length-clip.SourceOffset)/Math.Max(.000001,clip.Speed);
                if(end<clip.Start+clip.Duration)
                {float begin=Math.Max(r.Position.X,X(Math.Max(clip.Start,end)));if(begin<r.End.X){DrawRect(new Rect2(begin,y+14,r.End.X-begin,7),new Color("49414d"));DrawString(ThemeDB.FallbackFont,new(begin+3,y+19),Fit("HOLD",r.End.X-begin-4),fontSize:8,modulate:new Color("c2b9c4"));}}
            }
        }
    }
}
