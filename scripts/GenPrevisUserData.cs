using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GenPrevis;

public partial class GenPrevisApp
{
    const string LegacyDataFolderName="Previs";
    static bool DiagnosticRun=>OS.GetCmdlineUserArgs().Any(argument=>argument=="--test-ui"||argument.StartsWith("--",StringComparison.Ordinal)&&!argument.Contains('=')&&argument.EndsWith("-test",StringComparison.Ordinal));

    void MigrateLegacyUserData()
    {
        if(DiagnosticRun)return;
        try
        {
            string destination=OS.GetUserDataDir();string? parent=Path.GetDirectoryName(destination.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar));
            if(parent!=null)ImportLegacyUserData(Path.Combine(parent,LegacyDataFolderName),destination);
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException)
        {GD.PushWarning("GenPrevis could not import previous user preferences: "+ex.Message);}
    }

    static int ImportLegacyUserData(string source,string destination)
    {
        source=Path.GetFullPath(source);destination=Path.GetFullPath(destination);
        if(!Directory.Exists(source)||source.Equals(destination,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))return 0;
        string marker=Path.Combine(destination,".legacy-previs-import.json");if(File.Exists(marker))return 0;
        int copied=0;
        foreach(string group in new[]{"settings","library/poses"})
        {
            string input=Path.Combine(source,group);if(!Directory.Exists(input))continue;
            var options=new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint};
            foreach(string file in Directory.EnumerateFiles(input,"*",options))
            {
                string relative=Path.GetRelativePath(source,file),target=Path.Combine(destination,relative);
                if(File.Exists(target))continue;Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target,false);copied++;
            }
        }
        Directory.CreateDirectory(destination);
        File.WriteAllText(marker,JsonSerializer.Serialize(new{product="GenPrevis",legacyProduct=LegacyDataFolderName,importedUtc=DateTime.UtcNow,copiedFiles=copied}));
        return copied;
    }
}
