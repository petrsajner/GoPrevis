using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Previs;

public partial class PrevisApp
{
    public async Task<int> RunObjectAppearanceChecks()
    {
        var original=Snapshot();string originalPath=_projectPath;double originalTime=_time;var undo=_undo.ToArray();var redo=_redo.ToArray();int checks=0;
        void Check(bool value,string text){Require(value,"Object appearance: "+text);checks++;}
        async Task Settle(){for(int i=0;i<2;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        static ulong Id(GodotObject? value)=>value?.GetInstanceId()??0;
        static bool ColorClose(Color a,Color b)=>new Vector3(a.R,a.G,a.B).DistanceTo(new Vector3(b.R,b.G,b.B))<.0001f;
        static IEnumerable<MeshInstance3D> Meshes(Entry entry)=>Descendants(entry.Node).OfType<MeshInstance3D>().Where(m=>m.Mesh!=null);
        static Material?[] Active(Entry entry)=>Meshes(entry).SelectMany(mesh=>Enumerable.Range(0,mesh.Mesh!.GetSurfaceCount()).Select(mesh.GetActiveMaterial)).ToArray();
        try
        {
            StopPoseEditing();ClearObjects();SetTime(0);var settings=SnapshotSceneSettings();settings.ObjectLabelsVisible=true;ApplySceneSettings(settings);
            var first=AddObject("chair",Vector3.Zero,"First chair");var second=AddObject("chair",new Vector3(2,0,0),"Second chair");ApplyObjectAppearance(first);ApplyObjectAppearance(second);
            var originalFirst=Active(first);var originalSecond=Active(second);var otherColors=originalSecond.OfType<BaseMaterial3D>().Select(m=>m.AlbedoColor).ToArray();
            Check(originalFirst.Zip(originalSecond).All(pair=>ReferenceEquals(pair.First,pair.Second)),"procedural chairs start with their authored shared source materials");
            Select(first);var red=new Color("e34b61");SetSelectedObjectColor(red);var colored=Active(first);
            Check(colored.OfType<BaseMaterial3D>().All(material=>ColorClose(material.AlbedoColor,red)&&material.AlbedoTexture==null),"a selected object's sketch color uses its own uniform RGB materials");
            Check(colored.Zip(originalFirst).All(pair=>!ReferenceEquals(pair.First,pair.Second)),"color editing duplicates materials instead of modifying the originals");
            Check(Active(second).Zip(originalSecond).All(pair=>ReferenceEquals(pair.First,pair.Second))&&Active(second).OfType<BaseMaterial3D>().Select((material,index)=>ColorClose(material.AlbedoColor,otherColors[index])).All(value=>value),"the other chair keeps both its original material IDs and colors");
            SetSelectedObjectColor(null);Check(Active(first).Zip(originalFirst).All(pair=>ReferenceEquals(pair.First,pair.Second)),"Original materials restores every authored material identity exactly");

            var imported=AddCatalogModel(ContentCatalog.Find("kenney-cars-sedan")!,new Vector3(0,0,-3),false);var importedOriginal=Active(imported);Check(importedOriginal.OfType<BaseMaterial3D>().Any(m=>m.AlbedoTexture!=null),"the imported test vehicle has a real authored palette texture");
            Select(imported);var blue=new Color("4fa6df");SetSelectedObjectColor(blue);Check(Active(imported).OfType<BaseMaterial3D>().All(m=>m.AlbedoTexture==null&&ColorClose(m.AlbedoColor,blue)),"a textured imported model receives the requested uniform sketch color");
            SetSelectedObjectColor(null);Check(Active(imported).Zip(importedOriginal).All(pair=>ReferenceEquals(pair.First,pair.Second)),"resetting an imported tint restores source textures and material objects");

            var human=AddCatalogModel(ContentCatalog.Find("human-average")!,new Vector3(-2,0,0),false);DropPose("pose-sit-upright",human,human.Node.Position);var humanBefore=CaptureLibraryBonePose(human);Select(human);SetSelectedObjectColor(new Color("6cd5a1"));
            Check(LibraryPoseEqual(humanBefore,CaptureLibraryBonePose(human)),"tinting a posed figure does not change its prepared bone pose");
            var fox=AddFox(new Vector3(2,0,2),true);SetTime(.25);var foxBefore=CaptureLibraryBonePose(fox);var foxOriginal=Active(fox);Select(fox);SetSelectedObjectColor(new Color("b788de"));
            Check(LibraryPoseEqual(foxBefore,CaptureLibraryBonePose(fox))&&fox.NativeTrack!=null,"native animal skinning and animation survive a color change");SetSelectedObjectColor(null);Check(Active(fox).Zip(foxOriginal).All(pair=>ReferenceEquals(pair.First,pair.Second)),"native animal reset restores its original materials");

            human.Data.Name="Seated performer label";human.Data.Appearance.ShowLabel=true;RefreshAllObjectLabels();var label=_objectAppearanceStates[human.Data.Id].Label;
            Check(label.Text==human.Data.Name&&label.Visible&&label.Layers==StudioCameraRig.EditorLayer&&(label.Layers&1u)==0,"object names create editor-only labels excluded from render pass layer one");
            var bounds=((RiggedActor)human.Node).GetPreparedPoseBounds();float top=Enumerable.Range(0,8).Select(i=>(human.Node.GlobalTransform*bounds.GetEndpoint(i)).Y).Max();Check(label.GlobalPosition.Y>top&&label.GlobalPosition.Y<top+.2f,"a seated figure label follows its actual posed top rather than a standing height");
            human.Data.Appearance.ShowLabel=false;RefreshAllObjectLabels();Check(!label.Visible,"per-object label visibility can hide one label");human.Data.Appearance.ShowLabel=true;RefreshAllObjectLabels();
            var camera=_cameras[0];camera.ShowLabel=false;RefreshAllObjectLabels();Check(camera.Rig.GetChildren().OfType<Label3D>().All(l=>!l.Visible),"a camera can hide its own scene label");camera.ShowLabel=true;RefreshAllObjectLabels();Check(camera.Rig.GetChildren().OfType<Label3D>().All(l=>l.Visible),"camera labels can be re-enabled independently");
            settings=SnapshotSceneSettings();settings.ObjectLabelsVisible=false;ApplySceneSettings(settings);Check(_objectAppearanceStates.Values.All(state=>!state.Label.Visible)&&_cameras.All(c=>c.Rig.GetChildren().OfType<Label3D>().All(l=>!l.Visible)),"global label visibility gates object and camera labels together");settings.ObjectLabelsVisible=true;ApplySceneSettings(settings);
            var rig=(RiggedActor)human.Node;DropMotion("human-walk-gesture",human,human.Node.Position);SetTime(0);var initialLabel=_objectAppearanceStates[human.Data.Id].Label.GlobalPosition;SetTime(1.3);var movedLabel=_objectAppearanceStates[human.Data.Id].Label.GlobalPosition;
            Check(movedLabel.DistanceTo(initialLabel)>.01f,"a label follows evaluated mocap movement in the working scene");
            human.Data.PoseCorrections.Root.Clear();TransformLayerRuntime.Upsert(human.Data.PoseCorrections.Root,_time,new Vector3(0,.2f,0));SetTime(_time);var raisedLabel=_objectAppearanceStates[human.Data.Id].Label.GlobalPosition;
            Check(Math.Abs(raisedLabel.Y-movedLabel.Y-.2f)<.002f,"label placement includes the final keyed skeletal correction");

            Select(first);SetSelectedObjectColor(red);var tintedIds=Active(first).Select(Id).ToArray();var tintOverrides=Meshes(first).Select(m=>Id(m.MaterialOverride)).ToArray();var capture=new SubViewport{Size=new Vector2I(160,90),World3D=_sceneViewport.World3D};AddChild(capture);var exportCamera=new Camera3D();capture.AddChild(exportCamera);
            using(var depth=new ControlPassRenderer(_world,exportCamera)){depth.BeginDepthPass();Check(Meshes(first).All(m=>m.MaterialOverride is ShaderMaterial),"a depth pass can temporarily override a user-tinted object");depth.EndDepthPass();}
            Check(Active(first).Select(Id).SequenceEqual(tintedIds)&&Meshes(first).Select(m=>Id(m.MaterialOverride)).SequenceEqual(tintOverrides),"depth cleanup restores the exact tinted materials and overrides");capture.QueueFree();
            int history=_undo.Count;var beforeInvalid=JsonSerializer.Serialize(first.Data.Appearance);bool rejected=false;try{SetSelectedObjectColor(new Color(float.NaN,0,0));}catch(FormatException){rejected=true;}
            Check(rejected&&_undo.Count==history&&JsonSerializer.Serialize(first.Data.Appearance)==beforeInvalid&&Active(first).Select(Id).SequenceEqual(tintedIds),"a non-finite color fails before changing data, materials or Undo history");
            string firstId=first.Data.Id,secondId=second.Data.Id;SetSelectedObjectColor(blue);Undo();first=_entries.Single(e=>e.Data.Id==firstId);second=_entries.Single(e=>e.Data.Id==secondId);
            Check(AppearanceColor(first.Data.Appearance) is {} restored&&ColorClose(restored,red)&&second.Data.Appearance.ColorOverride==null,"Undo restores the selected tint and leaves another object's authored appearance intact");

            Select(first);first.Data.Appearance.ShowLabel=false;ApplyObjectAppearance(first);camera=_cameras[0];camera.ShowLabel=false;settings=SnapshotSceneSettings();settings.BackgroundColor=new float[]{.1f,.15f,.2f,1};settings.Brightness=.72f;settings.GroundHeight=.23f;ApplySceneSettings(settings);
            string directory=ProjectSettings.GlobalizePath("user://appearance-checks");Directory.CreateDirectory(directory);string project=Path.Combine(directory,"appearance-roundtrip.previs");SaveTo(project);LoadProjectFromPath(project);await Settle();first=_entries.Single(e=>e.Data.Id==firstId);
            Check(Snapshot().Version==7&&AppearanceColor(first.Data.Appearance) is {} savedColor&&ColorClose(savedColor,red)&&!first.Data.Appearance.ShowLabel,"scene format seven reopens each object's saved sketch color and label flag");
            Check(!_cameras[0].ShowLabel&&Math.Abs(SnapshotSceneSettings().Brightness-.72f)<.0001&&Math.Abs(SnapshotSceneSettings().GroundHeight-.23f)<.0001f,"camera labels and scene display settings survive the same project roundtrip");
            Check(Active(first).OfType<BaseMaterial3D>().All(m=>ColorClose(m.AlbedoColor,red))&&!_objectAppearanceStates[first.Data.Id].Label.Visible,"reopened runtime materials and labels match their persisted data");
            var empty=AddObject("box",Vector3.Zero);empty.Data.Appearance.ColorOverride=null;ApplyObjectAppearance(empty);Check(empty.Data.Appearance.ShowLabel&&_objectAppearanceStates.ContainsKey(empty.Data.Id),"default appearance is valid when no color override is present");
            GD.Print("OBJECT_APPEARANCE_OK checks="+checks);return checks;
        }
        finally
        {
            Restore(original,"");_projectPath=originalPath;SetTime(originalTime);_undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);
        }
    }
}
