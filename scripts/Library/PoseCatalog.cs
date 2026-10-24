using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Previs;

/// <summary>A project-authored static blocking pose; it never creates a motion clip.</summary>
public sealed record LibraryPose
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public string[] Tags { get; init; } = Array.Empty<string>();
    public string Description { get; init; } = "";
    public StaticPoseData Pose { get; init; } = new();
}

public static class PoseCatalog
{
    public static IReadOnlyList<LibraryPose> All { get; } = Load();
    public static LibraryPose? Find(string id) => All.FirstOrDefault(pose => pose.Id.Equals(id, StringComparison.Ordinal));

    static IReadOnlyList<LibraryPose> Load()
    {
        const string path="res://assets/content/manifests/human-poses.json";
        string physical=ProjectSettings.GlobalizePath(path);
        string json=File.Exists(physical)?File.ReadAllText(physical):Godot.FileAccess.FileExists(path)?Godot.FileAccess.GetFileAsString(path):throw new FileNotFoundException("The bundled pose library is missing.",path);
        var items=JsonSerializer.Deserialize<LibraryPose[]>(json,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??Array.Empty<LibraryPose>();
        if(items.Length==0||items.Any(p=>string.IsNullOrWhiteSpace(p.Id)||string.IsNullOrWhiteSpace(p.Name))||items.Select(p=>p.Id).Distinct(StringComparer.Ordinal).Count()!=items.Length)throw new InvalidDataException("The bundled pose library has an empty or duplicate pose ID.");
        return Array.AsReadOnly(items);
    }
}
