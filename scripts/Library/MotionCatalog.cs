using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Godot;

namespace Previs;

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
}

public static class MotionCatalog
{
    public const string FoxModelPath = "res://assets/library/fox.glb";
    public const string FoxBytesPath = "res://assets/library/fox.glb.bytes";
    public const float FoxModelScale = .01f;

    public static IReadOnlyList<LibraryMotion> All { get; } = new LibraryMotion[]
    {
        new() { Id="human-walk-gesture", Name="Walk & gesture", ActorFamily="human", SourcePath="res://samples/synthetic-walk-gesture.bvh", SourceScale=.01f, Duration=4, Attribution="Previs original synthetic test motion", Description="Prepared human · walk and arm gesture" },
        new() { Id="human-cmu-walk", Name="Captured walk", ActorFamily="human", SourcePath="res://samples/real/CMU_02_01_Walk.bvh", SourceScale=.055f, Duration=344*.0083333, Attribution="CMU Graphics Lab · Bruce Hahne conversion", Description="Prepared human · recorded walking" },
        new() { Id="fox-walk", Name="Fox · Walk", ActorFamily="fox", SourcePath=FoxModelPath, SourceAnimation="Walk", Duration=.7083333134651184, Attribution="PixelMannen · tomkranis · AsoboStudio / scurest", Description="Fox rig · walking cycle" },
        new() { Id="fox-run", Name="Fox · Run", ActorFamily="fox", SourcePath=FoxModelPath, SourceAnimation="Run", Duration=1.1583333015441895, Attribution="PixelMannen · tomkranis · AsoboStudio / scurest", Description="Fox rig · running cycle" },
        new() { Id="fox-survey", Name="Fox · Look around", ActorFamily="fox", SourcePath=FoxModelPath, SourceAnimation="Survey", Duration=3.4166667461395264, Attribution="PixelMannen · tomkranis · AsoboStudio / scurest", Description="Fox rig · alert looking-around action" }
    };

    public static LibraryMotion? Find(string id) => All.FirstOrDefault(motion => motion.Id.Equals(id, StringComparison.Ordinal));
    public static IEnumerable<LibraryMotion> ForFamily(string family) => All.Where(motion => motion.ActorFamily.Equals(family, StringComparison.Ordinal));

    /// <summary>Returns a real file path suitable for runtime import and portable project asset copies.</summary>
    public static string MaterializeSource(LibraryMotion motion)
    {
        string path=motion.SourcePath;
        if(!path.StartsWith("res://",StringComparison.Ordinal))return Path.GetFullPath(path);
        string physical=ProjectSettings.GlobalizePath(path);if(File.Exists(physical))return physical;
        // glTF import remapping must not replace the original bytes used by runtime import.
        string raw=path==FoxModelPath?FoxBytesPath:path;
        if(!Godot.FileAccess.FileExists(raw))throw new FileNotFoundException("The bundled motion source is missing: "+motion.Name,raw);
        byte[] bytes=Godot.FileAccess.GetFileAsBytes(raw);if(bytes.Length==0)throw new IOException("The bundled motion source is empty: "+motion.Name);
        string directory=ProjectSettings.GlobalizePath("user://library/"+motion.Id);Directory.CreateDirectory(directory);
        string destination=Path.Combine(directory,Path.GetFileName(path));File.WriteAllBytes(destination,bytes);return destination;
    }
}
