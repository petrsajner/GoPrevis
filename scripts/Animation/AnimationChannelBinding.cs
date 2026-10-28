using Godot;
using System;

namespace GoPrevis;

/// <summary>Shared property description used by the inspector, dope sheet and graph editor.</summary>
public sealed class AnimationChannelBinding
{
    public string TargetId="",TargetName="",Group="",Path="",Label="",Unit="";
    public bool Discrete;
    public double Minimum=-1e9,Maximum=1e9;
    public Color Color=new("b3a1ef");
    public Func<ScalarAnimationCurve?> Curve=()=>null;
    public Func<ScalarAnimationCurve> EnsureCurve=()=>new();
    public Func<double> Read=()=>0;
    public Action Clear=()=>{};
    public Action<double>? Write;
    internal Action<double>? Apply;
    internal Func<double>? BaseRead;
    public string Identity=>TargetId+":"+Path;
}
