using System;
using System.Collections.Generic;

namespace GoPrevis;

/// <summary>
/// Animation channels addressed by stable property paths, such as Position.X or
/// Light.Intensity. Values are kept independently from the object's base state.
/// </summary>
public sealed class PropertyAnimationData
{
    public Dictionary<string, ScalarAnimationCurve> Channels { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>A scalar value curve; vector components use separate instances.</summary>
public sealed class ScalarAnimationCurve
{
    public bool Enabled { get; set; } = true;
    /// <summary>Booleans and enumerations hold their value instead of interpolating.</summary>
    public bool Discrete { get; set; }
    public List<ScalarKeyData> Keys { get; set; } = new();
}

/// <summary>
/// One value at one scene time. Bezier slopes use value units per second;
/// weights specify the handle's time length as a fraction of its adjacent interval.
/// The outgoing key selects the interpolation used toward the following key.
/// </summary>
public sealed class ScalarKeyData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public double Time { get; set; }
    public double Value { get; set; }
    public string Interpolation { get; set; } = "Linear";
    public double InSlope { get; set; }
    public double OutSlope { get; set; }
    public double InWeight { get; set; } = 1.0 / 3;
    public double OutWeight { get; set; } = 1.0 / 3;
    public bool BrokenTangents { get; set; }
}
