using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    void OpenLibraryQa()
    {
        _libraryPreferences=new();_libraryExpanded=true;_layoutBreakpoint=-1;ApplyResponsiveLayout();ShowCategory("Motions");
        int source=Enumerable.Range(0,_librarySource!.ItemCount).First(i=>_librarySource.GetItemText(i)=="Universal Animation Library 1");_librarySource.Select(source);_librarySearch!.Text="Walk";PopulateLibraryBrowser(true);GetWindow().Title="GoPrevis 0.19.0 — Library QA";
    }
    async Task RunLibraryBrowserChecksAndQuit()
    {try{await RunLibraryBrowserChecks();GetTree().Quit(0);}catch(Exception e){GD.PushError("LIBRARY_BROWSER_FAILED "+e);GetTree().Quit(1);}}
    public async Task RunLibraryBrowserChecks()
    {
        string folder=Path.GetFullPath(ProjectSettings.GlobalizePath("res://output/library-browser-checks/run-"+Guid.NewGuid().ToString("N")[..10]));Directory.CreateDirectory(folder);int checks=0;
        void Check(bool yes,string message){Require(yes,"Library browser: "+message);checks++;GD.Print("PASS Library browser: "+message);}
        async Task Settle(){for(int i=0;i<6;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        void Choose(OptionButton option,string text){int index=Enumerable.Range(0,option.ItemCount).First(i=>option.GetItemText(i)==text);option.Select(index);option.EmitSignal(OptionButton.SignalName.ItemSelected,index);}
        int Columns()=>Enumerable.Range(0,Math.Min(50,_libraryGrid!.ItemCount)).TakeWhile(i=>Math.Abs(_libraryGrid.GetItemRect(i).Position.Y-_libraryGrid.GetItemRect(0).Position.Y)<1).Count();
        try
        {
            _libraryPreferences=new();_libraryExpanded=false;_layoutBreakpoint=-1;ApplyResponsiveLayout();ShowCategory("Motions");await Settle();
            Check(_libraryGrid!.ItemCount==MotionCatalog.All.Count&&_libraryGrid.ItemCount>500,"the motion catalog is one continuous list, including more than 500 motions");
            int compactColumns=Columns();float compactWidth=_layoutLibraryDock.Size.X;ToggleLibrarySize();await Settle();
            Check(compactColumns==2,"the compact library keeps two columns beside its scrollbar");
            Check(_layoutLibraryDock.Size.X>compactWidth+200&&Columns()>compactColumns,"Expand gives the library more real width and more thumbnail columns");
            ToggleLibrarySize();await Settle();Check(Columns()==2,"Collapse restores two columns after the panel has actually resized");GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder,"compact-two-column-browser.png"));ToggleLibrarySize();await Settle();
            Check(_sceneContainer.Size.X>=330&&_layoutInspectorDock.GetGlobalRect().End.X<=GetViewportRect().Size.X+2,"the expanded library retains a usable scene and inspector");
            _librarySearch!.Text="sitting talking";PopulateLibraryBrowser(true);await Settle();Check(_browserResults.Length>0&&_browserResults.All(c=>LibraryPreferences.Matches(c.Name+" "+c.Detail+" "+string.Join(' ',c.Motion!.Tags),"sitting talking")),"multiword searching narrows a large catalog by all typed words");
            _librarySearch.Text="";Choose(_librarySource!,"Universal Animation Library 1");Check(_browserResults.Length==42,"the source facet isolates the 42 animations from the first Standard pack");
            _libraryGrid.Select(0);_libraryGrid.EmitSignal(ItemList.SignalName.ItemSelected,0L);await Settle();string favorite=BrowserKey(_browserSelected!);_libraryFavorite!.EmitSignal(BaseButton.SignalName.Pressed);Choose(_libraryScope!,"Favorites");
            Check(_browserResults.Length==1&&BrowserKey(_browserResults[0])==favorite,"Favorites finds a chosen motion without paging");
            LibraryPrefs.Collect("Street shot",favorite);SaveLibraryPreferences();FillLibraryScopes("Collection: Street shot");PopulateLibraryBrowser(true);Check(_browserResults.Length==1,"a named collection recalls its selected animation");
            var motion=_browserSelected!.Motion!;UseBrowserCard(_browserSelected);Check(_selected?.Node is RiggedActor&&_selected.Data.MotionClips.Any(c=>c.Name==motion.Name),"the selected new motion applies through the real scene runtime");
            Check(LibraryPrefs.Recent.First()==favorite,"using an item records its stable identity in recent history");
            var reloaded=LibraryPreferences.Load(LibraryPreferencesPath);Check(reloaded.Favorites.Contains(favorite)&&reloaded.Collections["Street shot"].Contains(favorite)&&reloaded.Recent.Contains(favorite),"favorites, collections and recent items survive preference reload");
            ShowCategory("Models");ShowCategory("Motions");await Settle();Check(_librarySource!.GetItemText(_librarySource.Selected)=="Universal Animation Library 1"&&_libraryScope!.GetItemText(_libraryScope.Selected)=="Collection: Street shot","switching library sections restores filters and collection choice");
            Choose(_libraryScope!,"All items");Choose(_librarySource,"All sources");_librarySearch.Text="no-such-motion-test";PopulateLibraryBrowser(true);Check(_browserResults.Length==0&&_libraryDetails!.Text.Contains("No matching",StringComparison.Ordinal),"an empty search gives a clear result without losing the catalog");
            _librarySearch.Text="walk";PopulateLibraryBrowser(true);await Settle();Check(_browserResults.Length>0,"clearing restrictive filters recovers ordinary walking motions");
            _libraryGrid.Select(0);_libraryGrid.EmitSignal(ItemList.SignalName.ItemSelected,0L);await Settle();GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder,"expanded-motion-browser.png"));
            _libraryGrid._GuiInput(new InputEventKey{Pressed=true,Keycode=Key.Delete});Check(_entries.Count>0&&MotionCatalog.All.Count>500,"library keyboard Delete cannot delete a selected scene actor or bundled asset");
            File.WriteAllText(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new{status="passed",checks,motions=MotionCatalog.All.Count,models=ContentCatalog.All.Count},_json));GD.Print("LIBRARY_BROWSER_OK checks="+checks+" output="+folder);
        }
        finally{SaveLibraryPreferences();}
    }
}
