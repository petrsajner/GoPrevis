using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    bool _aiBusy,_aiReplaceScene;
    bool _aiShuttingDown;
    AcceptDialog? _aiDialog,_aiConnectionDialog;
    OptionButton _aiModelChoice=null!,_aiModeChoice=null!;
    TextEdit _aiPrompt=null!;
    Label _aiProgress=null!,_aiConnectionStatus=null!;
    RichTextLabel _aiResult=null!;
    Button _aiBuild=null!,_aiCancel=null!,_aiView=null!,_aiResume=null!;
    LineEdit _aiCustomModel=null!,_aiKeyInput=null!;
    ProgressBar _aiActivity=null!;
    MenuButton _aiExamples=null!;
    CancellationTokenSource? _aiCancellation;
    AiDirectorClient? _aiService;
    AiDirectorResult? _aiLastResult;
    AiConnectionSettings? _aiSettings;
    AiGenerationSession? _aiSession;
    readonly System.Collections.Generic.List<AiPreviewImage> _aiReferences=new();
    DateTime _aiStarted;
    string AiSettingsPath=>ProjectSettings.GlobalizePath(DiagnosticRun?"res://output/test-settings/ai-connections.json":"user://settings/ai-connections.json");
    AiConnectionSettings AiSettings=>_aiSettings??=AiConnectionSettings.Load(AiSettingsPath);
    string AiSessionPath=>ProjectSettings.GlobalizePath(DiagnosticRun?"res://output/test-settings/ai-session.json":"user://ai-sessions/current.json");
    static readonly string[] AiExamplePrompts={
        "Create a 10 second shot. A blue car starts parked, accelerates, turns left along a curved road and comes to a smooth stop. Add a few trees and two roadside buildings. A 35 mm camera follows the car from behind and to its right, looking at the car throughout. Keep it in frame. Use editable paths and an eased start and stop.",
        "Create an 8 second cafe shot with a table, two chairs and two people. One person is seated on a chair; the other walks toward the table with a walking motion. A wide camera slowly dollies closer while looking at the walking person. Add a second static overview camera.",
        "Create an 8 second shot in a park. A dog runs along a curved path toward a standing person and stops near them. Use actual compatible dog animation clips and transition from run to idle. The camera tracks the dog. Add a bench and a few trees."};
    async void OpenAiDirector()
    {
        if(_exportBusy)return;
        if(_aiDialog==null)BuildAiDirector();RefreshAiConnection();
        _aiDialog!.Size=new Vector2I(650,700);
        // Wrapped labels need their real width before AcceptDialog measures its minimum.
        for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        if(_aiShuttingDown)return;_aiDialog.Size=new Vector2I(650,700);_aiDialog.PopupCentered(new Vector2I(650,700));FitAiDialogContent(_aiDialog);
    }
    void BuildAiDirector()
    {
        _aiDialog=new AcceptDialog{Title="AI Director",Exclusive=true,Transient=true,Unresizable=false,WrapControls=false,MinSize=new Vector2I(620,600)};AddChild(_aiDialog);_aiDialog.GetOkButton().Hide();_aiDialog.CloseRequested+=()=>_aiCancellation?.Cancel();_aiDialog.Canceled+=()=>_aiCancellation?.Cancel();
        var panel=new VBoxContainer{CustomMinimumSize=new Vector2(610,575)};panel.AddThemeConstantOverride("separation",9);_aiDialog.AddChild(panel);
        var intro=Text("Optional AI assistance for building and editing shots. All manual tools and exports work without a key. Describe a shot below; AI uses your library and changes stay editable and undoable.",12,true);intro.CustomMinimumSize=new Vector2(600,0);intro.AutowrapMode=TextServer.AutowrapMode.WordSmart;panel.AddChild(intro);
        _aiModelChoice=new OptionButton{SizeFlagsHorizontal=SizeFlags.ExpandFill};foreach(string model in new[]{"GPT-6 Luna · OpenRouter","GPT-6 Luna · OpenAI","Gemini 3.8 Flash · Google","Custom model · OpenRouter","GPT-6.1 Sol · OpenRouter (quality)"})_aiModelChoice.AddItem(model);
        _aiModelChoice.Select(Math.Clamp(AiSettings.Selection,0,4));_aiModelChoice.ItemSelected+=index=>{AiSettings.Selection=(int)index;AiSettings.Save(AiSettingsPath);RefreshAiConnection();};panel.AddChild(Row(_aiModelChoice,Button("Connection…",OpenAiConnection)));
        _aiCustomModel=new LineEdit{PlaceholderText="OpenRouter model ID",Text=AiSettings.CustomModel};_aiCustomModel.TextChanged+=text=>{AiSettings.CustomModel=text;};panel.AddChild(_aiCustomModel);
        _aiConnectionStatus=Text("",11,true);panel.AddChild(_aiConnectionStatus);
        _aiModeChoice=new OptionButton();_aiModeChoice.AddItem("Edit current scene");_aiModeChoice.AddItem("New scene · replace current shot (Undo available)");panel.AddChild(_aiModeChoice);
        var examples=_aiExamples=new MenuButton{Text="Examples"};foreach(string title in new[]{"Car starts, turns and stops","Cafe with seated and walking people","Dog runs to a person"})examples.GetPopup().AddItem(title);examples.GetPopup().IdPressed+=index=>{_aiPrompt.Text=AiExamplePrompts[(int)index];_aiModeChoice.Select(1);};_aiReferencesButton=Button("References · 0",OpenAiReferences);panel.AddChild(Row(Text("Shot description",12,true),Space(),_aiReferencesButton,examples));
        _aiPrompt=new TextEdit{PlaceholderText="Example: A woman walks to a table, stops and turns her head. The camera follows her, then slowly moves closer.",CustomMinimumSize=new Vector2(0,140),SizeFlagsVertical=SizeFlags.ExpandFill,WrapMode=TextEdit.LineWrappingMode.Boundary};panel.AddChild(_aiPrompt);
        _aiPrompt.GuiInput+=input=>{if(input is InputEventKey{Pressed:true,Echo:false,CtrlPressed:true,Keycode:Key.Enter}){_aiPrompt.AcceptEvent();_=BuildAiShot();}};
        _aiProgress=Text("Ready · Ctrl+Enter to build",11,true);_aiProgress.ClipText=true;panel.AddChild(_aiProgress);
        _aiActivity=new ProgressBar{Indeterminate=true,ShowPercentage=false,CustomMinimumSize=new Vector2(0,5),Visible=false};panel.AddChild(_aiActivity);
        _aiResult=new RichTextLabel{BbcodeEnabled=false,ScrollActive=true,CustomMinimumSize=new Vector2(0,125),SizeFlagsVertical=SizeFlags.ExpandFill};panel.AddChild(_aiResult);
        _aiBuild=Button("Build shot",()=>{_=BuildAiShot();});_aiCancel=Button("Cancel",()=>_aiCancellation?.Cancel());_aiCancel.Disabled=true;
        _aiResume=Button("Resume",()=>{_=BuildAiShot(true);});_aiResume.TooltipText="Continue the saved request. Its starting scene is restored when necessary; your current scene remains in Undo.";_aiSession??=AiGenerationSession.Load(AiSessionPath);_aiResume.Disabled=_aiSession==null||_aiSession.Status=="Completed";
        _aiView=Button("View shot",()=>{_aiDialog.Hide();SelectCamera(_cameras.First(c=>c.Id==ActiveCameraId));if(!_pilot)TogglePilot();});_aiView.Disabled=true;panel.AddChild(Row(_aiBuild,_aiCancel,_aiResume,Space(),_aiView,Button("Close",()=>{if(_aiBusy)_aiCancellation?.Cancel();_aiDialog.Hide();})));
        RefreshAiConnection();
    }
    static void FitAiDialogContent(AcceptDialog dialog)
    {
        if(dialog.GetChildren().OfType<VBoxContainer>().FirstOrDefault() is not {} content)return;
        content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);content.OffsetLeft=9;content.OffsetTop=9;content.OffsetRight=-9;content.OffsetBottom=-16;
        content.UpdateMinimumSize();content.Size=dialog.GetVisibleRect().Size-new Vector2(18,25);content.QueueSort();
    }
    void RefreshAiConnection()
    {
        if(_aiDialog==null)return;
        _aiCustomModel.Visible=AiSettings.Selection==3;
        bool available=AiCredentials.Has(AiSettings.Provider,AiSettings.UseVividia);
        _aiConnectionStatus.Text=available?"API key available · "+AiCredentials.Source(AiSettings.Provider,AiSettings.UseVividia):"No API key · open Connection to add your provider key";
        _aiBuild.Disabled=_aiBusy||!available;
    }
    async void OpenAiConnection()
    {
        if(_aiBusy)return;
        if(_aiConnectionDialog!=null){_aiConnectionDialog.GetParent().RemoveChild(_aiConnectionDialog);_aiConnectionDialog.QueueFree();}
        _aiConnectionDialog=new AcceptDialog{Title="AI connection · "+AiSettings.Provider,Exclusive=true,Transient=true,WrapControls=false,MinSize=new Vector2I(520,270)};_aiDialog!.AddChild(_aiConnectionDialog);_aiConnectionDialog.GetOkButton().Hide();
        var panel=new VBoxContainer{CustomMinimumSize=new Vector2(500,225)};panel.AddThemeConstantOverride("separation",10);_aiConnectionDialog.AddChild(panel);
        var info=Text("Use your provider key. Keys stay on this computer and are excluded from saved projects and exports.",12,true);info.CustomMinimumSize=new Vector2(500,0);info.AutowrapMode=TextServer.AutowrapMode.WordSmart;panel.AddChild(info);
        _aiKeyInput=new LineEdit{Secret=true,PlaceholderText="API key · leave blank to keep the current connection"};panel.AddChild(_aiKeyInput);
        var connectionError=Text("",11,true);connectionError.AutowrapMode=TextServer.AutowrapMode.WordSmart;panel.AddChild(connectionError);
        panel.AddChild(Button("Save connection",()=>{try{if(!string.IsNullOrWhiteSpace(_aiKeyInput.Text))AiCredentials.Save(AiSettings.Provider,_aiKeyInput.Text);_aiKeyInput.Text="";AiSettings.Save(AiSettingsPath);RefreshAiConnection();_aiConnectionDialog.Hide();}catch(Exception error){connectionError.Text=error.Message;}}));
        // Vividia's key, read where Vividia keeps it — offered when Vividia has one for this provider.
        if(AiCredentials.VividiaHas(AiSettings.Provider)||AiSettings.UseVividia)
            panel.AddChild(Button(AiSettings.UseVividia?"Stop using the Vividia key":"Use existing Vividia key",()=>{try{AiSettings.UseVividia=!AiSettings.UseVividia;AiSettings.Save(AiSettingsPath);RefreshAiConnection();_aiConnectionDialog.Hide();}catch(Exception error){connectionError.Text=error.Message;}}));
        var price=Text(AiSettings.Provider=="gemini"?"Gemini 3.8 Flash · $0.75 input / $3.75 output per 1M tokens through Dec 31, 2026. Higher rates apply from Jan 1, 2027.":AiSettings.Selection==4?"GPT-6.1 Sol · $2 input / $10 output per 1M tokens. High reasoning; use when planning quality matters more than price.":AiSettings.Selection==3?"Custom model prices depend on the selected model. Actual token usage is reported after planning.":"GPT-6 Luna · $0.10 input / $0.50 output per 1M tokens. OpenRouter account fees may apply.",11,true);price.CustomMinimumSize=new Vector2(500,0);price.AutowrapMode=TextServer.AutowrapMode.WordSmart;panel.AddChild(price);
        panel.AddChild(Button("Close",()=>{_aiKeyInput.Text="";_aiConnectionDialog.Hide();}));_aiConnectionDialog.CloseRequested+=()=>_aiKeyInput.Text="";_aiConnectionDialog.VisibilityChanged+=()=>{if(!_aiConnectionDialog.Visible)_aiKeyInput.Text="";};
        _aiConnectionDialog.Size=new Vector2I(550,320);for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        if(_aiShuttingDown)return;_aiConnectionDialog.Size=new Vector2I(550,320);_aiConnectionDialog.PopupCentered(new Vector2I(550,320));FitAiDialogContent(_aiConnectionDialog);
    }
    async Task<AiDirectorResult> RunAiDirector(string prompt,bool replace,AiDirectorConnection connection,CancellationToken cancellation,AiDirectorClient? service=null,AiGenerationSession? session=null)
    {
        if(_exportBusy)throw new InvalidOperationException("Wait until the shot export finishes.");
        StopHistoryPlayback();StopPoseEditing();ReleaseObjectGizmoDrag();EndArrangeDrag();_rightDown=false;_middleDown=false;
        _aiReplaceScene=replace;string fingerprint=StableAiSceneSignature();
        if(session!=null){if(session.SceneSignature!=""&&session.SceneSignature!=fingerprint)throw new InvalidOperationException("The saved starting scene could not be restored. The draft and provider work remain saved.");session.OriginalScene??=Snapshot();if(session.SceneSignature==""){session.OriginalProjectPath=_projectPath;session.OriginalTime=_time;}session.SceneSignature=fingerprint;session.ReplaceScene=replace;}
        var result=await (service??(_aiService??=new())).GenerateAsync(connection,prompt,AiSceneContext(replace),AiReadTool,plan=>{_=CompileAiPlan(plan,replace);},message=>{if(_aiProgress!=null)_aiProgress.Text=message.StartsWith("Correcting plan:",StringComparison.Ordinal)?"Correcting scene plan…":message;if(OS.GetCmdlineUserArgs().Any(argument=>argument.Contains("live-test",StringComparison.Ordinal)))GD.Print("AI_PROGRESS "+message);},cancellation,(proposal,token)=>PreviewAiPlan(proposal,replace,token),session,state=>{state.Save(AiSessionPath);if(_aiResult!=null)_aiResult.Text=AiUsageText(state)+"\nDraft and completed provider work are saved. Cancel pauses; Resume continues this request.";},_aiReferences);
        cancellation.ThrowIfCancellationRequested();
        if(fingerprint!=StableAiSceneSignature())throw new InvalidOperationException("The scene changed while AI was planning. Run the request again to use the updated scene.");
        if(_aiShuttingDown)throw new OperationCanceledException();await ApplyAiPlan(result.Plan,replace,cancellation);if(session!=null){session.Status="Completed";session.Save(AiSessionPath);}return result;
    }
    static string AiUsageText(AiGenerationSession state)=>state.Requests+" provider request(s) · "+state.InputTokens+" input / "+state.OutputTokens+" output tokens"+(state.EstimatedUsd is {} cost?" · usage $"+cost.ToString("0.0000",System.Globalization.CultureInfo.InvariantCulture):"")+(state.Draft is {} draft?"\nSaved draft: "+draft.Objects.Count+" objects · "+draft.Cameras.Count+" cameras":"");
    async Task BuildAiShot(bool resume=false)
    {
        if(_aiBusy||_aiPrompt==null)return;
        if(resume)_aiSession=AiGenerationSession.Load(AiSessionPath);
        if(resume&&(_aiSession==null||_aiSession.Status=="Completed")){_aiResult.Text="There is no paused request to resume.";return;}
        if(resume&&_aiSession!.SceneSignature!=""&&StableAiSceneSignature()!=_aiSession.SceneSignature)
        {
            var starting=_aiSession.OriginalScene??JsonSerializer.Deserialize<ProjectData>(_aiSession.SceneSignature)??throw new InvalidOperationException("The saved starting scene is unavailable.");
            PushUndo();Restore(starting,"");_projectPath=_aiSession.OriginalProjectPath;SetTime(_aiSession.OriginalTime);
        }
        string prompt=resume?_aiSession!.Prompt:_aiPrompt.Text.Trim();if(prompt==""){_aiResult.Text="Describe the shot first.";return;}
        string provider=resume?_aiSession!.Provider:AiSettings.Provider,model=resume?_aiSession!.Model:AiSettings.Model;
        string? key=AiCredentials.Read(provider,AiSettings.UseVividia);if(key==null){OpenAiConnection();return;}
        if(!resume)
        {
            if(File.Exists(AiSessionPath)){string archive=Path.Combine(Path.GetDirectoryName(AiSessionPath)!,"previous-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".json");File.Copy(AiSessionPath,archive);}
            _aiSession=new(){Provider=provider,Model=model,Prompt=prompt,ReplaceScene=_aiModeChoice.Selected==1};
        }
        _aiLastResult=null;_aiBusy=true;_aiStarted=DateTime.UtcNow;_aiExamples.Disabled=true;_aiCustomModel.Editable=false;_aiCancellation=new();_aiBuild.Disabled=true;_aiModelChoice.Disabled=true;_aiModeChoice.Disabled=true;_aiPrompt.Editable=false;_aiCancel.Disabled=false;_aiActivity.Visible=true;_aiView.Disabled=true;_aiResult.Text="Planning your shot…";
        try
        {
            _aiResume.Disabled=true;AiSettings.Save(AiSettingsPath);var result=await RunAiDirector(prompt,_aiSession!.ReplaceScene,new(provider,model,key),_aiCancellation.Token,session:_aiSession);
            _aiLastResult=result;string cost=result.EstimatedUsd is {} amount?" · estimated $"+amount.ToString("0.0000",System.Globalization.CultureInfo.InvariantCulture):" · cost not supplied by provider";
            _aiResult.Text=result.Plan.Summary+"\n\n"+string.Join("\n",result.VerifiedOperations)+"\n\n"+result.Requests+" request(s) · "+result.InputTokens+" input / "+result.OutputTokens+" output tokens"+cost+"\nAll scene changes are editable. Ctrl+Z undoes this complete request.";
            _aiProgress.Text="Shot ready · "+(DateTime.UtcNow-_aiStarted).TotalSeconds.ToString("0")+" s";_aiView.Disabled=false;_aiModeChoice.Select(0);
        }
        catch(OperationCanceledException){if(_aiShuttingDown)return;if(_aiSession!=null){_aiSession.Status="Paused";_aiSession.Save(AiSessionPath);}_aiResult.Text="Paused. Draft and completed provider work are saved. Resume continues the same request.\n"+(_aiSession==null?"":AiUsageText(_aiSession));_aiProgress.Text="Paused · scene unchanged";}
        catch(Exception error){if(_aiShuttingDown)return;if(_aiSession!=null){_aiSession.Status="Paused";_aiSession.Save(AiSessionPath);}_aiResult.Text="Request paused. "+error.Message+"\nSaved work is available through Resume.\n"+(_aiSession==null?"":AiUsageText(_aiSession));_aiProgress.Text="Paused · draft saved · scene unchanged";}
        finally{_aiBusy=false;_aiCancellation?.Dispose();_aiCancellation=null;if(!_aiShuttingDown){_aiExamples.Disabled=false;_aiCustomModel.Editable=true;_aiModelChoice.Disabled=false;_aiModeChoice.Disabled=false;_aiPrompt.Editable=true;_aiCancel.Disabled=true;_aiActivity.Visible=false;_aiResume.Disabled=_aiSession==null||_aiSession.Status=="Completed";RefreshAiConnection();}}
    }
}
