using System;
using System.Collections.Generic;
using Godot;

namespace Previs;

/// <summary>Run with Godot --headless --path . --script res://scripts/Testing/ActorPathProbe.cs.</summary>
public partial class ActorPathProbe : SceneTree
{
    public override void _Initialize()
    {
        try
        {
            GD.Print($"ACTOR_PATH_OK checks={RunChecks()}");
            Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError("ACTOR_PATH_FAILED " + exception);
            Quit(1);
        }
    }

    public static int RunChecks()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Actor path check failed: " + message);
            checks++;
        }
        static bool Near(Vector3 a, Vector3 b, float tolerance = .0001f) => a.DistanceTo(b) <= tolerance;
        static ActorPathData Path(params float[][] points) => new() { Enabled = true, Start = 2, Duration = 6, Points = new List<float[]>(points) };

        var empty = Path();
        Check(Near(ActorPathRuntime.Evaluate(empty, 5).Position, Vector3.Zero), "empty path has a safe origin");
        Check(ActorPathRuntime.Length(empty) == 0 && ActorPathRuntime.SamplePolyline(empty).Length == 0, "empty path length and samples");
        var singleton = Path(new[] { 1f, 0f, 3f });
        Check(Near(ActorPathRuntime.Evaluate(singleton, 100).Position, new Vector3(1, 0, 3)), "single point holds");
        Check(ActorPathRuntime.SamplePolyline(singleton).Length == 1, "single point polyline");

        var straight = Path(new[] { 1f, 0f, 0f }, new[] { 1f, 0f, 0f }, new[] { 4f, 0f, 0f }, new[] { 7f, 0f, 0f });
        Check(Math.Abs(ActorPathRuntime.Length(straight) - 6) < .0001, "duplicate points do not add distance");
        Check(Near(ActorPathRuntime.Evaluate(straight, 5).Position, new Vector3(4, 0, 0)), "constant-speed straight midpoint");
        Check(Near(ActorPathRuntime.Evaluate(straight, -10).Position, new Vector3(1, 0, 0)), "before-start hold");
        Check(Near(ActorPathRuntime.Evaluate(straight, 100).Position, new Vector3(7, 0, 0)), "after-end hold");
        Check(Near(ActorPathRuntime.Evaluate(straight, 2).Tangent, Vector3.Right) && Near(ActorPathRuntime.Evaluate(straight, 8).Tangent, Vector3.Right), "endpoint heading");
        var allDuplicates = Path(new[] { 2f, 0f, -1f }, new[] { 2f, 0f, -1f }, new[] { 2f, 0f, -1f });
        Check(ActorPathRuntime.Length(allDuplicates) == 0 && ActorPathRuntime.Evaluate(allDuplicates, 3).Tangent.IsFinite(), "all duplicate points remain finite");

        var curved = Path(new[] { 0f, 0f, 0f }, new[] { .2f, 0f, .4f }, new[] { 4f, 0f, 4f }, new[] { 6f, 0f, 0f }, new[] { 6.3f, 0f, -.3f });
        Vector3 firstPoint = ActorPathRuntime.Evaluate(curved, 2).Position;
        Vector3 lastPoint = ActorPathRuntime.Evaluate(curved, 8).Position;
        Check(Near(firstPoint, Vector3.Zero) && Near(lastPoint, new Vector3(6.3f, 0, -.3f)), "curved path exact endpoints");
        double targetStep = ActorPathRuntime.Length(curved) / 600;
        Vector3 previous = firstPoint;
        double maximumSpeedError = 0;
        var captured = new (Vector3 Position, Vector3 Tangent)[601];
        for (int i = 0; i <= 600; i++)
        {
            var state = ActorPathRuntime.Evaluate(curved, 2 + i * .01);
            Check(state.Position.IsFinite() && state.Tangent.IsFinite() && Math.Abs(state.Tangent.Length() - 1) < .0001, "finite normalized curved heading at sample " + i);
            Check(Math.Abs(state.Position.Y) < .0001, "ground-plane curve at sample " + i);
            if (i > 0) maximumSpeedError = Math.Max(maximumSpeedError, Math.Abs(previous.DistanceTo(state.Position) / targetStep - 1));
            previous = state.Position;
            captured[i] = state;
        }
        Check(maximumSpeedError < .04, $"constant approximate speed with uneven point spacing (error={maximumSpeedError:0.0000})");
        for (int i = 600; i >= 0; i -= 7)
        {
            var state = ActorPathRuntime.Evaluate(curved, 2 + i * .01);
            Check(state == captured[i], "reverse seeks do not depend on playback history");
        }

        float oldLength = ActorPathRuntime.Length(curved);
        curved.Points[^1][0] += 3;
        Check(ActorPathRuntime.Length(curved) > oldLength, "in-place point edits invalidate the geometry cache");
        Check(Near(ActorPathRuntime.Evaluate(curved, 8).Position, new Vector3(9.3f, 0, -.3f)), "edited endpoint is immediately visible");
        curved.Points = new List<float[]> { new[] { 1f, 0f, 1f }, new[] { 1f, 0f, 5f } };
        Check(Math.Abs(ActorPathRuntime.Length(curved) - 4) < .0001, "replacing point list invalidates the cache");
        curved.Start = 10;
        curved.Duration = 2;
        Check(Near(ActorPathRuntime.Evaluate(curved, 11).Position, new Vector3(1, 0, 3)), "timing edits apply without geometry changes");
        curved.Duration = 0;
        Check(Near(ActorPathRuntime.Evaluate(curved, 9).Position, new Vector3(1, 0, 1)) && Near(ActorPathRuntime.Evaluate(curved, 10).Position, new Vector3(1, 0, 5)), "zero-duration path is an immediate endpoint cut");
        var malformed = Path(new[] { float.NaN, 0f, 0f }, Array.Empty<float>(), new[] { 2f, 0f, 3f });
        Check(Near(ActorPathRuntime.Evaluate(malformed, 3).Position, new Vector3(2, 0, 3)), "invalid points are ignored safely");
        Check(ActorPathRuntime.SamplePolyline(straight, 0).Length == 0 && ActorPathRuntime.SamplePolyline(straight, 1).Length == 1, "polyline sample count boundaries");

        var timed = Path(new[] { 0f, 0f, 0f }, new[] { 3f, 0f, 0f }, new[] { 9f, 0f, 0f });
        double[] fractions = ActorPathRuntime.WaypointFractions(timed);
        Check(fractions.Length == 3 && Math.Abs(fractions[1] - 1.0 / 3) < .000001 && fractions[0] == 0 && fractions[2] == 1, "waypoint fractions use arc length instead of point index");
        double[] defaults = ActorPathRuntime.PointTimes(timed);
        Check(defaults[0] == 0 && Math.Abs(defaults[1] - 2) < .000001 && defaults[2] == 6, "default waypoint times maintain constant distance speed");
        fractions[1] = .9;
        defaults[1] = 99;
        Check(Math.Abs(ActorPathRuntime.WaypointFractions(timed)[1] - 1.0 / 3) < .000001 && Math.Abs(ActorPathRuntime.PointTimes(timed)[1] - 2) < .000001, "public defaults cannot mutate cached geometry or source data");
        timed.PointTimes = new List<double> { 0, 1, 6 };
        Check(Near(ActorPathRuntime.Evaluate(timed, 3).Position, new Vector3(3, 0, 0)), "custom key reaches its exact waypoint");
        Check(Near(ActorPathRuntime.Evaluate(timed, 5.5).Position, new Vector3(6, 0, 0)), "custom key intervals interpolate distance at different speeds");
        Check(Near(ActorPathRuntime.Evaluate(timed, 1).Position, Vector3.Zero) && Near(ActorPathRuntime.Evaluate(timed, 20).Position, new Vector3(9, 0, 0)), "custom key endpoint holds");
        double[] copiedTimes = ActorPathRuntime.PointTimes(timed);
        copiedTimes[1] = 99;
        Check(timed.PointTimes[1] == 1, "custom times are returned as an independent array");
        timed.PointTimes[1] = 3;
        Check(Near(ActorPathRuntime.Evaluate(timed, 5).Position, new Vector3(3, 0, 0)), "mutable key time edits apply immediately");
        Check(Near(ActorPathRuntime.Evaluate(timed, 3).Position, new Vector3(1, 0, 0)), "key timing edits change earlier interpolation immediately");

        var paused = Path(new[] { 0f, 0f, 0f }, new[] { 3f, 0f, 0f }, new[] { 3f, 0f, 0f }, new[] { 9f, 0f, 0f });
        paused.PointTimes = new List<double> { 0, 1, 3, 6 };
        double[] pauseFractions = ActorPathRuntime.WaypointFractions(paused);
        Check(pauseFractions.Length == 4 && pauseFractions[1] == pauseFractions[2], "duplicate original points retain separate timing keys at the same distance");
        Check(Near(ActorPathRuntime.Evaluate(paused, 3.2).Position, new Vector3(3, 0, 0)) && Near(ActorPathRuntime.Evaluate(paused, 4.8).Position, new Vector3(3, 0, 0)), "separate timestamps at one position pause the actor");
        paused.PointTimes = new List<double> { 1, 1, 1, 5 };
        Check(Near(ActorPathRuntime.Evaluate(paused, 2.9).Position, Vector3.Zero) && Near(ActorPathRuntime.Evaluate(paused, 3).Position, new Vector3(3, 0, 0)), "coincident key times perform a deterministic cut to the last coincident key");

        var keyedCurve = Path(new[] { 0f, 0f, 0f }, new[] { .2f, 0f, .4f }, new[] { 4f, 0f, 4f }, new[] { 6f, 0f, 0f }, new[] { 6.3f, 0f, -.3f });
        keyedCurve.PointTimes = new List<double> { 0, .4, 2, 3.5, 6 };
        for (int i = 0; i < keyedCurve.Points.Count; i++)
        {
            float[] waypoint = keyedCurve.Points[i];
            Check(Near(ActorPathRuntime.Evaluate(keyedCurve, keyedCurve.Start + keyedCurve.PointTimes[i]).Position, new Vector3(waypoint[0], waypoint[1], waypoint[2])), "curved waypoint exact arrival at key " + i);
        }
        var timedCaptured = new (Vector3 Position, Vector3 Tangent)[61];
        for (int i = 0; i < timedCaptured.Length; i++) timedCaptured[i] = ActorPathRuntime.Evaluate(keyedCurve, 2 + i * .1);
        for (int i = timedCaptured.Length - 1; i >= 0; i--)
            Check(ActorPathRuntime.Evaluate(keyedCurve, 2 + i * .1) == timedCaptured[i], "timed path reverse seeks are deterministic");

        foreach (List<double> invalidTimes in new[]
        {
            new List<double> { 0, 1 },
            new List<double> { 0, double.NaN, 6 },
            new List<double> { 0, double.PositiveInfinity, 6 },
            new List<double> { -1, 2, 6 },
            new List<double> { 0, 7, 6 }
        })
        {
            timed.PointTimes = invalidTimes;
            Check(Near(ActorPathRuntime.Evaluate(timed, 5).Position, new Vector3(4.5f, 0, 0)), "invalid key times preserve original constant-distance evaluation");
            Check(Math.Abs(ActorPathRuntime.PointTimes(timed)[1] - 2) < .000001, "invalid key times return safe default key times");
        }
        double[] invalidFractions = ActorPathRuntime.WaypointFractions(malformed);
        Check(invalidFractions.Length == malformed.Points.Count && Array.TrueForAll(invalidFractions, double.IsFinite), "invalid geometry entries retain a safe waypoint mapping");
        Check(ActorPathRuntime.PointTimes(empty).Length == 0, "empty path returns no key times");
        return checks;
    }
}
