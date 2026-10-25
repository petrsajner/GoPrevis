using System;
using System.Collections.Generic;
using Godot;

namespace GenPrevis;

/// <summary>
/// Evaluates the final object layer after mocap blending. Path locomotion provides
/// the base pose; authored position/rotation corrections and scale factors are
/// applied last. Evaluation depends only on time and input data, never play history.
/// </summary>
public static class TransformLayerRuntime
{
    private const float DirectionEpsilonSquared = .00000001f;

    /// <param name="blendedMotionFacing">
    /// Heading derived from the final blended mocap pose in actor-local space.
    /// Do not pass a separately blended pair of source headings.
    /// </param>
    public static (Vector3 Position, Vector3 RotationDegrees, Vector3 Scale) Evaluate(
        Vector3 basePosition, Vector3 baseRotationDegrees, Vector3 baseScale,
        TransformAnimationData? keys, ActorPathData? path, double time,
        Vector3 blendedMotionFacing)
    {
        basePosition = FiniteOr(basePosition, Vector3.Zero);
        baseRotationDegrees = FiniteOr(baseRotationDegrees, Vector3.Zero);
        baseScale = FiniteOr(baseScale, Vector3.One);
        Vector3 position = basePosition;
        Vector3 rotation = baseRotationDegrees;
        if (path?.Enabled == true && path.Points?.Count >= 2)
        {
            var sample = ActorPathRuntime.Evaluate(path, time);
            position = sample.Position;
            if (path.OrientToPath)
            {
                Vector3 target = Horizontal(sample.Tangent);
                Vector3 source = Horizontal(blendedMotionFacing);
                float pathHeading = Mathf.RadToDeg(Mathf.Atan2(target.X, target.Z));
                float motionHeading = Mathf.RadToDeg(Mathf.Atan2(source.X, source.Z));
                // Static/manual rotation is retained on top of path alignment.
                rotation.Y += pathHeading - motionHeading;
            }
        }

        Vector3 positionOffset = EvaluateVector(keys?.Position, time, Vector3.Zero);
        Vector3 rotationOffset = EvaluateVector(keys?.Rotation, time, Vector3.Zero, angle: true);
        Vector3 scaleFactor = EvaluateVector(keys?.Scale, time, Vector3.One);
        return (position + positionOffset, rotation + rotationOffset, baseScale * scaleFactor);
    }

    /// <summary>
    /// Samples an XYZ parameter as one key group. Duplicate timestamps use the
    /// last valid key in the supplied list. Keys need not be sorted. Values hold
    /// before the first key and after the last key; invalid serialized keys are ignored.
    /// Rotation values are degrees interpolated along the short arc per component.
    /// </summary>
    public static Vector3 EvaluateVector(IReadOnlyList<TransformKeyData>? keys,
        double time, Vector3 fallback, bool angle = false)
    {
        fallback = FiniteOr(fallback, angle ? Vector3.Zero : fallback);
        if (!fallback.IsFinite()) fallback = Vector3.Zero;
        if (keys == null || keys.Count == 0) return fallback;
        if (double.IsNaN(time)) time = 0;
        TransformKeyData? before = null, after = null;
        foreach (TransformKeyData? key in keys)
        {
            if (!Valid(key)) continue;
            if (key!.Time <= time)
            {
                if (before == null || key.Time >= before.Time) before = key;
            }
            else if (after == null || key.Time <= after.Time) after = key;
        }
        if (before == null) return after == null ? fallback : Value(after);
        if (after == null) return Value(before);
        float weight = KeyInterpolation.Weight(before.Interpolation,(time - before.Time) / (after.Time - before.Time));
        Vector3 from = Value(before), to = Value(after);
        if (!angle) return from.Lerp(to, weight);
        return new Vector3(
            LerpDegrees(from.X, to.X, weight),
            LerpDegrees(from.Y, to.Y, weight),
            LerpDegrees(from.Z, to.Z, weight));
    }

    /// <summary>Adds or replaces one XYZ key, preserving its identity on replacement.</summary>
    public static TransformKeyData Upsert(List<TransformKeyData> keys, double time,
        Vector3 value, double tolerance = .000001)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (!double.IsFinite(time) || time < 0)
            throw new FormatException("A transform key time must be finite and zero or greater.");
        if (!value.IsFinite())
            throw new FormatException("Transform key values must contain three finite numbers.");
        if (!double.IsFinite(tolerance) || tolerance < 0) tolerance = .000001;
        TransformKeyData? existing = null;
        double distance = double.PositiveInfinity;
        foreach (TransformKeyData? key in keys)
        {
            if (key == null || !double.IsFinite(key.Time)) continue;
            double difference = Math.Abs(key.Time - time);
            if (difference <= tolerance && difference <= distance)
            {
                existing = key;
                distance = difference;
            }
        }
        if (existing == null)
        {
            existing = new TransformKeyData();
            keys.Add(existing);
        }
        existing.Time = time;
        existing.Value = new[] { value.X, value.Y, value.Z };
        keys.Sort((left, right) => (left?.Time ?? double.NegativeInfinity)
            .CompareTo(right?.Time ?? double.NegativeInfinity));
        return existing;
    }

    public static bool Remove(List<TransformKeyData> keys, string id)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return keys.RemoveAll(key => key != null && key.Id == id) > 0;
    }

    public static bool RemoveAtTime(List<TransformKeyData> keys, double time,
        double tolerance = .000001)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (!double.IsFinite(time)) return false;
        if (!double.IsFinite(tolerance) || tolerance < 0) tolerance = .000001;
        return keys.RemoveAll(key => key != null && Math.Abs(key.Time - time) <= tolerance) > 0;
    }

    public static List<TransformKeyData> Channel(TransformAnimationData keys, string parameter)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return parameter.ToLowerInvariant() switch
        {
            "position" => keys.Position,
            "rotation" => keys.Rotation,
            "scale" => keys.Scale,
            _ => throw new FormatException("Choose Position, Rotation, or Scale for a transform key.")
        };
    }

    private static bool Valid(TransformKeyData? key) => key != null &&
        double.IsFinite(key.Time) && key.Time >= 0 && key.Value?.Length == 3 &&
        float.IsFinite(key.Value[0]) && float.IsFinite(key.Value[1]) && float.IsFinite(key.Value[2]);
    private static Vector3 Value(TransformKeyData key) => new(key.Value[0], key.Value[1], key.Value[2]);
    private static float LerpDegrees(float from, float to, float weight) =>
        Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(from), Mathf.DegToRad(to), weight));
    private static Vector3 FiniteOr(Vector3 value, Vector3 fallback) => value.IsFinite() ? value : fallback;
    private static Vector3 Horizontal(Vector3 direction)
    {
        if (!direction.IsFinite()) return Vector3.Forward;
        direction.Y = 0;
        return direction.LengthSquared() > DirectionEpsilonSquared ? direction.Normalized() : Vector3.Forward;
    }
}
