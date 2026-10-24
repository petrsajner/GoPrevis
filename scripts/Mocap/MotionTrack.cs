using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Previs;

public partial class RiggedActor
{
    /// <summary>Number of prepared clips; changing clip timing never reloads a cached source.</summary>
    public int MotionTrackClipCount => _motionTrack.Length;
    public int MotionTrackPreparedSourceCount => _preparedMotions.Count;
    public double MotionTrackDurationSeconds => _motionTrack.Length == 0 ? 0 : _motionTrack.Max(clip => clip.End);
    public string MotionTrackSummary => _motionTrack.Length == 0
        ? _staticPose == null ? "No motion clips. Import a BVH motion to begin." : "Static pose · " + _staticPose.Name
        : $"{_motionTrack.Length} clips · {MotionTrackDurationSeconds:0.##} s · quaternion crossfades · continuous root motion. Overlap blends use the incoming clip's Blend In; gaps hold the previous pose.";

    private PreparedMotionClip[] _motionTrack = Array.Empty<PreparedMotionClip>();
    private readonly Dictionary<string, PreparedMotion> _preparedMotions = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private Quaternion[] _trackPose = Array.Empty<Quaternion>();
    private Quaternion[] _trackIncomingPose = Array.Empty<Quaternion>();

    private sealed class PreparedMotion
    {
        public required BvhMotion Motion;
        public required int[] Mapping;
        public required Quaternion[] Corrections;
        public required Vector3[] RootCurve;
        public required Quaternion[] SourceRotations;
        public required Quaternion[] GlobalRotations;
        public required float Scale;
        public float GroundCorrection;
        public Vector3 RestForward;
    }

    private sealed class PreparedMotionClip
    {
        public required PreparedMotion Source;
        public required string Name;
        public double Start, Duration, SourceOffset, Speed, FadeDuration;
        public bool Loop, InPlace;
        public Vector3 Alignment;
        public double End => Start + Duration;
        public double SourceTime(double sceneSeconds) => SourceOffset + Math.Clamp(sceneSeconds - Start, 0, Duration) * Speed;
    }

