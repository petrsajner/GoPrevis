using Godot;
using System;
using System.IO;

namespace GoPrevis;

/// <summary>Run with Godot --headless --path . --script res://scripts/Mocap/MocapProbe.cs.</summary>
public partial class MocapProbe : SceneTree
{
    public override async void _Initialize()
    {
        try
        {
            int checks = BvhChecks.Run();
            var actor = new RiggedActor { Name = "MocapProbeActor" };
            Root.AddChild(actor);
            string synthetic = ProjectSettings.GlobalizePath("res://samples/synthetic-walk-gesture.bvh");
            actor.LoadBvh(synthetic);
            Check(actor.JointCount == 21 && Math.Abs(actor.DurationSeconds - 4) < 0.00001, "synthetic clip metadata");
            var skeleton = actor.GetNode<Skeleton3D>("PreparedHumanoid");
            int arm = skeleton.FindBone("RightUpperArm");
            actor.ApplyTime(0.25, false);
            Quaternion first = skeleton.GetBonePoseRotation(arm);
            Vector3 root = actor.RootMotionOffset;
            actor.ApplyTime(2.5, false);
            Check(Math.Abs(first.Dot(skeleton.GetBonePoseRotation(arm))) < 0.999f, "visible arm pose changes");
            actor.ApplyTime(0.25, false);
            Check(Math.Abs(first.Dot(skeleton.GetBonePoseRotation(arm))) > 0.999999f && root.IsEqualApprox(actor.RootMotionOffset), "actor scrub determinism");
            actor.ApplyTime(1.0, false, true);
            Check(Math.Abs(actor.RootMotionOffset.X) < 0.0001f && Math.Abs(actor.RootMotionOffset.Z) < 0.0001f, "in-place root filtering");
            try { actor.LoadBvh(Path.Combine(Path.GetDirectoryName(synthetic)!, "missing-test-motion.bvh")); }
            catch (FormatException) { }
            Check(actor.MotionName == "synthetic-walk-gesture", "failed import preserves playable motion");
            string real = ProjectSettings.GlobalizePath("res://samples/real/CMU_02_01_Walk.bvh");
            if (File.Exists(real))
            {
                actor.LoadBvh(real, 0.055f);
                Check(actor.JointCount == 31 && actor.ImportSummary.Contains("21/21"), "real CMU import mapping");
                actor.ApplyTime(0.2, false);
                Quaternion walkPose = skeleton.GetBonePoseRotation(skeleton.FindBone("LeftUpperLeg"));
                actor.ApplyTime(1.0, false);
                Check(Math.Abs(walkPose.Dot(skeleton.GetBonePoseRotation(skeleton.FindBone("LeftUpperLeg")))) < 0.9999f, "real capture animates the prepared rig");
                GD.Print("REAL_MOCAP_OK " + actor.ImportSummary);
            }
            await ToSignal(this, SignalName.ProcessFrame);
            await ToSignal(this, SignalName.ProcessFrame);
            GD.Print($"MOCAP_PROBE_OK parser_checks={checks} skeleton_bones={skeleton.GetBoneCount()} motion={actor.MotionName}");
            actor.QueueFree();
            await ToSignal(this, SignalName.ProcessFrame);
            Quit();
        }
        catch (Exception exception)
        {
            GD.PushError("MOCAP_PROBE_FAILED " + exception);
            Quit(1);
        }
    }

    private static void Check(bool passed, string description)
    {
        if (!passed) throw new InvalidOperationException("Mocap runtime check failed: " + description);
    }
}
