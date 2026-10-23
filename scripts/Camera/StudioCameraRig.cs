using Godot;
using System;
using System.Collections.Generic;

namespace Previs;

/// <summary>A shot key stores a world-space pose and a full-frame lens length.</summary>
public sealed class ShotCameraKey
{
    public double TimeSeconds { get; set; }
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    public float FocalLengthMm { get; set; } = 35f;

    public ShotCameraKey() { }

    public ShotCameraKey(double timeSeconds, Vector3 position, Quaternion rotation, float focalLengthMm)
    {
        TimeSeconds = timeSeconds;
        Position = position;
        Rotation = rotation;
        FocalLengthMm = focalLengthMm;
    }
}

/// <summary>
/// A shot camera with deterministic timeline evaluation. The rig itself carries
/// the pose so the editor can pilot it simply by assigning GlobalTransform.
/// </summary>
public partial class StudioCameraRig : Node3D
{
    public const uint EditorLayer = 1u << 1;
    public const float SensorWidthMm = 36f;
    private const float PreviewAspect = 16f / 9f;
    private const float FrustumLength = 2.1f;
    private static readonly Vector3 SceneTarget = new(0f, 1f, 0f);

    private float _focalLengthMm = 35f;
    private ImmediateMesh _frustumMesh = null!;

    public Camera3D ShotCamera { get; private set; } = null!;
    public List<ShotCameraKey> Keys { get; } = new();

    /// <summary>Horizontal field of view uses a 36 mm full-frame sensor width.</summary>
    public float FocalLengthMm
    {
        get => _focalLengthMm;
        set
        {
            float next = float.IsFinite(value) ? Mathf.Clamp(value, 8f, 300f) : 35f;
            bool changed = !Mathf.IsEqualApprox(_focalLengthMm, next);
            _focalLengthMm = next;
            if (ShotCamera != null)
                ShotCamera.Fov = HorizontalFovDegrees(next);
            if (changed && _frustumMesh != null)
                UpdateFrustum();
        }
    }

    public override void _Ready()
    {
        ShotCamera = new Camera3D
        {
            Name = "ShotCamera",
            Near = 0.05f,
            Far = 500f,
            CullMask = 1u,
            KeepAspect = Camera3D.KeepAspectEnum.Width,
            Fov = HorizontalFovDegrees(_focalLengthMm)
        };
        AddChild(ShotCamera);
        BuildEditorRepresentation();
        ResetShot();
    }

    public void ResetShot()
    {
        FocalLengthMm = 35f;
        SetPose(new Vector3(6f, 3.2f, 7f), SceneTarget);
    }

    public void SetPose(Vector3 position, Vector3 target)
    {
        if (!Finite(position) || !Finite(target))
            return;
        GlobalTransform = new Transform3D(new Basis(RotationToward(position, target)), position);
    }

    public void CaptureKey(double seconds)
    {
        seconds = SafeTime(seconds);
        var key = new ShotCameraKey(seconds, GlobalPosition,
            SafeRotation(GlobalBasis.Orthonormalized().GetRotationQuaternion()), FocalLengthMm);
        // A small tolerance lets frame/timecode conversion replace the same key.
        int existing = Keys.FindIndex(item => Math.Abs(item.TimeSeconds - seconds) < 0.0001);
        if (existing >= 0)
            Keys[existing] = key;
        else
            Keys.Add(key);
        Keys.Sort((left, right) => left.TimeSeconds.CompareTo(right.TimeSeconds));
    }

    public void ClearKeys() => Keys.Clear();

