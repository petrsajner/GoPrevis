using System;
using System.Collections.Generic;

namespace GenPrevis;

public sealed class ProjectData
{
    public const int CurrentVersion=11;
    public int Version { get; set; } = CurrentVersion;
    public int Fps { get; set; } = 24;
    public double Duration { get; set; } = 8;
    public List<ObjectData> Objects { get; set; } = new();
    public List<CameraData> CameraKeys { get; set; } = new();
    public float[] ShotPosition { get; set; } = Array.Empty<float>();
    public float[] ShotRotation { get; set; } = Array.Empty<float>();
    public float FocalLength { get; set; } = 35;
    public List<CameraTrackData> Cameras { get; set; } = new();
    public string ActiveCameraId { get; set; } = "";
    public bool DirectionalLightEnabled { get; set; }
    public SceneSettingsData SceneSettings { get; set; } = new();
    public ProjectSetupData Setup { get; set; } = new();
}

public sealed class ObjectData
{
    public SceneLightData? Light { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "box";
    public string Name { get; set; } = "Box";
    public float[] Position { get; set; } = new float[3];
    public float[] Rotation { get; set; } = new float[3];
    public float[] Scale { get; set; } = new float[] { 1, 1, 1 };
    public string? MotionPath { get; set; }
    public string? MotionName { get; set; }
    public string? ModelPath { get; set; }
    public string? ModelAnimation { get; set; }
    public string? LibraryModelId { get; set; }
    public float ModelUnitScale { get; set; } = 1;
    public bool ModelGrounded { get; set; }
    public float SourceScale { get; set; } = .01f;
    public bool Loop { get; set; } = true;
    public bool InPlace { get; set; }
    public double Speed { get; set; } = 1;
    public List<MotionClipData> MotionClips { get; set; } = new();
    public ActorPathData Path { get; set; } = new();
    public float[] PathOffset { get; set; } = new float[3];
    public TransformAnimationData TransformAnimation { get; set; } = new();
    public string ActorFamily { get; set; } = "";
    public StaticPoseData? StaticPose { get; set; }
    public PoseCorrectionAnimationData PoseCorrections { get; set; } = new();
    public ObjectAppearanceData Appearance { get; set; } = new();
}

public sealed class MotionClipData
{
    public bool IsPlaceholder { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string MotionPath { get; set; } = "";
    public string Name { get; set; } = "Motion";
    public double Start { get; set; }
    public double Duration { get; set; } = 3;
    public double SourceOffset { get; set; }
    public double Speed { get; set; } = 1;
    public double BlendIn { get; set; } = .3;
    public bool Loop { get; set; } = true;
    public bool InPlace { get; set; }
    public float SourceScale { get; set; } = .01f;
    public string? SourceAnimation { get; set; }
}

public sealed class ActorPathData
{
    public bool Enabled { get; set; }
    public List<float[]> Points { get; set; } = new();
    public List<double> PointTimes { get; set; } = new();
    public List<KeyInterpolationData> PointInterpolation { get; set; } = new();
    public double Start { get; set; }
    public double Duration { get; set; } = 8;
    public bool OrientToPath { get; set; } = true;
}

public sealed class CameraTrackData
{
    public CameraTrackingData Tracking { get; set; } = new();
    public CameraReferenceData Reference { get; set; } = new();
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Camera A";
    public bool ShowLabel { get; set; } = true;
    public List<CameraData> Keys { get; set; } = new();
    public float[] Position { get; set; } = new float[] { 6, 3.2f, 7 };
    public float[] Rotation { get; set; } = new float[3];
    public float FocalLength { get; set; } = 35;
    public float[] Scale { get; set; } = new float[]{1,1,1};
    public TransformAnimationData TransformAnimation { get; set; } = new();
    public List<LensKeyData> LensKeys { get; set; } = new();
}

public sealed class LensKeyData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public double Time { get; set; }
    public float Value { get; set; } = 35;
    public KeyInterpolationData Interpolation { get; set; } = new();
}

public sealed class CameraData
{
    public double Time { get; set; }
    public float[] Position { get; set; } = new float[3];
    public float[] Quaternion { get; set; } = new float[] { 0, 0, 0, 1 };
    public float FocalLength { get; set; } = 35;
}
