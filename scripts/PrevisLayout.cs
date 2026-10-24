using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Previs;

/// <summary>
/// Desktop chrome uses real layout pixels. The project must disable whole-canvas
/// stretching; only the 3D viewport grows as the desktop window gets larger.
/// </summary>
public partial class PrevisApp
{
    VBoxContainer _layoutRoot=null!,_cameraQuickControls=null!;
    PanelContainer _layoutLibraryDock=null!,_layoutInspectorDock=null!;
    VSplitContainer _layoutWorkSplit=null!,_layoutLibraryObjectsSplit=null!;
    HSplitContainer _layoutLibrarySplit=null!,_layoutInspectorSplit=null!;
    ScrollContainer _layoutInspectorScroll=null!;
    SubViewportContainer _layoutCameraPreview=null!;
    Button _finishPathButton=null!;
    Control _layoutHeader=null!,_layoutTimelinePane=null!;
    int _layoutBreakpoint=-1;

    void BuildResponsiveUi()
    {
        _layoutRoot=new VBoxContainer();_layoutRoot.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _layoutRoot.AddThemeConstantOverride("separation",1);AddChild(_layoutRoot);

        var fileMenu=new MenuButton{Text="File",FocusMode=FocusModeEnum.None};
        var filePopup=fileMenu.GetPopup();
        foreach(var item in new[]{"New scene","Open project…","Save project","Import motion…","Import 3D model…"})filePopup.AddItem(item);
        filePopup.IdPressed+=id=>{switch(id){case 0:NewScene();break;case 1:OpenProject();break;case 2:SaveProject();break;case 3:ImportMotion();break;case 4:ImportModel();break;}};
        var editMenu=new MenuButton{Text="Edit",FocusMode=FocusModeEnum.None};
        foreach(var item in new[]{"Undo","Redo","Duplicate selected","Delete selected"})editMenu.GetPopup().AddItem(item);
        editMenu.GetPopup().IdPressed+=id=>{switch(id){case 0:Undo();break;case 1:Redo();break;case 2:DuplicateSelected();break;case 3:DeleteSelected();break;}};
        var export=Button("Export frame",ExportFrame);export.AddThemeStyleboxOverride("normal",Style(new Color("e5e2ec"),4));export.AddThemeColorOverride("font_color",new Color("202026"));
        var header=Row(Text("PREVIS",14),fileMenu,editMenu,Space(),Button("Save",SaveProject),export);
        header.AddThemeConstantOverride("separation",6);_layoutHeader=CompactPanel(header,4,2);_layoutRoot.AddChild(_layoutHeader);

        _layoutWorkSplit=new VSplitContainer{SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=SizeFlags.ExpandFill,DraggerVisibility=SplitContainer.DraggerVisibilityEnum.Hidden};
        _layoutWorkSplit.AddThemeConstantOverride("separation",5);_layoutRoot.AddChild(_layoutWorkSplit);
        var upper=new HBoxContainer{SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=SizeFlags.ExpandFill};upper.AddThemeConstantOverride("separation",1);_layoutWorkSplit.AddChild(upper);

        var rail=new VBoxContainer{CustomMinimumSize=new Vector2(54,0)};rail.AddThemeConstantOverride("separation",3);
        foreach(var category in new[]{"Scene","People","Animals","Props","Environment","Cameras","Camera moves","Motions"})
        {
            string captured=category;string label=category switch{"Environment"=>"World","Camera moves"=>"Moves",_=>category};
            var button=Button(label,()=>ShowCategory(captured),true);button.TooltipText=category;button.ClipText=true;button.CustomMinimumSize=new Vector2(52,34);
            button.AddThemeFontSizeOverride("font_size",11);button.AddThemeStyleboxOverride("normal",CompactStyle(new Color("1a1a20"),4,2));
            button.AddThemeStyleboxOverride("hover",CompactStyle(new Color("303039"),4,2));button.AddThemeStyleboxOverride("pressed",CompactStyle(new Color("383045"),4,2));
            rail.AddChild(button);_tabs[category]=button;
        }
        rail.AddChild(new Control{SizeFlagsVertical=SizeFlags.ExpandFill});upper.AddChild(CompactPanel(rail,2,5));

        _layoutLibrarySplit=new HSplitContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill,DraggerVisibility=SplitContainer.DraggerVisibilityEnum.Hidden};
        _layoutLibrarySplit.AddThemeConstantOverride("separation",5);upper.AddChild(_layoutLibrarySplit);
        _layoutLibraryObjectsSplit=new VSplitContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill,DraggerVisibility=SplitContainer.DraggerVisibilityEnum.Hidden};
        _layoutLibraryObjectsSplit.AddThemeConstantOverride("separation",5);
        var librarySection=new VBoxContainer{CustomMinimumSize=new Vector2(180,120),SizeFlagsVertical=SizeFlags.ExpandFill};librarySection.AddThemeConstantOverride("separation",5);
        librarySection.AddChild(Text("LIBRARY",11,true));
        var libraryScroll=new ScrollContainer{SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};
        _library=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};_library.AddThemeConstantOverride("separation",5);libraryScroll.AddChild(_library);librarySection.AddChild(libraryScroll);_layoutLibraryObjectsSplit.AddChild(librarySection);
        var sceneSection=new VBoxContainer{CustomMinimumSize=new Vector2(180,148),SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill};sceneSection.AddThemeConstantOverride("separation",5);sceneSection.AddChild(Text("SCENE",11,true));
        _objectList=new ItemList{CustomMinimumSize=new Vector2(0,125),SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill};
        _objectList.ItemSelected+=HandleObjectListSelection;sceneSection.AddChild(_objectList);_layoutLibraryObjectsSplit.AddChild(sceneSection);
        _layoutLibraryDock=CompactPanel(_layoutLibraryObjectsSplit,6,6);_layoutLibraryDock.CustomMinimumSize=new Vector2(216,0);_layoutLibraryDock.SizeFlagsHorizontal=SizeFlags.Fill;_layoutLibraryDock.SizeFlagsVertical=SizeFlags.ExpandFill;_layoutLibrarySplit.AddChild(_layoutLibraryDock);

        _layoutInspectorSplit=new HSplitContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill,DraggerVisibility=SplitContainer.DraggerVisibilityEnum.Hidden};
        _layoutInspectorSplit.AddThemeConstantOverride("separation",5);_layoutLibrarySplit.AddChild(_layoutInspectorSplit);
        var center=new VBoxContainer{CustomMinimumSize=new Vector2(340,180),SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill};center.AddThemeConstantOverride("separation",1);_layoutInspectorSplit.AddChild(center);
        _drawPathButton=Button("Draw path",StartDrawingPath,true);_finishPathButton=Button("Finish",FinishDrawingPath);_finishPathButton.Visible=false;
        var toolbar=Row(Button("Select",()=>_moveTool=false),Button("Move",()=>_moveTool=true),_drawPathButton,_finishPathButton,Space(),Button("Frame",FrameSelected),Button("Perspective",()=>{if(_pilot)TogglePilot();else ResetEditorView();}));toolbar.AddThemeConstantOverride("separation",4);center.AddChild(CompactPanel(toolbar,4,2));
        _sceneContainer=CreateSceneViewportContainer();_sceneContainer.Stretch=true;_sceneContainer.SizeFlagsVertical=SizeFlags.ExpandFill;_sceneContainer.SizeFlagsHorizontal=SizeFlags.ExpandFill;_sceneContainer.MouseFilter=MouseFilterEnum.Stop;
        _sceneViewport=new SubViewport{Size=new Vector2I(800,500),RenderTargetUpdateMode=SubViewport.UpdateMode.Always,HandleInputLocally=true,Msaa3D=Viewport.Msaa.Msaa4X};_sceneContainer.AddChild(_sceneViewport);_sceneContainer.GuiInput+=ViewportInput;center.AddChild(_sceneContainer);
        _framing=new CameraFrameOverlay{App=this,MouseFilter=MouseFilterEnum.Ignore};_framing.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);_sceneContainer.AddChild(_framing);

        var right=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill};right.AddThemeConstantOverride("separation",6);
        var previewToggle=Button("Preview",()=>{_layoutCameraPreview.Visible=!_layoutCameraPreview.Visible;_previewViewport.RenderTargetUpdateMode=_layoutCameraPreview.Visible?SubViewport.UpdateMode.Always:SubViewport.UpdateMode.Disabled;});previewToggle.TooltipText="Show or hide the active camera preview";
        _cameraChoice=new OptionButton{SizeFlagsHorizontal=SizeFlags.ExpandFill,FitToLongestItem=false,ClipText=true};_cameraChoice.ItemSelected+=i=>{if(!_refreshing&&i>=0&&i<_cameras.Count)SelectCamera(_cameras[(int)i]);};right.AddChild(Row(_cameraChoice,previewToggle));
        _layoutCameraPreview=new SubViewportContainer{Stretch=true,CustomMinimumSize=new Vector2(0,144),MouseFilter=MouseFilterEnum.Ignore};
        _layoutCameraPreview.Resized+=FitCompactPreview;
        _previewViewport=new SubViewport{Size=new Vector2I(480,270),RenderTargetUpdateMode=SubViewport.UpdateMode.Always,Msaa3D=Viewport.Msaa.Msaa4X};_layoutCameraPreview.AddChild(_previewViewport);right.AddChild(_layoutCameraPreview);
        _cameraInfo=Text("Active camera",11,true);_cameraInfo.ClipText=true;right.AddChild(_cameraInfo);
        _cameraQuickControls=new VBoxContainer();_cameraQuickControls.AddThemeConstantOverride("separation",4);right.AddChild(_cameraQuickControls);
        _pilotButton=Button("Camera view",TogglePilot,true);_cameraQuickControls.AddChild(_pilotButton);
        _lens=Spin(14,200,1,35);_lens.CustomMinimumSize=new Vector2(64,0);_lens.ValueChanged+=ChangeCameraLens;
        _cameraQuickControls.AddChild(Row(Text("Lens",11,true),_lens,Text("mm",11,true),Button("◆ Key",CaptureCameraKey)));
        right.AddChild(new HSeparator());_layoutInspectorScroll=new ScrollContainer{SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,FollowFocus=true};
        _inspector=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};_inspector.AddThemeConstantOverride("separation",6);_layoutInspectorScroll.AddChild(_inspector);right.AddChild(_layoutInspectorScroll);BuildInspector();
        foreach(var label in Descendants(_inspector).OfType<Label>())if(label.Text.Length>40)label.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        foreach(var option in Descendants(_inspector).OfType<OptionButton>()){option.FitToLongestItem=false;option.ClipText=true;}
        _layoutInspectorDock=CompactPanel(right,7,6);_layoutInspectorDock.CustomMinimumSize=new Vector2(286,0);_layoutInspectorDock.SizeFlagsHorizontal=SizeFlags.Fill;_layoutInspectorDock.SizeFlagsVertical=SizeFlags.ExpandFill;_layoutInspectorSplit.AddChild(_layoutInspectorDock);

        var timelinePane=new VBoxContainer{CustomMinimumSize=new Vector2(0,205),SizeFlagsHorizontal=SizeFlags.ExpandFill};timelinePane.AddThemeConstantOverride("separation",1);_layoutWorkSplit.AddChild(timelinePane);_layoutTimelinePane=timelinePane;
        _play=Button("▶",()=>{_playing=!_playing;_play.Text=_playing?"Ⅱ":"▶";});_play.TooltipText="Play / pause (Space)";_play.CustomMinimumSize=new Vector2(28,0);
        _timeLabel=Text("00:00:00",13);_scrub=new HSlider{MinValue=0,MaxValue=_duration,Step=1.0/_fps,SizeFlagsHorizontal=SizeFlags.ExpandFill};_scrub.ValueChanged+=v=>{if(!_refreshing)SetTime(v);};
        _durationField=Spin(1,120,1,_duration);_durationField.CustomMinimumSize=new Vector2(64,0);_durationField.SizeFlagsHorizontal=SizeFlags.ShrinkEnd;_durationField.TooltipText="Scene duration in seconds";_durationField.ValueChanged+=v=>{if(_refreshing)return;_duration=v;_scrub.MaxValue=v;SetTime(Math.Min(_time,v));};
        _fpsChoice=new OptionButton();foreach(int f in new[]{24,25,30,60})_fpsChoice.AddItem(f.ToString(),f);_fpsChoice.TooltipText="Scene frame rate";_fpsChoice.ItemSelected+=i=>{if(_refreshing)return;_fps=_fpsChoice.GetItemId((int)i);_scrub.Step=1.0/_fps;SetTime(_time);};
        var transport=Row(_play,Button("|◀",()=>SetTime(0)),_timeLabel,_scrub,Text("Length",11,true),_durationField,Text("fps",11,true),_fpsChoice);transport.AddThemeConstantOverride("separation",5);timelinePane.AddChild(CompactPanel(transport,6,2));
        _timeline=new TimelineView{App=this,CustomMinimumSize=new Vector2(0,150),SizeFlagsHorizontal=SizeFlags.ExpandFill};
        var timelineScroll=new ScrollContainer{SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};timelineScroll.AddChild(_timeline);var tracksPanel=CompactPanel(timelineScroll,2,2);tracksPanel.SizeFlagsVertical=SizeFlags.ExpandFill;timelinePane.AddChild(tracksPanel);
        _status=Text("Ready",11,true);_status.ClipText=true;_status.TooltipText="RMB orbit · MMB pan · Wheel zoom · F frame selection · Space play";_layoutRoot.AddChild(CompactPanel(_status,6,1));

        _fileDialog=new FileDialog{Access=FileDialog.AccessEnum.Filesystem,UseNativeDialog=false};_fileDialog.FileSelected+=path=>{try{_fileAction?.Invoke(path);}catch(Exception ex){Error(ex.Message);}};AddChild(_fileDialog);
        _message=new AcceptDialog{Title="Previs"};AddChild(_message);
        Resized+=ApplyResponsiveLayout;ApplyResponsiveLayout();Callable.From(ApplyResponsiveLayout).CallDeferred();
    }

    StyleBoxFlat CompactStyle(Color color,int horizontal,int vertical)
    {
        var style=Style(color,4);style.ContentMarginLeft=horizontal;style.ContentMarginRight=horizontal;style.ContentMarginTop=vertical;style.ContentMarginBottom=vertical;return style;
    }
    PanelContainer CompactPanel(Control child,int horizontal=6,int vertical=4)
    {
        var panel=new PanelContainer();var style=CompactStyle(_panel,horizontal,vertical);style.SetBorderWidthAll(1);style.BorderColor=_line;panel.AddThemeStyleboxOverride("panel",style);panel.AddChild(child);return panel;
    }
    void ApplyResponsiveLayout()
    {
        if(_layoutRoot==null)return;float width=GetViewportRect().Size.X,height=GetViewportRect().Size.Y;int breakpoint=width<1450?0:width<2150?1:2;
        if(breakpoint!=_layoutBreakpoint)
        {
            _layoutBreakpoint=breakpoint;_layoutLibraryDock.CustomMinimumSize=new Vector2(breakpoint==0?198:breakpoint==1?216:232,0);_layoutInspectorDock.CustomMinimumSize=new Vector2(breakpoint==0?270:286,0);
        }
        FitCompactPreview();_layoutTimelinePane.CustomMinimumSize=new Vector2(0,height<850?172:205);UpdateCompactChrome();
    }
    void FitCompactPreview(){if(_layoutCameraPreview==null||_layoutCameraPreview.Size.X<1)return;float height=Mathf.Round(_layoutCameraPreview.Size.X*9f/16f);if(Math.Abs(_layoutCameraPreview.CustomMinimumSize.Y-height)>.5f)_layoutCameraPreview.CustomMinimumSize=new Vector2(0,height);}
    void UpdateCompactChrome(){if(_finishPathButton!=null)_finishPathButton.Visible=_drawingPath;}

    /// <summary>Geometry-level checks for automated desktop layout validation.</summary>
    public async Task<int> RunResponsiveLayoutChecks()
    {
        var window=GetWindow();var originalSize=window.Size;var originalMode=window.Mode;int checks=0;
        try
        {
            window.Mode=Window.ModeEnum.Windowed;
            foreach(var size in new[]{new Vector2I(1280,800),new Vector2I(1920,1080),new Vector2I(2560,1440)})
            {
                window.Size=new Vector2I((int)Math.Round(size.X*window.ContentScaleFactor),(int)Math.Round(size.Y*window.ContentScaleFactor));ApplyResponsiveLayout();for(int i=0;i<4;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);ApplyResponsiveLayout();
                void Check(bool condition,string label){if(!condition)throw new InvalidOperationException($"Layout {size}: {label}");checks++;}
                var actual=GetViewportRect().Size;var scene=_sceneContainer.GetGlobalRect();
                Check(Math.Abs(actual.X-size.X)<2&&Math.Abs(actual.Y-size.Y)<2,"viewport tracks window pixels rather than a stretched fixed canvas");
                Check(_layoutHeader.Size.Y<=46,"header stays compact instead of scaling with resolution");
                Check(_play.Size.Y<=36,"play button stays desktop sized");
                Check(scene.Size.X>=actual.X*(actual.X<1500?.43f:.60f),"3D view receives available horizontal space");
                Check(scene.Size.Y>=actual.Y*.46f,"3D view retains useful height");
                Check(_layoutInspectorDock.GetGlobalRect().End.X<=actual.X+2,"inspector remains in bounds");
                Check(_layoutLibraryDock.Size.X<actual.X*.25f,"library is a compact side dock");
                Check(scene.Position.X>=0&&scene.End.X<=actual.X+2,"viewport remains inside window");
                GD.Print($"LAYOUT_METRICS window={size.X}x{size.Y} header={_layoutHeader.Size.Y:0} button={_play.Size.Y:0} scene={scene.Size.X:0}x{scene.Size.Y:0} library={_layoutLibraryDock.Size.X:0} inspector={_layoutInspectorDock.Size.X:0}");
            }
            GD.Print("RESPONSIVE_LAYOUT_OK checks="+checks);return checks;
        }
        finally{window.Size=originalSize;window.Mode=originalMode;ApplyResponsiveLayout();}
    }
}
