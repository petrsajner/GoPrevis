using System;
using System.Collections.Generic;
using System.Linq;

namespace GoPrevis;

/// <summary>
/// Deterministic scalar curve evaluation. Source key lists may be unsorted;
/// duplicate timestamps use the last valid entry. Euler channels are ordinary
/// unbounded numbers, so authored full revolutions are never shortened.
/// </summary>
public static class PropertyCurveRuntime
{
    public static readonly string[] Modes = { "Linear", "Bezier", "Hold", "Ease in/out", "Stop", "Auto", "Flat" };
    public const double TimeTolerance = .000001;

    public static bool HasKeys(ScalarAnimationCurve? curve) => curve?.Enabled == true &&
        curve.Keys != null && curve.Keys.Any(Valid);

    public static double Evaluate(ScalarAnimationCurve? curve, double time, double fallback)
    {
        fallback = double.IsFinite(fallback) ? fallback : 0;
        if (curve?.Enabled != true || curve.Keys == null) return fallback;
        if (double.IsNaN(time)) time = 0;
        Locate(curve.Keys, time, out var before, out var after);
        if (before == null) return after?.Value ?? fallback;
        if (after == null || curve.Discrete) return before.Value;
        return FiniteResult(Segment(before, after, time, velocity: false), before.Value);
    }

    public static double EvaluateDiscrete(ScalarAnimationCurve? curve, double time, double fallback)
    {
        if (curve?.Enabled != true || curve.Keys == null) return double.IsFinite(fallback) ? fallback : 0;
        if (double.IsNaN(time)) time = 0;
        Locate(curve.Keys, time, out var before, out var after);
        return before?.Value ?? after?.Value ?? (double.IsFinite(fallback) ? fallback : 0);
    }

    /// <summary>
    /// Integrates a parameter over scene time. This is the source clock for
    /// animated playback rates: changing rate never multiplies all prior time.
    /// Bezier integration is performed in its parametric time domain; a
    /// three-point Gaussian quadrature is exact for unclipped cubic curves.
    /// Optional value limits support positive physical playback rates even when
    /// an artist's value handles overshoot their keyed values.
    /// </summary>
    public static double Integrate(ScalarAnimationCurve? curve, double from, double to, double fallback,
        double minimum = double.NegativeInfinity, double maximum = double.PositiveInfinity)
    {
        if (!double.IsFinite(from) || !double.IsFinite(to)) throw new FormatException("Integration times must be finite.");
        if (from == to) return 0;
        if (to < from) return -Integrate(curve, to, from, fallback, minimum, maximum);
        if (double.IsNaN(minimum) || double.IsNaN(maximum) || minimum > maximum) throw new FormatException("Integration value limits are invalid.");
        fallback = Math.Clamp(double.IsFinite(fallback) ? fallback : 0, minimum, maximum);
        if (!HasKeys(curve)) return fallback * (to - from);
        var keys = OrderedKeys(curve);
        double total = 0, cursor = from;
        for (int i = 0; i < keys.Count && cursor < to; i++)
        {
            if (cursor < keys[i].Time)
            {
                double finish = Math.Min(to, keys[i].Time);
                if (i == 0) total += Math.Clamp(keys[0].Value, minimum, maximum) * (finish - cursor);
                else total += SegmentIntegral(keys[i - 1], keys[i], cursor, finish, curve!.Discrete, minimum, maximum);
                cursor = finish;
            }
        }
        if (cursor < to) total += Math.Clamp(keys[^1].Value, minimum, maximum) * (to - cursor);
        return FiniteResult(total, 0);
    }

    /// <summary>
    /// Signed value change per scene second, not a normalized speed percentage.
    /// Zero outside the keyed range. At knots uses the outgoing derivative,
    /// except for the final key, where it returns the incoming derivative.
    /// A discrete/held interval has zero velocity; its instantaneous jump is not
    /// represented as an artificial numeric spike.
    /// </summary>
    public static double Velocity(ScalarAnimationCurve? curve, double time, double fallback = 0)
    {
        if (curve?.Enabled != true || curve.Discrete || curve.Keys == null || double.IsNaN(time)) return 0;
        Locate(curve.Keys, time, out var before, out var after);
        if (before == null) return 0;
        if (after == null)
        {
            if (time != before.Time) return 0;
            ScalarKeyData? preceding = null;
            foreach (var key in curve.Keys)
                if (Valid(key) && key.Time < before.Time && (preceding == null || key.Time >= preceding.Time)) preceding = key;
            return preceding == null ? 0 : FiniteResult(Segment(preceding, before, time, velocity: true), 0);
        }
        return FiniteResult(Segment(before, after, time, velocity: true), 0);
    }

