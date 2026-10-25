using System;
using System.Linq;

namespace GoPrevis;

/// <summary>Persistent scene display settings shared by the working view and camera exports.</summary>
public sealed class SceneSettingsData
{
    public float[] BackgroundColor { get; set; } = new float[]{37/255f,37/255f,41/255f,1};
    public float Brightness { get; set; } = 1;
    public bool GroundVisible { get; set; } = true;
    public float GroundHeight { get; set; }
    public float GroundOpacity { get; set; } = 1;
    public bool ObjectLabelsVisible { get; set; } = true;

    public SceneSettingsData ValidatedCopy()
    {
        if(BackgroundColor==null||BackgroundColor.Length!=4||BackgroundColor.Any(v=>!float.IsFinite(v)||v<0||v>1))throw new FormatException("Scene background must contain four color values from zero to one.");
        if(!float.IsFinite(Brightness)||Brightness<0||Brightness>4)throw new FormatException("Scene brightness must be between zero and 400 percent.");
        if(!float.IsFinite(GroundHeight)||Math.Abs(GroundHeight)>1000)throw new FormatException("Ground height must be a finite value between -1000 and 1000 metres.");
        if(!float.IsFinite(GroundOpacity)||GroundOpacity<0||GroundOpacity>1)throw new FormatException("Ground opacity must be between zero and one.");
        return new SceneSettingsData{BackgroundColor=BackgroundColor.ToArray(),Brightness=Brightness,GroundVisible=GroundVisible,GroundHeight=GroundHeight,GroundOpacity=GroundOpacity,ObjectLabelsVisible=ObjectLabelsVisible};
    }
}
