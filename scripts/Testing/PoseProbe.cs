using Godot;
using System;

namespace Previs;

/// <summary>Run --headless --script res://scripts/Testing/PoseProbe.cs for integrated static-pose checks.</summary>
public partial class PoseProbe:SceneTree
{
    public override async void _Initialize()
    {
        try
        {
            var app=new PrevisApp();app.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);Root.AddChild(app);
            for(int frame=0;frame<5;frame++)await ToSignal(this,SignalName.ProcessFrame);
            await app.RunPoseLibraryChecks();Quit(0);
        }
        catch(Exception error){GD.PushError("POSE_PROBE_FAILED "+error);Quit(1);}
    }
}
