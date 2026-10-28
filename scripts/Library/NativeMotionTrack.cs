using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GoPrevis;

/// <summary>
/// Sequences animations belonging to one imported rig. It samples and blends native local
/// poses first; it never writes to the outer scene-object wrapper used by path and manual layers.
/// </summary>
public sealed partial class NativeMotionTrack
{
    readonly Node _modelRoot;
    readonly AnimationPlayer _player;
    readonly Node3D[] _nodes;
    readonly Bone[] _bones;
    readonly int[] _roots;
    readonly Pose _rest, _result, _incoming, _source, _authoredRest;
    readonly Quaternion _restRootRotation;
    Clip[] _clips = Array.Empty<Clip>();
    ulong _clipAnimationStamp;
    readonly Dictionary<string,Cycle> _cycles=new(StringComparer.Ordinal);
    sealed record Cycle(Vector3[] Bones,Vector3 Node);

    readonly record struct Bone(Skeleton3D Skeleton, int Index);
    sealed class Pose
    {
        public readonly Vector3[] Position, Scale, NodePosition, NodeScale;
        public readonly Quaternion[] Rotation, NodeRotation;
        public Pose(int bones, int nodes)
        {
            Position=new Vector3[bones]; Scale=new Vector3[bones]; Rotation=new Quaternion[bones];
            NodePosition=new Vector3[nodes]; NodeScale=new Vector3[nodes]; NodeRotation=new Quaternion[nodes];
        }
    }
    sealed class Clip
    {
        public required string Name;
        public double Start, Duration, Offset, Speed, Blend;
        public double BlendLimit;
        public PropertyAnimationData Properties = new();
        public bool Loop, InPlace;
        public Vector3[] BoneAlignment=Array.Empty<Vector3>();
        public Vector3 NodeAlignment;
        public double End=>Start+Duration;
        public double SourceTime(double time)=>MotionClipClock.SourceTime(Properties,time,Start,Duration,Offset,Speed);
        public bool Loops(double time)=>MotionClipClock.Flag(Properties,"Loop",MotionClipClock.ParameterTime(time,Start,Duration),Loop);
        public bool IsInPlace(double time)=>MotionClipClock.Flag(Properties,"InPlace",MotionClipClock.ParameterTime(time,Start,Duration),InPlace);
    }

    public int ClipCount=>_clips.Length;
    public int PreparedSourceCount=>_cycles.Count;
    public Node ModelRoot=>_modelRoot;
    public double DurationSeconds=>_clips.Length==0?0:_clips.Max(c=>c.End);
    public Vector3 AnimatedForward { get; private set; } = Vector3.Back;
    public string Summary=>_clips.Length==0?"No native motion clips.":$"{_clips.Length} native clips · {_bones.Length} bones · deterministic local-pose blends";

