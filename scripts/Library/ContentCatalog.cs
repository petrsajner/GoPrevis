using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;

namespace Previs;

/// <summary>A ready-to-place model, including provenance and its authored unit conversion.</summary>
public sealed record LibraryModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public string ActorFamily { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public float Scale { get; init; } = 1;
    public string? DefaultAnimation { get; init; }
    public bool Animated { get; init; }
    public string Attribution { get; init; } = "";
    public string License { get; init; } = "";
    public string SourceUrl { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string BuiltinKind { get; init; } = "";
    public float[] BodyScale { get; init; } = new float[] { 1, 1, 1 };
    public float[] Color { get; init; } = new float[] { .65f, .71f, .95f };
    public bool IsBuiltin => BuiltinKind.Length != 0;
}

public static class ContentCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Dictionary<string, string> MaterializedSources = new(StringComparer.Ordinal);
    private static readonly object SourceLock = new();

    public static IReadOnlyList<LibraryModel> All { get; } = LoadModels();

    public static LibraryModel? Find(string id) => All.FirstOrDefault(model => model.Id.Equals(id, StringComparison.Ordinal));
    public static IEnumerable<LibraryModel> ForCategory(string category) => All.Where(model => model.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
    public static IEnumerable<LibraryModel> ForFamily(string family) => All.Where(model => model.ActorFamily.Equals(family, StringComparison.Ordinal));

    private static IReadOnlyList<LibraryModel> LoadModels()
    {
        var models = new List<LibraryModel>
        {
            Human("human-average", "Average Adult", new float[]{1,1,1}, new float[]{.65f,.71f,.95f}),
            Human("human-broad", "Broad Adult", new float[]{1.18f,1,1.10f}, new float[]{.50f,.68f,.73f}),
            Human("human-slim", "Slim Adult", new float[]{.85f,1,.90f}, new float[]{.77f,.59f,.66f}),
            Human("human-tall", "Tall Adult", new float[]{.98f,1.12f,.98f}, new float[]{.66f,.70f,.50f}),
            Human("human-youth", "Youth", new float[]{.90f,.88f,.90f}, new float[]{.80f,.67f,.46f}),
            Human("human-child", "Child", new float[]{.70f,.65f,.72f}, new float[]{.53f,.68f,.85f}),
            new() { Id="fox_actor", Name="Fox (classic)", Category="Animals", ActorFamily="fox", SourcePath=MotionCatalog.FoxModelPath,
                Scale=MotionCatalog.FoxModelScale, DefaultAnimation="Survey", Animated=true,
                Attribution="PixelMannen · tomkranis · AsoboStudio / scurest", License="CC0-1.0 / CC-BY-4.0",
                SourceUrl="https://github.com/KhronosGroup/glTF-Sample-Assets/tree/main/Models/Fox",
                Sha256="d97044e701822bac5a62696459b27d7b375aada5de8574ed4362edbba94771f7" }
        };
        foreach (string kind in PropFactory.Kinds)
        {
            string category = kind switch
            {
                "table" or "chair" => "Furniture", "car" => "Vehicles", "tree" or "dog" => "Nature",
                "wall" or "floor" => "Architecture", _ => "Props"
            };
            models.Add(new LibraryModel { Id=kind, Name=PropFactory.DisplayName(kind), Category=category,
                BuiltinKind=kind, Attribution="Previs original procedural blockout model", License="Previs project asset" });
        }
        models.AddRange(ReadManifest<LibraryModel>("res://assets/content/manifests/animal-models.json"));
        models.AddRange(ReadManifest<LibraryModel>("res://assets/content/manifests/prop-models.json"));
        ValidateUniqueIds(models.Select(model => model.Id), "model");
        return models.AsReadOnly();
    }

    private static LibraryModel Human(string id, string name, float[] scale, float[] color) => new()
    {
        Id=id, Name=name, Category="People", ActorFamily="human", BuiltinKind="actor", BodyScale=scale, Color=color,
        Animated=true, Attribution="Previs original procedural mannequin", License="Previs project asset"
    };

    internal static IReadOnlyList<T> ReadManifest<T>(string path)
    {
        string physical = ProjectSettings.GlobalizePath(path);
        string text;
        if (File.Exists(physical)) text = File.ReadAllText(physical);
        else if (Godot.FileAccess.FileExists(path)) text = Godot.FileAccess.GetFileAsString(path);
        else
        {
            GD.PushWarning("Bundled library manifest is missing: " + path);
            return Array.Empty<T>();
        }
        try { return JsonSerializer.Deserialize<T[]>(text, JsonOptions) ?? Array.Empty<T>(); }
        catch (JsonException error) { throw new InvalidDataException("Invalid bundled library manifest: " + path, error); }
    }

    internal static void ValidateUniqueIds(IEnumerable<string> ids, string kind)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in ids)
            if (string.IsNullOrWhiteSpace(id) || !used.Add(id))
                throw new InvalidDataException("The bundled " + kind + " library has an empty or duplicate ID: " + id);
    }

    public static string MaterializeSource(LibraryModel model) => MaterializeSource(model.Id, model.SourcePath);

    /// <summary>
    /// Runtime importers and project asset copies need a real file with its original
    /// extension. Raw .glb.bytes resources survive Godot's import remapping. Sources
    /// are cached by resource and content, so all clips on one animal share one file.
    /// </summary>
    public static string MaterializeSource(string id, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new FileNotFoundException("This library item has no external source: " + id);
        if (!path.StartsWith("res://", StringComparison.Ordinal))
        {
            string external = path.StartsWith("user://", StringComparison.Ordinal) ? ProjectSettings.GlobalizePath(path) : Path.GetFullPath(path);
            if (!File.Exists(external)) throw new FileNotFoundException("The library source is missing: " + id, external);
            return external;
        }

        lock (SourceLock)
        {
            if (MaterializedSources.TryGetValue(path, out string? cached) && File.Exists(cached)) return cached;
            string raw = path + ".bytes";
            bool hasRaw = Godot.FileAccess.FileExists(raw);
            string physical = ProjectSettings.GlobalizePath(path);
            // Unimported development BVHs can be read directly without copying.
            if (!hasRaw && File.Exists(physical))
            {
                MaterializedSources[path] = physical;
                return physical;
            }
            if (!hasRaw) raw = path;
            if (!Godot.FileAccess.FileExists(raw)) throw new FileNotFoundException("The bundled library source is missing: " + id, raw);
            byte[] bytes = Godot.FileAccess.GetFileAsBytes(raw);
            if (bytes.Length == 0) throw new IOException("The bundled library source is empty: " + id);
            string sourceKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..12].ToLowerInvariant();
            string contentKey = Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
            string directory = ProjectSettings.GlobalizePath("user://library/sources/" + sourceKey + "/" + contentKey);
            string destination = Path.Combine(directory, Path.GetFileName(path));
            Directory.CreateDirectory(directory);
            if (!File.Exists(destination) || new FileInfo(destination).Length != bytes.Length) File.WriteAllBytes(destination, bytes);
            MaterializedSources[path] = destination;
            return destination;
        }
    }
}
