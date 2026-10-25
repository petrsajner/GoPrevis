using Godot;
using System;

namespace GoPrevis;

/// <summary>Headless parser checks that exercise format handling, rotation order, and absolute sampling.</summary>
public static class BvhChecks
{
    public static int Run()
    {
        const string text = """
            HIERARCHY
            ROOT mixamorig:Hips{
              OFFSET 0 100 0
              CHANNELS 6 Xposition Yposition Zposition Zrotation Xrotation Yrotation
              JOINT Chest { OFFSET 0 20 0 CHANNELS 3 Yrotation Zrotation Xrotation
                End Site {OFFSET 0 10 0}
              }
            }
            MOTION
            Frames : 2
            Frame Time: 0.5
            0 0 0 90 90 0 0 0 0
            2 0 -4 90 90 0 90 0 0
            """;
        BvhMotion motion = BvhMotion.Parse(text);
        Check(motion.FrameCount == 2 && motion.AnimatedJointCount == 2 && motion.Joints.Count == 3 && motion.ChannelCount == 9, "hierarchy and End Site parsing");
        Check(Math.Abs(motion.SourceFps - 2) < 0.000001 && Math.Abs(motion.DurationSeconds - 1) < 0.000001, "source timebase");
        var pose = new Quaternion[motion.Joints.Count];
        motion.Sample(0, false, pose);
        Quaternion expected = new Quaternion(Vector3.Back, Mathf.Pi / 2) * new Quaternion(Vector3.Right, Mathf.Pi / 2);
        Check(Math.Abs(pose[0].Dot(expected)) > 0.99999f, "declared intrinsic rotation order");
        motion.Sample(0.25, false, pose);
        Quaternion mid = (expected * new Quaternion(Vector3.Up, Mathf.Pi / 4)).Normalized();
        Check(Math.Abs(pose[1].Dot(mid)) > 0.99999f, "quaternion interpolation and hierarchy");
        Quaternion remembered = pose[1];
        motion.Sample(0.499, false, pose);
        motion.Sample(0.25, false, pose);
        Check(Math.Abs(pose[1].Dot(remembered)) > 0.999999f, "absolute scrub determinism");
        Vector3[] roots = motion.BuildRootCurve(0);
        Check(motion.SampleRootCurve(roots, 0.25, false).IsEqualApprox(new Vector3(1, 0, -2)), "root interpolation");
        Check(motion.SampleRootCurve(roots, 5, false).IsEqualApprox(new Vector3(2, 0, -4)), "non-looping end clamp");
        Check(motion.SampleRootCurve(roots, 1.25, true).IsEqualApprox(new Vector3(5, 0, -10)), "continuous looping root displacement");
        Check(motion.SampleRootCurve(roots, -0.25, true).IsEqualApprox(new Vector3(-1, 0, 2)), "negative looping time");
        Reject(text.Replace("Frame Time: 0.5", "Frame Time: 0"), "zero frame time");
        Reject(text[..text.LastIndexOf("2 0 -4", StringComparison.Ordinal)], "truncated frame data");
        Reject(text + "\n7", "unexpected extra values");
        Reject(text.Replace("Zrotation Xrotation Yrotation", "Zrotation Xrotation Xrotation"), "duplicate channel");
        Reject(text.Replace("90 90 0 90 0 0", "NaN 90 0 90 0 0"), "non-finite motion");
        return 14;
    }

    private static void Check(bool passed, string description)
    {
        if (!passed) throw new InvalidOperationException("BVH check failed: " + description);
    }

    private static void Reject(string text, string description)
    {
        try { BvhMotion.Parse(text); }
        catch (FormatException) { return; }
        throw new InvalidOperationException("BVH check failed: accepted " + description);
    }
}
