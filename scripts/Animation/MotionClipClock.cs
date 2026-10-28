using System;

namespace GoPrevis;

/// <summary>One deterministic time-remapping convention for BVH, native rigs and tracking samples.</summary>
public static class MotionClipClock
{
    public static double ParameterTime(double time, double start, double duration) => Math.Clamp(time, start, start + duration);

    public static double SourceTime(PropertyAnimationData? properties, double time, double start, double duration, double offset, double speed)
    {
        double at = ParameterTime(time, start, duration);
        var rate = Curve(properties, "Speed");
        double travelled = PropertyCurveRuntime.HasKeys(rate)
            ? PropertyCurveRuntime.Integrate(rate, start, at, speed, 0, double.PositiveInfinity)
            : Math.Clamp(time - start, 0, duration) * Math.Max(.000001, speed);
        return Math.Max(0, Number(properties, "SourceOffset", at, offset)) + travelled;
    }

    public static double Number(PropertyAnimationData? properties, string path, double time, double fallback)
        => PropertyCurveRuntime.Evaluate(Curve(properties, path), time, fallback);

    public static bool Flag(PropertyAnimationData? properties, string path, double time, bool fallback)
        => PropertyCurveRuntime.EvaluateDiscrete(Curve(properties, path), time, fallback ? 1 : 0) >= .5;

    public static double BlendDuration(PropertyAnimationData? properties, double time, double start, double duration, double fallback, double overlap)
        => Math.Clamp(Number(properties, "BlendIn", ParameterTime(time, start, duration), fallback), 0, Math.Max(0, Math.Min(duration, overlap)));

    static ScalarAnimationCurve? Curve(PropertyAnimationData? properties, string path)
        => properties?.Channels != null && properties.Channels.TryGetValue(path, out var curve) ? curve : null;

    /// <summary>Cache invalidation detects in-place graph edits without copying or reloading motion sources.</summary>
    public static ulong Fingerprint(PropertyAnimationData? properties)
    {
        if (properties?.Channels == null || properties.Channels.Count == 0) return 0;
        ulong hash = 14695981039346656037UL;
        void Add(ulong value) { hash ^= value; hash *= 1099511628211UL; }
        void Text(string? text) { if (text != null) foreach (char c in text) Add(c); Add(0xff); }
        foreach (var pair in properties.Channels)
        {
            Text(pair.Key); var curve = pair.Value;
            if (curve == null) { Add(0); continue; }
            Add(curve.Enabled ? 1UL : 0); Add(curve.Discrete ? 1UL : 0);
            if (curve.Keys == null) continue;
            foreach (var key in curve.Keys)
            {
                if (key == null) { Add(0); continue; }
                Add((ulong)BitConverter.DoubleToInt64Bits(key.Time)); Add((ulong)BitConverter.DoubleToInt64Bits(key.Value));
                Add((ulong)BitConverter.DoubleToInt64Bits(key.InSlope)); Add((ulong)BitConverter.DoubleToInt64Bits(key.OutSlope));
                Add((ulong)BitConverter.DoubleToInt64Bits(key.InWeight)); Add((ulong)BitConverter.DoubleToInt64Bits(key.OutWeight)); Text(key.Interpolation);
            }
        }
        return hash;
    }
}