    public NativeMotionTrack(Node modelRoot, AnimationPlayer player)
    {
        _modelRoot=modelRoot??throw new ArgumentNullException(nameof(modelRoot));
        _player=player??throw new ArgumentNullException(nameof(player));
        _nodes=Descendants(modelRoot).OfType<Node3D>().ToArray();
        var skeletons=Descendants(modelRoot).OfType<Skeleton3D>().ToArray();
        _bones=skeletons.SelectMany(s=>Enumerable.Range(0,s.GetBoneCount()).Select(i=>new Bone(s,i))).ToArray();
        if(_bones.Length==0)throw new FormatException("The imported model has no skeletal animation rig.");
        _roots=Enumerable.Range(0,_bones.Length).Where(i=>_bones[i].Skeleton.GetBoneParent(_bones[i].Index)<0).OrderBy(i=>BoneName(i).StartsWith("Control_",StringComparison.Ordinal)?1:0).ToArray();
        if(_roots.Length==0)throw new FormatException("The imported model has no root bone.");
        _player.Set("callback_mode_process",2); // Manual: the scene timeline is the only clock.
        _player.PlaybackDefaultBlendTime=0;
        _player.Stop(true);
        foreach(var skeleton in skeletons)skeleton.ResetBonePoses();
        _rest=new Pose(_bones.Length,_nodes.Length);_result=new Pose(_bones.Length,_nodes.Length);_incoming=new Pose(_bones.Length,_nodes.Length);_source=new Pose(_bones.Length,_nodes.Length);_authoredRest=new Pose(_bones.Length,_nodes.Length);
        Capture(_authoredRest);Copy(_authoredRest,_rest);PrepareEditableHierarchy();
        _roots=Enumerable.Range(0,_bones.Length).Where(i=>_bones[i].Skeleton.GetBoneParent(_bones[i].Index)<0).OrderBy(i=>BoneName(i).StartsWith("Control_",StringComparison.Ordinal)?1:0).ToArray();_restRootRotation=_rest.Rotation[_roots[0]];
        foreach(string name in _player.GetAnimationList())if(name!="RESET")
        {
            var animation=_player.GetAnimation(name);animation.LoopMode=Animation.LoopModeEnum.None;
            if(!double.IsFinite(animation.Length)||animation.Length<=0)continue;
            SampleAt(name,0,_result);SampleAt(name,animation.Length,_incoming);
            var roots=_roots.Select(root=>Horizontal(_incoming.Position[root]-_result.Position[root])).ToArray();
            _cycles[name]=new Cycle(roots,_nodes.Length>0?Horizontal(_incoming.NodePosition[0]-_result.NodePosition[0]):Vector3.Zero);
        }
        Apply(_rest);
    }

    public void SetClips(IReadOnlyList<MotionClipData> clips)
    {
        ArgumentNullException.ThrowIfNull(clips);
        var prepared=new List<Clip>();
        foreach(var data in clips.OrderBy(c=>c.Start))
        {
            string name=data.SourceAnimation??data.Name;
            if(string.IsNullOrWhiteSpace(name)||!_player.HasAnimation(name)||!_cycles.ContainsKey(name))throw new FormatException("This model has no playable animation named '"+name+"'. Drop a motion compatible with its rig.");
            if(!double.IsFinite(data.Start)||data.Start<0||!double.IsFinite(data.Duration)||data.Duration<=0||!double.IsFinite(data.Start+data.Duration))throw new FormatException("Motion start and duration must be finite; duration must be greater than zero.");
            if(!double.IsFinite(data.SourceOffset)||data.SourceOffset<0||!double.IsFinite(data.Speed)||data.Speed<=0||!double.IsFinite(data.BlendIn)||data.BlendIn<0)throw new FormatException("Motion offset, speed and blend time are invalid.");
            var clip=new Clip{Name=name,Start=data.Start,Duration=data.Duration,Offset=data.SourceOffset,Speed=data.Speed,Loop=data.Loop,InPlace=data.InPlace,BoneAlignment=new Vector3[_roots.Length],Properties=data.PropertyAnimation??new()};
            if(prepared.Count>0){clip.BlendLimit=Math.Min(clip.Duration,Math.Max(0,prepared[^1].End-clip.Start));clip.Blend=Math.Min(data.BlendIn,clip.BlendLimit);}
            prepared.Add(clip);
        }
        // Validation above is atomic; sampling below cannot encounter missing source animations.
        for(int i=0;i<prepared.Count;i++)
        {
            var clip=prepared[i];
            if(i==0)Copy(_rest,_result);else EvaluatePrepared(prepared.Take(i).ToArray(),clip.Start,_result);
            Sample(clip,clip.Start,_incoming);
            for(int root=0;root<_roots.Length;root++)clip.BoneAlignment[root]=Horizontal(_result.Position[_roots[root]]-_incoming.Position[_roots[root]]);
            if(_nodes.Length>0)clip.NodeAlignment=Horizontal(_result.NodePosition[0]-_incoming.NodePosition[0]);
        }
        _clips=prepared.ToArray();_clipAnimationStamp=ClipAnimationStamp();Evaluate(0);
    }

