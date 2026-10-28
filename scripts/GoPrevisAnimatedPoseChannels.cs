using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GoPrevis;

public partial class GoPrevisApp
{
    const string PropertyPoseBonePrefix = "Pose.Bones.";
    readonly Dictionary<string, (int Signature, ScalarAnimationCurve[] Curves)> _poseMigrationCache = new(StringComparer.Ordinal);
    readonly Dictionary<string, PropertyAnimationData> _propertyPoseBaselines = new(StringComparer.Ordinal);

    void CapturePropertyPoseBaseline(Entry entry)
    {
        _propertyPoseBaselines[entry.Data.Id] = new PropertyAnimationData
        { Channels = entry.Data.PropertyAnimation.Channels.Where(p => p.Key.StartsWith("Pose.", StringComparison.Ordinal))
            .ToDictionary(p => p.Key, p => PropertyCurveRuntime.Copy(p.Value), StringComparer.Ordinal) };
    }
    void ClearPropertyPoseChannels(Entry entry)
    {
        foreach (string path in entry.Data.PropertyAnimation.Channels.Keys.Where(p => p.StartsWith("Pose.", StringComparison.Ordinal)).ToArray())
            entry.Data.PropertyAnimation.Channels.Remove(path);
    }
    void RestorePropertyPoseBaseline(Entry entry)
    {
        if (!_propertyPoseBaselines.TryGetValue(entry.Data.Id, out var saved)) return;
        ClearPropertyPoseChannels(entry);
        foreach (var pair in saved.Channels) entry.Data.PropertyAnimation.Channels[pair.Key] = PropertyCurveRuntime.Copy(pair.Value);
    }

    IEnumerable<AnimationChannelBinding> PoseAnimationBindings(Entry entry)
    {
        for (int component = 0; component < 3; component++)
        {
            int axis = component;
            string path = "Pose.Root." + "XYZ"[axis];
            yield return BindProperty(entry.Data.Id, entry.Data.Name, "Pose corrections/Pelvis offset", path,
                "XYZ"[axis].ToString(), "m", entry.Data.PropertyAnimation,
                () => PoseAxisBase(entry, path, axis, null, _time),
                value => WritePropertyPoseBase(entry, null, axis, value), -10000, 10000,
                migrate: () => LegacyPoseCurves(entry, null)[axis]);
        }
        IEnumerable<string> names = entry.Node is RiggedActor actor ? actor.PoseBoneNames
            : entry.NativeTrack != null ? entry.NativeTrack.PoseBoneNames : Array.Empty<string>();
        foreach (string name in names.Distinct(StringComparer.Ordinal))
        {
            string bone = name;
            string label = entry.NativeTrack?.PoseBoneDisplayName(bone) ?? PoseBoneLabel(bone);
            for (int component = 0; component < 3; component++)
            {
                int axis = component;
                string path = PoseBonePropertyPath(bone, axis);
                yield return BindProperty(entry.Data.Id, entry.Data.Name, "Pose corrections/" + label + "/Rotation", path,
                    "XYZ"[axis].ToString(), "°", entry.Data.PropertyAnimation,
                    () => PoseAxisBase(entry, path, axis, bone, _time),
                    value => WritePropertyPoseBase(entry, bone, axis, value), -100000, 100000,
                    migrate: () => LegacyPoseCurves(entry, bone)[axis]);
            }
        }
    }

    static string PoseBonePropertyPath(string bone, int axis) => PropertyPoseBonePrefix + bone + ".Rotation." + "XYZ"[axis];
    static bool TryPoseBoneProperty(string path, out string bone, out int axis)
    {
        bone = ""; axis = -1;
        if (!path.StartsWith(PropertyPoseBonePrefix, StringComparison.Ordinal)) return false;
        int suffix = path.LastIndexOf(".Rotation.", StringComparison.Ordinal);
        if (suffix <= PropertyPoseBonePrefix.Length || path.Length != suffix + 11) return false;
        axis = "XYZ".IndexOf(path[^1]);
        if (axis < 0) return false;
        bone = path[PropertyPoseBonePrefix.Length..suffix];
        return true;
    }
    static Vector3 LegacyPoseEuler(Entry entry, string bone, double time)
    {
        entry.Data.PoseCorrections.Bones.TryGetValue(bone, out var keys);
        return PoseCorrectionRuntime.EvaluateRotation(keys, time).GetEuler() * (180f / Mathf.Pi);
    }
    static double PoseAxisBase(Entry entry, string path, int axis, string? bone, double time)
    {
        // A deliberately emptied split channel suppresses that legacy component.
        if (entry.Data.PropertyAnimation.Channels.ContainsKey(path)) return 0;
        return bone == null ? PoseCorrectionRuntime.EvaluateRoot(entry.Data.PoseCorrections, time)[axis]
            : LegacyPoseEuler(entry, bone, time)[axis];
    }
    void WritePropertyPoseBase(Entry entry, string? bone, int axis, double value)
    {
        var data = entry.Data.PoseCorrections;
        if (bone == null)
        {
            var root = PoseCorrectionRuntime.EvaluateRoot(data, _time); root[axis] = (float)value;
            if (_time > 0 && data.Root.Count == 0) TransformLayerRuntime.Upsert(data.Root, 0, Vector3.Zero);
            TransformLayerRuntime.Upsert(data.Root, _time, root);
            string path = "Pose.Root." + "XYZ"[axis];
            if (entry.Data.PropertyAnimation.Channels.TryGetValue(path, out var curve)) PropertyCurveRuntime.Upsert(curve, _time, value);
        }
        else
        {
            var angles = LegacyPoseEuler(entry, bone, _time); angles[axis] = (float)value;
            if (!data.Bones.TryGetValue(bone, out var keys)) data.Bones[bone] = keys = new();
            if (_time > 0 && keys.Count == 0) PoseCorrectionRuntime.UpsertRotation(keys, 0, Quaternion.Identity);
            PoseCorrectionRuntime.UpsertRotation(keys, _time, Quaternion.FromEuler(angles * (Mathf.Pi / 180f)));
            string path = PoseBonePropertyPath(bone, axis);
            if (entry.Data.PropertyAnimation.Channels.TryGetValue(path, out var curve)) PropertyCurveRuntime.Upsert(curve, _time, value);
        }
    }

