using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Previs;

/// <summary>The prototype's prepared humanoid. BVH motion is retargeted to its fixed proportions.</summary>
public partial class RiggedActor : Node3D
{
    public string MotionName { get; private set; } = "No motion";
    public double DurationSeconds => _motion?.DurationSeconds ?? 0;
    public double SourceFps => _motion?.SourceFps ?? 0;
    public int JointCount => _motion?.AnimatedJointCount ?? 0;
    public string ImportSummary { get; private set; } = "Prepared humanoid · import a BVH motion or load the included synthetic sample.";
    public Vector3 RootMotionOffset { get; private set; } = Vector3.Zero;
    /// <summary>Current source-facing direction in actor-local XZ, including evaluated hip yaw.</summary>
    public Vector3 AnimatedForward { get; private set; } = Vector3.Forward;

    private Skeleton3D? _skeleton;
    private StaticBody3D? _pickBody;
    private MeshInstance3D? _faceMarker;
    private BvhMotion? _motion;
    private int[] _sourceMap = Array.Empty<int>();
    private Quaternion[] _corrections = Array.Empty<Quaternion>();
    private Quaternion[] _sourceRotations = Array.Empty<Quaternion>();
    private Quaternion[] _targetRotations = Array.Empty<Quaternion>();
    private Vector3[] _targetPositions = Array.Empty<Vector3>();
    private Vector3[] _rootCurve = Array.Empty<Vector3>();
    private float _sourceScale = 0.01f;
    private float _initialGroundCorrection;
    private Vector3 _sourceForward = Vector3.Forward;
    private readonly List<BoneDefinition> _bones = new();
    private StandardMaterial3D? _bodyMaterial;
    private StandardMaterial3D? _jointMaterial;

    private sealed record BoneDefinition(string Name, int Parent, Vector3 Offset, Vector3 Aim, string[] Aliases);

    public override void _Ready() => EnsureRig();

    public void LoadBvh(string absolutePath, float sourceScale = 0.01f)
    {
        if (!float.IsFinite(sourceScale) || sourceScale <= 0 || sourceScale > 1000)
            throw new FormatException("The source scale must be a positive finite value no greater than 1000.");
        EnsureRig();
        // Parse and prepare entirely before changing the currently playable motion.
        BvhMotion candidate = BvhMotion.FromFile(absolutePath);
        int[] map = CreateMapping(candidate);
        if (map[0] < 0) throw new FormatException("No humanoid hips/pelvis joint was found. Use a humanoid BVH with Hips, Pelvis, or a standard Mixamo/CMU equivalent.");
        int mapped = map.Count(index => index >= 0);
        if (mapped < 7) throw new FormatException($"Only {mapped} humanoid joints could be mapped. This prototype requires a humanoid BVH with recognizable spine and limb names.");
        var corrections = new Quaternion[_bones.Count];
        for (int i = 0; i < map.Length; i++)
        {
            corrections[i] = Quaternion.Identity;
            if (map[i] < 0) continue;
            Vector3 sourceAim = SourceAim(candidate, map, i);
            if (sourceAim.LengthSquared() > 0.00001f && _bones[i].Aim.LengthSquared() > 0.00001f)
                corrections[i] = Align(_bones[i].Aim.Normalized(), sourceAim.Normalized());
        }
        Vector3[] rootCurve = candidate.BuildRootCurve(map[0]);
        var mappedSources = map.Where(index => index >= 0).ToHashSet();
        string[] unmapped = candidate.Joints.Select((joint, index) => (joint, index))
            .Where(item => !item.joint.IsEndSite && !mappedSources.Contains(item.index))
            .Select(item => item.joint.Name).ToArray();
        string[] missing = _bones.Select((bone, index) => (bone, index)).Where(item => map[item.index] < 0).Select(item => item.bone.Name).ToArray();
        string summary = $"{candidate.FrameCount} frames · {candidate.SourceFps:0.##} fps · {candidate.DurationSeconds:0.##} s · {mapped}/{_bones.Count} prepared joints mapped. Y-up source; scale {sourceScale:0.####} m/unit. Fixed target proportions; no foot IK.";
        if (unmapped.Length > 0) summary += " Unmapped source joints: " + string.Join(", ", unmapped) + ".";
        if (missing.Length > 0) summary += " Prepared joints without source: " + string.Join(", ", missing) + ".";
        if (candidate.HasNonRootTranslation) summary += " Local translations contribute to the hip path; limb lengths stay fixed.";
        _motion = candidate;
        _sourceMap = map;
        _corrections = corrections;
        _rootCurve = rootCurve;
        _sourceRotations = new Quaternion[candidate.Joints.Count];
        _sourceScale = sourceScale;
        _initialGroundCorrection = 0;
        // BVH collections use both +Z and -Z as character forward. Match the
        // small heading marker to the foot-rest direction rather than reversing the motion.
        Vector3 sourceForward = SourceAim(candidate, map, FindBone("LeftFoot")) + SourceAim(candidate, map, FindBone("RightFoot"));
        sourceForward.Y = 0;
        _sourceForward = sourceForward.LengthSquared() > 0.00001f ? sourceForward.Normalized() : Vector3.Forward;
        if (_faceMarker != null && sourceForward.LengthSquared() > 0.00001f)
        {
            sourceForward = sourceForward.Normalized();
            _faceMarker.Position = sourceForward * 0.101f + Vector3.Up * 0.10f;
            _faceMarker.Quaternion = Align(Vector3.Forward, sourceForward);
        }
        MotionName = Path.GetFileNameWithoutExtension(absolutePath);
        ImportSummary = summary;
        ApplyPose(0, false, false, calibrateGround: true);
        ApplyTime(0, false);
    }

