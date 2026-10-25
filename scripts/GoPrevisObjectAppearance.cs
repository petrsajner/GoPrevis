using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GoPrevis;

public partial class GoPrevisApp
{
    sealed class ObjectMaterialState
    {
        public MeshInstance3D Mesh=null!;
        public Material? OriginalOverride;
        public Material?[] OriginalSurfaces=Array.Empty<Material?>();
        public readonly List<Material> Tints=new();
    }
    sealed class ObjectAppearanceState
    {
        public Node3D Root=null!;
        public readonly List<ObjectMaterialState> Meshes=new();
        public Label3D Label=null!;
        public float[]? AppliedColor;
    }
    readonly Dictionary<string,ObjectAppearanceState> _objectAppearanceStates=new(StringComparer.Ordinal);
    VBoxContainer _objectAppearancePanel=null!,_objectMaterialControls=null!;
    CheckButton _objectShowLabel=null!;
    ColorPickerButton _objectColor=null!;
    LineEdit _objectColorHex=null!;
    Button _objectOriginalColor=null!,_objectMaterialHeader=null!;
    bool _objectColorGesture,_objectMaterialExpanded=true;

    static Color? AppearanceColor(ObjectAppearanceData data)
    {
        var value=data.ColorOverride;
        if(value==null)return null;
        if(value.Length!=3||value.Any(v=>!float.IsFinite(v)||v<0||v>1))throw new FormatException("Object color must contain three RGB values between zero and one.");
        return new Color(value[0],value[1],value[2],1);
    }
    static bool SameAppearanceColor(float[]? a,float[]? b)=>a==null?b==null:b!=null&&a.Length==b.Length&&a.Zip(b).All(pair=>Math.Abs(pair.First-pair.Second)<.000001f);

