using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GoPrevis;

public partial class GoPrevisApp
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
        BuildLibraryBrowser(category,ContentCatalog.All.Where(model=>groups.Contains(model.Category,StringComparer.Ordinal)).Select(model=>new ContentCard(model.Id,model.Name,model.Category+" · "+model.License+"\n"+model.Attribution+"\nDrag into the scene.",model,null)));
    }

    void BuildObjectLightingSection()=>BuildSceneSettingsControls(_library);
}
