using Godot;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    AcceptDialog? _aiReferencesDialog;
    Button _aiReferencesButton=null!;
    ItemList _aiReferenceList=null!;
    AiSketchCanvas _aiSketch=null!;
    OptionButton _aiSketchView=null!;
    LineEdit _aiSketchNotes=null!;
    string ReferenceCaption(string type,string notes)=>"USER SCENE REFERENCE · "+type+". "+(type=="Top view"?"This is a floor plan: X is horizontal and -Z points up; Y is height. ":type=="Side view"?"This is an elevation: X is horizontal and Y is vertical. ":"")+notes;
    async void OpenAiReferences()
    {
        if(_aiBusy)return;
        if(_aiReferencesDialog==null)
        {
            _aiReferencesDialog=new AcceptDialog{Title="Scene references and sketches",Exclusive=true,Transient=true,WrapControls=false,MinSize=new Vector2I(610,570)};_aiDialog!.AddChild(_aiReferencesDialog);_aiReferencesDialog.GetOkButton().Hide();
            var panel=new VBoxContainer{CustomMinimumSize=new Vector2(610,570)};panel.AddThemeConstantOverride("separation",7);_aiReferencesDialog.AddChild(panel);
            var help=Text("Import a floor plan/elevation or sketch one below. References are sent to the selected AI model with the shot description.",12,true);help.CustomMinimumSize=new Vector2(590,0);help.AutowrapMode=TextServer.AutowrapMode.WordSmart;panel.AddChild(help);
            _aiSketchView=new OptionButton();foreach(string type in new[]{"Top view","Side view","Reference"})_aiSketchView.AddItem(type);_aiSketchView.ItemSelected+=index=>{_aiSketch.ViewType=_aiSketchView.GetItemText((int)index);_aiSketch.QueueRedraw();};
            panel.AddChild(Row(Button("Import image…",ImportAiReference),_aiSketchView,Button("Undo stroke",()=>_aiSketch.UndoStroke()),Button("Clear sketch",()=>_aiSketch.Clear())));
            _aiSketch=new AiSketchCanvas{CustomMinimumSize=new Vector2(590,285),SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill};panel.AddChild(_aiSketch);
            _aiSketchNotes=new LineEdit{PlaceholderText="Reference notes: labels, road direction, scale, object names…"};panel.AddChild(_aiSketchNotes);
            _aiReferenceList=new ItemList{CustomMinimumSize=new Vector2(0,85),SizeFlagsHorizontal=SizeFlags.ExpandFill};panel.AddChild(_aiReferenceList);
            panel.AddChild(Row(Button("Add sketch",()=>{_=AddAiSketch();}),Button("Remove selected",()=>{var selected=_aiReferenceList.GetSelectedItems();foreach(int index in selected.OrderDescending())_aiReferences.RemoveAt(index);RefreshAiReferences();}),Space(),Button("Done",()=>_aiReferencesDialog.Hide())));
        }
        RefreshAiReferences();_aiReferencesDialog.Size=new Vector2I(670,640);for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);_aiReferencesDialog.Size=new Vector2I(670,640);_aiReferencesDialog.PopupCentered(new Vector2I(670,640));FitAiDialogContent(_aiReferencesDialog);
    }
    void RefreshAiReferences()
    {
        _aiReferencesButton.Text="References · "+_aiReferences.Count;
        if(_aiReferenceList==null)return;_aiReferenceList.Clear();for(int i=0;i<_aiReferences.Count;i++)_aiReferenceList.AddItem((i+1)+". "+_aiReferences[i].Caption);
    }
    void ImportAiReference()
    {
        if(_aiReferencesDialog==null)return;var picker=new FileDialog{Title="Import scene reference",Access=FileDialog.AccessEnum.Filesystem,FileMode=FileDialog.FileModeEnum.OpenFile,Filters=new[]{"*.png,*.jpg,*.jpeg,*.webp ; Reference images"},UseNativeDialog=false};_aiReferencesDialog.AddChild(picker);
        picker.CurrentDir=FileLocations.DirectoryFor("ai_reference",OS.GetSystemDir(OS.SystemDir.Documents));
        picker.FileSelected+=path=>
        {
            try{using var image=Image.LoadFromFile(path);if(image.IsEmpty())throw new InvalidOperationException("Could not read the selected image.");ResizeAiReference(image);_aiReferences.Add(new(ReferenceCaption(_aiSketchView.GetItemText(_aiSketchView.Selected),_aiSketchNotes.Text),"data:image/png;base64,"+Convert.ToBase64String(image.SavePngToBuffer())));FileLocations.Remember("ai_reference",System.IO.Path.GetDirectoryName(path));RefreshAiReferences();}
            catch(Exception error){Status(error.Message);}finally{picker.QueueFree();}
        };
        picker.Canceled+=()=>picker.QueueFree();picker.PopupCentered(new Vector2I(900,600));
    }
    async Task AddAiSketch()
    {
        var view=new SubViewport{Size=new Vector2I(900,500),OwnWorld3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Once};AddChild(view);
        try
        {
            var drawing=new AiSketchCanvas{Size=new Vector2(900,500),ViewType=_aiSketch.ViewType,MouseFilter=MouseFilterEnum.Ignore};foreach(var stroke in _aiSketch.Strokes)drawing.Strokes.Add(new(stroke));view.AddChild(drawing);
            using var image=await CaptureShotImage(view,CancellationToken.None);_aiReferences.Add(new(ReferenceCaption(_aiSketch.ViewType,_aiSketchNotes.Text),"data:image/png;base64,"+Convert.ToBase64String(image.SavePngToBuffer())));RefreshAiReferences();
        }
        finally{view.QueueFree();}
    }
    static void ResizeAiReference(Image image)
    {float factor=Math.Min(1,1600f/Math.Max(image.GetWidth(),image.GetHeight()));if(factor<1)image.Resize((int)Math.Round(image.GetWidth()*factor),(int)Math.Round(image.GetHeight()*factor),Image.Interpolation.Lanczos);}
}
