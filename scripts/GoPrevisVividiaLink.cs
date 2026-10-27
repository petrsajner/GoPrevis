using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace GoPrevis;

/// <summary>
/// "→ Vividia" (Petr, 27. 10. 2026): an exported shot goes straight into Vividia or Vividia Pro on this
/// computer. Vividia writes vividia-app.json into its own data folder at every start (where its exe is);
/// GoPrevis starts that exe with <c>--goprevis &lt;export folder&gt;</c> — a running Vividia takes the
/// folder from the second launch, one that is not running starts and takes it — and Vividia imports the
/// colour, depth and pose passes into its Video library with the manifest's camera and shot content.
/// The button is there only when a Vividia is found.
/// </summary>
public partial class GoPrevisApp
{
    sealed record VividiaApp(string Product,string Exe);
    AcceptDialog? _shotExportedDialog;
    readonly List<Button> _shotExportedButtons=new();

    /// <summary>The Vividia products on this computer that said where they are.</summary>
    static List<VividiaApp> FindVividia()
    {
        var found=new List<VividiaApp>();
        string data=System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
        foreach(var (identifier,product) in new[]{("com.vividia.desktop","Vividia"),("com.vividia.pro","Vividia Pro")})
        {
            string info=Path.Combine(data,identifier,"vividia-app.json");
            try
            {
                if(!File.Exists(info))continue;
                using var document=JsonDocument.Parse(File.ReadAllText(info));var root=document.RootElement;
                string? exe=root.TryGetProperty("exe",out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
                string name=root.TryGetProperty("product",out var named)&&named.ValueKind==JsonValueKind.String?named.GetString()??product:product;
                if(!string.IsNullOrWhiteSpace(exe)&&File.Exists(exe))found.Add(new VividiaApp(name,exe));
            }
            catch(Exception error)when(error is IOException or JsonException or UnauthorizedAccessException){GD.PushWarning(product+" could not be found: "+error.Message);}
        }
        return found;
    }

    /// <summary>Hand an exported shot to Vividia; says what went wrong when it cannot.</summary>
    bool SendShotToVividia(VividiaApp app,string directory)
    {
        try
        {
            var start=new ProcessStartInfo(app.Exe){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(app.Exe)??""};
            start.ArgumentList.Add("--goprevis");start.ArgumentList.Add(directory);
            using var process=Process.Start(start)??throw new InvalidOperationException("the program did not start");
            Status("Shot sent to "+app.Product+": "+directory);return true;
        }
        catch(Exception error){Error(app.Product+" could not be opened: "+error.Message);return false;}
    }

    /// <summary>The export's end: where it is, and "→ Vividia" for each Vividia on this computer.</summary>
    void ShowShotExported(ShotExportResult result)
    {
        if(_shotExportedDialog!=null){_shotExportedDialog.GetParent()?.RemoveChild(_shotExportedDialog);_shotExportedDialog.QueueFree();}
        var apps=FindVividia();
        var dialog=new AcceptDialog{Title="Shot exported",DialogText="Export complete.\n\n"+result.Directory+(apps.Count>0?"\n\nSend it to "+string.Join(" or ",apps.ConvertAll(a=>a.Product))+" to use its passes there.":""),Exclusive=true};
        AddChild(dialog);_shotExportedDialog=dialog;
        _shotExportedButtons.Clear();
        for(int i=0;i<apps.Count;i++)_shotExportedButtons.Add(dialog.AddButton("→ "+apps[i].Product,true,"vividia"+i));
        dialog.CustomAction+=action=>
        {
            string name=action.ToString();
            if(!name.StartsWith("vividia",StringComparison.Ordinal)||!int.TryParse(name["vividia".Length..],out int index)||index<0||index>=apps.Count)return;
            dialog.Hide();SendShotToVividia(apps[index],result.Directory);
        };
        dialog.PopupCentered(new Vector2I(660,230));
    }
}
