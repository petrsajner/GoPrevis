using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    /// <summary>Measures the actual animation workspace and captures its rendered UI at three desktop sizes.</summary>
    async Task RunAnimationLayoutChecks(string folder,Action<bool,string> check)
    {
        var window=GetWindow();var originalSize=window.Size;var originalMode=window.Mode;
        float originalScale=window.ContentScaleFactor;string originalWorkspaceMode=_animationWorkspaceMode;
        var observations=new List<object>();
        async Task Settle(){for(int i=0;i<6;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        static object Rect(Rect2 value)=>new{x=value.Position.X,y=value.Position.Y,width=value.Size.X,height=value.Size.Y};
        bool Fits(Control control,Vector2 viewport)
        {
            var r=control.GetGlobalRect();return r.Size.X>0&&r.Size.Y>0&&r.Position.X>=-1&&r.Position.Y>=-1&&r.End.X<=viewport.X+1&&r.End.Y<=viewport.Y+1;
        }
        bool RowHasNoOverlap(HBoxContainer row)
        {
            var controls=row.GetChildren().OfType<Control>().Where(c=>c.IsVisibleInTree()&&c.Size.X>1&&c.Size.Y>1).OrderBy(c=>c.GetGlobalRect().Position.X).ToArray();
            for(int i=1;i<controls.Length;i++)if(controls[i-1].GetGlobalRect().End.X>controls[i].GetGlobalRect().Position.X+1)return false;
            return true;
        }
        try
        {
            Directory.CreateDirectory(folder);window.Mode=Window.ModeEnum.Windowed;window.ContentScaleFactor=1;
            SetAnimationWorkspaceMode("Graph");
            foreach(var size in new[]{new Vector2I(1100,700),new Vector2I(1280,800),new Vector2I(2560,1440)})
            {
                window.Size=size;ApplyResponsiveLayout();ApplyAnimationToolbarLayout(true);await Settle();
                // The second layout uses the actual settled workspace width, not the previous window's width.
                ApplyAnimationToolbarLayout(true);RefreshAnimationWorkspace();await Settle();
                var viewport=GetViewportRect().Size;string label=size.X+" × "+size.Y;
                // Preserve the actual failure view too, before an assertion can stop the run.
                bool screenshotSaved=true;
                if(DisplayServer.GetName()!="headless")
                {
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    using var image=GetViewport().GetTexture().GetImage();
                    screenshotSaved=image.SavePng(Path.Combine(folder,"animation-layout-"+size.X+"x"+size.Y+".png"))==Godot.Error.Ok;
                }
                check(Math.Abs(viewport.X-size.X)<=1&&Math.Abs(viewport.Y-size.Y)<=1,"Animation layout "+label+": viewport uses the requested 1x desktop size");
                Control[] toolbar={_animationModes["Dope sheet"],_animationModes["Graph"],_animationModes["Clips"],_animationSearch!,_animationAnimatedOnly!,_animationSelectedOnly!,_animationPinnedOnly!,_animationExpandButton!,_animationKeyButton!,_animationGraphKind!,_animationPreset!,_animationFit!};
                check(toolbar.All(c=>c.IsVisibleInTree()&&Fits(c,viewport)),"Animation layout "+label+": every mode, search, filter and graph toolbar control stays in the viewport");
                check(_animationToolbarMain!=null&&RowHasNoOverlap(_animationToolbarMain)&&(_animationToolbarSecondary==null||!_animationToolbarSecondary.IsVisibleInTree()||RowHasNoOverlap(_animationToolbarSecondary)),"Animation layout "+label+": toolbar controls have no horizontal overlaps");
                bool narrow=size.X<1450;
                check(_animationToolbarSecondary?.Visible==narrow,"Animation layout "+label+": toolbar uses "+(narrow?"two compact rows":"one compact row"));
                if(_animationToolbarSecondary?.IsVisibleInTree()==true)
                    check(_animationToolbarMain!.GetGlobalRect().End.Y<=_animationToolbarSecondary.GetGlobalRect().Position.Y+1,"Animation layout "+label+": toolbar rows do not overlap vertically");
                Control[] numeric={_animationSelectedKeyInfo!,_animationSelectedTime!,_animationSelectedValue!,_animationSelectedSpeed!};
                check(_animationKeyControls?.IsVisibleInTree()==true&&Fits(_animationKeyControls,viewport)&&numeric.All(c=>c.IsVisibleInTree()&&Fits(c,viewport)),"Animation layout "+label+": selected-key Time, Value and Speed controls remain visible");
                var numericRow=_animationSelectedTime?.GetParent() as HBoxContainer;
                check(numericRow!=null&&RowHasNoOverlap(numericRow),"Animation layout "+label+": selected-key numeric fields do not overlap");
                check(_animationGraph!=null&&Fits(_animationGraph,viewport)&&_animationGraph.Size.X>=280&&_animationGraph.Size.Y>=80,"Animation layout "+label+": graph keeps a visible usable drawing area");
                check(_animationKeyControls!.GetGlobalRect().End.Y<=_animationGraphPanel!.GetGlobalRect().Position.Y+1,"Animation layout "+label+": numeric key controls do not cover the graph");
                check(_animationModes["Graph"].Text=="Graph"&&_animationGraphKind!.GetItemText(0)=="Value graph"&&_animationGraphKind.GetItemText(1)=="Speed graph"&&_animationAnimatedOnly!.Text=="Animated only","Animation layout "+label+": animation controls use English labels");
                observations.Add(new{window=new[]{size.X,size.Y},viewport=new[]{viewport.X,viewport.Y},toolbarRows=narrow?2:1,
                    main=Rect(_animationToolbarMain!.GetGlobalRect()),secondary=_animationToolbarSecondary?.Visible==true?Rect(_animationToolbarSecondary.GetGlobalRect()):null,
                    numeric=Rect(_animationKeyControls.GetGlobalRect()),graph=Rect(_animationGraph!.GetGlobalRect()),
                    controls=toolbar.Select(c=>new{name=c is Button button?button.Text:c.Name.ToString(),rect=Rect(c.GetGlobalRect())}).ToArray()});
                if(DisplayServer.GetName()!="headless")check(screenshotSaved,"Animation layout "+label+": actual application screenshot is saved");
            }
        }
        finally
        {
            File.WriteAllText(Path.Combine(folder,"animation-layout-report.json"),JsonSerializer.Serialize(new{measurements=observations},_json));
            window.ContentScaleFactor=originalScale;window.Mode=Window.ModeEnum.Windowed;window.Size=originalSize;window.Mode=originalMode;
            SetAnimationWorkspaceMode(originalWorkspaceMode);ApplyResponsiveLayout();ApplyAnimationToolbarLayout(true);await Settle();
        }
    }
}