    /// <summary>
    /// Evaluate from absolute shot time; scrubbing and export give the same pose
    /// regardless of playback direction, frame rate, or previously displayed frame.
    /// </summary>
    public void Evaluate(double seconds)
    {
        if (Keys.Count == 0)
            return;

        seconds = SafeTime(seconds);
        Keys.Sort((left, right) => left.TimeSeconds.CompareTo(right.TimeSeconds));
        if (seconds <= Keys[0].TimeSeconds)
        {
            ApplyKey(Keys[0]);
            return;
        }
        if (seconds >= Keys[^1].TimeSeconds)
        {
            ApplyKey(Keys[^1]);
            return;
        }

        for (int index = 1; index < Keys.Count; index++)
        {
            ShotCameraKey end = Keys[index];
            if (seconds > end.TimeSeconds)
                continue;
            ShotCameraKey start = Keys[index - 1];
            double span = end.TimeSeconds - start.TimeSeconds;
            float weight = span > 0.000001
                ? (float)Math.Clamp((seconds - start.TimeSeconds) / span, 0.0, 1.0)
                : 1f;
            Quaternion from = SafeRotation(start.Rotation);
            Quaternion to = SafeRotation(end.Rotation);
            // q and -q encode the same rotation. Select the short arc explicitly.
            if (from.Dot(to) < 0f)
                to = new Quaternion(-to.X, -to.Y, -to.Z, -to.W);
            Quaternion rotation = SafeRotation(from.Slerp(to, weight));
            Vector3 position = start.Position.Lerp(end.Position, weight);
            if (Finite(position))
                GlobalTransform = new Transform3D(new Basis(rotation), position);
            FocalLengthMm = Mathf.Lerp(start.FocalLengthMm, end.FocalLengthMm, weight);
            return;
        }
    }

    public void SetPreset(string id, double durationSeconds = 8.0)
    {
        double duration = double.IsFinite(durationSeconds) ? Math.Max(0.1, durationSeconds) : 8.0;
        Keys.Clear();
        switch (id)
        {
            case "Dolly in":
                AddTargetKey(0.0, new Vector3(6f, 3.2f, 7f), SceneTarget);
                AddTargetKey(duration, new Vector3(3f, 2.1f, 3.5f), SceneTarget);
                break;
            case "Truck right":
                AddTargetKey(0.0, new Vector3(-4f, 2.3f, 7f), new Vector3(-4f, 1.2f, 0f));
                AddTargetKey(duration, new Vector3(4f, 2.3f, 7f), new Vector3(4f, 1.2f, 0f));
                break;
            case "Crane up":
                AddTargetKey(0.0, new Vector3(6f, 1.5f, 7f), SceneTarget);
                AddTargetKey(duration, new Vector3(6f, 5.5f, 7f), SceneTarget);
                break;
            case "Orbit":
                const int orbitSteps = 16;
                float startAngle = Mathf.Atan2(6f, 7f);
                float radius = new Vector2(6f, 7f).Length();
                for (int index = 0; index <= orbitSteps; index++)
                {
                    float fraction = index / (float)orbitSteps;
                    float angle = startAngle + fraction * Mathf.Pi * 0.5f;
                    var position = new Vector3(Mathf.Sin(angle) * radius, 3.2f, Mathf.Cos(angle) * radius);
                    AddTargetKey(fraction * duration, position, SceneTarget);
                }
                break;
            default:
                // Static preserves the composition the user has just made.
                CaptureKey(0.0);
                CaptureKey(duration);
                break;
        }
        Evaluate(0.0);
    }

    private void AddTargetKey(double seconds, Vector3 position, Vector3 target)
    {
        Keys.Add(new ShotCameraKey(seconds, position, RotationToward(position, target), 35f));
    }

    private void ApplyKey(ShotCameraKey key)
    {
        if (Finite(key.Position))
            GlobalTransform = new Transform3D(new Basis(SafeRotation(key.Rotation)), key.Position);
        FocalLengthMm = key.FocalLengthMm;
    }

    private static Quaternion RotationToward(Vector3 position, Vector3 target)
    {
        Vector3 direction = target - position;
        if (direction.LengthSquared() < 0.000001f)
            direction = Vector3.Forward;
        direction = direction.Normalized();
        Vector3 up = Math.Abs(direction.Dot(Vector3.Up)) > 0.999f ? Vector3.Back : Vector3.Up;
        return SafeRotation(new Transform3D(Basis.Identity, position)
            .LookingAt(position + direction, up).Basis.GetRotationQuaternion());
    }