    ScalarAnimationCurve[] LegacyPoseCurves(Entry entry, string? bone)
    {
        var data = entry.Data.PoseCorrections;
        var hash = new HashCode(); hash.Add(_fps);
        double[] sourceTimes;
        if (bone == null)
        {
            foreach (var key in data.Root)
            {
                hash.Add(key.Time); foreach (float value in key.Value ?? Array.Empty<float>()) hash.Add(value);
                PoseInterpolationHash(ref hash, key.Interpolation);
            }
            sourceTimes = data.Root.Where(k => double.IsFinite(k.Time) && k.Time >= 0).Select(k => k.Time).Distinct().OrderBy(t => t).ToArray();
        }
        else
        {
            data.Bones.TryGetValue(bone, out var keys);
            foreach (var key in keys ?? new())
            {
                hash.Add(key.Time); foreach (float value in key.Rotation ?? Array.Empty<float>()) hash.Add(value);
                PoseInterpolationHash(ref hash, key.Interpolation);
            }
            sourceTimes = (keys ?? new()).Where(PoseCorrectionRuntime.ValidRotation).Select(k => k.Time).Distinct().OrderBy(t => t).ToArray();
        }
        string cacheId = entry.Data.Id + "/" + (bone == null ? "root-offset" : "bone/" + bone);
        int signature = hash.ToHashCode();
        if (_poseMigrationCache.TryGetValue(cacheId, out var cached) && cached.Signature == signature) return cached.Curves;
        var curves = new[] { new ScalarAnimationCurve(), new ScalarAnimationCurve(), new ScalarAnimationCurve() };
        if (sourceTimes.Length > 0)
        {
            var samples = new SortedSet<double>(sourceTimes);
            long first = (long)Math.Ceiling(sourceTimes[0] * Math.Max(1, _fps));
            long last = (long)Math.Floor(sourceTimes[^1] * Math.Max(1, _fps));
            for (long frame = first; frame <= last; frame++) samples.Add(frame / (double)Math.Max(1, _fps));
            var values = new List<(double Time, double[] Value)>();
            double[]? previous = null;
            foreach (double time in samples)
            {
                Vector3 sample = bone == null ? PoseCorrectionRuntime.EvaluateRoot(data, time) : LegacyPoseEuler(entry, bone, time);
                var xyz = new[] { (double)sample.X, sample.Y, sample.Z };
                if (bone != null && previous != null)
                    for (int axis = 0; axis < 3; axis++) xyz[axis] = previous[axis] + WrapPoseDegrees(xyz[axis] - previous[axis]);
                values.Add((time, xyz)); previous = xyz;
            }
            // Preserve the sampled quaternion motion while dropping only nearly
            // exact linear samples; the original authored times remain anchors.
            for (int axis = 0; axis < 3; axis++)
            {
                int component = axis;
                var retained = SimplifyPoseSamples(values, component, sourceTimes, bone == null ? 1e-7 : 1e-5);
                foreach (int index in retained)
                {
                    var sample = values[index];
                    var key = PropertyCurveRuntime.Upsert(curves[axis], sample.Time, sample.Value[axis]);
                    key.Id = "legacy-pose:" + (bone ?? "Root offset") + ":" + axis + ":" + sample.Time.ToString("R", CultureInfo.InvariantCulture);
                }
            }
        }
        _poseMigrationCache[cacheId] = (signature, curves);
        return curves;
    }
    static void PoseInterpolationHash(ref HashCode hash, KeyInterpolationData? interpolation)
    {
        if (interpolation == null) return;
        hash.Add(interpolation.Mode);
        foreach (float value in interpolation.Control1 ?? Array.Empty<float>()) hash.Add(value);
        foreach (float value in interpolation.Control2 ?? Array.Empty<float>()) hash.Add(value);
    }
    static double WrapPoseDegrees(double value) => value - 360 * Math.Floor((value + 180) / 360);
    static int[] SimplifyPoseSamples(List<(double Time, double[] Value)> points, int axis, double[] anchors, double tolerance)
    {
        var keep = new SortedSet<int>();
        if (points.Count == 0) return Array.Empty<int>();
        keep.Add(0); keep.Add(points.Count - 1);
        for (int i = 0; i < points.Count; i++) if (Array.BinarySearch(anchors, points[i].Time) >= 0) keep.Add(i);
        var spans = keep.Zip(keep.Skip(1), (first, last) => (first, last)).ToArray();
        var pending = new Stack<(int First, int Last)>(spans);
        while (pending.Count > 0)
        {
            var (first, last) = pending.Pop(); if (last <= first + 1) continue;
            double duration = points[last].Time - points[first].Time;
            double error = tolerance; int best = -1;
            for (int i = first + 1; i < last; i++)
            {
                double progress = duration <= 0 ? 0 : (points[i].Time - points[first].Time) / duration;
                double linear = points[first].Value[axis] + progress * (points[last].Value[axis] - points[first].Value[axis]);
                double difference = Math.Abs(points[i].Value[axis] - linear);
                if (difference > error) { error = difference; best = i; }
            }
            if (best < 0) continue;
            keep.Add(best); pending.Push((first, best)); pending.Push((best, last));
        }
        return keep.ToArray();
    }

