using System;
using System.Collections.Generic;

namespace Previs;

public sealed class ProjectData
{
    public int Version { get; set; } = 1;
    public int Fps { get; set; } = 24;
    public double Duration { get; set; } = 8;
    public List<ObjectData> Objects { get; set; } = new();
    public List<CameraData> CameraKeys { get; set; } = new();
    public float[] ShotPosition { get; set; } = Array.Empty<float>();
    public float[] ShotRotation { get; set; } = Array.Empty<float>();
    public float FocalLength { get; set; } = 35;
}

public sealed class ObjectData
{
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
    public float SourceScale { get; set; } = .01f;
    public bool Loop { get; set; } = true;
    public bool InPlace { get; set; }
    public double Speed { get; set; } = 1;
}

public sealed class CameraData
{
    public double Time { get; set; }
    public float[] Position { get; set; } = new float[3];
    public float[] Quaternion { get; set; } = new float[] { 0, 0, 0, 1 };
    public float FocalLength { get; set; } = 35;
}
