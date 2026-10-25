using Godot;
using System;

namespace GenPrevis;

/// <summary>Run with --headless --script res://scripts/Testing/LayoutProbe.cs.</summary>
public partial class LayoutProbe:SceneTree
{
    public override async void _Initialize()
    {
        try
        {
            var app=new GenPrevisApp();app.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);Root.AddChild(app);
            for(int i=0;i<5;i++)await ToSignal(this,SignalName.ProcessFrame);
            int checks=await app.RunResponsiveLayoutChecks();
            if(checks<24)throw new InvalidOperationException("Not all desktop layout sizes were checked.");
            GD.Print("LAYOUT_PROBE_OK checks="+checks);Quit(0);
        }
        catch(Exception ex){GD.PushError("LAYOUT_PROBE_FAILED "+ex);Quit(1);}
    }
}
