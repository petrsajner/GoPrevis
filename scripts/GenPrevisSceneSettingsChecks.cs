using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GenPrevis;

public partial class GenPrevisApp
{
    public async Task RunSceneSettingsChecks()
    {
        var original=Snapshot();string originalPath=_projectPath;double originalTime=_time;var undo=_undo.ToArray();var redo=_redo.ToArray();bool auto=_autoKey;var editor=_editorCamera.Transform;int checks=0;bool passed=false;string? failure=null;var observations=new List<object>();
        void Check(bool value,string message){observations.Add(new{message,passed=value});Require(value,"Scene settings: "+message);checks++;}
        async Task Settle(){for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        try
        {
            _playing=false;_autoKey=false;StopPoseEditing();ClearObjects();SetTime(0);await Settle();
            var settings=new SceneSettingsData{BackgroundColor=new float[]{.08f,.12f,.2f,1},Brightness=.6f,GroundVisible=true,GroundHeight=1.3f,GroundOpacity=.55f,ObjectLabelsVisible=false};ApplySceneSettings(settings);
            Check(_sceneDisplayEnvironment!.Environment.BackgroundColor.IsEqualApprox(new Color(.08f,.12f,.2f,1)),"background uses the selected scene color");
            Check(_sceneGround!.Visible&&_sceneGrid!.Visible&&Math.Abs(_sceneGround.Position.Y-1.3)<.0001&&Math.Abs(_sceneGrid.Position.Y-1.3)<.0001,"ground and grid share the requested world height");
            Check(Math.Abs(_sceneGroundMaterial!.AlbedoColor.A-.55)<.0001&&_sceneGroundMaterial.Transparency==BaseMaterial3D.TransparencyEnum.Alpha,"ground opacity affects its own material");
            Check(Math.Abs(SnapshotSceneSettings().Brightness-.6)<.0001&&!_lighting.DirectionalEnabled,"global fill brightness changes without introducing a directional light");
            var snapshot=SnapshotSceneSettings();settings.BackgroundColor[0]=.9f;Check(SnapshotSceneSettings().BackgroundColor[0]<.1f,"scene settings are independent validated snapshots");
            bool rejected=false;var invalid=SnapshotSceneSettings();invalid.Brightness=float.NaN;try{ApplySceneSettings(invalid);}catch(FormatException){rejected=true;}Check(rejected&&Math.Abs(SnapshotSceneSettings().Brightness-.6)<.0001,"invalid scene values fail atomically");
            settings=SnapshotSceneSettings();settings.GroundVisible=false;ApplySceneSettings(settings);Check(!_sceneGround!.Visible&&!_sceneGrid!.Visible,"hiding ground also hides its editor grid");
            var box=AddObject("box",new Vector3(-2,4,0),"Placement box");box.Data.Rotation=new float[]{12,35,0};box.Data.Scale=new float[]{1.2f,.8f,.7f};SetTime(0);Select(box);var before=box.Node.Transform;int count=_undo.Count;PlaceSelectedOnGround();
            Check(Math.Abs(PlacementWorldBounds(box).Position.Y-1.3)<.001f,"Place on ground aligns rotated/scaled geometry to the configured hidden ground");
            Check(_undo.Count==count+1&&box.Data.TransformAnimation.Position.Count==0,"placement is a single undoable transform edit");
            string id=box.Data.Id;Undo();box=_entries.Single(e=>e.Data.Id==id);Check(box.Node.Transform.IsEqualApprox(before),"Undo restores the complete pre-placement transform");Redo();box=_entries.Single(e=>e.Data.Id==id);Check(Math.Abs(PlacementWorldBounds(box).Position.Y-1.3)<.001f,"Redo restores placement");
            settings=SnapshotSceneSettings();settings.GroundVisible=true;ApplySceneSettings(settings);var table=AddObject("table",new Vector3(2,1.3f,0),"Support table");SetTime(0);Select(box);_editorCamera.Position=new Vector3(4,4.5f,5);var target=PlacementWorldBounds(table);_editorCamera.LookAt(target.GetCenter());await Settle();await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            var surface=new Vector3(target.GetCenter().X,target.End.Y,target.GetCenter().Z);var point=ObjectGizmoToInput(_editorCamera.UnprojectPosition(surface));BeginPlaceOnObject();
            Check(TryPlacementSurface(box,point,out var actual)&&Math.Abs(actual.Y-surface.Y)<.003f,"Place on object uses a real upward-facing mesh surface");
            _sceneContainer.EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=point});
            Check(!_placeOnObject&&Math.Abs(PlacementWorldBounds(box).Position.Y-surface.Y)<.003f,"the viewport placement tool puts the selected object on the clicked support");
            var chair=AddObject("chair",new Vector3(0,1.3f,0),"Seat");DropPose("pose-sit-upright",null,new Vector3(-2,1.3f,1));var seated=_selected!;var seatedBones=CaptureLibraryBonePose(seated);
            _editorCamera.Position=new Vector3(2,3.8f,-3);_editorCamera.LookAt(chair.Node.GlobalPosition+Vector3.Up*.5f);await Settle();await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            var seatPoint=chair.Node.GlobalPosition+new Vector3(0,.4875f,-.08f);point=ObjectGizmoToInput(_editorCamera.UnprojectPosition(seatPoint));BeginPlaceOnObject();_sceneContainer.EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=point});
            Check(!_placeOnObject&&Math.Abs(PlacementSupport(seated,true).Y-seatPoint.Y)<.005f,"seated figures align their thigh support to a chair instead of putting feet on the seat");
            Check(PlacementWorldBounds(seated).Position.Y<seatPoint.Y-.2f&&LibraryPoseEqual(seatedBones,CaptureLibraryBonePose(seated)),"seated placement preserves the pose and leaves the legs below the support surface");
            settings=SnapshotSceneSettings();settings.ObjectLabelsVisible=true;ApplySceneSettings(settings);Select(box);SetSelectedObjectColor(new Color("eb7755"));box.Data.Appearance.ShowLabel=false;_cameras[0].ShowLabel=false;RefreshAllObjectLabels();
            string directory=ProjectSettings.GlobalizePath("user://scene-settings-checks");Directory.CreateDirectory(directory);string file=Path.Combine(directory,"scene-settings.previs");SaveTo(file);var saved=JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(file))!;
            Check(saved.Version==10&&Math.Abs(saved.SceneSettings.GroundHeight-1.3)<.0001&&saved.Objects.Single(e=>e.Id==id).Appearance.ColorOverride!=null&&!saved.Cameras[0].ShowLabel,"format 10 stores scene, object and camera display properties");
            LoadProjectFromPath(file);box=_entries.Single(e=>e.Data.Id==id);Check(Math.Abs(SceneGroundHeight-1.3)<.0001&&!box.Data.Appearance.ShowLabel&&box.Data.Appearance.ColorOverride!.Length==3&&!_cameras[0].ShowLabel,"saved display settings and local labels reopen exactly");
            var legacy=JsonSerializer.Deserialize<ProjectData>(JsonSerializer.Serialize(original))!;legacy.Version=5;legacy.SceneSettings=new();foreach(var item in legacy.Objects)item.Appearance=new();Restore(legacy,"");Check(Math.Abs(SceneGroundHeight)<.0001&&Math.Abs(SnapshotSceneSettings().Brightness-1)<.0001,"older projects load neutral scene defaults");
            int appearance=await RunObjectAppearanceChecks();Check(appearance>=25,"material isolation, labels, tint restoration and appearance persistence pass");
            int framing=await RunCameraFramingChecks();Check(framing>=60,"all Topview framing variants pass actual camera geometry checks");
            if(DisplayServer.GetName()!="headless"){int rendered=await RunSceneDisplayRenderChecks();Check(rendered>=10,"background, brightness, ground and export label exclusion pass actual pixel checks");}
            passed=true;GD.Print("SCENE_SETTINGS_OK checks="+checks+" appearance="+appearance+" framing="+framing);
        }
        catch(Exception ex){failure=ex.ToString();throw;}
        finally
        {
            CancelPlacement();string report=ProjectSettings.GlobalizePath("res://output/scene-settings-report.json");Directory.CreateDirectory(Path.GetDirectoryName(report)!);File.WriteAllText(report,JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,failure,observations},new JsonSerializerOptions{WriteIndented=true}));Restore(original,"");_projectPath=originalPath;SetTime(originalTime);_editorCamera.Transform=editor;_autoKey=auto;_undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);
        }
    }
    async Task RunSceneSettingsChecksAndQuit()
    {
        try{await RunSceneSettingsChecks();GetTree().Quit(0);}catch(Exception ex){GD.PushError("SCENE_SETTINGS_FAILED "+ex);GetTree().Quit(1);}
    }
}