    public void Evaluate(double sceneSeconds, bool forceInPlace=false, PoseCorrectionAnimationData? corrections=null, StaticPoseData? staticPose=null)
    {
        if(!double.IsFinite(sceneSeconds))sceneSeconds=0;
        RefreshAnimatedAlignments();
        if(_clips.Length==0){Copy(_rest,_result);ApplyStaticPose(_result,staticPose);}else EvaluatePrepared(_clips,sceneSeconds,_result);
        if(forceInPlace)
        {
            foreach(int root in _roots){_result.Position[root].X=_rest.Position[root].X;_result.Position[root].Z=_rest.Position[root].Z;}
            if(_nodes.Length>0){_result.NodePosition[0].X=_rest.NodePosition[0].X;_result.NodePosition[0].Z=_rest.NodePosition[0].Z;}
        }
        Copy(_result,_source);ApplyCorrections(_result,corrections,sceneSeconds);Apply(_result);
        // Derive heading from the FINAL blended root, never by blending source headings separately.
        // Path alignment uses uncorrected source facing. A manual root turn is a
        // correction above locomotion and must not be cancelled by path heading.
        Quaternion rootDelta=(_source.Rotation[_roots[0]]*_restRootRotation.Inverse()).Normalized();
        Quaternion nodeDelta=_nodes.Length>0?(_source.NodeRotation[0]*_rest.NodeRotation[0].Inverse()).Normalized():Quaternion.Identity;
        var forward=Horizontal(nodeDelta*rootDelta*Vector3.Back);
        AnimatedForward=forward.LengthSquared()>.000001f?forward.Normalized():Vector3.Back;
    }

