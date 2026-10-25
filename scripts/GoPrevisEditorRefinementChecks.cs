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
    async Task RunEditorRefinementChecksAndQuit()
    {
        try{await RunEditorRefinementChecks();GetTree().Quit(0);}
        catch(Exception error){GD.PushError("EDITOR_REFINEMENTS_FAILED "+error);GetTree().Quit(1);}
    }
    public async Task RunEditorRefinementChecks()
    {
        var original=Snapshot();string project=_projectPath;double time=_time;var undo=_undo.ToArray();var redo=_redo.ToArray();var editor=_editorCamera.Transform;
        string output=ProjectSettings.GlobalizePath("res://output/editor-refinement-checks/run-"+Guid.NewGuid().ToString("N")[..10]);Directory.CreateDirectory(output);
        int checks=0;bool passed=false;string? failure=null;var observations=new List<object>();SubViewport? render=null;
        void Check(bool value,string message){observations.Add(new{message,passed=value});Require(value,"Editor refinements: "+message);checks++;GD.Print("PASS Editor refinements: "+message);}
        async Task Settle(){for(int i=0;i<4;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        void Click(Vector2 p,bool ctrl=false)=>_timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=p,CtrlPressed=ctrl});
        void Release(Vector2 p)=>_timeline._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=p});
        void Box(Vector2 from,Vector2 to,bool add=false){Click(from,add);_timeline._GuiInput(new InputEventMouseMotion{Position=to});Release(to);}
        Vector2 At(Rect2 row,double seconds)=>new(row.Position.X+row.Size.X*(float)(seconds/_duration),row.Position.Y+11);
        void Delete(){GetViewport().GuiReleaseFocus();_UnhandledInput(new InputEventKey{Pressed=true,Keycode=Key.Delete});}
        void Send(InputEvent input)=>_sceneContainer.EmitSignal(Control.SignalName.GuiInput,input);
        try
        {
            StopPoseEditing();StopHistoryPlayback();ClearObjects();ClearCameras();CreateCamera("Camera A",null,true);_duration=8;_scrub.MaxValue=8;SetTime(0);
            AddObject("box",Vector3.Zero);_projectPath="";
            SaveTo(Path.Combine(output,"My shot"));Check(File.Exists(Path.Combine(output,"My shot.goprevis")),"a bare project name saves a real .goprevis file");
            SaveTo(Path.Combine(output,"Shot 01.v2"));Check(File.Exists(Path.Combine(output,"Shot 01.v2.goprevis")),"a dot in the project name never suppresses the extension");
            SaveTo(Path.Combine(output,"Existing.GOPREVIS"));Check(File.Exists(Path.Combine(output,"Existing.GOPREVIS"))&&!File.Exists(Path.Combine(output,"Existing.GOPREVIS.goprevis")),"an explicit project extension is not doubled");
            SaveTo(Path.Combine(output,"Legacy.previs"));Check(File.Exists(Path.Combine(output,"Legacy.previs")),"an explicitly opened legacy filename remains supported");
            _projectPath="";SaveProject();await Settle();_fileDialog.CurrentDir=output;_fileDialog.GetLineEdit().Text="Typed shot.v3";
            _fileDialog.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);await Settle();
            Check(File.Exists(Path.Combine(output,"Typed shot.v3.goprevis")),"the actual Save dialog accepts only a typed name and appends its project extension");_fileDialog.Hide();

            ClearObjects();var box=AddObject("box",Vector3.Zero,"Keyed box");string boxId=box.Data.Id;
            foreach(double t in new[]{1d,2d,6d})TransformLayerRuntime.Upsert(box.Data.TransformAnimation.Position,t,new Vector3((float)t,0,0));
            TransformLayerRuntime.Upsert(box.Data.TransformAnimation.Rotation,2,new Vector3(0,45,0));TransformLayerRuntime.Upsert(box.Data.TransformAnimation.Scale,3,Vector3.One);
            box.Data.Path=new(){Enabled=true,Duration=8,Points=new(){new float[]{0,0,0},new float[]{2,0,1},new float[]{4,0,0}},PointTimes=new(){0,2,8}};
            var actor=AddObject("actor",new Vector3(0,0,2));string actorId=actor.Data.Id;
            actor.Data.PoseCorrections.Bones["Head"]=new(){new(){Time=2.5},new(){Time=5}};actor.Data.PoseCorrections.Root.Add(new(){Time=2.7,Value=new float[3]});
            var camera=_cameras.Single();TransformLayerRuntime.Upsert(camera.Animation.Position,1.5,Vector3.Zero);TransformLayerRuntime.Upsert(camera.Animation.Position,6,Vector3.One);camera.LensKeys.Add(new(){Time=2.2,Value=50});
            _timeline.ExpandObject(boxId);_timeline.ExpandPoseCorrections(actorId);RefreshTimelineSize();await Settle();
            var cameraRow=_timeline.KeyRowRect("Transform",camera.Id);var lastRow=_timeline.KeyRowRect("Pose",actorId,"Head");
            int history=_undo.Count;var boxFrom=At(cameraRow,.8)+Vector2.Up*9;var boxTo=At(lastRow,3.4)+Vector2.Down*11;
            Click(boxFrom);_timeline._GuiInput(new InputEventMouseMotion{Position=boxTo});await Settle();
            if(DisplayServer.GetName()!="headless")GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,"timeline-selection.png"));Release(boxTo);
            Check(_timeline.SelectedKeyCount==9,"a rectangle selects nine unique keys across camera, path, transform, lens and pose tracks without summary duplicates");
            _name.GrabFocus();_UnhandledInput(new InputEventKey{Pressed=true,Keycode=Key.Delete});
            Check(box.Data.TransformAnimation.Position.Count==3&&_entries.Count==2,"Delete while typing a name cannot delete keys or scene objects");
            Delete();Check(_entries.Count==2&&_cameras.Count==1,"timeline Delete preserves the scene objects and camera");
            Check(box.Data.TransformAnimation.Position.Count==1&&box.Data.TransformAnimation.Position[0].Time==6&&box.Data.TransformAnimation.Rotation.Count==0&&box.Data.TransformAnimation.Scale.Count==0,"Delete removes only the selected transform times and channels");
            Check(actor.Data.PoseCorrections.Bones["Head"].Single().Time==5&&actor.Data.PoseCorrections.Root.Count==0,"Delete removes selected additive pose keys and preserves later corrections");
            Check(box.Data.Path.Points.Count==2&&box.Data.Path.PointTimes.SequenceEqual(new[]{0d,8d})&&box.Data.Path.Enabled,"path-key deletion keeps spatial points and arrival times aligned");
            Check(camera.Animation.Position.Single().Time==6&&camera.LensKeys.Count==0&&_undo.Count==history+1,"all selected channels are removed in exactly one Undo action");
            Undo();box=_entries.Single(e=>e.Data.Id==boxId);actor=_entries.Single(e=>e.Data.Id==actorId);camera=_cameras.Single();
            Check(box.Data.Path.Points.Count==3&&box.Data.TransformAnimation.Position.Count==3&&actor.Data.PoseCorrections.Root.Count==1&&camera.LensKeys.Count==1,"one Undo restores the complete multi-track deletion");
            Redo();Check(_entries.Single(e=>e.Data.Id==boxId).Data.Path.Points.Count==2,"Redo repeats the complete deletion");Undo();box=_entries.Single(e=>e.Data.Id==boxId);
            var positionRow=_timeline.KeyRowRect("Transform",boxId,"Position");var rotationRow=_timeline.KeyRowRect("Transform",boxId,"Rotation");
            Box(At(positionRow,1.8)+Vector2.Up*9,At(positionRow,2.2)+Vector2.Down*9);
            Check(_timeline.SelectedKeyCount==1,"a rectangle on an expanded Position row selects only that XYZ key");
            Box(At(rotationRow,1.8)+Vector2.Up*9,At(rotationRow,2.2)+Vector2.Down*9,true);Check(_timeline.SelectedKeyCount==2,"Ctrl adds a second rectangle to the selection");
            Click(At(rotationRow,2),true);Release(At(rotationRow,2));Check(_timeline.SelectedKeyCount==1,"Ctrl-click toggles a single selected key");
            Delete();Check(box.Data.TransformAnimation.Position.Count==2&&box.Data.TransformAnimation.Rotation.Count==1,"channel-specific Delete preserves the coincident rotation key");

            ClearObjects();var scaled=AddObject("box",new Vector3(0,.3f,0));scaled.Data.Scale=new[]{1f,2f,3f};SetTime(0);Select(scaled);
            _editorCamera.Position=new Vector3(5,5,7);_editorCamera.LookAt(new Vector3(0,.3f,0));await Settle();
            _Input(new InputEventKey{Keycode=Key.Shift,Pressed=true,ShiftPressed=true});_Input(new InputEventKey{Keycode=Key.Ctrl,Pressed=true,ShiftPressed=true,CtrlPressed=true});
            Check(Enumerable.Range(0,3).All(axis=>ObjectGizmoScaleColor(axis)==new Color("ffdc62")),"pressing Ctrl makes all scale cubes yellow before dragging");
            Vector2 Project(Vector3 p)=>ObjectGizmoToInput(_editorCamera.UnprojectPosition(p));
            var origin=scaled.Node.GlobalPosition;float length=ObjectGizmoWorldLength(origin);var axis=ObjectGizmoScaleAxis(scaled.Node,0);
            var from=Project(origin+axis*length*.58f);var to=Project(origin+axis*length*.58f*1.5f);history=_undo.Count;
            Send(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=from,ShiftPressed=true,CtrlPressed=true});
            Send(new InputEventMouseMotion{Position=to,Relative=to-from,ShiftPressed=true,CtrlPressed=true});
            Send(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=to,ShiftPressed=true,CtrlPressed=true});
            Check(scaled.Node.Scale.DistanceTo(new Vector3(1.5f,3,4.5f))<.025f,"Ctrl-drag of an actual scale cube scales all three axes and preserves their proportions");
            Check(_undo.Count==history+1,"uniform scale remains one reversible gesture");
            _Input(new InputEventKey{Keycode=Key.Ctrl,Pressed=false,ShiftPressed=true});Check(ObjectGizmoScaleColor(0)==ObjectGizmoColors[0],"releasing Ctrl restores the individual axis colors");
            Check(ObjectGizmoScaled(new Vector3(.1f,1,2),1,.001f,true).DistanceTo(new Vector3(.05f,.5f,1))<.0001f,"uniform scaling clamps one shared factor at the minimum without distorting proportions");
            Check(ObjectGizmoScaled(new Vector3(1,2,4),2,1000,true).DistanceTo(new Vector3(250,500,1000))<.001f,"uniform scaling preserves proportions at the maximum bound");
            _viewportShift=false;_viewportCtrl=false;

            ClearObjects();SetTime(0);_lighting.DirectionalEnabled=false;ApplySceneSettings(new(){Brightness=.1f});
            Check(!Descendants(_world).OfType<Light3D>().Any(l=>l.IsVisibleInTree()),"a neutral scene starts without any active explicit light");
            AddObject("box",Vector3.Zero);ShowCategory("Objects");
            Check(Descendants(_library).OfType<Button>().Any(b=>b.Text=="+ Point light")&&Descendants(_library).OfType<Button>().Any(b=>b.Text=="+ Directional light"),"both light types are discoverable in Objects / 3D scene / Global lighting");
            AddSceneLight("point_light");var point=_selected!;string pointId=point.Data.Id;point.Data.Position=new[]{-3f,4f,2f};SetTime(0);
            Check(_lightPanel!.Visible&&point.Node.GetNode<OmniLight3D>("Light").ShadowEnabled&&point.Node.Position.Y==4,"a selected point light exposes properties and defaults to real shadows");
            _lightEnergy.Value=1.6;_lightRange.Value=12;
            Check(Math.Abs(point.Data.Light!.Energy-1.6f)<.001&&point.Data.Light.Range==12,"the real inspector updates point intensity and range");
            AddSceneLight("directional_light");var directional=_selected!;string directionalId=directional.Data.Id;
            Check(!_lightRangeRow.Visible&&directional.Node.RotationDegrees.DistanceTo(new Vector3(-48,-28,0))<.001f,"directional lights use rotation and omit the point-only range control");
            Check(Descendants(point.Node).Concat(Descendants(directional.Node)).OfType<MeshInstance3D>().All(m=>m.Layers==StudioCameraRig.EditorLayer&&m.CastShadow==GeometryInstance3D.ShadowCastingSetting.Off),"light handles are editor-only and never render or cast fake shadows");
            ChangeSceneLight(data=>{data.Enabled=false;data.Energy=.8f;data.Color=new[]{.9f,.95f,1f};});
            string saved=Path.Combine(output,"Lights");SaveTo(saved);LoadProjectFromPath(saved+".goprevis");point=_entries.Single(e=>e.Data.Id==pointId);directional=_entries.Single(e=>e.Data.Id==directionalId);
            Check(Snapshot().Version==ProjectData.CurrentVersion&&point.Data.Light!.Range==12&&!directional.Data.Light!.Enabled&&directional.Node.GetNode<Light3D>("Light").LightColor.B==1,"save/reopen preserves independent lights, colors, intensity, range and enabled state");
            Select(point);history=_undo.Count;_lightShadows.ButtonPressed=false;Check(!point.Node.GetNode<Light3D>("Light").ShadowEnabled&&_undo.Count==history+1,"the visible Cast shadows switch records an Undo action");
            Undo();point=_entries.Single(e=>e.Data.Id==pointId);directional=_entries.Single(e=>e.Data.Id==directionalId);Check(point.Node.GetNode<Light3D>("Light").ShadowEnabled,"Undo restores the shadow switch");

            if(DisplayServer.GetName()!="headless")
            {
                render=new SubViewport{Size=new Vector2I(640,360),World3D=_world.GetWorld3D(),RenderTargetUpdateMode=SubViewport.UpdateMode.Always,PositionalShadowAtlasSize=2048};AddChild(render);
                var cameraView=new Camera3D{Current=true,CullMask=1,Fov=50,Position=new Vector3(5,4.5f,7)};render.AddChild(cameraView);cameraView.LookAt(new Vector3(0,.2f,0));
                async Task<Image> Frame(string name){await Settle();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);var image=render.GetTexture().GetImage();image.SavePng(Path.Combine(output,name+".png"));return image;}
                int ShadowPixels(Image without,Image with){int count=0;for(int y=0;y<360;y++)for(int x=0;x<640;x++){var a=without.GetPixel(x,y);var b=with.GetPixel(x,y);if((a.R+a.G+a.B-b.R-b.G-b.B)/3>.025f)count++;}return count;}
                foreach(var light in new[]{point,directional})
                {
                    point.Data.Light!.Enabled=light==point;directional.Data.Light!.Enabled=light==directional;ApplySceneLight(point);ApplySceneLight(directional);
                    light.Data.Light!.Shadows=false;ApplySceneLight(light);using var without=await Frame(light.Data.Kind+"-without-shadow");
                    light.Data.Light.Shadows=true;ApplySceneLight(light);using var with=await Frame(light.Data.Kind+"-with-shadow");
                    int pixels=ShadowPixels(without,with);GD.Print("SHADOW_PROBE atlas="+render.PositionalShadowAtlasSize+" quadrants="+render.PositionalShadowAtlasQuad0+","+render.PositionalShadowAtlasQuad1+","+render.PositionalShadowAtlasQuad2+","+render.PositionalShadowAtlasQuad3+" pixels="+pixels);Check(pixels>50,light.Data.Kind+" produces a visible geometric shadow in the actual camera color render (darkened pixels="+pixels+")");
                    _shot.GlobalTransform=cameraView.GlobalTransform;SyncCameraFromRig(_cameras.Single());
                    async Task<string> ExportLight(bool shadows)
                    {
                        light.Data.Light!.Shadows=shadows;ApplySceneLight(light);
                        string target=Path.Combine(output,light.Data.Kind+(shadows?"-export-shadow":"-export-no-shadow"));
                        await ExportShotAsync(new(){OutputDirectory=target,Width=640,Height=360,Fps=1,StartSeconds=0,EndSeconds=1,ExportMp4=false,ExportPngSequence=true,IncludeColor=true,IncludeDepth=true,IncludePose=false});return target;
                    }
                    string unshadowed=await ExportLight(false),shadowed=await ExportLight(true);
                    using var exportWithout=Image.LoadFromFile(Directory.GetFiles(Path.Combine(unshadowed,"color"),"*.png").Single());
                    using var exportWith=Image.LoadFromFile(Directory.GetFiles(Path.Combine(shadowed,"color"),"*.png").Single());
                    Check(ShadowPixels(exportWithout,exportWith)>50,light.Data.Kind+" shadows survive the actual shot exporter, including its first frame");
                    Check(File.ReadAllBytes(Directory.GetFiles(Path.Combine(unshadowed,"depth"),"*.png").Single()).SequenceEqual(File.ReadAllBytes(Directory.GetFiles(Path.Combine(shadowed,"depth"),"*.png").Single())),light.Data.Kind+" shadow switch does not contaminate the geometric depth pass");
                }
                Select(point);ShowCategory("Objects");await Settle();GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output,"editor-ui.png"));
            }
            passed=true;GD.Print("EDITOR_REFINEMENTS_OK checks="+checks+" output="+output);
        }
        catch(Exception error){failure=error.ToString();throw;}
        finally
        {
            render?.QueueFree();_fileDialog.Hide();ReleaseObjectGizmoDrag();_viewportShift=false;_viewportCtrl=false;StopPoseEditing();
            File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(new{status=passed?"passed":"failed",checks,failure,observations},_json));
            Restore(original,"");_projectPath=project;SetTime(time);_editorCamera.Transform=editor;_undo.Clear();_redo.Clear();foreach(var item in undo.Reverse())_undo.Push(item);foreach(var item in redo.Reverse())_redo.Push(item);
        }
    }
}