    PoseCorrectionAnimationData EvaluatedPropertyPose(Entry entry, double time)
    {
        var original = entry.Data.PoseCorrections;
        var channels = entry.Data.PropertyAnimation.Channels;
        var rotations = channels.Keys.Where(path => TryPoseBoneProperty(path, out _, out _))
            .Select(path => { TryPoseBoneProperty(path, out string name, out _); return name; }).Distinct(StringComparer.Ordinal).ToArray();
        bool root = channels.Keys.Any(path => path is "Pose.Root.X" or "Pose.Root.Y" or "Pose.Root.Z");
        if (!root && rotations.Length == 0) return original;
        var evaluated = new PoseCorrectionAnimationData
        { Bones = new Dictionary<string, List<PoseCorrectionKeyData>>(original.Bones, StringComparer.Ordinal), Root = original.Root };
        if (root)
        {
            Vector3 offset = PoseCorrectionRuntime.EvaluateRoot(original, time);
            for (int axis = 0; axis < 3; axis++)
                if (channels.TryGetValue("Pose.Root." + "XYZ"[axis], out var curve)) offset[axis] = (float)PropertyCurveRuntime.Evaluate(curve, time, 0);
            evaluated.Root = new() { new TransformKeyData { Id = "property-pose-root", Time = time, Value = new[] { offset.X, offset.Y, offset.Z } } };
        }
        foreach (string bone in rotations)
        {
            Vector3 angles = LegacyPoseEuler(entry, bone, time);
            for (int axis = 0; axis < 3; axis++)
                if (channels.TryGetValue(PoseBonePropertyPath(bone, axis), out var curve)) angles[axis] = (float)PropertyCurveRuntime.Evaluate(curve, time, 0);
            Quaternion delta = Quaternion.FromEuler(angles * (Mathf.Pi / 180f)).Normalized();
            evaluated.Bones[bone] = new() { new PoseCorrectionKeyData { Id = "property-pose:" + bone, Time = time, Rotation = PoseCorrectionRuntime.Values(delta) } };
        }
        return evaluated;
    }

    /// <summary>Call after legacy IK/pose writes, before SetTime reevaluates the figure.</summary>
    void SyncPropertyPoseEdits(Entry entry, IEnumerable<string>? bones = null, bool includeRoot = true)
    {
        var channels = entry.Data.PropertyAnimation.Channels;
        if (includeRoot)
        {
            Vector3 value = PoseCorrectionRuntime.EvaluateRoot(entry.Data.PoseCorrections, _time);
            for (int axis = 0; axis < 3; axis++)
                if (channels.TryGetValue("Pose.Root." + "XYZ"[axis], out var curve)) PropertyCurveRuntime.Upsert(curve, _time, value[axis]);
        }
        foreach (string bone in bones ?? entry.Data.PoseCorrections.Bones.Keys)
        {
            Vector3 angles = LegacyPoseEuler(entry, bone, _time);
            for (int axis = 0; axis < 3; axis++)
            {
                if (!channels.TryGetValue(PoseBonePropertyPath(bone, axis), out var curve)) continue;
                double previous = PropertyCurveRuntime.Evaluate(curve, _time, angles[axis]);
                double value = previous + WrapPoseDegrees(angles[axis] - previous);
                PropertyCurveRuntime.Upsert(curve, _time, value);
            }
        }
    }
}