    public void ApplyTime(double seconds, bool loop = true, bool inPlace = false, double speed = 1.0)
    {
        EnsureRig();
        if (_motion == null) return;
        if (!double.IsFinite(speed)) speed = 1;
        ApplyPose(seconds * speed, loop, inPlace, calibrateGround: false);
    }

    public void ClearMotion()
    {
        EnsureRig();
        _motion = null;
        _motionTrack = Array.Empty<PreparedMotionClip>();
        MotionName = "No motion";
        ImportSummary = "Prepared humanoid · import a BVH motion or load the included synthetic sample.";
        RootMotionOffset = Vector3.Zero;
        AnimatedForward = Vector3.Forward;
        if (_pickBody != null) _pickBody.Position = Vector3.Zero;
        SetNeutralPose();
    }

    /// <summary>Sets the mannequin's matte body color without replacing its prepared rig.</summary>
    public void SetBodyColor(Color color)
    {
        EnsureRig();
        if (_bodyMaterial != null) _bodyMaterial.AlbedoColor = color;
    }

    private void ApplyPose(double time, bool loop, bool inPlace, bool calibrateGround)
    {
        if (_motion == null || _skeleton == null) return;
        _motion.Sample(time, loop, _sourceRotations);
        Vector3 rootMotion = _motion.SampleRootCurve(_rootCurve, time, loop) * _sourceScale;
        if (inPlace) { rootMotion.X = 0; rootMotion.Z = 0; }
        for (int i = 0; i < _bones.Count; i++)
        {
            int parent = _bones[i].Parent;
            Quaternion parentRotation = parent >= 0 ? _targetRotations[parent] : Quaternion.Identity;
            _targetRotations[i] = _sourceMap[i] >= 0
                ? (_sourceRotations[_sourceMap[i]] * _corrections[i]).Normalized()
                : parentRotation;
            Quaternion localRotation = (parentRotation.Inverse() * _targetRotations[i]).Normalized();
            Vector3 localPosition = _bones[i].Offset;
            if (parent < 0) localPosition += rootMotion + Vector3.Up * _initialGroundCorrection;
            _targetPositions[i] = parent < 0 ? localPosition : _targetPositions[parent] + parentRotation * localPosition;
            _skeleton.SetBonePosePosition(i, localPosition);
            _skeleton.SetBonePoseRotation(i, localRotation);
        }
        AnimatedForward = HorizontalFacing(_targetRotations[0] * _sourceForward);
        float minFoot = FootMinimum(FindBone("LeftFoot"));
        minFoot = Math.Min(minFoot, FootMinimum(FindBone("RightFoot")));
        if (calibrateGround)
        {
            _initialGroundCorrection = 0.012f - minFoot;
            return;
        }
        // A floor guard prevents penetration; it is deliberately not advertised as foot locking.
        float floorCorrection = Math.Max(0, 0.005f - minFoot);
        RootMotionOffset = rootMotion + Vector3.Up * (_initialGroundCorrection + floorCorrection);
        if (_pickBody != null) _pickBody.Position = RootMotionOffset;
        if (floorCorrection > 0)
            _skeleton.SetBonePosePosition(0, _bones[0].Offset + RootMotionOffset);
    }

