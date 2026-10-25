using System;
using System.Linq;

namespace GoPrevis;

public sealed class SceneLightData
{
    public bool Enabled { get; set; } = true;
    public float[] Color { get; set; } = new[]{1f,1f,1f};
    public float Energy { get; set; } = 1;
    public float Range { get; set; } = 10;
    public bool Shadows { get; set; } = true;
    public SceneLightData ValidatedCopy()
    {
        if(Color==null||Color.Length!=3||Color.Any(x=>!float.IsFinite(x)||x<0||x>1))throw new FormatException("Light color must contain three values between zero and one.");
        if(!float.IsFinite(Energy)||Energy<0||Energy>16)throw new FormatException("Light intensity must be between zero and 16.");
        if(!float.IsFinite(Range)||Range<.1f||Range>500)throw new FormatException("Point light range must be between 0.1 and 500 metres.");
        return new(){Enabled=Enabled,Color=Color.ToArray(),Energy=Energy,Range=Range,Shadows=Shadows};
    }
}
