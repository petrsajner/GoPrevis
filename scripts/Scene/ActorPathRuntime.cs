using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace GoPrevis;

/// <summary>
/// Stateless, distance-based evaluation of a world-space actor path. The geometry cache is
/// weakly held and detects edits to point arrays as well as replacement of the point list.
/// Enabled and OrientToPath are application choices; evaluation does not change the data.
/// </summary>
public static class ActorPathRuntime
{
    private const int StepsPerSegment = 64;
    private const float PointEpsilonSquared = 0.0000000001f;
    private static readonly ConditionalWeakTable<ActorPathData, CacheEntry> Cache = new();
    private static readonly Vector3 DefaultTangent = Vector3.Back;

    public static (Vector3 Position, Vector3 Tangent) Evaluate(ActorPathData path, double seconds)
    {
        Curve curve = GetCurve(path);
        if(path.ProgressOverride is float progress&&float.IsFinite(progress))return curve.AtDistance(Math.Clamp(progress,0,1)*curve.TotalLength);
        double start = double.IsFinite(path.Start) ? path.Start : 0;
        if (ValidPointTimes(path.PointTimes, curve.WaypointFractions.Length))
        {
            double localTime = double.IsNaN(seconds) ? double.NegativeInfinity : seconds - start;
            return curve.AtDistance(TimedFraction(path.PointTimes, curve.WaypointFractions, path.PointInterpolation,localTime) * curve.TotalLength);
        }
        double fraction;
        if (double.IsNaN(seconds) || seconds < start) fraction = 0;
        else if (!double.IsFinite(path.Duration) || path.Duration <= 0) fraction = 1;
        else fraction = Math.Clamp((seconds - start) / path.Duration, 0, 1);
        // Missing authored arrival times still use each waypoint's constant-speed defaults,
        // so a newly drawn path can be eased before its timing keys are edited.
        if(curve.WaypointFractions.Length>1&&path.PointInterpolation?.Count>0)
            fraction=TimedFraction(new List<double>(PointTimes(path)),curve.WaypointFractions,path.PointInterpolation,double.IsNaN(seconds)?double.NegativeInfinity:seconds-start);
        return curve.AtDistance(fraction * curve.TotalLength);
    }

    /// <summary>Approximate arc length in world units, using the same curve as evaluation.</summary>
    public static float Length(ActorPathData path) => (float)GetCurve(path).TotalLength;
    public static double Progress(ActorPathData path,double seconds)
    {
        if(path.ProgressOverride is float value&&float.IsFinite(value))return Math.Clamp(value,0,1);
        var curve=GetCurve(path);
        return ValidPointTimes(path.PointTimes,curve.WaypointFractions.Length)?TimedFraction(path.PointTimes,curve.WaypointFractions,path.PointInterpolation,seconds-path.Start)
            :Math.Clamp((seconds-path.Start)/Math.Max(.000001,path.Duration),0,1);
    }

    /// <summary>
    /// Arc-length fraction at each original control point, including duplicates. Invalid
    /// points hold the previous valid position; invalid leading entries use the first point.
    /// The returned array is independent of the cached geometry and may safely be edited.
    /// </summary>
    public static double[] WaypointFractions(ActorPathData path)
        => (double[])GetCurve(path).WaypointFractions.Clone();

    /// <summary>Relative waypoint key times, or constant-speed defaults for invalid/missing keys.</summary>
    public static double[] PointTimes(ActorPathData path)
    {
        Curve curve = GetCurve(path);
        if (ValidPointTimes(path.PointTimes, curve.WaypointFractions.Length)) return path.PointTimes.ToArray();
        double duration = double.IsFinite(path.Duration) ? Math.Max(0, path.Duration) : 0;
        var times = new double[curve.WaypointFractions.Length];
        for (int i = 0; i < times.Length; i++) times[i] = curve.WaypointFractions[i] * duration;
        return times;
    }

    private static bool ValidPointTimes(List<double>? times, int count)
    {
        if (times == null || count == 0 || times.Count != count) return false;
        double previous = 0;
        foreach (double time in times)
        {
            if (!double.IsFinite(time) || time < previous) return false;
            previous = time;
        }
        return true;
    }

