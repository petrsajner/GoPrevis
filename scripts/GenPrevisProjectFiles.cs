using System;
using System.IO;

namespace GenPrevis;

public partial class GenPrevisApp
{
    // Dots in a shot name (e.g. "Scene 02.v3") are part of the name, not a file type.
    static string ProjectSavePath(string path)
    {
        if(string.IsNullOrWhiteSpace(path)||string.IsNullOrWhiteSpace(Path.GetFileName(path)))
            throw new ArgumentException("Enter a project name.");
        path=path.TrimEnd(' ','.');
        return path.EndsWith(".genprevis",StringComparison.OrdinalIgnoreCase)||path.EndsWith(".previs",StringComparison.OrdinalIgnoreCase)
            ?path:path+".genprevis";
    }
}
