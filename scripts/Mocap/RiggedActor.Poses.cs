using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GoPrevis;

public partial class RiggedActor
{
    private StaticPoseData? _staticPose;
    private bool _staticPosePicker;

    public bool HasStaticPose => _staticPose != null;
    public string StaticPoseName => _staticPose?.Name ?? "Standing";

    /// <summary>
    /// Stores a static fallback pose without touching source clips or the outer
    /// object transform. Call SetMotionClips(empty) to deliberately freeze it.
    /// </summary>
    public void SetStaticPose(StaticPoseData? pose)
    {
        EnsureRig();
        StaticPoseData? validated = pose == null ? null : CopyValidatedStaticPose(pose);
        _staticPose = validated;
        if (_motionTrack.Length == 0) ApplyStaticPose();
    }

    /// <summary>Returns an independent serializable snapshot of the stored fallback.</summary>
    public StaticPoseData? GetStaticPoseSnapshot() => _staticPose == null ? null : CloneStaticPose(_staticPose);

    /// <summary>
    /// Captures the currently evaluated prepared rig. Source XZ travel and the
    /// object's world transform are excluded; the evaluated hip height is kept.
    /// </summary>
    public StaticPoseData BakeCurrentPose(string name = "Custom pose")
    {
        EnsureRig();
        var root = _skeleton!.GetBonePosePosition(0) - _bones[0].Offset;
        Vector3 forward = _skeleton.GetBonePoseRotation(0).Inverse() * AnimatedForward;
        forward.Y = 0;
        if (forward.LengthSquared() < .00001f)
        {
            forward = _faceMarker == null ? Vector3.Forward : new Vector3(_faceMarker.Position.X, 0, _faceMarker.Position.Z);
            if (forward.LengthSquared() < .00001f) forward = Vector3.Forward;
        }
        forward = forward.Normalized();
        var pose = new StaticPoseData
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Custom pose" : name.Trim(),
            RootPosition = new[] { 0f, root.Y, 0f },
            Forward = new[] { forward.X, forward.Y, forward.Z }
        };
        for (int i = 0; i < _bones.Count; i++)
        {
            var rotation = _skeleton.GetBonePoseRotation(i).Normalized();
            pose.BoneRotations[_bones[i].Name] = new[] { rotation.X, rotation.Y, rotation.Z, rotation.W };
        }
        return CopyValidatedStaticPose(pose);
    }

    /// <summary>Applies the stored pose in rig-local space; it never moves the actor node.</summary>
    public void ApplyStaticPose()
    {
        EnsureRig();
        if (_motionTrack.Length != 0) return;
        _motion = null;
        if (_staticPose == null)
        {
            RootMotionOffset = Vector3.Zero;
            AnimatedForward = Vector3.Forward;
            MotionName = "No motion";
            ImportSummary = "Prepared humanoid · no motion or static pose selected.";
            RestoreMotionPicker();
            if (_pickBody != null) _pickBody.Position = Vector3.Zero;
            SetNeutralPose();
            ResetStaticFaceMarker();
            return;
        }

        Vector3 root = new(_staticPose.RootPosition[0], _staticPose.RootPosition[1], _staticPose.RootPosition[2]);
        for (int i = 0; i < _bones.Count; i++)
        {
            var definition = _bones[i];
            Quaternion localRotation = Quaternion.Identity;
            if (_staticPose.BoneRotations.TryGetValue(definition.Name, out float[]? values))
                localRotation = new Quaternion(values[0], values[1], values[2], values[3]);
            Quaternion parentRotation = definition.Parent < 0 ? Quaternion.Identity : _targetRotations[definition.Parent];
            Vector3 localPosition = definition.Offset + (definition.Parent < 0 ? root : Vector3.Zero);
            _targetRotations[i] = (parentRotation * localRotation).Normalized();
            _targetPositions[i] = definition.Parent < 0 ? localPosition : _targetPositions[definition.Parent] + parentRotation * localPosition;
            _skeleton!.SetBonePosePosition(i, localPosition);
            _skeleton.SetBonePoseRotation(i, localRotation);
            _skeleton.SetBonePoseScale(i, Vector3.One);
        }
        RootMotionOffset = root;
        // Root yaw remains meaningful for path orientation; a lying root whose
        // forward vector points vertically uses its right axis as a stable fallback.
        Vector3 poseForward = new(_staticPose.Forward[0], _staticPose.Forward[1], _staticPose.Forward[2]);
        Vector3 facing = _targetRotations[0] * poseForward;
        facing.Y = 0;
        if (facing.LengthSquared() < .00001f)
        {
            var right = _targetRotations[0] * Vector3.Right;
            right.Y = 0;
            facing = Vector3.Up.Cross(right);
        }
        AnimatedForward = HorizontalFacing(facing);
        MotionName = _staticPose.Name;
        ImportSummary = "Static pose · " + _staticPose.Name + " · no motion clip required.";
        ResetStaticFaceMarker(poseForward);
        FitStaticPosePicker();
    }

    private void ResetStaticFaceMarker(Vector3? direction = null)
    {
        if (_faceMarker == null) return;
        Vector3 forward = direction ?? Vector3.Forward;
        _faceMarker.Position = forward * .101f + Vector3.Up * .10f;
        _faceMarker.Quaternion = Align(Vector3.Forward, forward);
    }

    /// <summary>
    /// Immediate rig-local bounds, calculated from bone poses rather than deferred
    /// BoneAttachment3D transforms. This keeps seated/lying picks and framing accurate.
    /// </summary>
    public Aabb GetPreparedPoseBounds()
    {
        EnsureRig();
        var minimum = Vector3.Zero;
        var maximum = Vector3.Zero;
        bool found = false;
        foreach (Node child in _skeleton!.GetChildren())
        {
            if (child is not BoneAttachment3D attachment) continue;
            int bone = attachment.BoneIdx;
            if (bone < 0 || bone >= _bones.Count) continue;
            // Query the skeleton's current local pose so this method also works
            // for neutral rigs that have not yet populated the retarget arrays.
            Transform3D boneTransform = PreparedBoneTransform(bone);
            CollectStaticMeshBounds(attachment, boneTransform, ref minimum, ref maximum, ref found);
        }
        return found ? new Aabb(minimum, maximum - minimum) : new Aabb(new Vector3(-.4f, 0, -.3f), new Vector3(.8f, 1.9f, .6f));
    }

    private Transform3D PreparedBoneTransform(int index)
    {
        Transform3D local = new(new Basis(_skeleton!.GetBonePoseRotation(index)), _skeleton.GetBonePosePosition(index));
        int parent = _bones[index].Parent;
        return parent < 0 ? local : PreparedBoneTransform(parent) * local;
    }

    private static void CollectStaticMeshBounds(Node node, Transform3D transform, ref Vector3 minimum, ref Vector3 maximum, ref bool found)
    {
        // The attachment itself is already represented by its bound bone; its
        // deferred native Transform must not be multiplied into the pose twice.
        if (node is MeshInstance3D { Mesh: not null } mesh)
        {
            var bounds = mesh.GetAabb();
            for (int i = 0; i < 8; i++)
            {
                var point = transform * bounds.GetEndpoint(i);
                if (!found) { minimum = maximum = point; found = true; }
                else { minimum = minimum.Min(point); maximum = maximum.Max(point); }
            }
        }
        foreach (Node child in node.GetChildren())
        {
            var next = child is Node3D spatial ? transform * spatial.Transform : transform;
            CollectStaticMeshBounds(child, next, ref minimum, ref maximum, ref found);
        }
    }

    private void FitStaticPosePicker()
    {
        if (_pickBody?.GetChild<CollisionShape3D>(0) is not { } collision) return;
        Aabb bounds = GetPreparedPoseBounds();
        Vector3 size = bounds.Size + Vector3.One * .06f;
        size = new Vector3(Math.Max(.12f, size.X), Math.Max(.12f, size.Y), Math.Max(.12f, size.Z));
        if (collision.Shape is not BoxShape3D box) collision.Shape = box = new BoxShape3D();
        box.Size = size;
        collision.Position = bounds.GetCenter();
        _pickBody.Position = Vector3.Zero;
        _staticPosePicker = true;
    }

    // Also used by the legacy BVH/ClearMotion API in RiggedActor.cs.
    internal void RestoreMotionPicker()
    {
        if (!_staticPosePicker || _pickBody?.GetChild<CollisionShape3D>(0) is not { } collision) return;
        collision.Shape = new CapsuleShape3D { Radius = .36f, Height = 1.8f };
        collision.Position = new Vector3(0, .9f, 0);
        _staticPosePicker = false;
    }

    private StaticPoseData CopyValidatedStaticPose(StaticPoseData pose)
    {
        if (pose.RootPosition == null || pose.RootPosition.Length != 3 || pose.RootPosition.Any(value => !float.IsFinite(value) || Math.Abs(value) > 1000))
            throw new FormatException("Static pose root offset must contain three finite metre values.");
        if (pose.BoneRotations == null || pose.BoneRotations.Count > _bones.Count)
            throw new FormatException("The static pose has an invalid bone collection.");
        if (pose.Forward == null || pose.Forward.Length != 3 || pose.Forward.Any(value => !float.IsFinite(value)))
            throw new FormatException("Static pose forward must contain three finite values.");
        Vector3 forward = new(pose.Forward[0], 0, pose.Forward[2]);
        if (!float.IsFinite(forward.LengthSquared()) || forward.LengthSquared() < .000001f) throw new FormatException("Static pose forward must have a finite horizontal direction.");
        forward = forward.Normalized();
        var result = new StaticPoseData
        {
            CatalogId = pose.CatalogId,
            Name = string.IsNullOrWhiteSpace(pose.Name) ? "Custom pose" : pose.Name.Trim(),
            RootPosition = pose.RootPosition.ToArray(),
            Forward = new[] { forward.X, 0f, forward.Z },
            BoneRotations = new Dictionary<string, float[]>(StringComparer.Ordinal)
        };
        foreach (var item in pose.BoneRotations)
        {
            if (FindBone(item.Key) < 0) throw new FormatException("The prepared human rig has no bone named '" + item.Key + "'.");
            var values = item.Value;
            if (values == null || values.Length != 4 || values.Any(value => !float.IsFinite(value)))
                throw new FormatException("Static pose bone '" + item.Key + "' needs a finite XYZW quaternion.");
            var rotation = new Quaternion(values[0], values[1], values[2], values[3]);
            if (!float.IsFinite(rotation.LengthSquared()) || rotation.LengthSquared() < .000001f) throw new FormatException("Static pose bone '" + item.Key + "' has an invalid quaternion.");
            rotation = rotation.Normalized();
            result.BoneRotations[item.Key] = new[] { rotation.X, rotation.Y, rotation.Z, rotation.W };
        }
        return result;
    }

    private static StaticPoseData CloneStaticPose(StaticPoseData pose) => new()
    {
        CatalogId = pose.CatalogId, Name = pose.Name, RootPosition = pose.RootPosition.ToArray(), Forward = pose.Forward.ToArray(),
        BoneRotations = pose.BoneRotations.ToDictionary(item => item.Key, item => item.Value.ToArray(), StringComparer.Ordinal)
    };
}