    private static Quaternion SafeRotation(Quaternion rotation)
    {
        if (!float.IsFinite(rotation.X) || !float.IsFinite(rotation.Y) ||
            !float.IsFinite(rotation.Z) || !float.IsFinite(rotation.W) || rotation.LengthSquared() < 0.000001f)
            return Quaternion.Identity;
        return rotation.Normalized();
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static double SafeTime(double seconds) => double.IsFinite(seconds) ? Math.Max(0.0, seconds) : 0.0;

    private static float HorizontalFovDegrees(float focalLengthMm) =>
        Mathf.RadToDeg(2f * Mathf.Atan(SensorWidthMm / (2f * focalLengthMm)));

    private void BuildEditorRepresentation()
    {
        var bodyMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color("a6a0fb"),
            Roughness = 0.75f
        };
        var lensMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color("302d4e"),
            Roughness = 0.45f
        };
        AddEditorMesh("CameraBody", new BoxMesh { Size = new Vector3(0.46f, 0.3f, 0.42f) },
            new Vector3(0f, 0f, 0.12f), Vector3.Zero, bodyMaterial);
        AddEditorMesh("CameraHandle", new BoxMesh { Size = new Vector3(0.3f, 0.075f, 0.19f) },
            new Vector3(0f, 0.22f, 0.13f), Vector3.Zero, bodyMaterial);
        AddEditorMesh("CameraLens", new CylinderMesh { TopRadius = 0.105f, BottomRadius = 0.12f, Height = 0.18f },
            new Vector3(0f, 0f, -0.17f), new Vector3(Mathf.Pi / 2f, 0f, 0f), lensMaterial);

        _frustumMesh = new ImmediateMesh();
        var frustumMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color("a29bff"),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            NoDepthTest = false
        };
        AddEditorMesh("CameraFrustum", _frustumMesh, Vector3.Zero, Vector3.Zero, frustumMaterial);
        UpdateFrustum();
    }

    private void AddEditorMesh(string name, Mesh mesh, Vector3 position, Vector3 rotation, Material material)
    {
        var instance = new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            Position = position,
            Rotation = rotation,
            MaterialOverride = material,
            Layers = EditorLayer,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled
        };
        AddChild(instance);
    }

    private void UpdateFrustum()
    {
        _frustumMesh.ClearSurfaces();
        float halfWidth = FrustumLength * SensorWidthMm / (2f * _focalLengthMm);
        float halfHeight = halfWidth / PreviewAspect;
        Vector3[] corners =
        {
            new(-halfWidth, -halfHeight, -FrustumLength),
            new(halfWidth, -halfHeight, -FrustumLength),
            new(halfWidth, halfHeight, -FrustumLength),
            new(-halfWidth, halfHeight, -FrustumLength)
        };
        _frustumMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        for (int index = 0; index < corners.Length; index++)
        {
            _frustumMesh.SurfaceAddVertex(Vector3.Zero);
            _frustumMesh.SurfaceAddVertex(corners[index]);
            _frustumMesh.SurfaceAddVertex(corners[index]);
            _frustumMesh.SurfaceAddVertex(corners[(index + 1) % corners.Length]);
        }
        // A short upper tick makes the camera's roll visible from the editor.
        _frustumMesh.SurfaceAddVertex(new Vector3(-halfWidth * 0.15f, halfHeight, -FrustumLength));
        _frustumMesh.SurfaceAddVertex(new Vector3(0f, halfHeight * 1.22f, -FrustumLength));
        _frustumMesh.SurfaceAddVertex(new Vector3(0f, halfHeight * 1.22f, -FrustumLength));
        _frustumMesh.SurfaceAddVertex(new Vector3(halfWidth * 0.15f, halfHeight, -FrustumLength));
        _frustumMesh.SurfaceEnd();
    }
}