    void EvaluatePrepared(IReadOnlyList<Clip> clips,double seconds,Pose result,int count=int.MaxValue)
    {
        Sample(clips[0],seconds,result);ApplyAlignment(clips[0],result);
        for(int i=1;i<Math.Min(clips.Count,count);i++)
        {
            var clip=clips[i];if(seconds<clip.Start)break;
            Sample(clip,seconds,_incoming);ApplyAlignment(clip,_incoming);
            float weight=ClipWeight(clip,seconds);
            Blend(result,_incoming,weight);
        }
    }
    void Sample(Clip clip,double seconds,Pose output)
    {
        double t=clip.SourceTime(seconds);
        double length=_player.GetAnimation(clip.Name).Length;
        bool loop=clip.Loops(seconds);
        double loops=loop&&length>0?Math.Floor(t/length):0;
        t=length<=0?0:loop?((t%length)+length)%length:Math.Clamp(t,0,length);
        SampleAt(clip.Name,t,output);
        if(loops!=0){var cycle=_cycles[clip.Name];for(int i=0;i<_roots.Length;i++)output.Position[_roots[i]]+=cycle.Bones[i]*(float)loops;if(_nodes.Length>0)output.NodePosition[0]+=cycle.Node*(float)loops;}
        if(clip.IsInPlace(seconds))
        {
            foreach(int root in _roots){output.Position[root].X=_rest.Position[root].X;output.Position[root].Z=_rest.Position[root].Z;}
            if(_nodes.Length>0){output.NodePosition[0].X=_rest.NodePosition[0].X;output.NodePosition[0].Z=_rest.NodePosition[0].Z;}
        }
    }
    static float ClipWeight(Clip clip,double time)
    {
        double blend=MotionClipClock.BlendDuration(clip.Properties,time,clip.Start,clip.Duration,clip.Blend,clip.BlendLimit);
        return blend<=0?1:(float)Math.Clamp((time-clip.Start)/blend,0,1);
    }
    ulong ClipAnimationStamp()
    {
        ulong value=0;foreach(var clip in _clips)value=(value*1099511628211UL)^MotionClipClock.Fingerprint(clip.Properties);return value;
    }
    void RefreshAnimatedAlignments()
    {
        ulong stamp=ClipAnimationStamp();if(stamp==_clipAnimationStamp)return;
        for(int i=0;i<_clips.Length;i++)
        {
            var clip=_clips[i];if(i==0)Copy(_rest,_result);else EvaluatePrepared(_clips,clip.Start,_result,i);
            Sample(clip,clip.Start,_incoming);
            for(int root=0;root<_roots.Length;root++)clip.BoneAlignment[root]=Horizontal(_result.Position[_roots[root]]-_incoming.Position[_roots[root]]);
            if(_nodes.Length>0)clip.NodeAlignment=Horizontal(_result.NodePosition[0]-_incoming.NodePosition[0]);
        }
        _clipAnimationStamp=stamp;
    }
    void SampleAt(string animation,double seconds,Pose output)
    {
        ReadAuthoredAnimation(animation,seconds,output);ConvertAuthoredPose(output);
    }
    void ApplyAlignment(Clip clip,Pose pose)
    {
        for(int i=0;i<_roots.Length;i++)pose.Position[_roots[i]]+=clip.BoneAlignment[i];
        if(_nodes.Length>0)pose.NodePosition[0]+=clip.NodeAlignment;
    }
    void Capture(Pose pose)
    {
        for(int i=0;i<_bones.Length;i++){var b=_bones[i];pose.Position[i]=b.Skeleton.GetBonePosePosition(b.Index);pose.Rotation[i]=Safe(b.Skeleton.GetBonePoseRotation(b.Index));pose.Scale[i]=b.Skeleton.GetBonePoseScale(b.Index);}
        for(int i=0;i<_nodes.Length;i++){pose.NodePosition[i]=_nodes[i].Position;pose.NodeRotation[i]=Safe(_nodes[i].Quaternion);pose.NodeScale[i]=_nodes[i].Scale;}
    }
    void Apply(Pose pose)
    {
        _skinBounds=null;
        for(int i=0;i<_nodes.Length;i++){_nodes[i].Position=pose.NodePosition[i];_nodes[i].Quaternion=pose.NodeRotation[i];_nodes[i].Scale=pose.NodeScale[i];}
        for(int i=0;i<_bones.Length;i++){var b=_bones[i];b.Skeleton.SetBonePosePosition(b.Index,pose.Position[i]);b.Skeleton.SetBonePoseRotation(b.Index,pose.Rotation[i]);b.Skeleton.SetBonePoseScale(b.Index,pose.Scale[i]);}
    }
    static void Copy(Pose source,Pose target)
    {
        Array.Copy(source.Position,target.Position,source.Position.Length);Array.Copy(source.Rotation,target.Rotation,source.Rotation.Length);Array.Copy(source.Scale,target.Scale,source.Scale.Length);
        Array.Copy(source.NodePosition,target.NodePosition,source.NodePosition.Length);Array.Copy(source.NodeRotation,target.NodeRotation,source.NodeRotation.Length);Array.Copy(source.NodeScale,target.NodeScale,source.NodeScale.Length);
    }
    static void Blend(Pose result,Pose incoming,float weight)
    {
        for(int i=0;i<result.Position.Length;i++){result.Position[i]=result.Position[i].Lerp(incoming.Position[i],weight);result.Rotation[i]=Safe(result.Rotation[i].Slerp(incoming.Rotation[i],weight));result.Scale[i]=result.Scale[i].Lerp(incoming.Scale[i],weight);}
        for(int i=0;i<result.NodePosition.Length;i++){result.NodePosition[i]=result.NodePosition[i].Lerp(incoming.NodePosition[i],weight);result.NodeRotation[i]=Safe(result.NodeRotation[i].Slerp(incoming.NodeRotation[i],weight));result.NodeScale[i]=result.NodeScale[i].Lerp(incoming.NodeScale[i],weight);}
    }
    static Quaternion Safe(Quaternion q)=>q.LengthSquared()<.000001f?Quaternion.Identity:q.Normalized();
    static Vector3 Horizontal(Vector3 vector)=>new(vector.X,0,vector.Z);
    static IEnumerable<Node> Descendants(Node root){yield return root;foreach(Node child in root.GetChildren())foreach(var node in Descendants(child))yield return node;}
}