    private static double TimedFraction(List<double> times, double[] fractions, List<KeyInterpolationData>? interpolation,double seconds)
    {
        if (seconds < times[0]) return fractions[0];
        if (seconds >= times[^1]) return fractions[^1];
        // Find the first key strictly after this time. Equal timestamps choose the
        // last coincident key, allowing an intentional instantaneous move.
        int low = 0, high = times.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (times[middle] <= seconds) low = middle + 1;
            else high = middle;
        }
        int before = low - 1;
        double alpha = KeyInterpolation.Weight(interpolation!=null&&before<interpolation.Count?interpolation[before]:null,(seconds - times[before]) / (times[low] - times[before]));
        return fractions[before] + (fractions[low] - fractions[before]) * alpha;
    }

    /// <summary>Equally spaced samples, including both endpoints. Safe for empty paths.</summary>
    public static Vector3[] SamplePolyline(ActorPathData path, int samples = 80)
    {
        Curve curve = GetCurve(path);
        if (curve.PointCount == 0 || samples <= 0) return Array.Empty<Vector3>();
        if (curve.PointCount == 1 || samples == 1) return new[] { curve.First };
        var positions = new Vector3[samples];
        for (int i = 0; i < samples; i++)
            positions[i] = curve.AtDistance(curve.TotalLength * i / (samples - 1)).Position;
        return positions;
    }

    private static Curve GetCurve(ActorPathData path)
    {
        ArgumentNullException.ThrowIfNull(path);
        CacheEntry entry = Cache.GetValue(path, static _ => new CacheEntry());
        ulong fingerprint = Fingerprint(path.Points);
        lock (entry)
        {
            if (entry.Geometry == null || entry.Fingerprint != fingerprint)
            {
                entry.Geometry = BuildCurve(path.Points);
                entry.Fingerprint = fingerprint;
            }
            return entry.Geometry;
        }
    }

    private static ulong Fingerprint(List<float[]>? points)
    {
        // Stable FNV-1a over the serialized geometry. Timing edits reuse the geometry.
        ulong hash = 14695981039346656037UL;
        static ulong Mix(ulong value, uint number) => unchecked((value ^ number) * 1099511628211UL);
        hash = Mix(hash, (uint)(points?.Count ?? 0));
        if (points == null) return hash;
        foreach (float[]? point in points)
        {
            hash = Mix(hash, (uint)(point?.Length ?? 0));
            if (point == null) continue;
            for (int i = 0; i < Math.Min(point.Length, 3); i++)
                hash = Mix(hash, BitConverter.SingleToUInt32Bits(point[i]));
        }
        return hash;
    }

    private static Curve BuildCurve(List<float[]>? source)
    {
        var points = new List<Vector3>();
        var originalIndices = new int[source?.Count ?? 0];
        if (source != null)
        {
            for (int i = 0; i < source.Count; i++)
            {
                originalIndices[i] = Math.Max(0, points.Count - 1);
                float[]? item = source[i];
                if (item == null || item.Length < 3) continue;
                var point = new Vector3(item[0], item[1], item[2]);
                if (!point.IsFinite()) continue;
                if (points.Count == 0 || points[^1].DistanceSquaredTo(point) > PointEpsilonSquared)
                    points.Add(point);
                originalIndices[i] = points.Count - 1;
            }
        }

        var waypointFractions = new double[originalIndices.Length];
        if (points.Count < 2) return new Curve(points.Count, points.Count == 0 ? Vector3.Zero : points[0], waypointFractions);
        var segments = new Segment[points.Count - 1];
        for (int i = 0; i < segments.Length; i++)
        {
            Vector3 p1 = points[i], p2 = points[i + 1];
            Vector3 p0 = i == 0 ? 2 * p1 - p2 : points[i - 1];
            Vector3 p3 = i + 2 == points.Count ? 2 * p2 - p1 : points[i + 2];
            // Centripetal Catmull-Rom avoids loops/cusps caused by unequal point spacing.
            float d01 = MathF.Sqrt(p0.DistanceTo(p1));
            float d12 = MathF.Sqrt(p1.DistanceTo(p2));
            float d23 = MathF.Sqrt(p2.DistanceTo(p3));
            Vector3 m1 = d12 * ((p1 - p0) / d01 - (p2 - p0) / (d01 + d12) + (p2 - p1) / d12);
            Vector3 m2 = d12 * ((p2 - p1) / d12 - (p3 - p1) / (d12 + d23) + (p3 - p2) / d23);
            segments[i] = new Segment(p1, p2, m1, m2);
        }

        var distances = new double[segments.Length * StepsPerSegment + 1];
        Vector3 previous = points[0];
        for (int i = 1; i < distances.Length; i++)
        {
            int segmentIndex = (i - 1) / StepsPerSegment;
            float parameter = (float)((i - 1) % StepsPerSegment + 1) / StepsPerSegment;
            Vector3 current = segments[segmentIndex].Position(parameter);
            distances[i] = distances[i - 1] + previous.DistanceTo(current);
            previous = current;
        }
        double totalLength = distances[^1];
        if (totalLength > 0)
            for (int i = 0; i < waypointFractions.Length; i++)
                waypointFractions[i] = distances[originalIndices[i] * StepsPerSegment] / totalLength;
        return new Curve(points.Count, points[0], waypointFractions, segments, distances);
    }

    private sealed class CacheEntry
    {
        public ulong Fingerprint;
        public Curve? Geometry;
    }

    private readonly struct Segment
    {
        private readonly Vector3 _a, _b, _c, _d;

        public Segment(Vector3 start, Vector3 end, Vector3 startTangent, Vector3 endTangent)
        {
            _a = 2 * start - 2 * end + startTangent + endTangent;
            _b = -3 * start + 3 * end - 2 * startTangent - endTangent;
            _c = startTangent;
            _d = start;
        }

        public Vector3 Position(float t) => ((_a * t + _b) * t + _c) * t + _d;
        public Vector3 Tangent(float t) => (3 * _a * t + 2 * _b) * t + _c;
    }

    private sealed class Curve
    {
        public int PointCount { get; }
        public Vector3 First { get; }
        public double[] WaypointFractions { get; }
        public double TotalLength => _distances.Length == 0 ? 0 : _distances[^1];
        private readonly Segment[] _segments;
        private readonly double[] _distances;

        public Curve(int pointCount, Vector3 first, double[] waypointFractions, Segment[]? segments = null, double[]? distances = null)
        {
            PointCount = pointCount;
            First = first;
            WaypointFractions = waypointFractions;
            _segments = segments ?? Array.Empty<Segment>();
            _distances = distances ?? Array.Empty<double>();
        }

        public (Vector3 Position, Vector3 Tangent) AtDistance(double distance)
        {
            if (_segments.Length == 0 || TotalLength <= 0) return (First, DefaultTangent);
            distance = Math.Clamp(distance, 0, TotalLength);
            int high = Array.BinarySearch(_distances, distance);
            double sample;
            if (high >= 0) sample = high;
            else
            {
                high = Math.Clamp(~high, 1, _distances.Length - 1);
                double interval = _distances[high] - _distances[high - 1];
                sample = high - 1 + (interval <= 0 ? 0 : (distance - _distances[high - 1]) / interval);
            }

            double globalParameter = sample / StepsPerSegment;
            int index = Math.Min((int)globalParameter, _segments.Length - 1);
            float t = (float)(globalParameter - index);
            Vector3 position = _segments[index].Position(t);
            Vector3 tangent = _segments[index].Tangent(t);
            if (tangent.LengthSquared() <= PointEpsilonSquared)
            {
                // A path may deliberately turn back on itself. Use a stable forward
                // secant at a stationary derivative, independent of the last seek.
                float low = Math.Max(0, t - .001f), upper = Math.Min(1, t + .001f);
                tangent = _segments[index].Position(upper) - _segments[index].Position(low);
            }
            tangent = tangent.LengthSquared() > PointEpsilonSquared ? tangent.Normalized() : DefaultTangent;
            return (position, tangent);
        }
    }
}
