using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace GenPrevis;

/// <summary>Headless end-to-end BVH track regression probe, run with --script res://scripts/Testing/MotionTrackProbe.cs.</summary>
public partial class MotionTrackProbe : SceneTree
{
    private int _checks;

    public override async void _Initialize()
    {
        RiggedActor? actor = null;
        string temp = Path.Combine(Path.GetTempPath(), "previs-motion-track-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            string firstPath = Path.Combine(temp, "move-x.bvh"), secondPath = Path.Combine(temp, "turn-move-z.bvh");
            File.WriteAllText(firstPath, Synthetic(0, Vector3.Right));
            File.WriteAllText(secondPath, Synthetic(90, Vector3.Back * 2));
            actor = new RiggedActor { Name = "MotionTrackProbeActor" };
            Root.AddChild(actor);
            MotionClipData Clip(string path, double start, double duration, double blend = 1) => new()
            {
                MotionPath = path, Name = Path.GetFileNameWithoutExtension(path), Start = start,
                Duration = duration, BlendIn = blend, Loop = true, SourceScale = 1
            };
            var a = Clip(firstPath, 0, 4); var b = Clip(secondPath, 3, 4);
            actor.SetMotionClips(new[] { a, b });
            var skeleton = actor.GetNode<Skeleton3D>("PreparedHumanoid");
            int hip = skeleton.FindBone("Hips");
            Check(actor.MotionTrackClipCount == 2 && actor.MotionTrackPreparedSourceCount == 2 && actor.MotionTrackDurationSeconds == 7, "prepared track metadata");
            actor.EvaluateTimeline(3);
            Vector3 boundary = actor.RootMotionOffset;
            Check(HorizontalClose(boundary, new Vector3(3, 0, 0)), "incoming root aligned at overlap start");
            Quaternion from = skeleton.GetBonePoseRotation(hip);
            actor.EvaluateTimeline(4);
            Quaternion to = skeleton.GetBonePoseRotation(hip);
            Check(Math.Abs(from.Dot(to)) < .8f, "two clips have distinct hip orientations");
            actor.EvaluateTimeline(3.5);
            Quaternion middle = skeleton.GetBonePoseRotation(hip);
            Check(Math.Abs(middle.Dot(from.Slerp(to, .5f))) > .999999f, "exact midblend uses quaternion slerp");
            Check(HorizontalClose(actor.RootMotionOffset, new Vector3(3.25f, 0, .5f)), "crossfade interpolates independently sampled continuous roots");
            Vector3 midRoot = actor.RootMotionOffset, midFacing = actor.AnimatedForward;
            Check(Math.Abs(midFacing.Length() - 1) < .00001f && Math.Abs(midFacing.Y) < .00001f, "blended source facing is normalized and horizontal");
            actor.EvaluateTimeline(3 - .00001);
            Check((actor.RootMotionOffset - boundary).Length() < .0001f, "root continuous before incoming start");
            actor.EvaluateTimeline(3 + .00001);
            Check((actor.RootMotionOffset - boundary).Length() < .0001f, "root continuous after incoming start");
            actor.EvaluateTimeline(4 - .00001); Vector3 beforeEnd = actor.RootMotionOffset;
            actor.EvaluateTimeline(4 + .00001);
            Check((beforeEnd - actor.RootMotionOffset).Length() < .0001f, "root continuous at blend completion");
            actor.EvaluateTimeline(6.6); actor.EvaluateTimeline(-5); actor.EvaluateTimeline(3.5);
            Check(midRoot.IsEqualApprox(actor.RootMotionOffset) && Math.Abs(middle.Dot(skeleton.GetBonePoseRotation(hip))) > .999999f && midFacing.IsEqualApprox(actor.AnimatedForward), "random access is deterministic for root, pose and facing");
            actor.EvaluateTimeline(3.5, true);
            Check(Math.Abs(actor.RootMotionOffset.X) < .00001f && Math.Abs(actor.RootMotionOffset.Z) < .00001f, "path override removes all accumulated horizontal root displacement");
            // A source removed after preparation proves that evaluation never goes back to disk.
            File.Delete(firstPath); File.Delete(secondPath);
            actor.EvaluateTimeline(1); actor.EvaluateTimeline(5); actor.EvaluateTimeline(3.5);
            Check(midRoot.IsEqualApprox(actor.RootMotionOffset), "prepared track plays after original source files are removed");

            a.Start = 2; a.Duration = 2; a.SourceOffset = 1; a.Speed = 2; a.Loop = false;
            actor.SetMotionClips(new[] { a });
            actor.EvaluateTimeline(1); Vector3 preRoll = actor.RootMotionOffset;
            actor.EvaluateTimeline(2);
            Check(preRoll.IsEqualApprox(actor.RootMotionOffset), "before first clip holds trimmed source start");
            actor.EvaluateTimeline(2.5);
            Check(HorizontalClose(actor.RootMotionOffset, Vector3.Right), "source offset and speed evaluated relative to clip start");
            actor.EvaluateTimeline(4); Vector3 endpoint = actor.RootMotionOffset;
            actor.EvaluateTimeline(100);
            Check(endpoint.IsEqualApprox(actor.RootMotionOffset), "after last clip holds endpoint");
            Check(Math.Abs(endpoint.X - 3) < .0001f, "non-loop source clamps to final source sample");

            a.Start = 0; a.Duration = 12; a.SourceOffset = 0; a.Speed = 1; a.Loop = true;
            actor.SetMotionClips(new[] { a });
            actor.EvaluateTimeline(4.99999); Vector3 loopBoundary = actor.RootMotionOffset;
            actor.EvaluateTimeline(5.00001);
            Check((loopBoundary - actor.RootMotionOffset).Length() < .0001f, "looping root stays continuous at the source wrap");
            actor.EvaluateTimeline(7);
            Check(HorizontalClose(actor.RootMotionOffset, Vector3.Right * 7), "looping roots accumulate displacement across cycles");
            actor.EvaluateTimeline(100);
            Check(HorizontalClose(actor.RootMotionOffset, Vector3.Right * 12), "looping source still holds at the timeline clip endpoint");

            a.Start = 0; a.Duration = 2; a.SourceOffset = 0; a.Speed = 1; a.Loop = true;
            b.Start = 4; b.Duration = 2; b.BlendIn = 5;
            actor.SetMotionClips(new[] { a, b });
            actor.EvaluateTimeline(2); Vector3 gapRoot = actor.RootMotionOffset; Quaternion gapPose = skeleton.GetBonePoseRotation(hip);
            actor.EvaluateTimeline(3);
            Check(gapRoot.IsEqualApprox(actor.RootMotionOffset) && Math.Abs(gapPose.Dot(skeleton.GetBonePoseRotation(hip))) > .999999f, "gap holds previous endpoint pose and root");
            actor.EvaluateTimeline(4);
            Check(gapRoot.IsEqualApprox(actor.RootMotionOffset), "gap transition root stays continuous");
            Check(Math.Abs(gapPose.Dot(skeleton.GetBonePoseRotation(hip))) < .8f, "no crossfade is invented across a gap");
            actor.EvaluateTimeline(4.5);
            Check(HorizontalClose(actor.RootMotionOffset, new Vector3(2, 0, 1)), "incoming root advances from held gap endpoint");
            Check(actor.MotionTrackPreparedSourceCount == 2, "editing trims and timing reuses prepared source cache");

            actor.EvaluateTimeline(4.5); Vector3 retainedRoot = actor.RootMotionOffset;
            int retainedCache = actor.MotionTrackPreparedSourceCount;
            bool rejected = false;
            try { actor.SetMotionClips(new[] { a, Clip(Path.Combine(temp, "missing.bvh"), 1, 2) }); }
            catch (FormatException) { rejected = true; }
            Check(rejected && actor.MotionTrackClipCount == 2 && actor.MotionTrackPreparedSourceCount == retainedCache && retainedRoot.IsEqualApprox(actor.RootMotionOffset), "failed source preparation preserves displayed pose, playable track and cache");
            rejected = false;
            var invalid = Clip(firstPath, 0, 1); invalid.Speed = double.NaN;
            try { actor.SetMotionClips(new[] { invalid }); } catch (FormatException) { rejected = true; }
            Check(rejected && actor.MotionTrackClipCount == 2, "invalid numeric clip input rejected atomically");

            a.InPlace = true; b.InPlace = true; b.Start = 1; b.BlendIn = .5;
            actor.SetMotionClips(new[] { a, b }); actor.EvaluateTimeline(2);
            Check(HorizontalClose(actor.RootMotionOffset, Vector3.Zero), "per-clip in-place filtering stays in place through aligned clips");

            string synthetic = ProjectSettings.GlobalizePath("res://samples/synthetic-walk-gesture.bvh");
            string cmu = ProjectSettings.GlobalizePath("res://samples/real/CMU_02_01_Walk.bvh");
            if (File.Exists(cmu))
            {
                var human = Clip(synthetic, 0, 3); human.SourceScale = .01f;
                var capture = Clip(cmu, 2, 4); capture.SourceScale = .055f;
                actor.SetMotionClips(new[] { human, capture }); actor.EvaluateTimeline(2.5);
                var capturePose = Enumerable.Range(0, skeleton.GetBoneCount()).Select(skeleton.GetBonePose).ToArray();
                var captureRoot = actor.RootMotionOffset;
                actor.EvaluateTimeline(5); actor.EvaluateTimeline(.5); actor.EvaluateTimeline(2.5);
                Check(captureRoot.IsEqualApprox(actor.RootMotionOffset) && capturePose.Select((pose, index) => pose.IsEqualApprox(skeleton.GetBonePose(index))).All(equal => equal), "real CMU plus synthetic crossfade remains deterministic");
                GD.Print("MOTION_TRACK_REAL_CMU_OK mixed_sources=2");
            }
            actor.SetMotionClips(Array.Empty<MotionClipData>());
            Check(actor.MotionTrackClipCount == 0 && actor.RootMotionOffset.IsEqualApprox(Vector3.Zero), "empty track clears pose and root");
            actor.QueueFree(); actor = null;
            await ToSignal(this, SignalName.ProcessFrame);
            GD.Print($"MOTION_TRACK_PROBE_OK checks={_checks}");
            Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError("MOTION_TRACK_PROBE_FAILED " + exception); actor?.QueueFree(); Quit(1);
        }
        finally
        {
            // Only the exact unique test directory created above is removed.
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }

    private void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
    private static bool HorizontalClose(Vector3 actual, Vector3 expected)
        => Math.Abs(actual.X - expected.X) < .0001f && Math.Abs(actual.Z - expected.Z) < .0001f;

    private static string Synthetic(float yaw, Vector3 velocity)
    {
        const string hierarchy = """
            HIERARCHY
            ROOT Hips { OFFSET 0 0 0 CHANNELS 6 Xposition Yposition Zposition Yrotation Xrotation Zrotation
              JOINT Spine { OFFSET 0 0.14 0 CHANNELS 3 Xrotation Yrotation Zrotation
                JOINT Chest { OFFSET 0 0.18 0 CHANNELS 3 Xrotation Yrotation Zrotation
                  JOINT Neck { OFFSET 0 0.19 0 CHANNELS 3 Xrotation Yrotation Zrotation
                    JOINT Head { OFFSET 0 0.12 0 CHANNELS 3 Xrotation Yrotation Zrotation End Site { OFFSET 0 0.12 0 } }
                  }
                }
              }
              JOINT LeftUpperLeg { OFFSET 0.105 -0.035 0 CHANNELS 3 Xrotation Yrotation Zrotation
                JOINT LeftLowerLeg { OFFSET 0 -0.410 0 CHANNELS 3 Xrotation Yrotation Zrotation
                  JOINT LeftFoot { OFFSET 0 -0.405 0 CHANNELS 3 Xrotation Yrotation Zrotation End Site { OFFSET 0 0 -0.14 } }
                }
              }
              JOINT RightUpperLeg { OFFSET -0.105 -0.035 0 CHANNELS 3 Xrotation Yrotation Zrotation
                JOINT RightLowerLeg { OFFSET 0 -0.410 0 CHANNELS 3 Xrotation Yrotation Zrotation
                  JOINT RightFoot { OFFSET 0 -0.405 0 CHANNELS 3 Xrotation Yrotation Zrotation End Site { OFFSET 0 0 -0.14 } }
                }
              }
            }
            MOTION
            Frames: 5
            Frame Time: 1
            """;
        var text = new StringBuilder(hierarchy); text.AppendLine();
        for (int frame = 0; frame < 5; frame++)
        {
            Vector3 p = new Vector3(100, 0, -50) + velocity * frame;
            float[] channels = new[] { p.X, p.Y, p.Z, yaw, 0f, 0f }.Concat(Enumerable.Repeat(0f, 30)).ToArray();
            text.AppendLine(string.Join(" ", channels.Select(value => value.ToString("R", CultureInfo.InvariantCulture))));
        }
        return text.ToString();
    }
}