    public static ScalarKeyData Upsert(ScalarAnimationCurve curve, double time, double value, double tolerance = TimeTolerance)
    {
        ArgumentNullException.ThrowIfNull(curve);
        if (!double.IsFinite(time) || time < 0) throw new FormatException("A property key time must be finite and zero or greater.");
        if (!double.IsFinite(value)) throw new FormatException("A property key value must be finite.");
        if (!double.IsFinite(tolerance) || tolerance < 0) tolerance = TimeTolerance;
        curve.Keys ??= new();
        ScalarKeyData? existing = null;
        double nearest = double.PositiveInfinity;
        foreach (var key in curve.Keys)
        {
            if (key == null || !double.IsFinite(key.Time)) continue;
            double distance = Math.Abs(key.Time - time);
            if (distance <= tolerance && distance <= nearest) { existing = key; nearest = distance; }
        }
        if (existing == null)
        {
            existing = new ScalarKeyData();
            curve.Keys.Add(existing);
        }
        if (string.IsNullOrEmpty(existing.Id)) existing.Id = Guid.NewGuid().ToString("N");
        existing.Time = time;
        existing.Value = value;
        curve.Keys.RemoveAll(other => other != null && !ReferenceEquals(other, existing) &&
            double.IsFinite(other.Time) && Math.Abs(other.Time - time) <= tolerance);
        curve.Keys.Sort((a, b) => (a?.Time ?? double.NegativeInfinity).CompareTo(b?.Time ?? double.NegativeInfinity));
        return existing;
    }

    public static bool Remove(ScalarAnimationCurve curve, string id)
    {
        ArgumentNullException.ThrowIfNull(curve);
        return curve.Keys != null && curve.Keys.RemoveAll(key => key != null && key.Id == id) > 0;
    }

    public static bool RemoveAtTime(ScalarAnimationCurve curve, double time, double tolerance = TimeTolerance)
    {
        ArgumentNullException.ThrowIfNull(curve);
        if (!double.IsFinite(time)) return false;
        if (!double.IsFinite(tolerance) || tolerance < 0) tolerance = TimeTolerance;
        return curve.Keys != null && curve.Keys.RemoveAll(key => key != null && Math.Abs(key.Time - time) <= tolerance) > 0;
    }

    public static ScalarAnimationCurve Copy(ScalarAnimationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        return new ScalarAnimationCurve { Enabled = curve.Enabled, Discrete = curve.Discrete,
            Keys = curve.Keys?.Where(key => key != null).Select(Copy).ToList() ?? new() };
    }

    public static ScalarKeyData Copy(ScalarKeyData key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new ScalarKeyData { Id = key.Id, Time = key.Time, Value = key.Value,
            Interpolation = key.Interpolation, InSlope = key.InSlope, OutSlope = key.OutSlope,
            InWeight = key.InWeight, OutWeight = key.OutWeight, BrokenTangents = key.BrokenTangents };
    }

    /// <summary>
    /// Sets a selected key's outgoing interval preset. Bezier and Auto expose
    /// both incoming/outgoing handles. Auto computes a monotone, time-aware
    /// tangent at this key; it is explicit data and stays editable afterwards.
    /// </summary>
    public static bool ApplyPreset(ScalarAnimationCurve curve, string keyId, string preset)
    {
        ArgumentNullException.ThrowIfNull(curve);
        var key = curve.Keys?.FirstOrDefault(k => k != null && k.Id == keyId);
        if (key == null) return false;
        string mode = CanonicalMode(preset);
        key.Interpolation = mode;
        if (mode is "Ease in/out" or "Stop" or "Flat") key.InSlope = key.OutSlope = 0;
        if (mode == "Flat") key.Interpolation = "Bezier";
        if (mode == "Auto")
        {
            var slope = AutoSlope(curve, key);
            key.InSlope = key.OutSlope = slope;
            key.Interpolation = "Bezier";
            key.BrokenTangents = false;
        }
        key.InWeight = key.OutWeight = 1.0 / 3;
        return true;
    }

    /// <summary>Sorted unique valid keys, suitable for dope-sheet and graph drawing.</summary>
    public static IReadOnlyList<ScalarKeyData> OrderedKeys(ScalarAnimationCurve? curve)
    {
        if (curve?.Keys == null) return Array.Empty<ScalarKeyData>();
        var unique = new SortedDictionary<double, ScalarKeyData>();
        foreach (var key in curve.Keys) if (Valid(key)) unique[key.Time] = key;
        return unique.Values.ToArray();
    }

    static void Locate(IReadOnlyList<ScalarKeyData> keys, double time, out ScalarKeyData? before, out ScalarKeyData? after)
    {
        before = after = null;
        foreach (var key in keys)
        {
            if (!Valid(key)) continue;
            if (key.Time <= time)
            {
                if (before == null || key.Time >= before.Time) before = key;
            }
            else if (after == null || key.Time <= after.Time) after = key;
        }
    }