    private float FootMinimum(int foot)
    {
        float minimum = float.PositiveInfinity;
        foreach (float x in new[] { -0.064f, 0.064f })
        foreach (float y in new[] { -0.070f, 0.020f })
        foreach (float z in new[] { -0.185f, 0.065f })
            minimum = Math.Min(minimum, (_targetPositions[foot] + _targetRotations[foot] * new Vector3(x, y, z)).Y);
        return minimum;
    }

    private void EnsureRig()
    {
        if (_skeleton != null) return;
        DefineBones();
        _targetRotations = new Quaternion[_bones.Count];
        _targetPositions = new Vector3[_bones.Count];
        _skeleton = new Skeleton3D { Name = "PreparedHumanoid" };
        AddChild(_skeleton);
        _pickBody = new StaticBody3D { Name = "PickBody", CollisionLayer = 1, CollisionMask = 0 };
        _pickBody.SetMeta("scene_root", this);
        _pickBody.SetMeta("actor", this);
        _pickBody.AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.36f, Height = 1.8f },
            Position = new Vector3(0, 0.9f, 0)
        });
        AddChild(_pickBody);
        _bodyMaterial = new StandardMaterial3D { AlbedoColor = new Color("ccd5e7"), Roughness = 0.78f, Metallic = 0.04f };
        _jointMaterial = new StandardMaterial3D { AlbedoColor = new Color("676486"), Roughness = 0.74f };
        var accent = new StandardMaterial3D { AlbedoColor = new Color("9a8ff4"), Roughness = 0.65f };
        for (int i = 0; i < _bones.Count; i++)
        {
            var bone = _bones[i];
            _skeleton.AddBone(bone.Name);
            _skeleton.SetBoneParent(i, bone.Parent);
            _skeleton.SetBoneRest(i, new Transform3D(Basis.Identity, bone.Offset));
        }
        AddSegment("Hips", new Vector3(0, 0.13f, 0), 0.115f, _bodyMaterial);
        AddSegment("Spine", new Vector3(0, 0.18f, 0), 0.13f, _bodyMaterial);
        AddSegment("Chest", new Vector3(0, 0.145f, 0), 0.148f, _bodyMaterial);
        AddSegment("Neck", new Vector3(0, 0.12f, 0), 0.048f, _jointMaterial);
        var head = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.108f, Height = 0.255f, RadialSegments = 24, Rings = 12 },
            Position = new Vector3(0, 0.087f, 0), MaterialOverride = _bodyMaterial
        };
        Attachment("Head").AddChild(head);
        // A small face marker makes heading readable in a flat-shaded previs render.
        _faceMarker = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.105f, 0.035f, 0.018f) },
            Position = new Vector3(0, 0.10f, -0.101f), MaterialOverride = accent
        };
        Attachment("Head").AddChild(_faceMarker);
        foreach (string side in new[] { "Left", "Right" })
        {
            float sign = side == "Left" ? 1 : -1;
            AddSegment(side + "Shoulder", new Vector3(sign * 0.10f, 0, 0), 0.055f, _bodyMaterial);
            AddSegment(side + "UpperArm", new Vector3(sign * 0.265f, 0, 0), 0.047f, _bodyMaterial);
            AddSegment(side + "LowerArm", new Vector3(sign * 0.235f, 0, 0), 0.038f, _bodyMaterial);
            AddSegment(side + "Hand", new Vector3(sign * 0.10f, 0, 0), 0.036f, _bodyMaterial);
            AddSegment(side + "UpperLeg", new Vector3(0, -0.410f, 0), 0.068f, _bodyMaterial);
            AddSegment(side + "LowerLeg", new Vector3(0, -0.405f, 0), 0.051f, _bodyMaterial);
            Attachment(side + "Foot").AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.128f, 0.09f, 0.25f) },
                Position = new Vector3(0, -0.025f, -0.06f), MaterialOverride = _jointMaterial
            });
            foreach (string jointName in new[] { "UpperArm", "LowerArm", "Hand", "UpperLeg", "LowerLeg", "Foot" })
                AddJoint(side + jointName, jointName.Contains("Leg", StringComparison.Ordinal) ? 0.058f : 0.043f);
        }
        SetNeutralPose();
    }

    private void SetNeutralPose()
    {
        if (_skeleton == null) return;
        for (int i = 0; i < _bones.Count; i++)
        {
            _skeleton.SetBonePosePosition(i, _bones[i].Offset);
            _skeleton.SetBonePoseRotation(i, Quaternion.Identity);
            _skeleton.SetBonePoseScale(i, Vector3.One);
        }
        _skeleton.SetBonePoseRotation(FindBone("LeftUpperArm"), new Quaternion(Vector3.Back, Mathf.DegToRad(-77)));
        _skeleton.SetBonePoseRotation(FindBone("RightUpperArm"), new Quaternion(Vector3.Back, Mathf.DegToRad(77)));
    }

    private BoneAttachment3D Attachment(string boneName)
    {
        var attachment = new BoneAttachment3D { Name = boneName + "Shape", BoneName = boneName, BoneIdx = FindBone(boneName) };
        _skeleton!.AddChild(attachment);
        return attachment;
    }

    private void AddSegment(string boneName, Vector3 end, float radius, Material material)
    {
        float length = end.Length();
        Attachment(boneName).AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = radius, Height = Math.Max(radius * 2, length * 0.92f), RadialSegments = 16, Rings = 6 },
            Position = end * 0.5f, Quaternion = Align(Vector3.Up, end.Normalized()), MaterialOverride = material
        });
    }

    private void AddJoint(string boneName, float radius) => Attachment(boneName).AddChild(new MeshInstance3D
    {
        Mesh = new SphereMesh { Radius = radius, Height = radius * 2, RadialSegments = 16, Rings = 8 },
        MaterialOverride = _jointMaterial
    });

    private int FindBone(string name) => _bones.FindIndex(bone => bone.Name == name);

    private void DefineBones()
    {
        void Add(string name, string parent, Vector3 offset, Vector3 aim, params string[] aliases) =>
            _bones.Add(new BoneDefinition(name, parent.Length == 0 ? -1 : FindBone(parent), offset, aim, aliases.Prepend(name).ToArray()));
        Add("Hips", "", new Vector3(0, 0.94f, 0), Vector3.Up, "pelvis", "hip", "root", "bip01pelvis", "hipsjoint");
        Add("Spine", "Hips", new Vector3(0, 0.14f, 0), Vector3.Up, "spine0", "lowerback", "abdomen", "abdomenlower", "spine01", "bip01spine");
        Add("Chest", "Spine", new Vector3(0, 0.18f, 0), Vector3.Up, "spine2", "spine3", "upperback", "thorax", "chestupper", "spine02", "bip01spine2");
        Add("Neck", "Chest", new Vector3(0, 0.19f, 0), Vector3.Up, "neck1", "lowerneck", "necklower", "neck01", "bip01neck");
        Add("Head", "Neck", new Vector3(0, 0.12f, 0), Vector3.Up, "head1", "headjoint", "bip01head");
        foreach (string side in new[] { "Left", "Right" })
        {
            string prefix = side == "Left" ? "l" : "r";
            float sign = side == "Left" ? 1 : -1;
            Add(side + "Shoulder", "Chest", new Vector3(sign * 0.095f, 0.105f, 0), new Vector3(sign, 0, 0), prefix + "clavicle", side + "Collar", prefix + "collar", "clavicle" + prefix, "shoulder" + prefix, "bip01" + prefix + "clavicle");
            Add(side + "UpperArm", side + "Shoulder", new Vector3(sign * 0.10f, 0, 0), new Vector3(sign, 0, 0), side + "Arm", prefix + "humerus", prefix + "upperarm", "upperarm" + prefix, "arm" + prefix, "bip01" + prefix + "upperarm");
            Add(side + "LowerArm", side + "UpperArm", new Vector3(sign * 0.265f, 0, 0), new Vector3(sign, 0, 0), side + "ForeArm", prefix + "radius", prefix + "forearm", "lowerarm" + prefix, "forearm" + prefix, "bip01" + prefix + "forearm");
            Add(side + "Hand", side + "LowerArm", new Vector3(sign * 0.235f, 0, 0), new Vector3(sign, 0, 0), prefix + "hand", "hand" + prefix, prefix + "wrist", "bip01" + prefix + "hand");
            Add(side + "UpperLeg", "Hips", new Vector3(sign * 0.105f, -0.035f, 0), Vector3.Down, side + "UpLeg", prefix + "femur", prefix + "thigh", "thigh" + prefix, "upleg" + prefix, "bip01" + prefix + "thigh");
            Add(side + "LowerLeg", side + "UpperLeg", new Vector3(0, -0.410f, 0), Vector3.Down, side + "Leg", prefix + "tibia", prefix + "shin", "calf" + prefix, "shin" + prefix, "bip01" + prefix + "calf");
            Add(side + "Foot", side + "LowerLeg", new Vector3(0, -0.405f, 0), Vector3.Forward, prefix + "foot", "foot" + prefix, prefix + "ankle", "bip01" + prefix + "foot");
            Add(side + "Toe", side + "Foot", new Vector3(0, -0.03f, -0.14f), Vector3.Forward, side + "ToeBase", prefix + "toes", prefix + "toe", "toebase" + prefix, "bip01" + prefix + "toe0");
        }
    }

    private int[] CreateMapping(BvhMotion candidate)
    {
        var map = Enumerable.Repeat(-1, _bones.Count).ToArray();
        var used = new HashSet<int>();
        for (int i = 0; i < _bones.Count; i++)
        {
            foreach (string alias in _bones[i].Aliases)
            {
                string wanted = Normalize(alias);
                for (int source = 0; source < candidate.Joints.Count; source++)
                {
                    if (!candidate.Joints[source].IsEndSite && !used.Contains(source) && Normalize(candidate.Joints[source].Name) == wanted)
                    { map[i] = source; used.Add(source); break; }
                }
                if (map[i] >= 0) break;
            }
        }
        // Many BVH exporters call the chest Spine1 instead of Spine2.
        if (map[2] < 0)
        {
            for (int source = 0; source < candidate.Joints.Count; source++)
                if (!used.Contains(source) && Normalize(candidate.Joints[source].Name) is "spine1" or "chestlower") { map[2] = source; break; }
        }
        return map;
    }

    private Vector3 SourceAim(BvhMotion motion, int[] mapping, int target)
    {
        int source = mapping[target];
        if (source < 0) return _bones[target].Aim;
        // Prefer the corresponding prepared child, including intermediate source joints.
        int child = _bones.FindIndex(bone => bone.Parent == target && mapping[_bones.IndexOf(bone)] >= 0);
        if (target == 0) child = FindBone("Spine");
        if (target == 2) child = FindBone("Neck");
        if (child >= 0 && mapping[child] >= 0)
        {
            Vector3 delta = Vector3.Zero;
            int current = mapping[child];
            int guard = 0;
            while (current >= 0 && current != source && guard++ < motion.Joints.Count)
            { delta += motion.Joints[current].Offset; current = motion.Joints[current].Parent; }
            if (current == source && delta.LengthSquared() > 0.00001f) return delta;
        }
        foreach (int sourceChild in motion.Joints[source].Children)
            if (motion.Joints[sourceChild].Offset.LengthSquared() > 0.00001f) return motion.Joints[sourceChild].Offset;
        return _bones[target].Aim;
    }

    private static string Normalize(string name)
    {
        int namespaceIndex = name.LastIndexOf(':');
        if (namespaceIndex >= 0) name = name[(namespaceIndex + 1)..];
        string normalized = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]", "");
        if (normalized.StartsWith("mixamorig", StringComparison.Ordinal)) normalized = normalized[9..];
        return normalized;
    }

    private static Quaternion Align(Vector3 from, Vector3 to)
    {
        float dot = Math.Clamp(from.Dot(to), -1, 1);
        if (dot > 0.99999f) return Quaternion.Identity;
        if (dot < -0.99999f)
        {
            Vector3 axis = from.Cross(Vector3.Right);
            if (axis.LengthSquared() < 0.0001f) axis = from.Cross(Vector3.Up);
            return new Quaternion(axis.Normalized(), Mathf.Pi);
        }
        Vector3 cross = from.Cross(to);
        return new Quaternion(cross.X, cross.Y, cross.Z, 1 + dot).Normalized();
    }
}
