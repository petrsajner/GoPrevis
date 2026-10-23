using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Previs;

/// <summary>Headless runtime GLB import/animation check; requires -- --model=absolute-path.</summary>
public partial class ModelImportProbe : SceneTree
{
    public override async void _Initialize()
    {
        Node? model = null;
        try
        {
            var argument = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--model=", StringComparison.Ordinal));
            Check(argument != null, "Pass -- --model=<absolute path to an animated GLB>.");
            string path = argument!["--model=".Length..];
            using var document = new GltfDocument();
            using var state = new GltfState();
            var result = document.AppendFromFile(path, state);
            Check(result == Error.Ok, "Runtime import failed: " + result);
            model = document.GenerateScene(state);
            Check(model != null, "Runtime importer returned no scene.");
            Root.AddChild(model!);
            await ToSignal(this, SignalName.ProcessFrame);
            await ToSignal(this, SignalName.ProcessFrame);

            var nodes = Descendants(model!).ToArray();
            var skeletons = nodes.OfType<Skeleton3D>().ToArray();
            var player = nodes.OfType<AnimationPlayer>().FirstOrDefault();
            Check(skeletons.Length >= 1, "The imported model has no native skeleton.");
            Check(player != null, "The imported model has no AnimationPlayer.");
            string[] clips = player!.GetAnimationList().Where(name => name != "RESET").ToArray();
            Check(clips.Length >= 2, "At least two native clips are required for this probe.");
            player.Set("callback_mode_process", 2); // Manual, matching application scrubbing.

            for (int clipIndex = 0; clipIndex < 2; clipIndex++)
            {
                string clipName = clips[clipIndex];
                double length = player.GetAnimation(clipName).Length;
                Check(length > 0, "Native clip has no duration: " + clipName);
                player.Play(clipName);
                player.Seek(length * 0.19, true);
                player.Advance(0);
                var first = CaptureBones(skeletons);
                player.Seek(length * 0.63, true);
                player.Advance(0);
                var second = CaptureBones(skeletons);
                Check(first.Length > 0 && first.Length == second.Length, "Native bone count changed.");
                bool moved = false;
                for (int i = 0; i < first.Length; i++)
                    moved |= !first[i].IsEqualApprox(second[i]);
                Check(moved, "The skeleton did not change when scrubbing native clip: " + clipName);
                GD.Print($"MODEL_NATIVE_CLIP_OK index={clipIndex} name={clipName} duration={length:0.000} bones={first.Length}");

                player.Seek(length * 0.19, true);
                player.Advance(0);
                var repeated = CaptureBones(skeletons);
                Check(first.Zip(repeated).All(pair => pair.First.IsEqualApprox(pair.Second)),
                    "Native clip scrubbing is not deterministic: " + clipName);
            }

            GD.Print($"MODEL_IMPORT_OK skeletons={skeletons.Length} clips={clips.Length} names={string.Join(",", clips)}");
            model!.QueueFree();
            model = null;
            await ToSignal(this, SignalName.ProcessFrame);
            await ToSignal(this, SignalName.ProcessFrame);
            Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError("MODEL_IMPORT_FAILED " + exception);
            model?.QueueFree();
            Quit(1);
        }
    }

    private static Transform3D[] CaptureBones(Skeleton3D[] skeletons)
        => skeletons.SelectMany(skeleton => Enumerable.Range(0, skeleton.GetBoneCount()).Select(skeleton.GetBonePose)).ToArray();

    private static IEnumerable<Node> Descendants(Node root)
    {
        yield return root;
        foreach (Node child in root.GetChildren())
            foreach (Node descendant in Descendants(child))
                yield return descendant;
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
    }
}
