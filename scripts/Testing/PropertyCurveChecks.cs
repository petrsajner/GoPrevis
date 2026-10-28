using System;
using System.Linq;
using System.Text.Json;

namespace GoPrevis;

/// <summary>Numeric regression checks independent of the editor or GPU.</summary>
public static class PropertyCurveChecks
{
    public static int Run(Action<bool, string> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        int count = 0;
        void Check(bool valid, string message) { check(valid, message); count++; }
        static bool Near(double a, double b, double tolerance = 1e-8) => Math.Abs(a - b) <= tolerance;
        static ScalarAnimationCurve Curve(double a, double b, double duration = 1)
        {
            var result = new ScalarAnimationCurve();
            PropertyCurveRuntime.Upsert(result, 0, a);
            PropertyCurveRuntime.Upsert(result, duration, b);
            return result;
        }

        var linear = Curve(-3, 9, 4);
        Check(Near(PropertyCurveRuntime.Evaluate(linear, 1, 0), 0), "scalar linear interpolation respects unequal time and value ranges");
        Check(Near(PropertyCurveRuntime.Velocity(linear, 2), 3), "linear velocity is signed value units per second");
        Check(Near(PropertyCurveRuntime.Evaluate(linear, -1, 0), -3) && Near(PropertyCurveRuntime.Evaluate(linear, 8, 0), 9), "values hold before the first and after the last key");
        Check(PropertyCurveRuntime.Velocity(linear, -1) == 0 && PropertyCurveRuntime.Velocity(linear, 8) == 0, "held extrapolation has zero velocity");
        Check(Near(PropertyCurveRuntime.Velocity(linear, 4), 3), "the final key exposes its incoming velocity in the speed graph");

        var angle = Curve(0, 720, 2);
        Check(Near(PropertyCurveRuntime.Evaluate(angle, 1, 0), 360), "Euler channels retain deliberate multiple revolutions");
        var decreasing = Curve(10, -10, 2);
        Check(Near(PropertyCurveRuntime.Velocity(decreasing, 1), -10), "value derivative preserves descending direction");

        var ease = Curve(0, 1);
        ease.Keys[0].Interpolation = "Ease in/out";
        Check(Near(PropertyCurveRuntime.Evaluate(ease, .25, 0), .15625), "ease in/out uses the cubic smoothstep value curve");
        Check(Near(PropertyCurveRuntime.Velocity(ease, .25), 1.125), "ease velocity matches the analytic derivative");
        ease.Keys[0].Interpolation = "Stop";
        Check(Near(PropertyCurveRuntime.Evaluate(ease, .25, 0), .103515625), "Stop value uses a smooth arrival with zero endpoint acceleration");
        Check(Near(PropertyCurveRuntime.Velocity(ease, .25), 1.0546875), "Stop velocity matches its quintic derivative");
        Check(PropertyCurveRuntime.Velocity(ease, 0) == 0 && PropertyCurveRuntime.Velocity(ease, 1) == 0, "Stop comes to rest at both ends");

        var weighted = Curve(0, 1, 4);
        weighted.Keys[0].Interpolation = "Bezier";
        weighted.Keys[0].OutWeight = .1;
        weighted.Keys[1].InWeight = .7;
        // At u=.25, cubic time controls [0,.4,1.2,4] give t=.4 seconds.
        Check(Near(PropertyCurveRuntime.Evaluate(weighted, .4, 0), .15625), "weighted Bezier solves the handle time instead of using normalized time as its parameter");
        Check(Near(PropertyCurveRuntime.Velocity(weighted, .4), 1.125 / 2.1), "weighted speed uses dy/du divided by dt/du");

        var overshoot = Curve(0, 1);
        overshoot.Keys[0].Interpolation = "Bezier";
        overshoot.Keys[0].OutSlope = 6;
        overshoot.Keys[1].InSlope = -6;
        Check(Near(PropertyCurveRuntime.Evaluate(overshoot, .5, 0), 2), "independent value tangents allow overshoot instead of clamping the curve to its endpoint values");
        Check(Near(PropertyCurveRuntime.Velocity(overshoot, .5), 1.5), "overshooting curves retain a consistent signed speed");

        var straightBezier = Curve(0, 12, 4);
        straightBezier.Keys[0].Interpolation = "Bezier";
        straightBezier.Keys[0].OutSlope = straightBezier.Keys[1].InSlope = 3;
        straightBezier.Keys[0].OutWeight = .65;
        straightBezier.Keys[1].InWeight = .2;
        Check(Near(PropertyCurveRuntime.Evaluate(straightBezier, 1.7, 0), 5.1) && Near(PropertyCurveRuntime.Velocity(straightBezier, 1.7), 3), "a line remains a line under unequal weighted time handles");
        straightBezier.Keys[0].OutWeight = straightBezier.Keys[1].InWeight = 0;
        Check(Near(PropertyCurveRuntime.Velocity(straightBezier, 0), 3) && Near(PropertyCurveRuntime.Velocity(straightBezier, 4), 3), "coincident endpoint handles use their limiting derivative without a sign error");

        var held = Curve(4, 9);
        held.Keys[0].Interpolation = "Hold";
        Check(PropertyCurveRuntime.Evaluate(held, .999, 0) == 4 && PropertyCurveRuntime.Evaluate(held, 1, 0) == 9, "Hold changes exactly at the next key");
        held.Discrete = true;
        held.Keys[0].Interpolation = "Bezier";
        Check(PropertyCurveRuntime.Evaluate(held, .6, 0) == 4 && PropertyCurveRuntime.Velocity(held, .6) == 0, "discrete properties cannot interpolate into invalid boolean or enumeration values");
        held.Enabled = false;
        Check(PropertyCurveRuntime.Evaluate(held, .6, 7) == 7 && !PropertyCurveRuntime.HasKeys(held), "disabled curves fall back to the property's base value");

        var disorder = new ScalarAnimationCurve();
        disorder.Keys.Add(new() { Time = 3, Value = 6 });
        disorder.Keys.Add(new() { Time = 0, Value = 0 });
        disorder.Keys.Add(new() { Time = 3, Value = 9 });
        disorder.Keys.Add(new() { Time = 1, Value = double.NaN });
        Check(Near(PropertyCurveRuntime.Evaluate(disorder, 1, -1), 3), "unsorted imports ignore invalid keys and use the last duplicate timestamp");
        Check(PropertyCurveRuntime.OrderedKeys(disorder).Count == 2, "graph and dope-sheet views receive sorted unique valid keys");
        var existing = disorder.Keys[2];
        existing.Interpolation = "Bezier"; existing.OutSlope = 5; existing.OutWeight = .4;
        var replaced = PropertyCurveRuntime.Upsert(disorder, 3, 12);
        Check(ReferenceEquals(existing, replaced) && replaced.Interpolation == "Bezier" && replaced.OutSlope == 5 && replaced.OutWeight == .4 && disorder.Keys.Count(k => k.Time == 3) == 1,
            "replacing a key preserves its identity and handles and consolidates imported duplicates");
        Check(PropertyCurveRuntime.Remove(disorder, replaced.Id) && !disorder.Keys.Any(k => k.Id == replaced.Id), "keys are removed by stable identity");

        var auto = new ScalarAnimationCurve();
        PropertyCurveRuntime.Upsert(auto, 0, 0);
        var middle = PropertyCurveRuntime.Upsert(auto, 1, 2);
        PropertyCurveRuntime.Upsert(auto, 3, 3);
        Check(PropertyCurveRuntime.ApplyPreset(auto, middle.Id, "Auto") && middle.Interpolation == "Bezier" && Near(middle.InSlope, middle.OutSlope) && middle.OutSlope > .5 && middle.OutSlope < 2,
            "Auto computes a linked time-aware monotone tangent");
        auto.Keys[2].Value = 0;
        PropertyCurveRuntime.ApplyPreset(auto, middle.Id, "Auto");
        Check(middle.InSlope == 0 && middle.OutSlope == 0, "automatic tangents flatten a local extremum rather than creating unwanted overshoot");

        var project = new PropertyAnimationData();
        project.Channels["Position.X"] = weighted;
        project.Channels["Position.Y"] = Curve(2, 5, 2);
        var roundTrip = JsonSerializer.Deserialize<PropertyAnimationData>(JsonSerializer.Serialize(project))!;
        Check(roundTrip.Channels.Count == 2 && Near(PropertyCurveRuntime.Evaluate(roundTrip.Channels["Position.X"], .4, 0), .15625) && Near(PropertyCurveRuntime.Evaluate(roundTrip.Channels["Position.Y"], 1, 0), 3.5),
            "JSON save/reopen retains independent axis times and weighted handles");
        var clone = PropertyCurveRuntime.Copy(weighted);
        clone.Keys[0].Value = 80;
        Check(weighted.Keys[0].Value == 0, "history copies do not share mutable key objects");

        bool rejectedTime = false, rejectedValue = false;
        try { PropertyCurveRuntime.Upsert(weighted, double.PositiveInfinity, 0); } catch (FormatException) { rejectedTime = true; }
        try { PropertyCurveRuntime.Upsert(weighted, 0, double.NaN); } catch (FormatException) { rejectedValue = true; }
        Check(rejectedTime && rejectedValue, "nonfinite authored key input is rejected before reaching rendering or serialization");
        Check(Near(PropertyCurveRuntime.Integrate(linear, 1, 3, 0), 6) && Near(PropertyCurveRuntime.Integrate(linear, 3, 1, 0), -6),
            "curve integration respects nonzero bounds and reversed time queries");
        Check(Near(PropertyCurveRuntime.Integrate(ease, 0, 1, 0), .5), "the Stop speed profile integrates to its symmetric mean value");
        Check(Near(PropertyCurveRuntime.Integrate(straightBezier, 0, 4, 0), 24), "Bezier integration uses parametric time and remains exact for a linear value relation");
        var positive = Curve(-1, 1, 2);
        Check(Near(PropertyCurveRuntime.Integrate(positive, 0, 2, 0, 0, double.PositiveInfinity), .5, 1e-7),
            "integrating a positive-only rate clips negative curve overshoot rather than reversing source time");
        var playback = new PropertyAnimationData();
        var speedCurve = playback.Channels["Speed"] = new();
        PropertyCurveRuntime.Upsert(speedCurve, 2, 1);PropertyCurveRuntime.Upsert(speedCurve, 4, 3);
        var offsetCurve = playback.Channels["SourceOffset"] = new();
        PropertyCurveRuntime.Upsert(offsetCurve, 2, .5);PropertyCurveRuntime.Upsert(offsetCurve, 4, 1.5);
        Check(Near(MotionClipClock.SourceTime(playback, 3, 2, 2, .5, 1), 2.5),
            "animated motion rate advances by its integral plus the sampled source offset");
        Check(Near(MotionClipClock.SourceTime(playback, 1, 2, 2, .5, 1), .5) && Near(MotionClipClock.SourceTime(playback, 9, 2, 2, .5, 1), 5.5),
            "source clocks hold their start/end through inactive scene gaps");
        double replay = MotionClipClock.SourceTime(playback, 3, 2, 2, .5, 1);
        _ = MotionClipClock.SourceTime(playback, 4, 2, 2, .5, 1);
        Check(MotionClipClock.SourceTime(playback, 3, 2, 2, .5, 1) == replay,
            "backward source-time seeks do not accumulate playback history");
        var boolCurve = playback.Channels["Loop"] = Curve(0, 1, 4);
        Check(!MotionClipClock.Flag(playback,"Loop",3,false) && MotionClipClock.Flag(playback,"Loop",4,false),
            "motion flags hold between keys even if a malformed source omitted the discrete marker");
        var stamp = MotionClipClock.Fingerprint(playback);speedCurve.Keys[1].Value = 4;
        Check(MotionClipClock.Fingerprint(playback) != stamp,
            "in-place graph edits invalidate prepared root-alignment caches without reloading the motion source");
        return count;
    }
}
