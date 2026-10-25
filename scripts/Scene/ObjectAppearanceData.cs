namespace Previs;

/// <summary>Optional sketch color and editor label settings, independent of the model's source materials.</summary>
public sealed class ObjectAppearanceData
{
    /// <summary>Null preserves the authored materials. A value is an opaque RGB color.</summary>
    public float[]? ColorOverride { get; set; }
    public bool ShowLabel { get; set; } = true;
}
