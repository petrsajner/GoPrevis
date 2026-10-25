using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GoPrevis;

/// <summary>User presets stay separate from the bundled library; projects embed their own pose snapshot.</summary>
public static class SavedPoseLibrary
{
    public static string StoragePath=>ProjectSettings.GlobalizePath("user://library/poses");
    static string FilePath(string id,string extension)
    {
        if(!Regex.IsMatch(id,"^[a-zA-Z0-9_-]+$"))throw new InvalidDataException("The saved pose ID is invalid.");
        return Path.Combine(StoragePath,id+extension);
    }
    public static string ThumbnailPath(string id)=>FilePath(id,".png");
    public static bool Exists(string id)=>File.Exists(FilePath(id,".json"));
    public static IReadOnlyList<LibraryPose> Load()
    {
        if(!Directory.Exists(StoragePath))return Array.Empty<LibraryPose>();var result=new List<LibraryPose>();
        foreach(string file in Directory.EnumerateFiles(StoragePath,"*.json").Order())
        {
            try
            {
                var pose=JsonSerializer.Deserialize<LibraryPose>(File.ReadAllText(file));
                if(pose==null||pose.Pose==null||pose.Id!=Path.GetFileNameWithoutExtension(file))continue;
                _=FilePath(pose.Id,".json");result.Add(pose with{Category="My poses",Tags=pose.Tags??Array.Empty<string>()});
            }
            catch(Exception ex)when(ex is IOException or JsonException or InvalidDataException){GD.PushWarning("Saved pose could not be read: "+Path.GetFileName(file));}
        }
        return result;
    }
    public static void Save(LibraryPose pose)
    {
        Directory.CreateDirectory(StoragePath);string path=FilePath(pose.Id,".json"),temporary=path+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(pose,new JsonSerializerOptions{WriteIndented=true}));File.Move(temporary,path,true);
    }
    public static void Delete(string id)
    {
        foreach(string extension in new[]{".json",".png"}){string path=FilePath(id,extension);if(File.Exists(path))File.Delete(path);}
    }
}
