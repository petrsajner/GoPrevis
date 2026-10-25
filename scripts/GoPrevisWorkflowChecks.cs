using Godot;
using System;
using System.Linq;

namespace GoPrevis;

public partial class GoPrevisApp
{
    void RunLayerWorkflowChecks()
    {
        var entry=_entries.First(e=>e.Node is RiggedActor);var actor=(RiggedActor)entry.Node;
        entry.Data.Rotation=new float[]{0,90,0};entry.Data.PathOffset=new float[]{.2f,.1f,0};entry.Data.TransformAnimation=new();
        TransformLayerRuntime.Upsert(entry.Data.TransformAnimation.Position,0,Vector3.Zero);TransformLayerRuntime.Upsert(entry.Data.TransformAnimation.Position,8,new Vector3(.4f,.2f,0));
        TransformLayerRuntime.Upsert(entry.Data.TransformAnimation.Rotation,0,new Vector3(0,10,0));TransformLayerRuntime.Upsert(entry.Data.TransformAnimation.Rotation,8,new Vector3(0,50,0));
        TransformLayerRuntime.Upsert(entry.Data.TransformAnimation.Scale,0,Vector3.One);TransformLayerRuntime.Upsert(entry.Data.TransformAnimation.Scale,8,Vector3.One*1.2f);
        var blend=entry.Data.MotionClips[1];var skeleton=actor.GetNode<Skeleton3D>("PreparedHumanoid");
        for(int i=0;i<=16;i++)
        {
            double time=blend.Start+blend.BlendIn*i/16;SetTime(time);var path=ActorPathRuntime.Evaluate(entry.Data.Path,time);
            Vector3 facing=entry.Node.GlobalBasis*(skeleton.GetBonePoseRotation(0)*Vector3.Forward);facing.Y=0;facing=facing.Normalized();
            float angle=Mathf.DegToRad(90+Mathf.Lerp(10,50,(float)(time/8)));var expected=new Basis(Vector3.Up,angle)*path.Tangent;expected.Y=0;expected=expected.Normalized();
            Require(facing.Dot(expected)>.998f,"Final manual yaw stays additive through mocap blend "+i);
            var expectedPosition=path.Position+Vector(entry.Data.PathOffset)+new Vector3(.4f,.2f,0)*(float)(time/8);
            Require(entry.Node.Position.IsEqualApprox(expectedPosition),"Manual position is above path "+i);
        }
        SetTime(4);Require(entry.Node.Scale.IsEqualApprox(Vector3.One*1.1f),"Grouped scale keys evaluate after motion and path");
        var original=entry.Node.GlobalTransform;SetTime(7);SetTime(4);Require(original.IsEqualApprox(entry.Node.GlobalTransform),"Final transform layers deterministic under reverse seek");
        SelectCamera(_cameras[1]);SetTime(0);ChangeTransform("Position",0,-2.5f);CaptureSelectedTransformKey("Rotation");SetTime(4);ChangeTransform("Rotation",1,40);ChangeCameraLens(65);SetTime(4);
        Require(Math.Abs(_selectedCamera!.Rig.FocalLengthMm-65)<.01&&_selectedCamera.Animation.Rotation.Count>=2,"Camera object inspector edits and keys rotation and lens");
        var camera=_selectedCamera;var pose=camera.Rig.GlobalTransform;SetTime(1);SetTime(4);Require(pose.IsEqualApprox(camera.Rig.GlobalTransform),"Camera channel keys evaluate independently");
        Require(_objectList.ItemCount==_entries.Count+_cameras.Count,"Cameras are included in unified object list");
        var fox=_entries.First(e=>e.Data.ActorFamily=="fox");fox.Data.MotionClips[0].Duration=3;RebuildTrack(fox);DropMotion("fox-run",fox,fox.Node.Position);
        Require(fox.NativeTrack?.ClipCount==2&&fox.Data.MotionClips[1].SourceAnimation=="Run","Animal motion library applies native walk/run sequence");
        fox.Data.Path=new ActorPathData{Enabled=true,Duration=8,Points=new(){new float[]{2.3f,0,1.3f},new float[]{1.5f,0,2.5f},new float[]{-.5f,0,2.5f}}};fox.Data.Rotation=new float[]{0,25,0};
        TransformLayerRuntime.Upsert(fox.Data.TransformAnimation.Rotation,0,Vector3.Zero);TransformLayerRuntime.Upsert(fox.Data.TransformAnimation.Rotation,8,new Vector3(0,30,0));SetTime(3);
        var expectedFox=ActorPathRuntime.Evaluate(fox.Data.Path,3).Position;Require(fox.Node.Position.IsEqualApprox(expectedFox),"Animal path is applied outside native motion blend");
        var saved=Snapshot();Restore(saved,"");Require(_entries.First(e=>e.Node is RiggedActor).Data.TransformAnimation.Rotation.Count==2&&_cameras[1].LensKeys.Count>=2,"Transform and lens channels survive scene restore");
        Select(_entries.First(e=>e.Node is RiggedActor));SetTime(1.25);
    }
}
