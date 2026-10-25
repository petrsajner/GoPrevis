using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GoPrevis;

/// <summary>Local UI preferences, deliberately independent of project files and Undo.</summary>
public sealed class EditorFileLocations
{
    readonly string _file;
    readonly Dictionary<string,string> _directories=new(StringComparer.Ordinal);

    public EditorFileLocations(string file)
    {
        _file=file;
        try
        {
            if(!File.Exists(file))return;
            var saved=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(file));
            if(saved!=null)foreach(var item in saved)if(!string.IsNullOrWhiteSpace(item.Key)&&!string.IsNullOrWhiteSpace(item.Value))_directories[item.Key]=item.Value;
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException){}
    }

    public string DirectoryFor(string purpose,string? fallbackDirectory=null)
    {
        if(_directories.TryGetValue(purpose,out var previous)&&NearestExisting(previous) is {} existing)return existing;
        return NearestExisting(fallbackDirectory)??Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    public void Remember(string purpose,string? directory)
    {
        if(string.IsNullOrWhiteSpace(purpose)||string.IsNullOrWhiteSpace(directory))return;
        string? full=NearestExisting(directory);if(full==null)return;
        if(_directories.TryGetValue(purpose,out var current)&&current==full)return;
        _directories[purpose]=full;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file))!);
            string temporary=_file+".tmp";File.WriteAllText(temporary,JsonSerializer.Serialize(_directories,new JsonSerializerOptions{WriteIndented=true}));File.Move(temporary,_file,true);
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){}
    }

    static string? NearestExisting(string? directory)
    {
        if(string.IsNullOrWhiteSpace(directory))return null;
        try
        {
            string? candidate=Path.GetFullPath(directory);
            while(candidate!=null){if(Directory.Exists(candidate))return candidate;candidate=Path.GetDirectoryName(candidate);}
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){}
        return null;
    }
}
