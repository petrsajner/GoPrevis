using System;
using System.Collections.Generic;
using System.IO;

namespace GenPrevis;

/// <summary>All selected outputs sample the same end-exclusive shot-time range.</summary>
public sealed class ShotExportSettings
{
    public string OutputDirectory { get; set; } = "";
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int Fps { get; set; } = 24;
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; } = 8;
    public bool ExportMp4 { get; set; } = true;
    public bool ExportPngSequence { get; set; } = true;
    public bool IncludeColor { get; set; } = true;
    public bool IncludeDepth { get; set; } = true;
    public bool IncludePose { get; set; } = true;
    public bool IncludeNativeRigs { get; set; }
    public float DepthNear { get; set; } = .05f;
    public float DepthFar { get; set; } = 30;
    public float PoseOcclusionTolerance { get; set; } = .18f;

    public int FrameCount => checked((int)Math.Ceiling((EndSeconds-StartSeconds)*Fps-1e-8));
    public double MediaDuration => FrameCount/(double)Fps;
    public double FrameTime(int index)
    {
        if(index<0||index>=FrameCount)throw new ArgumentOutOfRangeException(nameof(index));
        return StartSeconds+index/(double)Fps;
    }
    public IEnumerable<double> FrameTimes()
    {
        for(int i=0;i<FrameCount;i++)yield return FrameTime(i);
    }
    public void Validate(double sceneDuration=600)
    {
        if(string.IsNullOrWhiteSpace(OutputDirectory))throw new ArgumentException("Choose an export folder.");
        _=Path.GetFullPath(OutputDirectory);
        if(Width<64||Height<64||Width>8192||Height>8192)throw new ArgumentOutOfRangeException(nameof(Width),"Export dimensions must be between 64 and 8192 pixels.");
        if(ExportMp4&&(Width%2!=0||Height%2!=0))throw new ArgumentException("MP4 requires even image dimensions.");
        if(Fps<1||Fps>120)throw new ArgumentOutOfRangeException(nameof(Fps),"Export frame rate must be between 1 and 120 fps.");
        if(!double.IsFinite(StartSeconds)||!double.IsFinite(EndSeconds)||StartSeconds<0||EndSeconds<=StartSeconds||EndSeconds>sceneDuration+1e-7)throw new ArgumentException("Choose a positive shot range within the scene duration.");
        if(!ExportMp4&&!ExportPngSequence)throw new ArgumentException("Choose MP4, PNG sequence, or both.");
        if(!IncludeColor&&!IncludeDepth&&!IncludePose)throw new ArgumentException("Choose at least one render pass.");
        if(!float.IsFinite(DepthNear)||!float.IsFinite(DepthFar)||DepthNear<0||DepthFar<=DepthNear)throw new ArgumentException("Depth far distance must be greater than its nonnegative near distance.");
        if(!float.IsFinite(PoseOcclusionTolerance)||PoseOcclusionTolerance<0||PoseOcclusionTolerance>10)throw new ArgumentException("Pose occlusion tolerance must be between 0 and 10 metres.");
        if(FrameCount<1||FrameCount>72000)throw new ArgumentException("Export must contain between 1 and 72000 frames.");
    }
}
