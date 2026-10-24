using System;
using System.Collections.Generic;

namespace Previs;

/// <summary>One time/value key for all three axes of a transform parameter.</summary>
public sealed class TransformKeyData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public double Time { get; set; }
    public float[] Value { get; set; } = new float[3];
}

/// <summary>
/// Corrections above the base transform and path. Position and rotation are
/// additive; scale is a multiplicative factor with identity (1, 1, 1).
/// </summary>
public sealed class TransformAnimationData
{
    public List<TransformKeyData> Position { get; set; } = new();
    public List<TransformKeyData> Rotation { get; set; } = new();
    public List<TransformKeyData> Scale { get; set; } = new();
}
