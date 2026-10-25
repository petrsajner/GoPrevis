using Godot;
using System;
using System.Linq;

namespace GoPrevis;

public partial class GoPrevisApp
{
    VBoxContainer? _lightPanel;
    CheckButton _lightEnabled=null!,_lightShadows=null!;
    ColorPickerButton _lightColor=null!;
    SpinBox _lightEnergy=null!,_lightRange=null!;
    Control _lightRangeRow=null!;
    bool _refreshingLight;
    static bool IsSceneLightKind(string kind)=>kind is "point_light" or "directional_light";
    static string SceneLightName(string kind)=>kind=="point_light"?"Point light":"Directional light";

    static Node3D CreateSceneLight(string kind)
    {
        var root=new Node3D();
        Light3D source=kind=="point_light"?new OmniLight3D{OmniRange=10,OmniShadowMode=OmniLight3D.ShadowMode.Cube}
            :new DirectionalLight3D{DirectionalShadowMaxDistance=80,SkyMode=DirectionalLight3D.SkyModeEnum.LightOnly};
        source.Name="Light";source.LightEnergy=1;source.ShadowEnabled=true;source.ShadowBias=kind=="point_light"?.03f:.1f;source.ShadowNormalBias=kind=="point_light"?.3f:2f;source.LightCullMask=1;source.ShadowCasterMask=1;root.AddChild(source);
        var material=new StandardMaterial3D{AlbedoColor=new Color("ffe295"),ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded};
        root.AddChild(new MeshInstance3D{Name="LightHandle",Layers=StudioCameraRig.EditorLayer,Mesh=new SphereMesh{Radius=.12f,Height=.24f},MaterialOverride=material,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        var lines=new ImmediateMesh();lines.SurfaceBegin(Mesh.PrimitiveType.Lines);
        for(int axis=0;axis<3;axis++)
        {
            var direction=ObjectGizmoAxisVector(axis);
            lines.SurfaceAddVertex(direction*.18f);lines.SurfaceAddVertex(direction*.32f);
            lines.SurfaceAddVertex(-direction*.18f);lines.SurfaceAddVertex(-direction*.32f);
        }
        if(kind=="directional_light")
        {
            lines.SurfaceAddVertex(Vector3.Zero);lines.SurfaceAddVertex(Vector3.Forward*.85f);
            foreach(float sign in new[]{-1f,1f}){lines.SurfaceAddVertex(Vector3.Forward*.85f);lines.SurfaceAddVertex(Vector3.Forward*.6f+Vector3.Right*.13f*sign);}
        }
        lines.SurfaceEnd();root.AddChild(new MeshInstance3D{Name="LightDirection",Layers=StudioCameraRig.EditorLayer,Mesh=lines,MaterialOverride=material,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        var picker=new StaticBody3D{Name="PickBody",CollisionLayer=1,CollisionMask=0};picker.SetMeta("scene_root",root);
        picker.AddChild(new CollisionShape3D{Shape=new SphereShape3D{Radius=.23f}});root.AddChild(picker);
        return root;
    }
    void ApplySceneLight(Entry entry)
    {
        if(!IsSceneLightKind(entry.Data.Kind))return;
        var data=(entry.Data.Light??new()).ValidatedCopy();entry.Data.Light=data;
        var source=entry.Node.GetNode<Light3D>("Light");source.Visible=data.Enabled;
        source.LightColor=new Color(data.Color[0],data.Color[1],data.Color[2]);source.LightEnergy=data.Energy;source.ShadowEnabled=data.Shadows;
        if(source is OmniLight3D point)point.OmniRange=data.Range;
    }
    void AddSceneLight(string kind)
    {
        StopPoseEditing();PushUndo();
        var entry=AddObject(kind,new Vector3(_orbitTarget.X,SceneGroundHeight+3,_orbitTarget.Z));
        if(kind=="directional_light"){entry.Data.Rotation=new[]{-48f,-28f,0f};entry.Node.RotationDegrees=Vector(entry.Data.Rotation);}
        Select(entry);SetTime(_time);Status("Added "+entry.Data.Name+". Shift: move/rotate · Light properties: intensity, color and shadows.");
    }
    void ChangeSceneLight(Action<SceneLightData> edit)
    {
        if(_refreshing||_refreshingLight||_selected is not {} entry||!IsSceneLightKind(entry.Data.Kind))return;
        var data=(entry.Data.Light??new()).ValidatedCopy();edit(data);data=data.ValidatedCopy();PushUndo();entry.Data.Light=data;ApplySceneLight(entry);RefreshLightInspector();
    }
    void BuildLightInspector()
    {
        _lightPanel=new VBoxContainer{Visible=false};_inspector.AddChild(_lightPanel);_lightPanel.AddChild(Text("LIGHT",11,true));
        _lightEnabled=new CheckButton{Text="Enabled"};_lightEnabled.Toggled+=value=>ChangeSceneLight(data=>data.Enabled=value);_lightPanel.AddChild(_lightEnabled);
        _lightColor=new ColorPickerButton{EditAlpha=false,CustomMinimumSize=new Vector2(92,25)};_lightColor.ColorChanged+=value=>ChangeSceneLight(data=>data.Color=new[]{value.R,value.G,value.B});_lightPanel.AddChild(Row(Text("Color",11,true),Space(),_lightColor));
        _lightEnergy=Spin(0,16,.01,1);_lightEnergy.ValueChanged+=value=>ChangeSceneLight(data=>data.Energy=(float)value);_lightPanel.AddChild(Row(Text("Intensity",11,true),Space(),_lightEnergy));
        _lightRange=Spin(.1,500,.1,10);_lightRange.ValueChanged+=value=>ChangeSceneLight(data=>data.Range=(float)value);_lightRangeRow=Row(Text("Range (m)",11,true),Space(),_lightRange);_lightPanel.AddChild(_lightRangeRow);
        _lightShadows=new CheckButton{Text="Cast shadows"};_lightShadows.Toggled+=value=>ChangeSceneLight(data=>data.Shadows=value);_lightPanel.AddChild(_lightShadows);
        var help=Text("Move/rotate with Shift. Shadows update in the viewport and color exports.",11,true);help.AutowrapMode=TextServer.AutowrapMode.WordSmart;_lightPanel.AddChild(help);
    }
    void RefreshLightInspector()
    {
        if(_lightPanel==null)return;bool visible=_inspectorSection=="Object"&&_selected!=null&&IsSceneLightKind(_selected.Data.Kind);_lightPanel.Visible=visible;
        if(!visible)return;_refreshingLight=true;var data=_selected!.Data.Light??new();
        _lightEnabled.ButtonPressed=data.Enabled;_lightShadows.ButtonPressed=data.Shadows;_lightEnergy.Value=data.Energy;_lightRange.Value=data.Range;
        _lightRangeRow.Visible=_selected.Data.Kind=="point_light";_lightColor.Color=new Color(data.Color[0],data.Color[1],data.Color[2]);_refreshingLight=false;
    }
}
