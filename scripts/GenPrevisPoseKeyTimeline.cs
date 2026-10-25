using System;
using System.Collections.Generic;
using System.Linq;

namespace GenPrevis;

public partial class GenPrevisApp
{
    const double PoseKeyTolerance=.00001;

    IEnumerable<double> PoseCorrectionTimes(Entry entry,string channel="All")
    {
        var data=entry.Data.PoseCorrections;
        if(channel=="Root")return data.Root.Select(key=>key.Time);
        if(channel!="All")return data.Bones.TryGetValue(channel,out var keys)?keys.Select(key=>key.Time):Array.Empty<double>();
        return data.Bones.Values.SelectMany(keys=>keys).Select(key=>key.Time).Concat(data.Root.Select(key=>key.Time));
    }

    void SelectPoseCorrectionTimeline(Entry entry,string channel,double time)
    {
        _playing=false;_play.Text="▶";Select(entry);SetTime(time);StartPoseEditing();
        if(channel=="Root")SelectPoseBone(entry.NativeTrack?.RootBoneName??"Hips");
        else if(channel!="All")SelectPoseBone(channel);
    }

    /// <summary>The timeline owns the drag's one Undo snapshot.</summary>
    void RetimePoseCorrection(Entry entry,string channel,double from,double to)
    {
        if(!double.IsFinite(from)||!double.IsFinite(to)||Math.Abs(from-to)<PoseKeyTolerance)return;
        to=Math.Clamp(to,0,_duration);var data=entry.Data.PoseCorrections;
        if(channel is "All" or "Root")
        {
            var moving=data.Root.Where(key=>Math.Abs(key.Time-from)<PoseKeyTolerance).ToArray();
            if(moving.Length>0)
            {
                data.Root.RemoveAll(key=>Math.Abs(key.Time-from)<PoseKeyTolerance||Math.Abs(key.Time-to)<PoseKeyTolerance);
                foreach(var key in moving){key.Time=to;data.Root.Add(key);}data.Root.Sort((a,b)=>a.Time.CompareTo(b.Time));
            }
        }
        foreach(var pair in data.Bones.Where(pair=>channel=="All"||pair.Key==channel))
        {
            var moving=pair.Value.Where(key=>Math.Abs(key.Time-from)<PoseKeyTolerance).ToArray();if(moving.Length==0)continue;
            pair.Value.RemoveAll(key=>Math.Abs(key.Time-from)<PoseKeyTolerance||Math.Abs(key.Time-to)<PoseKeyTolerance);
            foreach(var key in moving){key.Time=to;pair.Value.Add(key);}pair.Value.Sort((a,b)=>a.Time.CompareTo(b.Time));
        }
        SetTime(_time);RefreshTimelineSize();
    }

    void RemovePoseCorrectionKey(Entry entry,string channel,double time)
    {
        if(!PoseCorrectionTimes(entry,channel).Any(value=>Math.Abs(value-time)<PoseKeyTolerance))return;
        PushUndo();var data=entry.Data.PoseCorrections;
        if(channel is "All" or "Root")data.Root.RemoveAll(key=>Math.Abs(key.Time-time)<PoseKeyTolerance);
        foreach(var pair in data.Bones.Where(pair=>channel=="All"||pair.Key==channel))pair.Value.RemoveAll(key=>Math.Abs(key.Time-time)<PoseKeyTolerance);
        foreach(string empty in data.Bones.Where(pair=>pair.Value.Count==0).Select(pair=>pair.Key).ToArray())data.Bones.Remove(empty);
        SetTime(_time);RefreshTimelineSize();
    }
}
