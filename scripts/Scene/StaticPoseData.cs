using System.Collections.Generic;

namespace Previs;

/// <summary>
/// A self-contained prepared-humanoid pose. The hip offset is relative to the
/// rig's rest hip position; rotations are local bone quaternions in XYZW order.
/// No motion file, source skeleton or scene-world transform is stored here.
/// </summary>
public sealed class StaticPoseData
{
    /// <summary>Null for the prepared human rig; native poses are restricted to this catalog model.</summary>
    public string? RigModelId { get; set; }
    public string? CatalogId { get; set; }
    public string Name { get; set; } = "Custom pose";
    public float[] RootPosition { get; set; } = new float[3];
    public float[] Forward { get; set; } = new float[] { 0, 0, -1 };
    public Dictionary<string, float[]> BoneRotations { get; set; } = new();
}
