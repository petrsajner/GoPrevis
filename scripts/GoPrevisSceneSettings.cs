using Godot;
using System;

namespace GoPrevis;

public partial class GoPrevisApp
{
    SceneSettingsData _sceneSettings=new();
    WorldEnvironment? _sceneDisplayEnvironment;
    MeshInstance3D? _sceneGround,_sceneGrid;
    StandardMaterial3D? _sceneGroundMaterial;
    ColorPickerButton? _sceneBackground;
    Label? _sceneBackgroundHex;
    HSlider? _sceneBrightnessSlider,_sceneGroundOpacitySlider;
    SpinBox? _sceneBrightnessNumber,_sceneGroundHeightNumber,_sceneGroundOpacityNumber;
    CheckButton? _sceneGroundVisible,_sceneGlobalLabels,_sceneDirectional;
    bool _refreshingSceneSettings;

    public float SceneGroundHeight=>_sceneSettings.GroundHeight;

    void InitializeSceneDisplay(WorldEnvironment environment,MeshInstance3D ground,MeshInstance3D grid)
    {
        _sceneDisplayEnvironment=environment;_sceneGround=ground;_sceneGrid=grid;
        _sceneGroundMaterial=ground.MaterialOverride is StandardMaterial3D existing?(StandardMaterial3D)existing.Duplicate():new StandardMaterial3D{AlbedoColor=new Color("4a4a50"),Roughness=1};
        ground.MaterialOverride=_sceneGroundMaterial;ApplySceneSettings(_sceneSettings);
    }
    SceneSettingsData SnapshotSceneSettings()=>_sceneSettings.ValidatedCopy();
    void ApplySceneSettings(SceneSettingsData settings)
    {
        var next=(settings??new SceneSettingsData()).ValidatedCopy();_sceneSettings=next;
        if(_sceneDisplayEnvironment?.Environment is {} environment)environment.BackgroundColor=new Color(next.BackgroundColor[0],next.BackgroundColor[1],next.BackgroundColor[2],next.BackgroundColor[3]);
        _lighting?.SetBrightness(next.Brightness);
        bool visible=next.GroundVisible&&next.GroundOpacity>0;
        if(_sceneGround!=null){_sceneGround.Position=new Vector3(0,next.GroundHeight,0);_sceneGround.Visible=visible;}
        if(_sceneGrid!=null){_sceneGrid.Position=new Vector3(0,next.GroundHeight,0);_sceneGrid.Visible=visible;}
        if(_sceneGroundMaterial!=null)
        {
            var color=_sceneGroundMaterial.AlbedoColor;color.A=next.GroundOpacity;_sceneGroundMaterial.AlbedoColor=color;
            _sceneGroundMaterial.Transparency=next.GroundOpacity<.9999f?BaseMaterial3D.TransparencyEnum.Alpha:BaseMaterial3D.TransparencyEnum.Disabled;
            _sceneGroundMaterial.DepthDrawMode=BaseMaterial3D.DepthDrawModeEnum.OpaqueOnly;
        }
        if(_sceneGrid?.MaterialOverride is StandardMaterial3D gridMaterial)
        {
            var color=gridMaterial.AlbedoColor;color.A=next.GroundOpacity;gridMaterial.AlbedoColor=color;
            gridMaterial.Transparency=next.GroundOpacity<.9999f?BaseMaterial3D.TransparencyEnum.Alpha:BaseMaterial3D.TransparencyEnum.Disabled;
        }
        RefreshAllObjectLabels();RefreshSceneSettingsControls();
    }
    void ChangeSceneSetting(Action<SceneSettingsData> edit)
    {
        if(_refreshing||_refreshingSceneSettings)return;var value=SnapshotSceneSettings();edit(value);value=value.ValidatedCopy();PushUndo();ApplySceneSettings(value);
    }
    VBoxContainer SceneSettingsFoldout(VBoxContainer parent,string title,bool expanded)
    {
        var holder=new VBoxContainer();holder.AddThemeConstantOverride("separation",5);parent.AddChild(holder);
        var content=new VBoxContainer{Visible=expanded};content.AddThemeConstantOverride("separation",6);
        var header=Button((expanded?"▾ ":"▸ ")+title,()=>{},true);header.Alignment=HorizontalAlignment.Left;header.ButtonPressed=expanded;header.AddThemeFontSizeOverride("font_size",11);header.AddThemeStyleboxOverride("normal",CompactStyle(new Color("24232b"),6,5));
        header.Pressed+=()=>{content.Visible=!content.Visible;header.Text=(content.Visible?"▾ ":"▸ ")+title;header.ButtonPressed=content.Visible;};holder.AddChild(header);holder.AddChild(content);return content;
    }
    void BuildSceneSettingsControls(VBoxContainer parent)
    {
        var content=SceneSettingsFoldout(parent,"3D scene",false);
        var background=SceneSettingsFoldout(content,"Background",true);
        _sceneBackground=new ColorPickerButton{EditAlpha=false,CustomMinimumSize=new Vector2(42,27)};
        _sceneBackgroundHex=Text("",11,true);_sceneBackgroundHex.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        background.AddChild(Row(Text("Color",11,true),Space(),_sceneBackground,_sceneBackgroundHex,AnimatedParameterKey("Scene.BackgroundColor",scene:true,group:true)));
        _sceneBackground.Pressed+=BeginNumericGesture;_sceneBackground.PopupClosed+=EndNumericGesture;
        _sceneBackground.ColorChanged+=color=>ChangeSceneSetting(value=>value.BackgroundColor=new[]{color.R,color.G,color.B,1f});

        var lighting=SceneSettingsFoldout(content,"Global lighting",true);
        _sceneBrightnessSlider=new HSlider{MinValue=0,MaxValue=400,Step=1,SizeFlagsHorizontal=SizeFlags.ExpandFill,CustomMinimumSize=new Vector2(60,0)};
        _sceneBrightnessNumber=Spin(0,400,1,100);_sceneBrightnessNumber.CustomMinimumSize=new Vector2(66,0);
        lighting.AddChild(Text("Brightness",11,true));lighting.AddChild(Row(_sceneBrightnessSlider,_sceneBrightnessNumber,Text("%",11,true),AnimatedParameterKey("Scene.Brightness",scene:true)));
        _sceneBrightnessSlider.DragStarted+=BeginNumericGesture;_sceneBrightnessSlider.DragEnded+=_=>EndNumericGesture();
        _sceneBrightnessSlider.ValueChanged+=value=>ChangeSceneSetting(data=>data.Brightness=(float)value/100);
        _sceneBrightnessNumber.ValueChanged+=value=>ChangeSceneSetting(data=>data.Brightness=(float)value/100);
        _sceneDirectional=new CheckButton{Text="Studio key light",TooltipText="Optional fixed studio light. Add a directional light below to control its rotation and properties.",ButtonPressed=_lighting.DirectionalEnabled};_sceneDirectional.Toggled+=value=>{if(_refreshing||_refreshingSceneSettings)return;PushUndo();SetStudioKeyBase(value);};lighting.AddChild(Row(_sceneDirectional,Space(),AnimatedParameterKey("Scene.StudioKeyEnabled",scene:true)));
        lighting.AddChild(Row(Button("+ Point light",()=>AddSceneLight("point_light")),Button("+ Directional light",()=>AddSceneLight("directional_light"))));

        var display=SceneSettingsFoldout(content,"Display",true);
        _sceneGlobalLabels=new CheckButton{Text="Object labels"};_sceneGlobalLabels.Toggled+=value=>ChangeSceneSetting(data=>data.ObjectLabelsVisible=value);display.AddChild(Row(_sceneGlobalLabels,Space(),AnimatedParameterKey("Scene.ObjectLabelsVisible",scene:true)));
        _sceneGroundVisible=new CheckButton{Text="Ground"};_sceneGroundVisible.Toggled+=value=>ChangeSceneSetting(data=>data.GroundVisible=value);display.AddChild(Row(_sceneGroundVisible,Space(),AnimatedParameterKey("Scene.GroundVisible",scene:true)));
        _sceneGroundHeightNumber=Spin(-1000,1000,.001,0);_sceneGroundHeightNumber.ValueChanged+=value=>ChangeSceneSetting(data=>data.GroundHeight=(float)value);display.AddChild(Row(Text("Height",11,true),_sceneGroundHeightNumber,Text("m",11,true),AnimatedParameterKey("Scene.GroundHeight",scene:true)));
        _sceneGroundOpacitySlider=new HSlider{MinValue=0,MaxValue=100,Step=1,SizeFlagsHorizontal=SizeFlags.ExpandFill,CustomMinimumSize=new Vector2(60,0)};_sceneGroundOpacityNumber=Spin(0,100,1,100);_sceneGroundOpacityNumber.CustomMinimumSize=new Vector2(66,0);
        display.AddChild(Text("Opacity",11,true));display.AddChild(Row(_sceneGroundOpacitySlider,_sceneGroundOpacityNumber,Text("%",11,true),AnimatedParameterKey("Scene.GroundOpacity",scene:true)));
        _sceneGroundOpacitySlider.DragStarted+=BeginNumericGesture;_sceneGroundOpacitySlider.DragEnded+=_=>EndNumericGesture();_sceneGroundOpacitySlider.ValueChanged+=value=>ChangeSceneSetting(data=>data.GroundOpacity=(float)value/100);_sceneGroundOpacityNumber.ValueChanged+=value=>ChangeSceneSetting(data=>data.GroundOpacity=(float)value/100);
        RefreshSceneSettingsControls();
    }
    void RefreshSceneSettingsControls()
    {
        if(_sceneBackground==null||!GodotObject.IsInstanceValid(_sceneBackground))return;bool before=_refreshingSceneSettings;_refreshingSceneSettings=true;
        var color=new Color(_sceneSettings.BackgroundColor[0],_sceneSettings.BackgroundColor[1],_sceneSettings.BackgroundColor[2],_sceneSettings.BackgroundColor[3]);_sceneBackground.Color=color;
        if(_sceneBackgroundHex!=null)_sceneBackgroundHex.Text="#"+color.ToHtml(false);
        if(_sceneBrightnessSlider!=null)_sceneBrightnessSlider.Value=_sceneSettings.Brightness*100;if(_sceneBrightnessNumber!=null)_sceneBrightnessNumber.Value=_sceneSettings.Brightness*100;
        if(_sceneGroundHeightNumber!=null)_sceneGroundHeightNumber.Value=_sceneSettings.GroundHeight;
        if(_sceneGroundOpacitySlider!=null)_sceneGroundOpacitySlider.Value=_sceneSettings.GroundOpacity*100;if(_sceneGroundOpacityNumber!=null)_sceneGroundOpacityNumber.Value=_sceneSettings.GroundOpacity*100;
        if(_sceneGroundVisible!=null)_sceneGroundVisible.ButtonPressed=_sceneSettings.GroundVisible;if(_sceneGlobalLabels!=null)_sceneGlobalLabels.ButtonPressed=_sceneSettings.ObjectLabelsVisible;if(_sceneDirectional!=null)_sceneDirectional.ButtonPressed=_lighting.DirectionalEnabled;
        _refreshingSceneSettings=before;
    }
}
