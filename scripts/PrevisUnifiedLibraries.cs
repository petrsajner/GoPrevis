using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Previs;

public partial class PrevisApp
{
    /// <summary>Compatibility names resolve to the five visible library sections.</summary>
    static string UnifiedLibraryCategory(string category)=>category switch
    {
        "Scene" or "People" or "Animals"=>"Models",
        "Props" or "Environment" or "World"=>"Objects",
        "Camera moves"=>"Cameras",
        _=>category
    };

    void BuildUnifiedLibrary(string category)
    {
        category=UnifiedLibraryCategory(category);
        if(category=="Cameras"){BuildCameraLibrary();return;}
        if(category is not ("Models" or "Objects"))return;

        bool models=category=="Models";if(!models)BuildObjectLightingSection();
        string[] groups=models?new[]{"People","Animals"}:new[]{"Furniture","Vehicles","Architecture","Nature","Props"};
        var catalog=ContentCatalog.All.Where(model=>groups.Contains(model.Category,StringComparer.Ordinal)).ToArray();
        var filter=new OptionButton{FitToLongestItem=false,ClipText=true,TooltipText="Choose a group or browse all sections below."};
        filter.AddItem("All groups");foreach(string group in groups)filter.AddItem(group);
        _library.AddChild(filter);
        var search=new LineEdit{PlaceholderText=models?"Search models…":"Search objects…",ClearButtonEnabled=true};_library.AddChild(search);
        var count=Text("",11,true);_library.AddChild(count);
        var sections=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};sections.AddThemeConstantOverride("separation",8);_library.AddChild(sections);
        var collapsed=new HashSet<string>(StringComparer.Ordinal);

        void Populate()
        {
            foreach(Node child in sections.GetChildren()){sections.RemoveChild(child);child.QueueFree();}
            string chosen=filter.Selected<=0?"":filter.GetItemText(filter.Selected);
            var matching=catalog.Where(model=>(chosen==""||model.Category==chosen)&&(search.Text==""||(model.Name+" "+model.Category).Contains(search.Text,StringComparison.OrdinalIgnoreCase))).ToArray();
            count.Text=matching.Length+" items · drag into scene";
            foreach(string group in groups)
            {
                var items=matching.Where(model=>model.Category==group).ToArray();if(items.Length==0)continue;
                var section=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};section.AddThemeConstantOverride("separation",4);sections.AddChild(section);
                var grid=new GridContainer{Columns=2,SizeFlagsHorizontal=SizeFlags.ExpandFill,Visible=!collapsed.Contains(group)||search.Text!=""||chosen!=""};
                var header=Button("",()=>{},true);header.Alignment=HorizontalAlignment.Left;header.ClipText=true;header.AddThemeFontSizeOverride("font_size",11);
                header.AddThemeStyleboxOverride("normal",CompactStyle(new Color("25232c"),6,5));header.AddThemeStyleboxOverride("pressed",CompactStyle(new Color("2b2635"),6,5));
                void UpdateHeader(){header.Text=(grid.Visible?"▾ ":"▸ ")+group+"  ·  "+items.Length;header.ButtonPressed=grid.Visible;}
                header.Pressed+=()=>{grid.Visible=!grid.Visible;if(grid.Visible)collapsed.Remove(group);else collapsed.Add(group);UpdateHeader();};UpdateHeader();
                section.AddChild(header);section.AddChild(grid);
                foreach(var model in items)
                    grid.AddChild(CreateContentCard(new ContentCard(model.Id,model.Name,model.Category+" · "+model.License+"\nDrag into the scene.",model,null)));
            }
            if(matching.Length==0)sections.AddChild(Text("No matching items.",11,true));
        }
        filter.ItemSelected+=_=>Populate();search.TextChanged+=_=>Populate();Populate();

    }

    void BuildObjectLightingSection()=>BuildSceneSettingsControls(_library);
}