    static bool Valid(ScalarKeyData? key) => key != null && double.IsFinite(key.Time) && key.Time >= 0 && double.IsFinite(key.Value);

    static string CanonicalMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "bezier" or "bézier" or "curved" or "manual" => "Bezier",
        "hold" or "constant" or "step" => "Hold",
        "ease in/out" or "easy ease" or "ease" => "Ease in/out",
        "stop" => "Stop", "auto" or "automatic" => "Auto", "flat" => "Flat",
        _ => "Linear"
    };

    static double Segment(ScalarKeyData first, ScalarKeyData last, double time, bool velocity)
    {
        double duration = last.Time - first.Time;
        if (duration <= 0) return velocity ? 0 : last.Value;
        double t = Math.Clamp((time - first.Time) / duration, 0, 1);
        double delta = last.Value - first.Value;
        switch (CanonicalMode(first.Interpolation))
        {
            case "Hold": return velocity ? 0 : first.Value;
            case "Ease in/out":
                return velocity ? delta / duration * 6 * t * (1 - t) : first.Value + delta * t * t * (3 - 2 * t);
            case "Stop":
                return velocity ? delta / duration * 30 * t * t * (t - 1) * (t - 1)
                    : first.Value + delta * t * t * t * (t * (t * 6 - 15) + 10);
            case "Flat":
                return Cubic(first, last, t, duration, velocity, forceFlat: true);
            case "Auto":
            case "Bezier": return Cubic(first, last, t, duration, velocity, forceFlat: false);
            default: return velocity ? delta / duration : first.Value + delta * t;
        }
    }

    static double Cubic(ScalarKeyData first, ScalarKeyData last, double time, double duration, bool velocity, bool forceFlat)
    {
        double x1 = Weight(first.OutWeight), x2 = 1 - Weight(last.InWeight);
        double slope1 = forceFlat ? 0 : Finite(first.OutSlope), slope2 = forceFlat ? 0 : Finite(last.InSlope);
        double y1 = first.Value + slope1 * duration * x1;
        double y2 = last.Value - slope2 * duration * (1 - x2);
        // Time handles need not have equal lengths: invert x(u), then sample y(u).
        double u;
        if (time <= 0) u = 0;
        else if (time >= 1) u = 1;
        else
        {
            double low = 0, high = 1;
            for (int i = 0; i < 52; i++)
            {
                double middle = (low + high) * .5;
                if (Bezier(middle, 0, x1, x2, 1) < time) low = middle; else high = middle;
            }
            u = (low + high) * .5;
        }
        if (!velocity) return FiniteResult(Bezier(u, first.Value, y1, y2, last.Value), first.Value);
        double dx = BezierDerivative(u, 0, x1, x2, 1) * duration;
        double dy = BezierDerivative(u, first.Value, y1, y2, last.Value);
        if (Math.Abs(dx) <= 1e-12 && Math.Abs(dy) <= 1e-12)
        {
            // Coincident endpoint handles: the second nonzero term determines
            // the slope of the curve, rather than an arbitrary zero/NaN.
            dx = BezierSecondDerivative(u, 0, x1, x2, 1) * duration;
            dy = BezierSecondDerivative(u, first.Value, y1, y2, last.Value);
            if (Math.Abs(dx) <= 1e-12 && Math.Abs(dy) <= 1e-12)
            { dx = 6 * (1 - 3 * x2 + 3 * x1) * duration; dy = 6 * (last.Value - 3 * y2 + 3 * y1 - first.Value); }
        }
        // A pathological vertical time tangent can have an infinite derivative.
        // Keep its graph representation finite without changing value sampling.
        return FiniteResult(dy / (Math.Abs(dx) > 1e-12 ? dx : 1e-12), 0);
    }

    static double SegmentIntegral(ScalarKeyData first, ScalarKeyData last, double from, double to, bool discrete, double minimum, double maximum)
    {
        double duration = last.Time - first.Time;
        double a = Math.Clamp((from - first.Time) / duration, 0, 1), b = Math.Clamp((to - first.Time) / duration, 0, 1);
        if (discrete || CanonicalMode(first.Interpolation) == "Hold") return Math.Clamp(first.Value, minimum, maximum) * (to - from);
        if (CanonicalMode(first.Interpolation) is "Bezier" or "Auto" or "Flat")
        {
            bool flat = CanonicalMode(first.Interpolation) == "Flat";
            double x1 = Weight(first.OutWeight), x2 = 1 - Weight(last.InWeight);
            double y1 = first.Value + (flat ? 0 : Finite(first.OutSlope)) * duration * x1;
            double y2 = last.Value - (flat ? 0 : Finite(last.InSlope)) * duration * (1 - x2);
            double ua = BezierTime(a, x1, x2), ub = BezierTime(b, x1, x2);
            double Integrand(double u) => Math.Clamp(Bezier(u, first.Value, y1, y2, last.Value), minimum, maximum)
                * BezierDerivative(u, 0, x1, x2, 1) * duration;
            bool bounded = new[] { first.Value, y1, y2, last.Value }.All(v => v >= minimum && v <= maximum);
            return bounded ? Gaussian3(Integrand, ua, ub) : AdaptiveIntegral(Integrand, ua, ub, 1e-9, 12);
        }
        bool inside = first.Value >= minimum && first.Value <= maximum && last.Value >= minimum && last.Value <= maximum;
        if (!inside) return AdaptiveIntegral(t => Math.Clamp(Segment(first, last, first.Time + t * duration, false), minimum, maximum) * duration, a, b, 1e-9, 12);
        double F(double t) => CanonicalMode(first.Interpolation) switch
        {
            "Ease in/out" => t * t * t - .5 * t * t * t * t,
            "Stop" => Math.Pow(t, 6) - 3 * Math.Pow(t, 5) + 2.5 * Math.Pow(t, 4),
            _ => .5 * t * t
        };
        return duration * (first.Value * (b - a) + (last.Value - first.Value) * (F(b) - F(a)));
    }

    static double BezierTime(double t, double x1, double x2)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        double low = 0, high = 1;
        for (int i = 0; i < 52; i++) { double mid = (low + high) * .5; if (Bezier(mid, 0, x1, x2, 1) < t) low = mid; else high = mid; }
        return (low + high) * .5;
    }
    static double Gaussian3(Func<double, double> function, double a, double b)
    {
        double middle = (a + b) * .5, half = (b - a) * .5, shift = half * .774596669241483377;
        return half * (5.0 / 9 * function(middle - shift) + 8.0 / 9 * function(middle) + 5.0 / 9 * function(middle + shift));
    }
    static double AdaptiveIntegral(Func<double, double> function, double a, double b, double tolerance, int remaining)
    {
        double whole = Gaussian3(function, a, b), mid = (a + b) * .5;
        double left = Gaussian3(function, a, mid), right = Gaussian3(function, mid, b);
        if (remaining <= 0 || Math.Abs(left + right - whole) <= tolerance * Math.Max(1, Math.Abs(left + right))) return left + right;
        return AdaptiveIntegral(function, a, mid, tolerance * .5, remaining - 1) + AdaptiveIntegral(function, mid, b, tolerance * .5, remaining - 1);
    }

    static double AutoSlope(ScalarAnimationCurve curve, ScalarKeyData key)
    {
        ScalarKeyData? previous = null, next = null;
        foreach (var item in curve.Keys)
        {
            if (!Valid(item) || item.Time == key.Time) continue;
            if (item.Time < key.Time && (previous == null || item.Time >= previous.Time)) previous = item;
            if (item.Time > key.Time && (next == null || item.Time <= next.Time)) next = item;
        }
        if (previous == null) return next == null ? 0 : (next.Value - key.Value) / (next.Time - key.Time);
        if (next == null) return (key.Value - previous.Value) / (key.Time - previous.Time);
        double leftDuration = key.Time - previous.Time, rightDuration = next.Time - key.Time;
        double left = (key.Value - previous.Value) / leftDuration, right = (next.Value - key.Value) / rightDuration;
        if (left * right <= 0) return 0;
        // Weighted harmonic mean preserves local extrema and avoids introducing
        // new overshoot when Auto is requested on monotone data.
        double w1 = 2 * rightDuration + leftDuration, w2 = rightDuration + 2 * leftDuration;
        return (w1 + w2) / (w1 / left + w2 / right);
    }

    static double Weight(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1.0 / 3;
    static double Finite(double value) => double.IsFinite(value) ? value : 0;
    static double FiniteResult(double value, double fallback) => double.IsFinite(value) ? value : fallback;
    static double Bezier(double u, double a, double b, double c, double d)
    { double v = 1 - u; return v * v * v * a + 3 * v * v * u * b + 3 * v * u * u * c + u * u * u * d; }
    static double BezierDerivative(double u, double a, double b, double c, double d)
    { double v = 1 - u; return 3 * (v * v * (b - a) + 2 * v * u * (c - b) + u * u * (d - c)); }
    static double BezierSecondDerivative(double u, double a, double b, double c, double d)
        => 6 * ((1 - u) * (c - 2 * b + a) + u * (d - 2 * c + b));
}