    ObjectAppearanceState EnsureObjectAppearanceState(Entry entry)
    {
        if(_objectAppearanceStates.TryGetValue(entry.Data.Id,out var state)&&GodotObject.IsInstanceValid(state.Root)&&state.Root==entry.Node)return state;
        if(state!=null)ReleaseObjectAppearanceState(state);
        state=new ObjectAppearanceState{Root=entry.Node};
        foreach(var mesh in Descendants(entry.Node).OfType<MeshInstance3D>().Where(m=>m.Mesh!=null))
        {
            var materials=new ObjectMaterialState{Mesh=mesh,OriginalOverride=mesh.MaterialOverride,OriginalSurfaces=Enumerable.Range(0,mesh.GetSurfaceOverrideMaterialCount()).Select(mesh.GetSurfaceOverrideMaterial).ToArray()};state.Meshes.Add(materials);
        }
        state.Label=new Label3D{Name="ObjectLabel",Text=entry.Data.Name,Layers=StudioCameraRig.EditorLayer,FontSize=32,OutlineSize=6,PixelSize=.00045f,FixedSize=true,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,NoDepthTest=false,Modulate=new Color("eeeaf5"),OutlineModulate=new Color("121216"),TopLevel=true};
        entry.Node.AddChild(state.Label);_objectAppearanceStates[entry.Data.Id]=state;return state;
    }
    static void RestoreObjectMaterials(ObjectAppearanceState state)
    {
        foreach(var material in state.Meshes)
        {
            if(GodotObject.IsInstanceValid(material.Mesh))
            {
                material.Mesh.MaterialOverride=material.OriginalOverride;
                for(int i=0;i<material.OriginalSurfaces.Length;i++)material.Mesh.SetSurfaceOverrideMaterial(i,material.OriginalSurfaces[i]);
            }
            foreach(var tint in material.Tints)tint.Dispose();material.Tints.Clear();
        }
    }
    static void ReleaseObjectAppearanceState(ObjectAppearanceState state)
    {
        RestoreObjectMaterials(state);if(GodotObject.IsInstanceValid(state.Label))state.Label.QueueFree();
    }
    void ClearObjectAppearanceStates()
    {
        foreach(var state in _objectAppearanceStates.Values)ReleaseObjectAppearanceState(state);_objectAppearanceStates.Clear();
    }
    static Material ObjectTintMaterial(Material? source,Color color)
    {
        BaseMaterial3D tint=source is BaseMaterial3D material?(BaseMaterial3D)material.Duplicate():new StandardMaterial3D{Roughness=.8f};
        tint.ResourceName="GoPrevis sketch color";tint.AlbedoTexture=null;tint.AlbedoColor=color;return tint;
    }
    void ApplyObjectAppearance(Entry entry)
    {
        entry.Data.Appearance??=new();var color=AppearanceColor(entry.Data.Appearance);var state=EnsureObjectAppearanceState(entry);
        if(!SameAppearanceColor(state.AppliedColor,entry.Data.Appearance.ColorOverride))
        {
            RestoreObjectMaterials(state);
            if(color is {} chosen)
            {
                foreach(var material in state.Meshes)
                {
                    if(!GodotObject.IsInstanceValid(material.Mesh)||material.Mesh.Mesh==null)continue;
                    if(material.OriginalOverride!=null)
                    {
                        var tint=ObjectTintMaterial(material.OriginalOverride,chosen);material.Tints.Add(tint);material.Mesh.MaterialOverride=tint;
                    }
                    else for(int i=0;i<material.Mesh.Mesh.GetSurfaceCount();i++)
                    {
                        var source=i<material.OriginalSurfaces.Length?material.OriginalSurfaces[i]:null;source??=material.Mesh.Mesh.SurfaceGetMaterial(i);
                        var tint=ObjectTintMaterial(source,chosen);material.Tints.Add(tint);material.Mesh.SetSurfaceOverrideMaterial(i,tint);
                    }
                }
            }
            state.AppliedColor=entry.Data.Appearance.ColorOverride?.ToArray();
        }
        RefreshObjectLabel(entry,state);
    }
    void RefreshObjectLabel(Entry entry,ObjectAppearanceState state)
    {
        if(!GodotObject.IsInstanceValid(state.Label))return;
        var local=entry.Node is RiggedActor actor?actor.GetPreparedPoseBounds():PropFactory.CalculateBounds(entry.Node);var minimum=Vector3.One*float.PositiveInfinity;var maximum=Vector3.One*float.NegativeInfinity;
        for(int i=0;i<8;i++){var point=entry.Node.GlobalTransform*local.GetEndpoint(i);minimum=minimum.Min(point);maximum=maximum.Max(point);}
        state.Label.GlobalPosition=new Vector3((minimum.X+maximum.X)*.5f,maximum.Y+.12f,(minimum.Z+maximum.Z)*.5f);state.Label.Text=entry.Data.Name;
        state.Label.Visible=entry.Node.IsVisibleInTree()&&_sceneSettings.ObjectLabelsVisible&&(entry.Data.Appearance?.ShowLabel??true);
    }
    void RefreshAllObjectLabels()
    {
        foreach(string id in _objectAppearanceStates.Keys.Where(id=>!_entries.Any(e=>e.Data.Id==id)).ToArray()){ReleaseObjectAppearanceState(_objectAppearanceStates[id]);_objectAppearanceStates.Remove(id);}
        foreach(var entry in _entries){var state=EnsureObjectAppearanceState(entry);RefreshObjectLabel(entry,state);}
        foreach(var camera in _cameras)foreach(var label in camera.Rig.GetChildren().OfType<Label3D>()){label.Text=camera.Name;label.Visible=_sceneSettings.ObjectLabelsVisible&&camera.ShowLabel;}
    }

