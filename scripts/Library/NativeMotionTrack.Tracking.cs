using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GoPrevis;

public sealed partial class NativeMotionTrack
{
    sealed record TrackingBinding(int Track,Animation.TrackType Type,int Bone,int Node);
    readonly Dictionary<string,TrackingBinding[]> _trackingBindings=new(StringComparer.Ordinal);
    TrackingBinding[] TrackingBindings(string name)
    {
        if(_trackingBindings.TryGetValue(name,out var bindings))return bindings;
        var animation=_player.GetAnimation(name);var result=new List<TrackingBinding>();var animationRoot=_player.GetNode(_player.RootNode);
        for(int track=0;track<animation.GetTrackCount();track++)
        {
            if(!animation.TrackIsEnabled(track))continue;var type=animation.TrackGetType(track);
            if(type is not(Animation.TrackType.Position3D or Animation.TrackType.Rotation3D or Animation.TrackType.Scale3D))continue;
            var path=animation.TrackGetPath(track);var node=animationRoot.GetNodeOrNull(new NodePath(path.GetConcatenatedNames()));
            int bone=-1,index=Array.FindIndex(_nodes,item=>item==node);
            if(node is Skeleton3D skeleton&&path.GetSubNameCount()>0)
            {int local=skeleton.FindBone(path.GetSubName(0));bone=Array.FindIndex(_bones,item=>item.Skeleton==skeleton&&item.Index==local);}
            if(bone>=0||index>=0)result.Add(new(track,type,bone,index));
        }
        return _trackingBindings[name]=result.ToArray();
    }
    void SampleTrackingClip(Clip clip,double seconds,Pose output)
    {
        double t=clip.Offset+Math.Clamp(seconds-clip.Start,0,clip.Duration)*clip.Speed;var animation=_player.GetAnimation(clip.Name);double length=animation.Length;
        double loops=clip.Loop&&length>0?Math.Floor(t/length):0;t=length<=0?0:clip.Loop?((t%length)+length)%length:Math.Clamp(t,0,length);
        ReadAuthoredAnimation(clip.Name,t,output);ConvertAuthoredPose(output);
        if(loops!=0){var cycle=_cycles[clip.Name];for(int i=0;i<_roots.Length;i++)output.Position[_roots[i]]+=cycle.Bones[i]*(float)loops;if(_nodes.Length>0)output.NodePosition[0]+=cycle.Node*(float)loops;}
        if(clip.InPlace){foreach(int root in _roots){output.Position[root].X=_rest.Position[root].X;output.Position[root].Z=_rest.Position[root].Z;}if(_nodes.Length>0){output.NodePosition[0].X=_rest.NodePosition[0].X;output.NodePosition[0].Z=_rest.NodePosition[0].Z;}}
        ApplyAlignment(clip,output);
    }
    void ReadAuthoredAnimation(string name,double t,Pose output)
    {
        Copy(_authoredRest,output);var animation=_player.GetAnimation(name);
        foreach(var binding in TrackingBindings(name))
        {
            if(binding.Type==Animation.TrackType.Position3D)
            {var value=animation.PositionTrackInterpolate(binding.Track,t);if(binding.Bone>=0)output.Position[binding.Bone]=value;else output.NodePosition[binding.Node]=value;}
            else if(binding.Type==Animation.TrackType.Rotation3D)
            {var value=Safe(animation.RotationTrackInterpolate(binding.Track,t));if(binding.Bone>=0)output.Rotation[binding.Bone]=value;else output.NodeRotation[binding.Node]=value;}
            else
            {var value=animation.ScaleTrackInterpolate(binding.Track,t);if(binding.Bone>=0)output.Scale[binding.Bone]=value;else output.NodeScale[binding.Node]=value;}
        }
    }
    Transform3D TrackingNodeTransform(Node3D node,Pose pose)
    {
        int index=Array.IndexOf(_nodes,node);var local=index<0?node.Transform:new Transform3D(new Basis(pose.NodeRotation[index]).Scaled(pose.NodeScale[index]),pose.NodePosition[index]);
        var parent=node.GetParentNode3D();return parent==null||node==_modelRoot?local:TrackingNodeTransform(parent,pose)*local;
    }
    /// <summary>Reads native animation tracks directly. It never seeks an AnimationPlayer or changes bone/node transforms.</summary>
    public (Vector3 Root,Vector3 Facing) SampleTrackingMotion(double time,bool forceInPlace,PoseCorrectionAnimationData? corrections,StaticPoseData? staticPose)
    {
        var result=new Pose(_bones.Length,_nodes.Length);var incoming=new Pose(_bones.Length,_nodes.Length);
        if(_clips.Length==0){Copy(_rest,result);ApplyStaticPose(result,staticPose);}
        else
        {
            SampleTrackingClip(_clips[0],time,result);
            for(int i=1;i<_clips.Length;i++){var clip=_clips[i];if(time<clip.Start)break;SampleTrackingClip(clip,time,incoming);Blend(result,incoming,clip.Blend<=0?1:(float)Math.Clamp((time-clip.Start)/clip.Blend,0,1));}
        }
        if(forceInPlace){foreach(int root in _roots){result.Position[root].X=_rest.Position[root].X;result.Position[root].Z=_rest.Position[root].Z;}if(_nodes.Length>0){result.NodePosition[0].X=_rest.NodePosition[0].X;result.NodePosition[0].Z=_rest.NodePosition[0].Z;}}
        Quaternion rootDelta=(result.Rotation[_roots[0]]*_restRootRotation.Inverse()).Normalized();
        Quaternion nodeDelta=_nodes.Length>0?(result.NodeRotation[0]*_rest.NodeRotation[0].Inverse()).Normalized():Quaternion.Identity;
        var forward=Horizontal(nodeDelta*rootDelta*Vector3.Back);forward=forward.LengthSquared()>.000001f?forward.Normalized():Vector3.Back;
        ApplyCorrections(result,corrections,time);
        var rootBone=_bones[_roots[0]];var current=TrackingNodeTransform(rootBone.Skeleton,result)*result.Position[_roots[0]];var rest=TrackingNodeTransform(rootBone.Skeleton,_rest)*_rest.Position[_roots[0]];
        return(current-rest,forward);
    }
}
