using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GoPrevis;

public partial class GoPrevisApp
{
    Label? _projectNameLabel;
    string ProjectDisplayName=>string.IsNullOrEmpty(_projectPath)?"Untitled":Path.GetFileNameWithoutExtension(_projectPath);
    void RefreshProjectIdentity()
    {
        if(_projectNameLabel!=null){_projectNameLabel.Text=ProjectDisplayName;_projectNameLabel.TooltipText=_projectPath==""?"This project has not been saved.":Path.GetFullPath(_projectPath);}
        if(IsInsideTree())GetWindow().Title="GoPrevis "+AppVersion+" — "+ProjectDisplayName+" — 3D control clips for AI video";
    }
    void SaveProjectAs()
    {
        ChooseFile("Save project as",FileDialog.FileModeEnum.SaveFile,new[]{"*.goprevis ; GoPrevis project"},SaveTo);
        if(_projectPath!="")_fileDialog.CurrentDir=Path.GetDirectoryName(Path.GetFullPath(_projectPath))!;
        _fileDialog.CurrentFile=ProjectDisplayName+".goprevis";_fileDialog.GetLineEdit().SelectAll();
    }

    void RebindSavedProjectHistory(string previous,string saved)
    {
        bool SameProject(string path)=>path==""||previous==""?path==previous:Path.GetFullPath(path).Equals(Path.GetFullPath(previous),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);
        void Rebind(Stack<(ProjectData Document,string Path)> history)
        {
            var states=history.ToArray();history.Clear();
            foreach(var state in states.Reverse())history.Push((state.Document,SameProject(state.Path)?saved:state.Path));
        }
        Rebind(_undo);Rebind(_redo);
    }

    // Dots in a shot name (e.g. "Scene 02.v3") are part of the name, not a file type.
    static string ProjectSavePath(string path)
    {
        if(string.IsNullOrWhiteSpace(path)||string.IsNullOrWhiteSpace(Path.GetFileName(path)))
            throw new ArgumentException("Enter a project name.");
        path=path.TrimEnd(' ','.');
        return path.EndsWith(".goprevis",StringComparison.OrdinalIgnoreCase)||path.EndsWith(".genprevis",StringComparison.OrdinalIgnoreCase)||path.EndsWith(".previs",StringComparison.OrdinalIgnoreCase)
            ?path:path+".goprevis";
    }
}
