using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Previs;

/// <summary>Real bundled Fox animation sequencing and layer-isolation regression probe.</summary>
public partial class NativeMotionTrackProbe : SceneTree
{
    int _checks;
    public override async void _Initialize()
    {
        Node3D? wrapper=null;
        try
        {
            var document=new GltfDocument();var state=new GltfState();
            string source=MotionCatalog.MaterializeSource(MotionCatalog.Find("fox-walk")!);
            Check(document.AppendFromFile(source,state)==Error.Ok,"bundled animal model loads");
            var model=document.GenerateScene(state);Check(model!=null,"bundled animal scene exists");
            wrapper=new Node3D{Position=new Vector3(3,0,2),RotationDegrees=new Vector3(0,35,0),Scale=Vector3.One*.01f};Root.AddChild(wrapper);wrapper.AddChild(model!);
            await ToSignal(this,SignalName.ProcessFrame);
            var player=Descendants(model!).OfType<AnimationPlayer>().First();var skeleton=Descendants(model!).OfType<Skeleton3D>().Single();
            var track=new NativeMotionTrack(model!,player);var wrapperPose=wrapper.Transform;
            Check(skeleton.GetBoneCount()==24&&track.PreparedSourceCount==3,"prepared animal rig and all three source clips");
            MotionClipData Clip(string name,double start=0,double duration=2,double blend=.5)=>new(){Name=name,SourceAnimation=name,MotionPath=MotionCatalog.FoxModelPath,Start=start,Duration=duration,BlendIn=blend,Loop=true};
            var a=Clip("Walk",0,2);var b=Clip("Run",1,2,1);
            track.SetClips(new[]{a});track.Evaluate(1.5);var walk=Capture(skeleton);
            track.SetClips(new[]{b});track.Evaluate(1.5);var run=Capture(skeleton);
            Check(walk.Zip(run).Any(pair=>Math.Abs(pair.First.Rotation.Dot(pair.Second.Rotation))<.999f),"walk and run have distinct captured bone poses");
            track.SetClips(new[]{a,b});Check(track.ClipCount==2&&Math.Abs(track.DurationSeconds-3)<.000001,"two native motion clips are sequenced");
            track.Evaluate(1.5);var middle=Capture(skeleton);
            for(int i=0;i<middle.Length;i++)
            {
                Check(Math.Abs(middle[i].Rotation.Dot(walk[i].Rotation.Slerp(run[i].Rotation,.5f)))>.99999f,"midblend quaternion matches independently sampled sources for bone "+i);
                Check(middle[i].Position.DistanceTo(walk[i].Position.Lerp(run[i].Position,.5f))<.0001f,"midblend position matches source poses for bone "+i);
            }
            var facing=track.AnimatedForward;
            Check(facing.IsFinite()&&Math.Abs(facing.Length()-1)<.00001&&Math.Abs(facing.Y)<.00001,"final blended facing is finite and horizontal");
            track.Evaluate(2.8);track.Evaluate(-1);track.Evaluate(1.5);Check(Equal(middle,Capture(skeleton))&&facing.IsEqualApprox(track.AnimatedForward),"native blending is deterministic under reverse seeks");
            Check(wrapper.Transform.IsEqualApprox(wrapperPose),"native sampling cannot override the outer path/manual transform layer");
            wrapper.RotationDegrees+=new Vector3(10,80,5);var manual=wrapper.Transform;track.Evaluate(1.7);Check(wrapper.Transform.IsEqualApprox(manual),"manual rotation remains above native animation across blend");
            track.Evaluate(1.5,true);foreach(int root in Enumerable.Range(0,skeleton.GetBoneCount()).Where(i=>skeleton.GetBoneParent(i)<0))
            {var position=skeleton.GetBonePosePosition(root);var rest=skeleton.GetBoneRest(root).Origin;Check(Math.Abs(position.X-rest.X)<.0001&&Math.Abs(position.Z-rest.Z)<.0001,"path mode suppresses native root XZ for bone "+root);}
            track.SetClips(new[]{a});track.Evaluate(.2);var loop=Capture(skeleton);track.Evaluate(.2+player.GetAnimation("Walk").Length);Check(Equal(loop,Capture(skeleton)),"native walk cycle loops at its source rate");
            a.Loop=false;a.Duration=8;track.SetClips(new[]{a});track.Evaluate(2);var end=Capture(skeleton);track.Evaluate(5);Check(Equal(end,Capture(skeleton)),"non-looping native clip holds its final source frame");
            a.Loop=true;a.Start=0;a.Duration=.5;b.Start=2;b.Duration=1;track.SetClips(new[]{a,b});track.Evaluate(.5);var gap=Capture(skeleton);track.Evaluate(1);Check(Equal(gap,Capture(skeleton)),"gap holds previous native pose");
            track.Evaluate(2.5);var beforeInvalid=Capture(skeleton);bool rejected=false;try{track.SetClips(new[]{Clip("Missing animation")});}catch(FormatException){rejected=true;}
            Check(rejected&&track.ClipCount==2&&Equal(beforeInvalid,Capture(skeleton)),"incompatible animation rejects atomically");
            Check(MotionCatalog.ForFamily("fox").Count()==3&&MotionCatalog.ForFamily("human").Count()>=2,"library contains human and animal motion cards");
            Check(MotionCatalog.Find("fox-walk")?.SourceAnimation=="Walk","animal library card identifies the real native clip");
            GD.Print($"NATIVE_MOTION_OK checks={_checks} bones={skeleton.GetBoneCount()} clips={track.ClipCount} sources={track.PreparedSourceCount}");
            wrapper.QueueFree();await ToSignal(this,SignalName.ProcessFrame);Quit(0);
        }
        catch(Exception exception){GD.PushError("NATIVE_MOTION_FAILED "+exception);wrapper?.QueueFree();Quit(1);}
    }
    readonly record struct BonePose(Vector3 Position,Quaternion Rotation,Vector3 Scale);
    static BonePose[] Capture(Skeleton3D skeleton)=>Enumerable.Range(0,skeleton.GetBoneCount()).Select(i=>new BonePose(skeleton.GetBonePosePosition(i),skeleton.GetBonePoseRotation(i),skeleton.GetBonePoseScale(i))).ToArray();
    static bool Equal(BonePose[] a,BonePose[] b)=>a.Length==b.Length&&a.Zip(b).All(pair=>pair.First.Position.IsEqualApprox(pair.Second.Position)&&Math.Abs(pair.First.Rotation.Dot(pair.Second.Rotation))>.999999f&&pair.First.Scale.IsEqualApprox(pair.Second.Scale));
    void Check(bool result,string message){if(!result)throw new InvalidOperationException(message);_checks++;}
    static IEnumerable<Node> Descendants(Node root){yield return root;foreach(Node child in root.GetChildren())foreach(var node in Descendants(child))yield return node;}
}
