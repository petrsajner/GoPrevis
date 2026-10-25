using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace GoPrevis;

/// <summary>Run with Godot --headless --path . --script res://scripts/Testing/TransformLayerProbe.cs.</summary>
public partial class TransformLayerProbe : SceneTree
{
    public override void _Initialize()
    {
        try { GD.Print($"TRANSFORM_LAYER_OK checks={RunChecks()}"); Quit(0); }
        catch (Exception ex) { GD.PushError("TRANSFORM_LAYER_FAILED " + ex); Quit(1); }
    }

    public static int RunChecks()
    {
        int count = 0;
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Transform layer check failed: " + message); count++; }
        static bool Near(Vector3 a, Vector3 b, float tolerance = .0002f) => a.DistanceTo(b) <= tolerance;
        static bool SameAngle(float a, float b, float tolerance = .001f) => Math.Abs(Mathf.Wrap(a - b, -180, 180)) < tolerance;
        static TransformKeyData Key(double time, params float[] value) => new() { Time = time, Value = value };
        static Vector3 Heading(float degrees) => new(Mathf.Sin(Mathf.DegToRad(degrees)), 0, Mathf.Cos(Mathf.DegToRad(degrees)));
        var empty = new TransformAnimationData();
        var first = TransformLayerRuntime.Evaluate(new Vector3(2, 0, 3), new Vector3(5, 25, 8), new Vector3(2, 3, 4), empty, null, 2, Vector3.Forward);
        Check(Near(first.Position, new Vector3(2, 0, 3)) && Near(first.RotationDegrees, new Vector3(5, 25, 8)) && Near(first.Scale, new Vector3(2, 3, 4)), "empty keys retain complete base transform");

        var values = new List<TransformKeyData> { Key(4, 4, 8, 12), Key(0, 0, 0, 0) };
        Check(Near(TransformLayerRuntime.EvaluateVector(values, 2, Vector3.Zero), new Vector3(2, 4, 6)), "all three position axes interpolate together");
        Check(Near(TransformLayerRuntime.EvaluateVector(values, -10, Vector3.One), Vector3.Zero), "before-first hold");
        Check(Near(TransformLayerRuntime.EvaluateVector(values, 100, Vector3.Zero), new Vector3(4, 8, 12)), "after-last hold");
        Check(Near(TransformLayerRuntime.EvaluateVector(values, 4, Vector3.Zero), new Vector3(4, 8, 12)), "exact key value");
        values.Add(Key(4, 6, 9, 15));
        Check(Near(TransformLayerRuntime.EvaluateVector(values, 4, Vector3.Zero), new Vector3(6, 9, 15)), "last duplicate timestamp wins");
        Check(values[0].Time == 4 && values[1].Time == 0, "evaluation does not reorder source data");
        Check(Near(TransformLayerRuntime.EvaluateVector(null, 3, Vector3.One), Vector3.One), "missing channel fallback");
        Check(Near(TransformLayerRuntime.EvaluateVector(new List<TransformKeyData> { Key(0, float.NaN, 0, 0), Key(1, 1, 2) }, 1, Vector3.One), Vector3.One), "invalid serialized values ignored");
        Check(Near(TransformLayerRuntime.EvaluateVector(values, double.NaN, Vector3.One), Vector3.Zero), "NaN time uses frame zero");
        Check(Near(TransformLayerRuntime.EvaluateVector(values, double.PositiveInfinity, Vector3.Zero), new Vector3(6, 9, 15)), "infinite forward seek holds last");

        var angles = new List<TransformKeyData> { Key(0, 350, 170, -170), Key(2, 10, -170, 170) };
        Vector3 angularMidpoint = TransformLayerRuntime.EvaluateVector(angles, 1, Vector3.Zero, true);
        Check(SameAngle(angularMidpoint.X, 0) && SameAngle(angularMidpoint.Y, 180) && SameAngle(angularMidpoint.Z, 180), "rotation interpolation uses each component's short arc");
        var captured = new List<TransformKeyData>();
        TransformKeyData capturedKey = TransformLayerRuntime.Upsert(captured, 1, new Vector3(1, 2, 3)); string id = capturedKey.Id;
        TransformLayerRuntime.Upsert(captured, 1.0000001, new Vector3(4, 5, 6));
        Check(captured.Count == 1 && captured[0].Id == id && Near(new Vector3(captured[0].Value[0], captured[0].Value[1], captured[0].Value[2]), new Vector3(4, 5, 6)), "upsert replaces one XYZ key and preserves identity");
        TransformLayerRuntime.Upsert(captured, 0, Vector3.Zero);
        Check(captured[0].Time == 0 && captured[1].Time > .9, "capture ordering is chronological");
        Check(TransformLayerRuntime.RemoveAtTime(captured, 0) && captured.Count == 1, "remove parameter key at time");
        Check(TransformLayerRuntime.Remove(captured, id) && captured.Count == 0, "remove parameter key by id");
        bool rejected = false; try { TransformLayerRuntime.Upsert(captured, -1, Vector3.Zero); } catch (FormatException) { rejected = true; }
        Check(rejected && captured.Count == 0, "negative capture rejected atomically");
        rejected = false; try { TransformLayerRuntime.Upsert(captured, 1, new Vector3(float.NaN, 0, 0)); } catch (FormatException) { rejected = true; }
        Check(rejected && captured.Count == 0, "non-finite capture rejected atomically");

        var path = new ActorPathData
        {
            Enabled = true, OrientToPath = true, Duration = 6,
            Points = new() { new float[] { 0, 0, 0 }, new float[] { 3, 0, 3 }, new float[] { 6, 0, 0 } }
        };
        var keys = new TransformAnimationData();
        TransformLayerRuntime.Upsert(keys.Position, 0, new Vector3(1, .25f, -2));
        TransformLayerRuntime.Upsert(keys.Position, 6, new Vector3(3, .75f, 0));
        TransformLayerRuntime.Upsert(keys.Rotation, 0, new Vector3(0, 10, 0));
        TransformLayerRuntime.Upsert(keys.Rotation, 6, new Vector3(12, 70, -6));
        TransformLayerRuntime.Upsert(keys.Scale, 0, Vector3.One);
        TransformLayerRuntime.Upsert(keys.Scale, 6, new Vector3(2, 3, .5f));
        Vector3 baseRotation = new(3, 35, 7), baseScale = new(2, 1, 4);
        for (int i = 0; i <= 60; i++)
        {
            double time = i / 10.0;
            float motionYaw = Mathf.Lerp(-160, 160, i / 60f);
            var result = TransformLayerRuntime.Evaluate(new Vector3(99, 0, 99), baseRotation, baseScale, keys, path, time, Heading(motionYaw));
            var expectedPath = ActorPathRuntime.Evaluate(path, time);
            var offset = TransformLayerRuntime.EvaluateVector(keys.Position, time, Vector3.Zero);
            var manual = TransformLayerRuntime.EvaluateVector(keys.Rotation, time, Vector3.Zero, true);
            float pathYaw = Mathf.RadToDeg(Mathf.Atan2(expectedPath.Tangent.X, expectedPath.Tangent.Z));
            Check(Near(result.Position, expectedPath.Position + offset), "manual position remains above path at sample " + i);
            Check(SameAngle(result.RotationDegrees.Y + motionYaw, pathYaw + baseRotation.Y + manual.Y), "path heading plus complete static/keyed yaw survives source-heading changes at sample " + i);
            Check(SameAngle(result.RotationDegrees.X, baseRotation.X + manual.X) && SameAngle(result.RotationDegrees.Z, baseRotation.Z + manual.Z), "pitch and roll corrections remain independent at sample " + i);
            Check(Near(result.Scale, baseScale * TransformLayerRuntime.EvaluateVector(keys.Scale, time, Vector3.One)), "multiplicative scale applied last at sample " + i);
        }
        var middle = TransformLayerRuntime.Evaluate(Vector3.Zero, baseRotation, baseScale, keys, path, 3, Heading(28));
        var later = TransformLayerRuntime.Evaluate(Vector3.Zero, baseRotation, baseScale, keys, path, 5, Heading(-40));
        var repeated = TransformLayerRuntime.Evaluate(Vector3.Zero, baseRotation, baseScale, keys, path, 3, Heading(28));
        Check(middle == repeated && middle != later, "reverse seeks reproduce identical final transform");
        path.OrientToPath = false;
        var ignored = TransformLayerRuntime.Evaluate(Vector3.Zero, baseRotation, baseScale, keys, path, 3, Heading(80));
        Check(Near(ignored.RotationDegrees, baseRotation + TransformLayerRuntime.EvaluateVector(keys.Rotation, 3, Vector3.Zero, true)), "disabling path orientation retains path position and all manual rotation");
        path.Enabled = false;
        var disabled = TransformLayerRuntime.Evaluate(new Vector3(10, 2, 3), baseRotation, baseScale, keys, path, 3, Heading(80));
        Check(Near(disabled.Position, new Vector3(10, 2, 3) + TransformLayerRuntime.EvaluateVector(keys.Position, 3, Vector3.Zero)), "disabled path uses object base position plus correction");
        var restored = JsonSerializer.Deserialize<TransformAnimationData>(JsonSerializer.Serialize(keys))!;
        var reopened = TransformLayerRuntime.Evaluate(new Vector3(10, 2, 3), baseRotation, baseScale, restored, path, 3, Heading(80));
        Check(reopened == disabled && restored.Position.Count == 2 && restored.Rotation[1].Value.Length == 3 && restored.Scale.Count == 2, "all grouped channels survive JSON roundtrip");
        Check(ReferenceEquals(TransformLayerRuntime.Channel(keys, "Rotation"), keys.Rotation), "parameter channel lookup");
        return count;
    }
}
