using System;

namespace GenPrevis;

/// <summary>Editor-only image/video background belonging to one camera.</summary>
public sealed class CameraReferenceData
{
    public string Path { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public float Opacity { get; set; } = .5f;
    public bool Fill { get; set; }
    public double OffsetSeconds { get; set; }

    public CameraReferenceData ValidatedCopy()
    {
        if(!float.IsFinite(Opacity)||Opacity<0||Opacity>1)
            throw new ArgumentException("Camera background opacity must be between 0 and 100%.");
        if(!double.IsFinite(OffsetSeconds)||Math.Abs(OffsetSeconds)>86400)
            throw new ArgumentException("Camera background time offset must be finite and within one day.");
        return new CameraReferenceData{Path=Path??"",Enabled=Enabled,Opacity=Opacity,Fill=Fill,OffsetSeconds=OffsetSeconds};
    }
}
