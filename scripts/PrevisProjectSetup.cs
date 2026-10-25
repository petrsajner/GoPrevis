using Godot;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Previs;

public partial class PrevisApp
{
    ProjectSetupData _projectSetup=new();
    ConfirmationDialog? _projectSetupDialog;
    OptionButton _setupAspect=null!,_setupFps=null!,_setupReferenceFit=null!;
    SpinBox _setupWidth=null!,_setupHeight=null!,_setupCustomFps=null!,_setupReferenceOpacity=null!,_setupReferenceOffset=null!;
    Label _setupReferenceName=null!;
    CheckButton _setupReferenceEnabled=null!;
    Button? _projectSetupButton;
    bool _refreshingProjectSetup;
    ProjectSetupData? _setupDraft;
    AspectRatioContainer? _previewAspect;
    static readonly int[] ProjectFrameRates={12,15,24,25,30,48,50,60};
    static readonly (string Name,int Width,int Height)[] ProjectFormats={
        ("16:9 · Landscape",1920,1080),("9:16 · Portrait",1080,1920),("1:1 · Square",1080,1080),
        ("4:3 · Classic",1440,1080),("3:2 · Photo",1620,1080),("2.39:1 · Cinema",2582,1080)};
    public Vector2I ProjectResolution=>new(_projectSetup.Width,_projectSetup.Height);
    public float ProjectAspect=>_projectSetup.Width/(float)_projectSetup.Height;
    public string ProjectAspectLabel
    {
        get
        {
            foreach(var format in ProjectFormats)
                if(Math.Abs(ProjectAspect-format.Width/(float)format.Height)<.001f)return format.Name.Split(' ')[0];
            return _projectSetup.Width+":"+_projectSetup.Height;
        }
    }
    ProjectSetupData SnapshotProjectSetup()=>_projectSetup.ValidatedCopy();
    void ApplyProjectSetup(ProjectSetupData? setup)
    {
        var next=(setup??new()).ValidatedCopy();bool sourceChanged=next.ReferencePath!=_projectSetup.ReferencePath;
        _projectSetup=next;
        if(_fpsChoice!=null){bool before=_refreshing;_refreshing=true;EnsureOptionId(_fpsChoice,_fps,_fps.ToString(CultureInfo.InvariantCulture));_fpsChoice.Select(Enumerable.Range(0,_fpsChoice.ItemCount).First(i=>_fpsChoice.GetItemId(i)==_fps));_refreshing=before;}
        if(_projectSetupButton!=null)_projectSetupButton.Text="Project · "+ProjectAspectLabel+" · "+_fps+" fps";
        if(_previewAspect!=null)_previewAspect.Ratio=ProjectAspect;
        FitCompactPreview();UpdateProjectCameraView();RefreshProjectSetupControls();
        if(sourceChanged)_=LoadCameraReferenceAsync();else UpdateCameraReference();
    }
    void ChangeProjectFps(int fps)
    {
        if(_refreshing)return;if(fps<1||fps>120)throw new ArgumentException("Choose a frame rate between 1 and 120 fps.");
        if(_fps==fps)return;PushUndo();_fps=fps;_scrub.Step=1.0/fps;
        EnsureOptionId(_fpsChoice,fps,fps.ToString(CultureInfo.InvariantCulture));
        bool before=_refreshing;_refreshing=true;_fpsChoice.Select(Enumerable.Range(0,_fpsChoice.ItemCount).First(i=>_fpsChoice.GetItemId(i)==fps));_refreshing=before;
        ApplyProjectSetup(_projectSetup);SetTime(_time);
    }
    static void EnsureOptionId(OptionButton option,int id,string label)
    {if(!Enumerable.Range(0,option.ItemCount).Any(i=>option.GetItemId(i)==id))option.AddItem(label,id);}
    public void UpdateProjectCameraView()
    {
        if(_previewCamera==null||_shot==null)return;
        foreach(var camera in _cameras)camera.Rig.AspectRatio=ProjectAspect;
        _previewCamera.KeepAspect=Camera3D.KeepAspectEnum.Width;
        _previewCamera.Fov=_shot.ShotCamera.Fov;
        if(_cameraInfo!=null)_cameraInfo.Text=$"{ActiveCameraName} · {_shot.FocalLengthMm:0} mm · {ProjectAspectLabel} · {_fps} fps · {ActiveCameraKeyCount()} keys";
        if(_pilot)
        {
            _editorCamera.KeepAspect=Camera3D.KeepAspectEnum.Width;
            float aspect=_sceneViewport.Size.X/(float)Math.Max(1,_sceneViewport.Size.Y);
            _editorCamera.Fov=Mathf.RadToDeg(2*Mathf.Atan(Mathf.Tan(Mathf.DegToRad(_shot.ShotCamera.Fov)*.5f)*Math.Max(1,aspect/ProjectAspect)));
        }
        _framing?.QueueRedraw();UpdateCameraReference();
    }
    static Rect2 AspectGate(Vector2 size,float aspect)
    {
        if(size.X<=0||size.Y<=0)return new Rect2();
        Vector2 fitted=size.X/size.Y>aspect?new Vector2(size.Y*aspect,size.Y):new Vector2(size.X,size.X/aspect);
        return new Rect2((size-fitted)*.5f,fitted);
    }
    void OpenProjectSetup()
    {
        if(_projectSetupDialog==null)BuildProjectSetupDialog();_setupDraft=SnapshotProjectSetup();
        RefreshProjectSetupControls();_projectSetupDialog!.PopupCentered(new Vector2I(520,570));
    }
    void BuildProjectSetupDialog()
    {
        _projectSetupDialog=new ConfirmationDialog{Title="Project setup",Exclusive=true,WrapControls=false};_projectSetupDialog.GetOkButton().Text="Apply";AddChild(_projectSetupDialog);
        var box=new VBoxContainer();box.AddThemeConstantOverride("separation",8);_projectSetupDialog.AddChild(box);
        box.AddChild(Text("SHOT FORMAT",11,true));_setupAspect=new OptionButton{SizeFlagsHorizontal=SizeFlags.ExpandFill};
        foreach(var format in ProjectFormats)_setupAspect.AddItem(format.Name);_setupAspect.AddItem("Custom");box.AddChild(_setupAspect);
        _setupWidth=Spin(64,8192,2,1920);_setupHeight=Spin(64,8192,2,1080);box.AddChild(Row(Text("Resolution",12,true),_setupWidth,Text("×"),_setupHeight,Text("px",11,true)));
        _setupFps=new OptionButton{SizeFlagsHorizontal=SizeFlags.ExpandFill};foreach(int fps in ProjectFrameRates)_setupFps.AddItem(fps+" fps",fps);_setupFps.AddItem("Custom",0);
        _setupCustomFps=Spin(1,120,1,24);box.AddChild(Row(Text("Frame rate",12,true),_setupFps,_setupCustomFps));
        _setupAspect.ItemSelected+=index=>{if(_refreshingProjectSetup)return;if(index<ProjectFormats.Length){_refreshingProjectSetup=true;var format=ProjectFormats[(int)index];_setupWidth.Value=format.Width;_setupHeight.Value=format.Height;_refreshingProjectSetup=false;}};
        void FormatEdited(double _){if(_refreshingProjectSetup)return;int match=Array.FindIndex(ProjectFormats,f=>f.Width==(int)_setupWidth.Value&&f.Height==(int)_setupHeight.Value);_setupAspect.Select(match<0?ProjectFormats.Length:match);}
        _setupWidth.ValueChanged+=FormatEdited;_setupHeight.ValueChanged+=FormatEdited;
        _setupFps.ItemSelected+=_=>{_setupCustomFps.Editable=_setupFps.GetSelectedId()==0;_setupCustomFps.Visible=_setupFps.GetSelectedId()==0;};
        box.AddChild(new HSeparator());box.AddChild(Text("CAMERA REFERENCE",11,true));
        var note=Text("Image or video guide in camera views only. It is excluded from every render and export. Videos follow scene time; audio is muted.",11,true);note.AutowrapMode=TextServer.AutowrapMode.WordSmart;box.AddChild(note);
        _setupReferenceName=Text("No reference",11,true);_setupReferenceName.ClipText=true;box.AddChild(_setupReferenceName);
        box.AddChild(Row(Button("Load image / video…",ChooseCameraReference),Button("Remove",()=>{if(_setupDraft!=null)_setupDraft.ReferencePath="";_setupReferenceName.Text="No reference";})));
        _setupReferenceEnabled=new CheckButton{Text="Show in camera views"};box.AddChild(_setupReferenceEnabled);
        _setupReferenceOpacity=Spin(0,100,1,50);_setupReferenceFit=new OptionButton();_setupReferenceFit.AddItem("Fit");_setupReferenceFit.AddItem("Fill / crop");box.AddChild(Row(Text("Opacity",12,true),_setupReferenceOpacity,Text("%",11,true),_setupReferenceFit));
        _setupReferenceOffset=Spin(-86400,86400,.01,0);box.AddChild(Row(Text("Video time offset",12,true),_setupReferenceOffset,Text("s",11,true)));
        var help=Text("Use opacity to compare the guide with your 3D scene. A positive offset starts later in the video; its last frame holds after the end.",11,true);help.AutowrapMode=TextServer.AutowrapMode.WordSmart;box.AddChild(help);
        _projectSetupDialog.Confirmed+=()=>
        {
            try
            {
                var next=(_setupDraft??SnapshotProjectSetup()).ValidatedCopy();next.Width=(int)_setupWidth.Value;next.Height=(int)_setupHeight.Value;
                next.ReferenceEnabled=_setupReferenceEnabled.ButtonPressed;next.ReferenceOpacity=(float)_setupReferenceOpacity.Value/100;next.ReferenceFill=_setupReferenceFit.Selected==1;next.ReferenceOffsetSeconds=_setupReferenceOffset.Value;next=next.ValidatedCopy();
                int fps=_setupFps.GetSelectedId()==0?(int)_setupCustomFps.Value:_setupFps.GetSelectedId();PushUndo();_fps=fps;_scrub.Step=1.0/fps;
                EnsureOptionId(_fpsChoice,fps,fps.ToString(CultureInfo.InvariantCulture));bool before=_refreshing;_refreshing=true;_fpsChoice.Select(Enumerable.Range(0,_fpsChoice.ItemCount).First(i=>_fpsChoice.GetItemId(i)==fps));_refreshing=before;
                _setupDraft=null;ApplyProjectSetup(next);SetTime(_time);Status("Project setup applied · "+ProjectAspectLabel+" · "+fps+" fps.");
            }catch(Exception ex){Error(ex.Message);}
        };
    }
    void RefreshProjectSetupControls()
    {
        if(_projectSetupDialog==null)return;var value=_setupDraft??_projectSetup;_refreshingProjectSetup=true;
        int match=Array.FindIndex(ProjectFormats,f=>f.Width==value.Width&&f.Height==value.Height);_setupAspect.Select(match<0?ProjectFormats.Length:match);_setupWidth.Value=value.Width;_setupHeight.Value=value.Height;
        int fpsIndex=Array.IndexOf(ProjectFrameRates,_fps);_setupFps.Select(fpsIndex<0?ProjectFrameRates.Length:fpsIndex);_setupCustomFps.Value=_fps;_setupCustomFps.Editable=fpsIndex<0;_setupCustomFps.Visible=fpsIndex<0;
        _setupReferenceName.Text=string.IsNullOrEmpty(value.ReferencePath)?"No reference":Path.GetFileName(value.ReferencePath);_setupReferenceName.TooltipText=value.ReferencePath;
        _setupReferenceEnabled.ButtonPressed=value.ReferenceEnabled;_setupReferenceOpacity.Value=value.ReferenceOpacity*100;_setupReferenceFit.Select(value.ReferenceFill?1:0);_setupReferenceOffset.Value=value.ReferenceOffsetSeconds;_refreshingProjectSetup=false;
    }
    void ChooseCameraReference()
    {
        var dialog=new FileDialog{Access=FileDialog.AccessEnum.Filesystem,FileMode=FileDialog.FileModeEnum.OpenFile,UseNativeDialog=false,Title="Choose camera reference"};
        dialog.Filters=new[]{"*.png,*.jpg,*.jpeg,*.webp,*.bmp ; Reference images","*.mp4,*.mov,*.mkv,*.webm,*.avi,*.m4v,*.ogv ; Reference videos"};_projectSetupDialog!.AddChild(dialog);
        dialog.FileSelected+=path=>{_setupDraft??=SnapshotProjectSetup();_setupDraft.ReferencePath=Path.GetFullPath(path);_setupReferenceName.Text=Path.GetFileName(path);_setupReferenceName.TooltipText=path;dialog.QueueFree();};dialog.Canceled+=()=>dialog.QueueFree();dialog.PopupCentered(new Vector2I(900,600));
    }
    partial class CameraFrameOverlay
    {
        void DrawProjectFrame()
        {
            if(App==null||!App._pilot)return;var gate=AspectGate(Size,App.ProjectAspect);
            DrawRect(new Rect2(0,0,Size.X,gate.Position.Y),Colors.Black);DrawRect(new Rect2(0,gate.End.Y,Size.X,Size.Y-gate.End.Y),Colors.Black);
            DrawRect(new Rect2(0,gate.Position.Y,gate.Position.X,gate.Size.Y),Colors.Black);DrawRect(new Rect2(gate.End.X,gate.Position.Y,Size.X-gate.End.X,gate.Size.Y),Colors.Black);
            DrawRect(gate,new Color(1,1,1,.3f),false,1);
            DrawString(ThemeDB.FallbackFont,new Vector2(12,20),App.ActiveCameraName.ToUpperInvariant()+" · "+App.ProjectAspectLabel,fontSize:12,modulate:Colors.White);
        }
    }
}