    void BuildObjectAppearanceInspector()
    {
        _objectAppearancePanel=new VBoxContainer();_objectAppearancePanel.AddThemeConstantOverride("separation",5);_inspector.AddChild(_objectAppearancePanel);
        _objectShowLabel=new CheckButton{Text="Show label"};_objectShowLabel.Toggled+=shown=>
        {
            if(_refreshing)return;PushUndo();if(_selectedCamera is {} camera)camera.ShowLabel=shown;else if(_selected is {} entry){entry.Data.Appearance??=new();entry.Data.Appearance.ShowLabel=shown;}RefreshAllObjectLabels();
        };_objectAppearancePanel.AddChild(_objectShowLabel);
        _objectMaterialHeader=Button("▾ Material",()=>{},true);_objectMaterialHeader.Alignment=HorizontalAlignment.Left;_objectMaterialHeader.ButtonPressed=true;_objectAppearancePanel.AddChild(_objectMaterialHeader);
        _objectMaterialControls=new VBoxContainer();_objectMaterialControls.AddThemeConstantOverride("separation",4);_objectAppearancePanel.AddChild(_objectMaterialControls);
        _objectMaterialHeader.Pressed+=()=>{_objectMaterialExpanded=!_objectMaterialExpanded;_objectMaterialControls.Visible=_objectMaterialExpanded;_objectMaterialHeader.Text=(_objectMaterialExpanded?"▾ ":"▸ ")+"Material";_objectMaterialHeader.ButtonPressed=_objectMaterialExpanded;};
        _objectColor=new ColorPickerButton{EditAlpha=false,CustomMinimumSize=new Vector2(42,27)};_objectColorHex=new LineEdit{PlaceholderText="#RRGGBB",SizeFlagsHorizontal=SizeFlags.ExpandFill};_objectColorHex.TextSubmitted+=hex=>
        {
            if(_refreshing||_selected==null)return;string value=hex.Trim();if(!Color.HtmlIsValid(value)){Error("Enter a valid RGB color, for example #b8bcc8.");return;}SetSelectedObjectColor(Color.FromHtml(value));
        };
        _objectColor.Pressed+=()=>{if(_selected==null)return;_objectColorGesture=true;BeginNumericGesture();};_objectColor.PopupClosed+=()=>{if(_objectColorGesture){_objectColorGesture=false;EndNumericGesture();}};
        _objectColor.ColorChanged+=color=>{if(!_refreshing&&_selected!=null)SetSelectedObjectColor(color);};_objectMaterialControls.AddChild(Row(Text("Color",11,true),_objectColor,_objectColorHex));
        var palette=new HBoxContainer();palette.AddThemeConstantOverride("separation",3);
        foreach(string hex in new[]{"e34b91","ed5656","ef9b4d","e6cd46","57bd70","41bba5","4fafe1","9475d9","aab0bc"})
        {
            var color=new Color(hex);var swatch=Button("",()=>SetSelectedObjectColor(color));swatch.TooltipText="#"+hex;swatch.CustomMinimumSize=new Vector2(22,22);var normal=Style(color,11);normal.SetContentMarginAll(0);swatch.AddThemeStyleboxOverride("normal",normal);var hover=Style(color.Lightened(.18f),11);hover.SetContentMarginAll(0);swatch.AddThemeStyleboxOverride("hover",hover);swatch.AddThemeStyleboxOverride("pressed",normal);palette.AddChild(swatch);
        }
        _objectMaterialControls.AddChild(palette);_objectOriginalColor=Button("Original materials",()=>SetSelectedObjectColor(null));_objectMaterialControls.AddChild(_objectOriginalColor);
        UpdateObjectAppearanceInspector();
    }
    void SetSelectedObjectColor(Color? color)
    {
        if(_selected==null)return;
        if(color is {} candidate&&(!float.IsFinite(candidate.R)||!float.IsFinite(candidate.G)||!float.IsFinite(candidate.B)))throw new FormatException("Object color must contain finite RGB values.");
        PushUndo();_selected.Data.Appearance??=new();_selected.Data.Appearance.ColorOverride=color is {} chosen?new[]{Math.Clamp(chosen.R,0,1),Math.Clamp(chosen.G,0,1),Math.Clamp(chosen.B,0,1)}:null;ApplyObjectAppearance(_selected);UpdateObjectAppearanceInspector();
    }
    void UpdateObjectAppearanceInspector()
    {
        if(_objectAppearancePanel==null)return;bool old=_refreshing;_refreshing=true;bool item=_selected!=null,camera=_selectedCamera!=null;
        _objectAppearancePanel.Visible=_inspectorSection=="Object"&&(item||camera);_objectShowLabel.Disabled=!item&&!camera;_objectShowLabel.ButtonPressed=_selectedCamera?.ShowLabel??_selected?.Data.Appearance?.ShowLabel??true;
        _objectShowLabel.TooltipText=_sceneSettings.ObjectLabelsVisible?"Show this name in the working viewport. Labels are excluded from rendered exports.":"Global object labels are hidden in 3D scene settings.";
        bool materialObject=item&&!IsSceneLightKind(_selected!.Data.Kind);_objectMaterialHeader.Visible=materialObject;_objectMaterialControls.Visible=materialObject&&_objectMaterialExpanded;
        if(item)
        {
            var color=AppearanceColor(_selected!.Data.Appearance??new());if(color==null){var mesh=Descendants(_selected.Node).OfType<MeshInstance3D>().FirstOrDefault(m=>m.Mesh!=null);color=mesh?.GetActiveMaterial(0) is BaseMaterial3D material?material.AlbedoColor:new Color("b8bcc8");}
            _objectColor.Color=color.Value;_objectColorHex.Text="#"+color.Value.ToHtml(false);_objectOriginalColor.Disabled=_selected.Data.Appearance?.ColorOverride==null;
        }
        _refreshing=old;
    }
}
