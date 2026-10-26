using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GoPrevis;

public partial class GoPrevisApp
{
    LibraryPreferences? _libraryPreferences;
    LibraryPreferences LibraryPrefs=>_libraryPreferences??=LibraryPreferences.Load(LibraryPreferencesPath);
    string LibraryPreferencesPath=>ProjectSettings.GlobalizePath(DiagnosticRun?"res://output/test-settings/library-browser.json":"user://settings/library-browser.json");
    bool _libraryExpanded,_refreshingBrowser;
    Button? _libraryExpandButton,_libraryFavorite;
    LibraryAssetList? _libraryGrid;
    LineEdit? _librarySearch;
    OptionButton? _libraryGroup,_libraryFamily,_librarySource,_libraryScope,_librarySort;
    Label? _libraryCount,_libraryDetails;
    HBoxContainer? _libraryDetailRow;
    VBoxContainer? _libraryPreviewHost;
    MenuButton? _libraryCollection;
    ContentCard[] _browserCatalog=Array.Empty<ContentCard>(),_browserResults=Array.Empty<ContentCard>();
    ContentCard? _browserSelected;
    readonly Dictionary<string,Texture2D?> _libraryIcons=new(StringComparer.Ordinal);
    Timer? _librarySearchDelay;
    string BrowserKey(ContentCard item)=>(item.Pose!=null?"pose:":item.Motion!=null?"motion:":"asset:")+item.Id;
    static string BrowserCategory(ContentCard item)=>item.Motion?.Category??item.Pose?.Category??item.Model?.Category??"Other";
    static string BrowserFamily(ContentCard item)=>item.Motion?.ActorFamily??(item.Pose!=null?(item.Pose.Pose.RigModelId==null?"human":ContentCatalog.Find(item.Pose.Pose.RigModelId)?.ActorFamily??""):item.Model?.ActorFamily??"");
    static string BrowserSource(ContentCard item)=>item.Id.StartsWith("human-ual1-",StringComparison.Ordinal)?"Universal Animation Library 1":item.Id.StartsWith("human-ual2-",StringComparison.Ordinal)?"Universal Animation Library 2":item.Motion?.License=="CMU"?"CMU recorded motion":item.Pose!=null?(item.Pose.Category=="My poses"?"My poses":"GoPrevis poses"):item.Model?.IsBuiltin==true?"GoPrevis models":(item.Motion?.Attribution??item.Model?.Attribution??"").Contains("Quaternius",StringComparison.OrdinalIgnoreCase)?"Quaternius":item.Model?.Attribution??item.Motion?.Attribution??"GoPrevis";
    static string BrowserFamilyLabel(string family)=>family=="human"?"Human":ContentCatalog.ForFamily(family).FirstOrDefault()?.Name??family;
    void SaveLibraryPreferences(){try{LibraryPrefs.Save(LibraryPreferencesPath);}catch(System.IO.IOException e){Status("Could not save library preferences: "+e.Message);}}
    void RecordLibraryUse(string kind,string id){LibraryPrefs.Used(kind+":"+id);SaveLibraryPreferences();}
    void ToggleLibrarySize()
    {
        _libraryExpanded=!_libraryExpanded;LibraryPrefs.Expanded=_libraryExpanded;SaveLibraryPreferences();_layoutBreakpoint=-1;ApplyResponsiveLayout();UpdateLibraryGridSize();
        if(_libraryExpandButton!=null)_libraryExpandButton.Text=_libraryExpanded?"Collapse":"Expand";
    }
    void UpdateLibraryGridSize()
    {
        if(_libraryGrid==null||!GodotObject.IsInstanceValid(_libraryGrid))return;
        float panel=_libraryGrid.GetThemeStylebox("panel").GetMinimumSize().X;
        float scrollbar=Math.Max(14,_libraryGrid.GetVScrollBar().GetCombinedMinimumSize().X);
        float separation=Math.Max(4,_libraryGrid.GetThemeConstant("h_separation"));
        int column=_libraryExpanded?132:Math.Max(54,(int)Math.Floor((_libraryGrid.Size.X-panel-scrollbar-separation*3)/2));
        _libraryGrid.MaxColumns=_libraryExpanded?0:2;_libraryGrid.FixedColumnWidth=column;
        _libraryGrid.FixedIconSize=_libraryExpanded?new Vector2I(112,82):new Vector2I(Math.Min(80,Math.Max(40,column-8)),64);
    }
    void BuildLibraryBrowser(string section,IEnumerable<ContentCard> items)
    {
        _browserCatalog=items.ToArray();_browserSelected=null;var state=LibraryPrefs.View(section);_refreshingBrowser=true;
        _librarySearch=new LineEdit{PlaceholderText="Search "+section.ToLowerInvariant()+"…",ClearButtonEnabled=true,Text=state.Query};_library.AddChild(_librarySearch);
        _libraryScope=new OptionButton{FitToLongestItem=false,ClipText=true,SizeFlagsHorizontal=SizeFlags.ExpandFill};FillLibraryScopes(state.Scope);
        _libraryGroup=new OptionButton{FitToLongestItem=false,ClipText=true,SizeFlagsHorizontal=SizeFlags.ExpandFill};AddBrowserOptions(_libraryGroup,"All categories",_browserCatalog.Select(BrowserCategory),state.Category);_library.AddChild(Row(_libraryScope,_libraryGroup));
        _libraryFamily=new OptionButton{FitToLongestItem=false,ClipText=true,SizeFlagsHorizontal=SizeFlags.ExpandFill};_libraryFamily.AddItem("All actors");_libraryFamily.SetItemMetadata(0,"");foreach(string family in _browserCatalog.Select(BrowserFamily).Where(f=>f!="").Distinct().OrderBy(BrowserFamilyLabel)){_libraryFamily.AddItem(BrowserFamilyLabel(family));_libraryFamily.SetItemMetadata(_libraryFamily.ItemCount-1,family);if(family==state.Family)_libraryFamily.Select(_libraryFamily.ItemCount-1);}_libraryFamily.Visible=section is "Motions" or "Poses" or "Models";
        _librarySource=new OptionButton{FitToLongestItem=false,ClipText=true,SizeFlagsHorizontal=SizeFlags.ExpandFill};AddBrowserOptions(_librarySource,"All sources",_browserCatalog.Select(BrowserSource),state.Source);_library.AddChild(Row(_libraryFamily,_librarySource));
        _librarySort=new OptionButton{FitToLongestItem=false};foreach(string sort in new[]{"Name","Category","Recently used"})_librarySort.AddItem(sort);_librarySort.Select(Array.IndexOf(new[]{"Name","Category","Recently used"},state.Sort) is int selected&&selected>=0?selected:0);
        _libraryCount=Text("",11,true);_libraryCount.ClipText=true;_libraryCount.SizeFlagsHorizontal=SizeFlags.ExpandFill;_library.AddChild(Row(_libraryCount,_librarySort));
        _libraryGrid=new LibraryAssetList{App=this,CustomMinimumSize=new Vector2(0,180),SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill,IconMode=ItemList.IconModeEnum.Top,MaxColumns=0,SameColumnWidth=true,MaxTextLines=2,SelectMode=ItemList.SelectModeEnum.Single};_library.AddChild(_libraryGrid);_libraryGrid.Resized+=UpdateLibraryGridSize;UpdateLibraryGridSize();
        _libraryGrid.ItemSelected+=index=>SelectBrowserItem((int)index,true);_libraryGrid.ItemActivated+=index=>UseBrowserItem((int)index);
        _libraryGrid.ItemClicked+=(index,position,button)=>{if(button==(long)MouseButton.Right){_libraryGrid.Select((int)index);SelectBrowserItem((int)index,true);_libraryFavorite?.GrabFocus();}};
        _libraryGrid.GetVScrollBar().ValueChanged+=value=>{if(_refreshingBrowser)return;state.Scroll=(int)value;SaveLibraryPreferences();};
        _libraryDetailRow=new HBoxContainer{CustomMinimumSize=new Vector2(0,126)};_libraryPreviewHost=new VBoxContainer{CustomMinimumSize=new Vector2(92,116)};_libraryDetailRow.AddChild(_libraryPreviewHost);
        _libraryDetails=Text("Select an item to preview it. Drag into the scene or double-click to use.",11,true);_libraryDetails.AutowrapMode=TextServer.AutowrapMode.WordSmart;_libraryDetails.SizeFlagsHorizontal=SizeFlags.ExpandFill;_libraryDetailRow.AddChild(_libraryDetails);_library.AddChild(_libraryDetailRow);
        _libraryFavorite=Button("☆ Favorite",()=>{if(_browserSelected==null)return;LibraryPrefs.ToggleFavorite(BrowserKey(_browserSelected));SaveLibraryPreferences();PopulateLibraryBrowser(false);});
        _libraryCollection=new MenuButton{Text="Collection…",TooltipText="Add the selected item to a reusable collection"};RefreshLibraryCollections();_libraryCollection.GetPopup().IdPressed+=id=>AddBrowserItemToCollection((int)id);
        _library.AddChild(Row(_libraryFavorite,_libraryCollection));_library.AddChild(Button(section=="Motions"?"Apply motion / add actor":section=="Poses"?"Apply pose / add actor":"Add to scene",()=>{if(_browserSelected!=null)UseBrowserCard(_browserSelected);}));
        _librarySearchDelay=new Timer{OneShot=true,WaitTime=.16};_library.AddChild(_librarySearchDelay);_librarySearchDelay.Timeout+=()=>PopulateLibraryBrowser(true);
        _librarySearch.TextChanged+=_=>{if(!_refreshingBrowser)_librarySearchDelay.Start();};foreach(var filter in new[]{_libraryScope,_libraryGroup,_libraryFamily,_librarySource,_librarySort})filter.ItemSelected+=_=>{if(!_refreshingBrowser)PopulateLibraryBrowser(true);};
        _refreshingBrowser=false;PopulateLibraryBrowser(false);Callable.From(()=>{if(GodotObject.IsInstanceValid(_libraryGrid))_libraryGrid.GetVScrollBar().Value=state.Scroll;}).CallDeferred();
    }
    static void AddBrowserOptions(OptionButton option,string all,IEnumerable<string> values,string selected)
    {option.AddItem(all);option.SetItemMetadata(0,"");foreach(string value in values.Where(v=>v!="").Distinct().Order(StringComparer.OrdinalIgnoreCase)){option.AddItem(value);option.SetItemMetadata(option.ItemCount-1,value);if(value==selected)option.Select(option.ItemCount-1);}}
    void FillLibraryScopes(string selected)
    {
        _libraryScope!.Clear();foreach(string value in new[]{"All items","Favorites","Recently used"}.Concat(LibraryPrefs.Collections.Keys.Order().Select(n=>"Collection: "+n))){_libraryScope.AddItem(value);if(value==selected)_libraryScope.Select(_libraryScope.ItemCount-1);}
    }
    void PopulateLibraryBrowser(bool resetScroll)
    {
        if(_libraryGrid==null||!GodotObject.IsInstanceValid(_libraryGrid))return;_refreshingBrowser=true;var state=LibraryPrefs.View(_category);
        state.Query=_librarySearch!.Text;state.Category=_libraryGroup!.GetSelectedMetadata().AsString();state.Family=_libraryFamily!.GetSelectedMetadata().AsString();state.Source=_librarySource!.GetSelectedMetadata().AsString();state.Scope=_libraryScope!.GetItemText(_libraryScope.Selected);state.Sort=_librarySort!.GetItemText(_librarySort.Selected);
        var query=_browserCatalog.Where(item=>LibraryPrefs.InScope(BrowserKey(item),state.Scope)&&(state.Category==""||BrowserCategory(item)==state.Category)&&(state.Family==""||BrowserFamily(item)==state.Family)&&(state.Source==""||BrowserSource(item)==state.Source)&&LibraryPreferences.Matches(item.Name+" "+item.Detail+" "+BrowserCategory(item)+" "+BrowserSource(item)+" "+string.Join(' ',item.Motion?.Tags??item.Pose?.Tags??Array.Empty<string>()),state.Query));
        _browserResults=(state.Sort=="Recently used"||state.Scope=="Recently used"?query.OrderBy(item=>{int index=LibraryPrefs.Recent.IndexOf(BrowserKey(item));return index<0?int.MaxValue:index;}):state.Sort=="Category"?query.OrderBy(BrowserCategory).ThenBy(item=>item.Name):query.OrderBy(item=>item.Name,StringComparer.OrdinalIgnoreCase)).ToArray();
        _libraryGrid.Clear();foreach(var item in _browserResults){string key=BrowserKey(item);_libraryGrid.AddItem((LibraryPrefs.Favorites.Contains(key)?"★ ":"")+item.Name,LibraryIcon(item));int index=_libraryGrid.ItemCount-1;_libraryGrid.SetItemMetadata(index,key);_libraryGrid.SetItemTooltip(index,item.Name+"\n"+item.Detail);}
        _libraryCount!.Text=_browserResults.Length+" / "+_browserCatalog.Length+" items";int selected=Array.FindIndex(_browserResults,item=>BrowserKey(item)==state.Selected);if(selected>=0){_libraryGrid.Select(selected);SelectBrowserItem(selected,false);}else{_browserSelected=null;ClearBrowserPreview();_libraryDetails!.Text=_browserResults.Length==0?"No matching items. Change the filters or search.":"Select an item to preview. Drag or double-click to use.";_libraryFavorite!.Disabled=true;_libraryCollection!.Disabled=true;}
        if(resetScroll){state.Scroll=0;_libraryGrid.GetVScrollBar().Value=0;}_refreshingBrowser=false;SaveLibraryPreferences();
    }
    Texture2D? LibraryIcon(ContentCard item)
    {
        if(_libraryIcons.TryGetValue(item.Id,out var icon))return icon;string path="res://assets/content/thumbnails/"+item.Id+".png";
        icon=ResourceLoader.Exists(path)?GD.Load<Texture2D>(path):null;string local=ProjectSettings.GlobalizePath(path);if(icon==null&&System.IO.File.Exists(local))icon=ImageTexture.CreateFromImage(Image.LoadFromFile(local));if(icon==null&&item.Pose!=null){string saved=SavedPoseLibrary.ThumbnailPath(item.Id);if(System.IO.File.Exists(saved))icon=ImageTexture.CreateFromImage(Image.LoadFromFile(saved));}
        _libraryIcons[item.Id]=icon;return icon;
    }
    void ClearBrowserPreview(){if(_libraryPreviewHost==null)return;foreach(Node child in _libraryPreviewHost.GetChildren()){_libraryPreviewHost.RemoveChild(child);child.QueueFree();}}
    void SelectBrowserItem(int index,bool save)
    {
        if(index<0||index>=_browserResults.Length)return;var item=_browserSelected=_browserResults[index];string key=BrowserKey(item);LibraryPrefs.View(_category).Selected=key;
        _libraryDetails!.Text=item.Name+"\n"+BrowserCategory(item)+"\n"+BrowserSource(item)+(item.Motion!=null?"\n"+item.Motion.Duration.ToString("0.##")+" s · "+BrowserFamilyLabel(item.Motion.ActorFamily):"");
        _libraryFavorite!.Disabled=false;_libraryFavorite.Text=LibraryPrefs.Favorites.Contains(key)?"★ Favorited":"☆ Favorite";_libraryCollection!.Disabled=false;
        ClearBrowserPreview();var preview=CreateContentCard(item);preview.CustomMinimumSize=new Vector2(90,116);_libraryPreviewHost!.AddChild(preview);if(item.Motion!=null)preview.EmitSignal(Control.SignalName.MouseEntered);if(save)SaveLibraryPreferences();
    }
    void UseBrowserItem(int index){if(index>=0&&index<_browserResults.Length)UseBrowserCard(_browserResults[index]);}
    void UseBrowserCard(ContentCard item)
    {try{if(item.Pose!=null)DropPose(item.Id,_selected!=null&&CompatiblePose(_selected,item.Pose)?_selected:null,new Vector3(0,SceneGroundHeight,0));else if(item.Motion!=null)DropMotion(item.Id,_selected!=null&&CompatibleMotion(_selected,item.Motion)?_selected:null,new Vector3(0,SceneGroundHeight,0));else DropAsset(item.Id,new Vector3(0,SceneGroundHeight,0));}catch(Exception e){Error(e.Message);}}
    void RefreshLibraryCollections()
    {if(_libraryCollection==null)return;var popup=_libraryCollection.GetPopup();popup.Clear();popup.AddItem("New collection…",0);foreach(string name in LibraryPrefs.Collections.Keys.Order())popup.AddItem(name);}
    void AddBrowserItemToCollection(int index)
    {
        if(_browserSelected==null)return;string key=BrowserKey(_browserSelected);
        void Add(string name){LibraryPrefs.Collect(name,key);SaveLibraryPreferences();RefreshLibraryCollections();string scope=_libraryScope!.GetItemText(_libraryScope.Selected);FillLibraryScopes(scope);Status("Added to collection: "+name);}
        if(index>0){Add(_libraryCollection!.GetPopup().GetItemText(index));return;}
        var dialog=new ConfirmationDialog{Title="New library collection",Exclusive=true,Transient=true,MinSize=new Vector2I(360,150)};var content=new VBoxContainer();content.AddChild(Text("Collection name",12));var input=new LineEdit{PlaceholderText="Example: Street pedestrians",CustomMinimumSize=new Vector2(320,30)};content.AddChild(input);dialog.AddChild(content);AddChild(dialog);dialog.Confirmed+=()=>{try{Add(input.Text);}catch(Exception e){Status(e.Message);}dialog.QueueFree();};dialog.Canceled+=()=>dialog.QueueFree();dialog.PopupCentered(new Vector2I(380,160));input.GrabFocus();
    }
    partial class LibraryAssetList:ItemList
    {
        public GoPrevisApp App=null!;
        public override Variant _GetDragData(Vector2 position)
        {
            int index=GetItemAtPosition(position,true);if(index<0||index>=App._browserResults.Length)return default;var item=App._browserResults[index];App.SelectBrowserItem(index,true);Select(index);SetDragPreview(new Label{Text=item.Name});
            return new Godot.Collections.Dictionary{{"goprevis_type",item.Pose!=null?"pose":item.Motion!=null?"motion":"asset"},{"id",item.Id}};
        }
        public override void _GuiInput(InputEvent input)
        {if(input is InputEventKey{Pressed:true,Keycode:Key.Delete}){AcceptEvent();App.Status("Library assets stay in the catalog. Use Favorites or collections to organize them.");}}
    }
}
