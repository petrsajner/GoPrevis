using System;

namespace GoPrevis;

/// <summary>Timing of the interval leaving this key. Old scenes default to linear.</summary>
public sealed class KeyInterpolationData
{
    public string Mode { get; set; } = "Linear";
    public float[] Control1 { get; set; } = new[] { .25f, .1f };
    public float[] Control2 { get; set; } = new[] { .25f, 1f };
    public KeyInterpolationData Copy() => new() { Mode=Mode, Control1=Control1 is {Length:2}?(float[])Control1.Clone():new[]{.25f,.1f}, Control2=Control2 is {Length:2}?(float[])Control2.Clone():new[]{.25f,1f} };
}

public static class KeyInterpolation
{
    public static readonly string[] Modes={"Linear","Ease in/out","Curved","Stop","Hold"};

    public static float Weight(KeyInterpolationData? data,double progress)
    {
        double t=double.IsNaN(progress)?0:Math.Clamp(progress,0,1);
        if(t<=0)return 0;if(t>=1)return 1;
        return data?.Mode switch
        {
            "Ease in/out" => (float)(t*t*(3-2*t)),
            "Stop" => (float)(t*t*t*(t*(t*6-15)+10)),
            "Hold" => 0,
            "Curved" => Bezier(data,t),
            _ => (float)t
        };
    }

    public static float[] Control(float[]? value,float x,float y)=>value is {Length:2}&&float.IsFinite(value[0])&&float.IsFinite(value[1])?new[]{Math.Clamp(value[0],0,1),Math.Clamp(value[1],0,1)}:new[]{x,y};

    static float Bezier(KeyInterpolationData data,double x)
    {
        var a=Control(data.Control1,.25f,.1f);var b=Control(data.Control2,.25f,1);
        static double At(double t,double first,double second){double s=1-t;return 3*s*s*t*first+3*s*t*t*second+t*t*t;}
        double low=0,high=1;
        // Solve time (x), not the Bezier parameter: arbitrary horizontal handles remain useful.
        for(int i=0;i<28;i++){double middle=(low+high)*.5;if(At(middle,a[0],b[0])<x)low=middle;else high=middle;}
        return (float)At((low+high)*.5,a[1],b[1]);
    }
}
