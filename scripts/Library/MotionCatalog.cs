using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Godot;

namespace GenPrevis;

/// <summary>A motion card describes a prepared source, never a command to open a file dialog.</summary>
public sealed record LibraryMotion
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ActorFamily { get; init; }
    public required string SourcePath { get; init; }
    public string? SourceAnimation { get; init; }
    public float SourceScale { get; init; } = 1;
    public double Duration { get; init; }
    public string Attribution { get; init; } = "";
    public string Description { get; init; } = "";
    public string Category { get; init; } = "Other";
    public string[] Tags { get; init; } = Array.Empty<string>();
    public string License { get; init; } = "";
    public string SourceUrl { get; init; } = "";
    public double SourceOffset { get; init; }
    public bool Loop { get; init; } = true;
}

public static class MotionCatalog
{
    public const string FoxModelPath = "res://assets/library/fox.glb";
    public const string FoxBytesPath = "res://assets/library/fox.glb.bytes";
    public const float FoxModelScale = .01f;

    public static IReadOnlyList<LibraryMotion> All { get; } = LoadMotions();

    private static IReadOnlyList<LibraryMotion> LoadMotions()
    {
        var motions = new List<LibraryMotion>
        {
            new() { Id="human-walk-gesture", Name="Walk & gesture", ActorFamily="human", SourcePath="res://samples/synthetic-walk-gesture.bvh", SourceScale=.01f, Duration=4, Attribution="GenPrevis original synthetic test motion", Description="Prepared human · walk and arm gesture", Category="Locomotion", Tags=new[]{"human","synthetic","walk","gesture"}, License="GenPrevis project asset" },
            new() { Id="human-cmu-walk", Name="Captured walk", ActorFamily="human", SourcePath="res://samples/real/CMU_02_01_Walk.bvh", SourceScale=.055f, SourceOffset=.0083333, Duration=342*.0083333, Loop=false, Attribution="CMU Graphics Lab · Bruce Hahne conversion", Description="Prepared human · recorded walking", Category="Locomotion", Tags=new[]{"human","mocap","walk","recorded","cmu"}, License="CMU", SourceUrl="https://mocap.cs.cmu.edu/" },
            new() { Id="fox-walk", Name="Fox (classic) · Walk", ActorFamily="fox", SourcePath=FoxModelPath, SourceAnimation="Walk", Duration=.7083333134651184, Attribution="PixelMannen · tomkranis · AsoboStudio / scurest", Description="Fox rig · walking cycle", Category="Locomotion", Tags=new[]{"animal","fox","walk","authored"}, License="CC0-1.0 / CC-BY-4.0", SourceUrl="https://github.com/KhronosGroup/glTF-Sample-Assets/tree/main/Models/Fox" },
            new() { Id="fox-run", Name="Fox (classic) · Run", ActorFamily="fox", SourcePath=FoxModelPath, SourceAnimation="Run", Duration=1.1583333015441895, Attribution="PixelMannen · tomkranis · AsoboStudio / scurest", Description="Fox rig · running cycle", Category="Locomotion", Tags=new[]{"animal","fox","run","authored"}, License="CC0-1.0 / CC-BY-4.0", SourceUrl="https://github.com/KhronosGroup/glTF-Sample-Assets/tree/main/Models/Fox" },
            new() { Id="fox-survey", Name="Fox (classic) · Look around", ActorFamily="fox", SourcePath=FoxModelPath, SourceAnimation="Survey", Duration=3.4166667461395264, Attribution="PixelMannen · tomkranis · AsoboStudio / scurest", Description="Fox rig · alert looking-around action", Category="Idle", Tags=new[]{"animal","fox","look","authored"}, License="CC0-1.0 / CC-BY-4.0", SourceUrl="https://github.com/KhronosGroup/glTF-Sample-Assets/tree/main/Models/Fox" }
        };
        motions.AddRange(ContentCatalog.ReadManifest<LibraryMotion>("res://assets/content/manifests/human-motions.json"));
        motions.AddRange(ContentCatalog.ReadManifest<LibraryMotion>("res://assets/content/manifests/animal-motions.json"));
        ContentCatalog.ValidateUniqueIds(motions.Select(motion => motion.Id), "motion");
        return motions.AsReadOnly();
    }

    public static LibraryMotion? Find(string id) => All.FirstOrDefault(motion => motion.Id.Equals(id, StringComparison.Ordinal));
    public static IEnumerable<LibraryMotion> ForFamily(string family) => All.Where(motion => motion.ActorFamily.Equals(family, StringComparison.Ordinal));

    /// <summary>Returns a real file path suitable for runtime import and portable project asset copies.</summary>
    public static string MaterializeSource(LibraryMotion motion) => ContentCatalog.MaterializeSource(motion.Id, motion.SourcePath);
}