    /// <summary>
    /// Atomically snapshots and prepares a BVH track. Later starts take precedence. An incoming clip
    /// crossfades only during its overlap with its predecessor, for at most BlendIn seconds. A gap
    /// holds the preceding endpoint. Root translation is aligned at every incoming start, so clips
    /// from unrelated capture origins do not teleport. No source files are read during evaluation.
    /// </summary>
    public void SetMotionClips(IReadOnlyList<MotionClipData> clips)
    {
        if (clips == null) throw new ArgumentNullException(nameof(clips));
        EnsureRig();
        var stagedSources = new Dictionary<string, PreparedMotion>(_preparedMotions.Comparer);
        var stagedClips = new List<PreparedMotionClip>();
        // Stable ordering makes a deliberate same-start cut deterministic.
        foreach (MotionClipData data in clips.OrderBy(clip => clip?.Start ?? 0))
        {
            if (data == null) throw new FormatException("A motion track contains an empty clip.");
            string label = string.IsNullOrWhiteSpace(data.Name) ? "Motion" : data.Name;
            void Valid(bool condition, string message) { if (!condition) throw new FormatException($"{label}: {message}"); }
            Valid(double.IsFinite(data.Start) && data.Start >= 0, "Clip start must be a finite time of zero or greater.");
            Valid(double.IsFinite(data.Duration) && data.Duration > 0 && double.IsFinite(data.Start + data.Duration), "Clip duration must be a positive finite time.");
            Valid(double.IsFinite(data.SourceOffset) && data.SourceOffset >= 0, "Source offset must be a finite time of zero or greater.");
            Valid(double.IsFinite(data.Speed) && data.Speed > 0 && double.IsFinite(data.SourceOffset + data.Duration * data.Speed), "Playback speed must be a positive finite value.");
            Valid(double.IsFinite(data.BlendIn) && data.BlendIn >= 0, "Blend In must be a finite time of zero or greater.");
            Valid(float.IsFinite(data.SourceScale) && data.SourceScale > 0 && data.SourceScale <= 1000, "Source scale must be positive and no greater than 1000.");
            Valid(!string.IsNullOrWhiteSpace(data.MotionPath), "Choose a BVH motion file.");
            string path;
            try { path = Path.GetFullPath(data.MotionPath); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            { throw new FormatException($"{label}: The motion file path is invalid.", ex); }
            string key = path + "\n" + data.SourceScale.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (!_preparedMotions.TryGetValue(key, out PreparedMotion? source) && !stagedSources.TryGetValue(key, out source))
            {
                source = PrepareTrackMotion(path, data.SourceScale);
                stagedSources.Add(key, source);
            }
            var clip = new PreparedMotionClip
            {
                Source = source, Name = label, Start = data.Start, Duration = data.Duration,
                SourceOffset = data.SourceOffset, Speed = data.Speed, Loop = data.Loop, InPlace = data.InPlace
            };
            // Align against the preceding composite at the exact overlap/gap boundary, rather
            // than against a previously played frame. Random-access scrubbing is then identical.
            Vector3 previousRoot = stagedClips.Count == 0 ? Vector3.Up * source.GroundCorrection : EvaluateTrackRoot(stagedClips, clip.Start);
            clip.Alignment = previousRoot - SampleClipRoot(clip, clip.Start);
            if (stagedClips.Count > 0)
                clip.FadeDuration = Math.Min(data.BlendIn, Math.Min(clip.Duration, Math.Max(0, stagedClips[^1].End - clip.Start)));
            stagedClips.Add(clip);
        }
        // No playable state or shared cache changes until every source and clip has succeeded.
        foreach (var item in stagedSources) _preparedMotions.Add(item.Key, item.Value);
        _motionTrack = stagedClips.ToArray();
        if (_trackPose.Length != _bones.Count)
        {
            _trackPose = new Quaternion[_bones.Count];
            _trackIncomingPose = new Quaternion[_bones.Count];
        }
        if (_motionTrack.Length == 0) { ClearMotion(); ApplyStaticPose(); return; }
        // Preserve the source-statistics API for callers opening a saved track directly.
        PreparedMotion first = _motionTrack[0].Source;
        _motion = first.Motion; _sourceMap = first.Mapping; _corrections = first.Corrections;
        _rootCurve = first.RootCurve; _sourceRotations = first.SourceRotations;
        _sourceScale = first.Scale; _initialGroundCorrection = first.GroundCorrection; _sourceForward = first.RestForward;
        MotionName = _motionTrack[0].Name;
        ImportSummary = MotionTrackSummary;
        EvaluateTimeline(0);
    }

    /// <summary>Evaluates absolute scene time. forceInPlace removes horizontal BVH displacement for drawn actor paths.</summary>
    public void EvaluateTimeline(double sceneSeconds, bool forceInPlace = false)
    {
        EnsureRig();
        if (_motionTrack.Length == 0) { if (_staticPose != null) ApplyStaticPose(); return; }
        RestoreMotionPicker();
        if (!double.IsFinite(sceneSeconds)) sceneSeconds = 0;
        PreparedMotionClip first = _motionTrack[0];
        SampleTrackPose(first, sceneSeconds, _trackPose);
        Vector3 root = SampleClipRoot(first, sceneSeconds) + first.Alignment;
        for (int i = 1; i < _motionTrack.Length; i++)
        {
            PreparedMotionClip clip = _motionTrack[i];
            if (sceneSeconds < clip.Start) break;
            SampleTrackPose(clip, sceneSeconds, _trackIncomingPose);
            float weight = ClipWeight(clip, sceneSeconds);
            for (int bone = 0; bone < _bones.Count; bone++)
                _trackPose[bone] = _trackPose[bone].Slerp(_trackIncomingPose[bone], weight).Normalized();
            root = root.Lerp(SampleClipRoot(clip, sceneSeconds) + clip.Alignment, weight);
        }
        if (forceInPlace) { root.X = 0; root.Z = 0; }
        // Derive locomotion alignment from the FINAL blended prepared-rig pose.
        // Interpolating separate source-facing metadata here fights path/manual layers.
        Vector3 facing=_trackPose[0]*Vector3.Forward;facing.Y=0;
        if(facing.LengthSquared()<.00001f){var right=_trackPose[0]*Vector3.Right;right.Y=0;facing=Vector3.Up.Cross(right);}
        AnimatedForward=HorizontalFacing(facing);
        if (_faceMarker != null)
        {
            _faceMarker.Position = Vector3.Forward * .101f + Vector3.Up * .10f;
            _faceMarker.Quaternion = Quaternion.Identity;
        }
        ApplyTrackPose(root);
    }

    private PreparedMotion PrepareTrackMotion(string path, float scale)
    {
        BvhMotion motion = BvhMotion.FromFile(path);
        int[] map = CreateMapping(motion);
        if (map[0] < 0) throw new FormatException("No humanoid hips/pelvis joint was found. Use a humanoid BVH with recognizable joint names.");
        int mapped = map.Count(index => index >= 0);
        if (mapped < 7) throw new FormatException($"Only {mapped} humanoid joints could be mapped. At least seven recognizable spine and limb joints are required.");
        var corrections = new Quaternion[_bones.Count];
        for (int i = 0; i < map.Length; i++)
        {
            corrections[i] = Quaternion.Identity;
            if (map[i] < 0) continue;
            Vector3 aim = SourceAim(motion, map, i);
            if (aim.LengthSquared() > .00001f && _bones[i].Aim.LengthSquared() > .00001f)
                corrections[i] = Align(_bones[i].Aim.Normalized(), aim.Normalized());
        }
        var source = new PreparedMotion
        {
            Motion = motion, Mapping = map, Corrections = corrections, Scale = scale,
            RootCurve = motion.BuildRootCurve(map[0]), SourceRotations = new Quaternion[motion.Joints.Count],
            GlobalRotations = new Quaternion[_bones.Count],
            RestForward = HorizontalFacing(SourceAim(motion, map, FindBone("LeftFoot")) + SourceAim(motion, map, FindBone("RightFoot")))
        };
        // Ground calibration uses temporary math only. Preparing a failed track cannot change
        // the currently displayed skeleton, its picker, or any playable legacy motion.
        var locals = new Quaternion[_bones.Count];
        SamplePreparedPose(source, 0, false, locals);
        var positions = new Vector3[_bones.Count];
        for (int i = 0; i < _bones.Count; i++)
        {
            int parent = _bones[i].Parent;
            positions[i] = parent < 0 ? _bones[i].Offset : positions[parent] + source.GlobalRotations[parent] * _bones[i].Offset;
        }
        float Minimum(int foot)
        {
            float minimum = float.PositiveInfinity;
            foreach (float x in new[] { -.064f, .064f })
            foreach (float y in new[] { -.070f, .020f })
            foreach (float z in new[] { -.185f, .065f })
                minimum = Math.Min(minimum, (positions[foot] + source.GlobalRotations[foot] * new Vector3(x, y, z)).Y);
            return minimum;
        }
        source.GroundCorrection = .012f - Math.Min(Minimum(FindBone("LeftFoot")), Minimum(FindBone("RightFoot")));
        return source;
    }

    private void SampleTrackPose(PreparedMotionClip clip, double sceneSeconds, Quaternion[] locals)
        => SamplePreparedPose(clip.Source, clip.SourceTime(sceneSeconds), clip.Loop, locals);

    private void SamplePreparedPose(PreparedMotion source, double time, bool loop, Quaternion[] locals)
    {
        source.Motion.Sample(time, loop, source.SourceRotations);
        for (int i = 0; i < _bones.Count; i++)
        {
            int parent = _bones[i].Parent;
            Quaternion parentRotation = parent < 0 ? Quaternion.Identity : source.GlobalRotations[parent];
            source.GlobalRotations[i] = source.Mapping[i] >= 0
                ? (source.SourceRotations[source.Mapping[i]] * source.Corrections[i]).Normalized() : parentRotation;
            locals[i] = (parentRotation.Inverse() * source.GlobalRotations[i]).Normalized();
        }
    }

    private static Vector3 SampleClipRoot(PreparedMotionClip clip, double sceneSeconds)
    {
        Vector3 root = clip.Source.Motion.SampleRootCurve(clip.Source.RootCurve, clip.SourceTime(sceneSeconds), clip.Loop) * clip.Source.Scale;
        if (clip.InPlace) { root.X = 0; root.Z = 0; }
        return root + Vector3.Up * clip.Source.GroundCorrection;
    }

    private static float ClipWeight(PreparedMotionClip clip, double time)
        => clip.FadeDuration <= 0 ? 1 : (float)Math.Clamp((time - clip.Start) / clip.FadeDuration, 0, 1);

    private static Vector3 EvaluateTrackRoot(IReadOnlyList<PreparedMotionClip> clips, double time)
    {
        Vector3 root = SampleClipRoot(clips[0], time) + clips[0].Alignment;
        for (int i = 1; i < clips.Count; i++)
        {
            if (time < clips[i].Start) break;
            root = root.Lerp(SampleClipRoot(clips[i], time) + clips[i].Alignment, ClipWeight(clips[i], time));
        }
        return root;
    }

    private static Vector3 ClipFacing(PreparedMotionClip clip)
        => HorizontalFacing(clip.Source.GlobalRotations[0] * clip.Source.RestForward);

    private static Vector3 HorizontalFacing(Vector3 direction)
    {
        direction.Y = 0;
        return direction.LengthSquared() > .00001f ? direction.Normalized() : Vector3.Forward;
    }

    private static Vector3 BlendFacing(Vector3 from, Vector3 to, float weight)
    {
        float yaw = Mathf.LerpAngle(Mathf.Atan2(from.X, from.Z), Mathf.Atan2(to.X, to.Z), weight);
        return new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
    }

    private void ApplyTrackPose(Vector3 root)
    {
        if (_skeleton == null) return;
        for (int i = 0; i < _bones.Count; i++)
        {
            int parent = _bones[i].Parent;
            Quaternion parentRotation = parent < 0 ? Quaternion.Identity : _targetRotations[parent];
            _targetRotations[i] = (parentRotation * _trackPose[i]).Normalized();
            Vector3 localPosition = _bones[i].Offset + (parent < 0 ? root : Vector3.Zero);
            _targetPositions[i] = parent < 0 ? localPosition : _targetPositions[parent] + parentRotation * localPosition;
            _skeleton.SetBonePosePosition(i, localPosition);
            _skeleton.SetBonePoseRotation(i, _trackPose[i]);
        }
        float minimum = Math.Min(FootMinimum(FindBone("LeftFoot")), FootMinimum(FindBone("RightFoot")));
        root.Y += Math.Max(0, .005f - minimum);
        RootMotionOffset = root;
        _skeleton.SetBonePosePosition(0, _bones[0].Offset + root);
        if (_pickBody != null) _pickBody.Position = root;
    }
}
