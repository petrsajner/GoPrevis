using System;

namespace GoPrevis;

/// <summary>Shot format and editor-only framing reference. FPS is stored by ProjectData.</summary>
public sealed class ProjectSetupData
{
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public string ReferencePath { get; set; } = "";
    public bool ReferenceEnabled { get; set; } = true;
    public float ReferenceOpacity { get; set; } = .5f;
    public bool ReferenceFill { get; set; }
    public double ReferenceOffsetSeconds { get; set; }

    public ProjectSetupData ValidatedCopy()
    {
        if(Width<64||Height<64||Width>8192||Height>8192||(Width&1)!=0||(Height&1)!=0)
            throw new ArgumentException("Project dimensions must be even values between 64 and 8192 pixels.");
        if(!float.IsFinite(ReferenceOpacity)||ReferenceOpacity<0||ReferenceOpacity>1)
            throw new ArgumentException("Reference opacity must be between 0 and 100%.");
        if(!double.IsFinite(ReferenceOffsetSeconds)||Math.Abs(ReferenceOffsetSeconds)>86400)
            throw new ArgumentException("Reference time offset must be finite and within one day.");
        return new ProjectSetupData{Width=Width,Height=Height,ReferencePath=ReferencePath??"",ReferenceEnabled=ReferenceEnabled,
            ReferenceOpacity=ReferenceOpacity,ReferenceFill=ReferenceFill,ReferenceOffsetSeconds=ReferenceOffsetSeconds};
    }
}
