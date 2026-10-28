using System;
using System.Collections.Generic;
using System.Linq;

namespace GoPrevis;

public readonly record struct KeyGroupPoint(string Channel,string Id,double Time);

/// <summary>One legal frame delta for an entire selection, without overwriting other keys.</summary>
public static class GroupKeyRetiming
{
    public static double ResolveDelta(IEnumerable<KeyGroupPoint> selected,IEnumerable<KeyGroupPoint> all,
        double desiredDelta,double duration,int fps,double minimumDelta=double.NegativeInfinity,double maximumDelta=double.PositiveInfinity)
    {
        var moving=selected.Where(k=>double.IsFinite(k.Time)).ToArray();if(moving.Length==0||!double.IsFinite(desiredDelta))return 0;
        fps=Math.Max(1,fps);duration=Math.Max(0,duration);
        double low=Math.Max(-moving.Min(k=>k.Time),minimumDelta),high=Math.Min(duration-moving.Max(k=>k.Time),maximumDelta);
        long first=(long)Math.Ceiling(low*fps-1e-8),last=(long)Math.Floor(high*fps+1e-8);if(first>last)return 0;
        long requested=(long)Math.Clamp(Math.Round(desiredDelta*fps),first,last);
        var ids=moving.Select(k=>(k.Channel,k.Id)).ToHashSet();
        var fixedTimes=all.Where(k=>double.IsFinite(k.Time)&&!ids.Contains((k.Channel,k.Id)))
            .GroupBy(k=>k.Channel).ToDictionary(g=>g.Key,g=>g.Select(k=>k.Time).OrderBy(t=>t).ToArray(),StringComparer.Ordinal);
        bool Free(long frame)
        {
            double delta=frame/(double)fps;
            foreach(var key in moving)
                if(fixedTimes.TryGetValue(key.Channel,out var times))
                {
                    double at=key.Time+delta;int found=Array.BinarySearch(times,at);
                    if(found>=0)return false;int next=~found;
                    if(next<times.Length&&Math.Abs(times[next]-at)<.000001||next>0&&Math.Abs(times[next-1]-at)<.000001)return false;
                }
            return true;
        }
        if(Free(requested))return requested/(double)fps;
        // Prefer the nearest legal delta toward the original selection; try the
        // other side only if that candidate is outside the allowed interval.
        for(long distance=1;distance<=last-first;distance++)
        {
            long toward=requested+(requested>=0?-distance:distance);
            if(toward>=first&&toward<=last&&Free(toward))return toward/(double)fps;
            long beyond=requested+(requested>=0?distance:-distance);
            if(beyond>=first&&beyond<=last&&Free(beyond))return beyond/(double)fps;
        }
        return 0;
    }
}
