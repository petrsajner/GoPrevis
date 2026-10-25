using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GenPrevis;

public partial class GenPrevisApp
{
    sealed class CameraBackgroundControls
    {
        public required VBoxContainer Panel;
        public required Label Name;
        public required CheckButton Enabled;
        public required SpinBox Opacity,Offset;
        public required OptionButton Fit;
        public required Button Load,Remove;
    }
    readonly List<CameraBackgroundControls> _cameraBackgroundControls=new();
    VBoxContainer? _cameraBackgroundInspector,_cameraBackgroundInspectorContent;
    Button? _cameraBackgroundHeader;
    bool _cameraBackgroundExpanded;
    AcceptDialog? _cameraBackgroundDialog;
    string _cameraBackgroundDialogId="";
    bool _refreshingCameraBackground;

    CameraBackgroundControls BuildCameraBackgroundControls(VBoxContainer parent,Func<CameraEntry?> camera)
    {
        var panel=new VBoxContainer();panel.AddThemeConstantOverride("separation",5);parent.AddChild(panel);
        panel.AddChild(Text("CAMERA BACKGROUND",11,true));
        var name=Text("No image or video",11,true);name.ClipText=true;panel.AddChild(name);
        var load=Button("Load image / video…",()=>ChooseCameraBackground(camera(),panel));
        var remove=Button("Remove",()=>EditCameraBackground(camera(),data=>data.Path=""));panel.AddChild(Row(load,remove));
        var enabled=new CheckButton{Text="Show camera background"};panel.AddChild(enabled);
        var opacity=Spin(0,100,1,50);var fit=new OptionButton{FitToLongestItem=false,SizeFlagsHorizontal=SizeFlags.ExpandFill,CustomMinimumSize=new Vector2(76,0)};fit.AddItem("Fit");fit.AddItem("Fill / crop");panel.AddChild(Row(Text("Opacity",11,true),opacity,Text("%",11,true),fit));
        var offset=Spin(-86400,86400,.01,0);panel.AddChild(Row(Text("Video offset",11,true),offset,Text("s",11,true)));
        var note=Text("This camera only · editor guide · never exported",10,true);note.AutowrapMode=TextServer.AutowrapMode.WordSmart;panel.AddChild(note);
        enabled.Toggled+=value=>{if(!_refreshingCameraBackground)EditCameraBackground(camera(),data=>data.Enabled=value);};
        opacity.ValueChanged+=value=>{if(!_refreshingCameraBackground)EditCameraBackground(camera(),data=>data.Opacity=(float)value/100);};
        fit.ItemSelected+=index=>{if(!_refreshingCameraBackground)EditCameraBackground(camera(),data=>data.Fill=index==1);};
        offset.ValueChanged+=value=>{if(!_refreshingCameraBackground)EditCameraBackground(camera(),data=>data.OffsetSeconds=value);};
        var result=new CameraBackgroundControls{Panel=panel,Name=name,Load=load,Remove=remove,Enabled=enabled,Opacity=opacity,Fit=fit,Offset=offset};_cameraBackgroundControls.Add(result);return result;
    }
    void BuildCameraBackgroundInspector()
    {
        _cameraBackgroundInspector=new VBoxContainer();_cameraBackgroundInspector.AddThemeConstantOverride("separation",4);_inspector.AddChild(_cameraBackgroundInspector);
        _cameraBackgroundHeader=Button("▸ Camera background · None",()=>{_cameraBackgroundExpanded=!_cameraBackgroundExpanded;RefreshCameraBackgroundControls();},true);
        _cameraBackgroundHeader.Alignment=HorizontalAlignment.Left;_cameraBackgroundHeader.ClipText=true;_cameraBackgroundHeader.TooltipText="Image or video background for this camera only. Expand to load and adjust it.";_cameraBackgroundInspector.AddChild(_cameraBackgroundHeader);
        _cameraBackgroundInspectorContent=BuildCameraBackgroundControls(_cameraBackgroundInspector,()=>_selectedCamera).Panel;RefreshCameraBackgroundControls();
    }
    void OpenActiveCameraBackground()
    {
        var camera=ActiveReferenceCamera;if(camera==null)return;
        if(_cameraBackgroundDialog==null)
        {
            _cameraBackgroundDialog=new AcceptDialog{Title="Camera background",Exclusive=true,WrapControls=false};_cameraBackgroundDialog.GetOkButton().Text="Done";AddChild(_cameraBackgroundDialog);
            var box=new VBoxContainer();_cameraBackgroundDialog.AddChild(box);BuildCameraBackgroundControls(box,()=>_cameras.FirstOrDefault(item=>item.Id==_cameraBackgroundDialogId));
        }
        _cameraBackgroundDialogId=camera.Id;_cameraBackgroundDialog.Title=camera.Name+" · Camera background";RefreshCameraBackgroundControls();_cameraBackgroundDialog.PopupCentered(new Vector2I(450,300));
    }
    void EditCameraBackground(CameraEntry? camera,Action<CameraReferenceData> edit)
    {
        if(camera==null||_refreshingCameraBackground||_refreshing||_exportBusy)return;
        var next=camera.Reference.ValidatedCopy();edit(next);next=next.ValidatedCopy();PushUndo();camera.Reference=next;
        UpdateCameraReference();RefreshCameraBackgroundControls();Status(camera.Name+" background updated · editor only.");
    }
    void ApplyActiveCameraReference(CameraReferenceData data)
    {
        if(ActiveReferenceCamera is not {} camera)return;camera.Reference=data.ValidatedCopy();UpdateCameraReference();RefreshCameraBackgroundControls();
    }
    void ChooseCameraBackground(CameraEntry? camera,Control anchor)
    {
        if(camera==null)return;string cameraId=camera.Id;
        var dialog=new FileDialog{Access=FileDialog.AccessEnum.Filesystem,FileMode=FileDialog.FileModeEnum.OpenFile,UseNativeDialog=false,Title="Choose background for "+camera.Name};
        dialog.Filters=new[]{"*.png,*.jpg,*.jpeg,*.webp,*.bmp ; Background images","*.mp4,*.mov,*.mkv,*.webm,*.avi,*.m4v,*.ogv ; Background videos"};
        if(anchor.GetWindow()==_cameraBackgroundDialog)_cameraBackgroundDialog!.AddChild(dialog);else AddChild(dialog);
        RestoreBackgroundDirectory(dialog,camera);
        dialog.FileSelected+=path=>
        {
            string full=Path.GetFullPath(path);RememberBackgroundDirectory(Path.GetDirectoryName(full));var target=_cameras.FirstOrDefault(item=>item.Id==cameraId);
            bool reload=target!=null&&string.Equals(target.Reference.Path,full,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);
            EditCameraBackground(target,data=>{data.Path=full;data.Enabled=true;});if(reload&&target?.Rig==_shot)_=LoadCameraReferenceAsync();dialog.QueueFree();
        };
        dialog.Canceled+=()=>{RememberBackgroundDirectory(dialog.CurrentDir);dialog.QueueFree();};dialog.PopupCentered(new Vector2I(900,600));
    }
    void RefreshCameraBackgroundControls()
    {
        bool before=_refreshingCameraBackground;_refreshingCameraBackground=true;
        foreach(var controls in _cameraBackgroundControls)
        {
            if(!GodotObject.IsInstanceValid(controls.Panel))continue;
            bool inspector=controls.Panel==_cameraBackgroundInspectorContent;
            var camera=inspector?_selectedCamera:_cameras.FirstOrDefault(item=>item.Id==_cameraBackgroundDialogId);
            bool visible=camera!=null&&(!inspector||_inspectorSection=="Object");
            if(inspector)
            {
                _cameraBackgroundInspector!.Visible=visible;
                if(_cameraBackgroundHeader!=null){_cameraBackgroundHeader.Text=(_cameraBackgroundExpanded?"▾ ":"▸ ")+"Camera background · "+(string.IsNullOrEmpty(camera?.Reference.Path)?"None":Path.GetFileName(camera.Reference.Path));_cameraBackgroundHeader.ButtonPressed=_cameraBackgroundExpanded;}
            }
            controls.Panel.Visible=visible&&(!inspector||_cameraBackgroundExpanded);if(camera==null)continue;
            var data=camera.Reference;controls.Name.Text=string.IsNullOrEmpty(data.Path)?"No image or video":Path.GetFileName(data.Path);controls.Name.TooltipText=data.Path;
            controls.Enabled.ButtonPressed=data.Enabled;controls.Opacity.Value=data.Opacity*100;controls.Fit.Select(data.Fill?1:0);controls.Offset.Value=data.OffsetSeconds;
            controls.Remove.Disabled=string.IsNullOrEmpty(data.Path);controls.Enabled.Disabled=string.IsNullOrEmpty(data.Path);
        }
        _refreshingCameraBackground=before;
    }
    void RestoreCameraReferences(ProjectData document,string baseDirectory)
    {
        foreach(var camera in _cameras)
        {
            var saved=document.Cameras.FirstOrDefault(item=>item.Id==camera.Id);
            CameraReferenceData reference=(saved?.Reference??new()).ValidatedCopy();
            if(document.Version<8&&camera.Rig==_shot&&document.Setup is {} legacy&&!string.IsNullOrEmpty(legacy.ReferencePath))
                reference=new CameraReferenceData{Path=legacy.ReferencePath,Enabled=legacy.ReferenceEnabled,Opacity=legacy.ReferenceOpacity,Fill=legacy.ReferenceFill,OffsetSeconds=legacy.ReferenceOffsetSeconds}.ValidatedCopy();
            if(!string.IsNullOrEmpty(reference.Path)&&!Path.IsPathRooted(reference.Path))reference.Path=Path.GetFullPath(Path.Combine(baseDirectory,reference.Path));
            camera.Reference=reference;
        }
        UpdateCameraReference();RefreshCameraBackgroundControls();
    }
}
