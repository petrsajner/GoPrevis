using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace GoPrevis;

public partial class GoPrevisApp
{
    async Task RunPropertyAnimationChecksAndQuit()
    {
        try { await RunPropertyAnimationChecks(); GetTree().Quit(0); }
        catch (Exception error) { GD.PushError("PROPERTY_ANIMATION_FAILED " + error); GetTree().Quit(1); }
    }

    public async Task<int> RunPropertyAnimationChecks()
    {
        string folder = Path.GetFullPath(ProjectSettings.GlobalizePath("res://output/property-animation-checks/run-" + Guid.NewGuid().ToString("N")[..10]));
        Directory.CreateDirectory(folder);
        var original = JsonSerializer.Deserialize<ProjectData>(JsonSerializer.Serialize(Snapshot(), _json))!;
        string originalPath = _projectPath; double originalTime = _time; bool playing = _playing, pilot = _pilot, auto = _autoKey, dirty = _projectDirty;
        string? selected = _selected?.Data.Id, selectedCamera = _selectedCamera?.Id;
        var undo = _undo.ToArray(); var redo = _redo.ToArray(); var editor = _editorCamera.Transform;
        var window = GetWindow(); var windowSize = window.Size; var windowMode = window.Mode; float windowScale = window.ContentScaleFactor;
        string workspaceMode = _animationWorkspaceMode; string search = _animationSearch?.Text ?? "";
        bool animatedOnly = _animationAnimatedOnly?.ButtonPressed == true, selectedOnly = _animationSelectedOnly?.ButtonPressed == true;
        bool passed = false; string? failure = null; int checks = 0; var observations = new List<object>(); RiggedActor? comparison = null;
        void Check(bool yes, string message) { Require(yes, "Property animation: " + message); checks++; observations.Add(new { message, passed = yes }); GD.Print("PASS Property animation: " + message); }
        static bool Near(double a, double b, double tolerance = .0001) => Math.Abs(a - b) <= tolerance;
        async Task Settle() { for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Screenshot(string name)
        {
            await Settle(); if (DisplayServer.GetName() == "headless") return;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage(); image.SavePng(Path.Combine(folder, name));
        }
        AnimationChannelBinding Binding(string id, string path) => AnimationBindings().Single(b => b.TargetId == id && b.Path == path);
        ScalarKeyData Key(string id, string path, double time, double value) => PropertyCurveRuntime.Upsert(Binding(id, path).EnsureCurve(), time, value);
        string PersistentState()
        {
            var data = Snapshot();
            return JsonSerializer.Serialize(new { data.Objects, data.Cameras, data.SceneSettings, data.PropertyAnimation, data.Fps, data.Duration }, _json);
        }
        void ExpandAll()
        {
            ShowAnimationExpandMenu(); _animationExpandMenu!.EmitSignal(PopupMenu.SignalName.IdPressed, 0L); _animationExpandMenu.Hide();
        }
        try
        {
            _ = PropertyCurveChecks.Run(Check);
            _playing = false; _autoKey = false; if (_pilot) TogglePilot(); StopPoseEditing(); ClearObjects(); ClearCameras();
            _projectPath = ""; _duration = 4; _fps = 24; _scrub.MaxValue = 4;
            window.Mode = Window.ModeEnum.Windowed; window.ContentScaleFactor = 1; window.Size = new Vector2I(1600, 1050);
            var camera = CreateCamera("Animation camera", null, true); camera.Rig.ClearKeys(); camera.Rig.SetPose(new Vector3(7, 4, 8), new Vector3(2, 1, 0)); SyncCameraFromRig(camera);
            var box = AddObject("box", new Vector3(1, 2, 3), "Property box"); string boxId = box.Data.Id, cameraId = camera.Id;
            var light = AddObject("point_light", new Vector3(0, 4, 3), "Animated point light"); string lightId = light.Data.Id;
            Key(boxId, "Transform.Position.X", 0, 1); var xEnd = Key(boxId, "Transform.Position.X", 4, 9);
            Key(boxId, "Transform.Position.Y", 0, 2); Key(boxId, "Transform.Position.Y", 2, 6);
            Key(boxId, "Transform.Scale.X", 0, 1); Key(boxId, "Transform.Scale.X", 2, 2);
            Key(boxId, "Transform.Rotation.Y", 0, 0); Key(boxId, "Transform.Rotation.Y", 4, 720);
            Key(boxId, "Color.R", 0, .1); Key(boxId, "Color.R", 2, .9);
            Key(lightId, "Light.Energy", 0, 0); Key(lightId, "Light.Energy", 4, 4);
            Key(lightId, "Light.Color.R", 0, .1); Key(lightId, "Light.Color.R", 4, .9);
            Key(cameraId, "Lens", 0, 28); Key(cameraId, "Lens", 4, 56);
            Key("scene", "Scene.Brightness", 0, .25); Key("scene", "Scene.Brightness", 4, 1.25);
            SetTime(1);
            Check(box.Node.Position.DistanceTo(new Vector3(3, 4, 3)) < .0001f, "independent position axes evaluate at their own key times while unkeyed Z retains its base");
            Check(Near(box.Node.Scale.X, 1.5) && Near(box.Node.Scale.Y, 1) && Near(box.Node.Scale.Z, 1), "scale keys affect only their own scalar axis");
            Check(Near(AnimatedObjectManual(box, "Rotation", 1).Y, 180) && Near(AnimatedObjectManual(box, "Rotation", 2).Y, 360), "authored Euler rotation keeps both revolutions instead of shortest-arc folding");
            Check(box.Node.Basis.Z.Dot(Vector3.Back) < -.999f, "the evaluated scalar rotation reaches the actual scene node");
            Check(Near(light.Node.GetNode<Light3D>("Light").LightEnergy, 1) && Near(light.Node.GetNode<Light3D>("Light").LightColor.R, .3), "animated light intensity and color update the real engine light");
            Check(Near(camera.Rig.FocalLengthMm, 35), "the camera lens samples the shared scalar animation system");
            Check(Near(_lighting.Brightness, .5), "scene brightness updates the actual lighting at scene time");
            Check(box.Data.Position.SequenceEqual(new float[] { 1, 2, 3 }) && box.Data.Appearance.ColorOverride == null && Near(light.Data.Light!.Energy, 1), "sampling restores authored base values rather than baking animated values into the document");
            var bindings = AnimationBindings();
            Check(bindings.Any(b => b.TargetId == boxId && b.Path == "Visible" && b.Discrete) && bindings.Any(b => b.TargetId == cameraId && b.Path == "Tracking.FollowSmoothness"), "the registry includes held visibility and camera-controller numeric parameters");

            var person = AddObject("actor", new Vector3(-2, 0, 0), "Walking actor"); string personId = person.Data.Id;
            string motion = MotionCatalog.MaterializeSource(MotionCatalog.Find("human-walk-gesture")!);
            LoadMotion(person, motion, .01f); var clip = person.Data.MotionClips[0]; clip.Duration = 4; clip.Loop = true;
            var rate = clip.PropertyAnimation.Channels["Speed"] = new(); PropertyCurveRuntime.Upsert(rate, 0, 1); PropertyCurveRuntime.Upsert(rate, 2, 3);
            person.Data.Path = new() { Enabled = true, Duration = 4, OrientToPath = true, Points = new() { new float[] { -2, 0, 0 }, new float[] { 2, 0, 0 } }, PointTimes = new() { 0, 4 } };
            RebuildTrack(person); Key(personId, "Transform.Position.Y", 0, 0); Key(personId, "Transform.Position.Y", 4, .4);
            comparison = new RiggedActor { Visible = false }; _world.AddChild(comparison);
            comparison.SetMotionClips(new[] { new MotionClipData { MotionPath = motion, Name = "Reference source clock", SourceScale = .01f, Duration = 4, Speed = 1, Loop = true } });
            SetTime(1); comparison.EvaluateTimeline(1.5, true);
            var actualSkeleton = person.Node.GetNode<Skeleton3D>("PreparedHumanoid"); var expectedSkeleton = comparison.GetNode<Skeleton3D>("PreparedHumanoid");
            Check(Enumerable.Range(0, actualSkeleton.GetBoneCount()).All(i => Math.Abs(actualSkeleton.GetBonePoseRotation(i).Dot(expectedSkeleton.GetBonePoseRotation(i))) > .99999f), "the real BVH skeleton uses integrated playback rate rather than time times the instantaneous rate");
            Check(Near(person.Node.Position.Y, .1) && Near(((RiggedActor)person.Node).RootMotionOffset.X, 0) && Near(((RiggedActor)person.Node).RootMotionOffset.Z, 0), "manual scalar offsets remain above the path while native horizontal source travel is suppressed");
            Check(AnimationBindings().Any(b => b.TargetId == personId && b.Path.StartsWith("Pose.", StringComparison.Ordinal)), "body correction channels are discoverable on the same animation hierarchy");
            comparison.QueueFree(); comparison = null;

            SetTime(1); Select(light); int editHistory = _undo.Count;
            ChangeSceneLight(data => data.Energy = 5); SetTime(1);
            Check(Binding(lightId,"Light.Energy").Curve()!.Keys.Any(k=>Near(k.Time,1)&&Near(k.Value,5))&&Near(light.Node.GetNode<Light3D>("Light").LightEnergy,5)
                &&Near(light.Data.Light!.Energy,1)&&_undo.Count==editHistory+1,"the original light inspector callback keys an existing animated intensity and retains its authored base in one Undo");
            Undo(); box=_entries.Single(e=>e.Data.Id==boxId);light=_entries.Single(e=>e.Data.Id==lightId);person=_entries.Single(e=>e.Data.Id==personId);camera=_cameras.Single(c=>c.Id==cameraId);
            Check(!Binding(lightId,"Light.Energy").Curve()!.Keys.Any(k=>Near(k.Time,1)),"Undo of the original numeric light edit removes its newly authored property key");
            Key(cameraId,"Tracking.FollowOffset.Z",0,6);Key(cameraId,"Tracking.FollowOffset.Z",4,10);SelectCamera(camera);SetTime(1);editHistory=_undo.Count;
            EditCameraTracking(data=>{data.FollowEnabled=true;data.FollowTargetId=boxId;data.FollowOffset[2]=9;});
            Vector3 expectedFollow;using(EvaluatePropertyState(1,false))expectedFollow=EvaluateTrackedCameraPose(camera,1).Position;
            Check(Binding(cameraId,"Tracking.FollowOffset.Z").Curve()!.Keys.Any(k=>Near(k.Time,1)&&Near(k.Value,9))
                &&Near(camera.Tracking.FollowOffset[2],6)&&camera.Rig.GlobalPosition.DistanceTo(expectedFollow)<.0001f&&_undo.Count==editHistory+1,
                "the original Follow inspector callback keys its animated distance and evaluates the live camera without replacing the base offset");
            Undo();box=_entries.Single(e=>e.Data.Id==boxId);light=_entries.Single(e=>e.Data.Id==lightId);person=_entries.Single(e=>e.Data.Id==personId);camera=_cameras.Single(c=>c.Id==cameraId);
            SetTime(1);editHistory=_undo.Count;ChangeSceneSetting(data=>data.Brightness=.9f);SetTime(1);
            Check(Binding("scene","Scene.Brightness").Curve()!.Keys.Any(k=>Near(k.Time,1)&&Near(k.Value,.9))&&Near(_lighting.Brightness,.9)
                &&Near(_sceneSettings.Brightness,1)&&_undo.Count==editHistory+1,"the original scene-brightness control records exactly one Undo and updates the keyed frame rather than changing the base");
            Undo();box=_entries.Single(e=>e.Data.Id==boxId);light=_entries.Single(e=>e.Data.Id==lightId);person=_entries.Single(e=>e.Data.Id==personId);camera=_cameras.Single(c=>c.Id==cameraId);

            foreach(string axis in new[]{"X","Y","Z"}){Key(personId,"Pose.Bones.RightUpperArm.Rotation."+axis,0,0);Key(personId,"Pose.Bones.RightUpperArm.Rotation."+axis,4,0);}
            SetTime(1);Select(person);StartPoseEditing();SelectPoseBone("RightHand");var posedActor=(RiggedActor)person.Node;
            var shoulder=posedActor.GetPoseJointPosition("RightUpperArm");var wrist=posedActor.GetPoseJointPosition("RightHand");
            var handTarget=posedActor.GlobalTransform*(shoulder+(wrist-shoulder)*.93f+new Vector3(0,.01f,-.01f));
            var actorTransform=person.Node.Transform;editHistory=_undo.Count;BeginPoseGesture();UpdatePoseEffectorWorld("RightHand",handTarget);EndPoseGesture();
            Check((posedActor.GlobalTransform*posedActor.GetPoseJointPosition("RightHand")).DistanceTo(handTarget)<.006f
                &&new[]{"X","Y","Z"}.All(axis=>Binding(personId,"Pose.Bones.RightUpperArm.Rotation."+axis).Curve()!.Keys.Any(k=>Near(k.Time,1))),
                "IK after splitting a joint into XYZ updates those same scalar curves and reaches the edited hand target");
            Check(person.Node.Transform.IsEqualApprox(actorTransform)&&person.Data.MotionClips.Count==1&&_undo.Count==editHistory+1,
                "split-axis IK remains additive above mocap and preserves object travel in a single gesture");StopPoseEditing();

            string document = PersistentState(), dirtyHash = ProjectContentHash(); MarkProjectClean();
            foreach (double t in new[] { 0.0, 3.5, .5, 2.0, 1.0 }) SetTime(t);
            RefreshProjectDirtyState();
            Check(PersistentState() == document && ProjectContentHash() == dirtyHash && !_projectDirty, "forward and reverse scrubbing do not mutate the saved document or mark it dirty");

            Select(box); _animationSelectedOnly!.ButtonPressed = true; _animationAnimatedOnly!.ButtonPressed = true; _animationSearch!.Text = "Property box";
            SetAnimationWorkspaceMode("Dope sheet"); ExpandAll(); await Settle();
            var bx = Binding(boxId, "Transform.Position.X"); var by = Binding(boxId, "Transform.Position.Y");
            Check(_animationDopePanel!.Visible && !_animationGraphPanel!.Visible && _animationRows.Any(r => !r.IsGroup && r.Channels.Contains(bx.Identity)) && _animationRows.Any(r => !r.IsGroup && r.Channels.Contains(by.Identity)), "the real dope sheet exposes independent animated XYZ property rows");
            Check(!_animationRows.Any(r => !r.IsGroup && r.Channels.Contains(Binding(boxId, "Transform.Position.Z").Identity)), "Animated only hides unkeyed axes");
            _animationSearch.Text = ""; int expandedRows = AnimationWorkspaceVisibleRowCount;
            ShowAnimationExpandMenu(); _animationExpandMenu!.EmitSignal(PopupMenu.SignalName.IdPressed, 2L); _animationExpandMenu.Hide(); await Settle();
            Check(AnimationWorkspaceVisibleRowCount < expandedRows, "the Expand menu's Collapse all action collapses the animation hierarchy");
            ExpandAll(); _animationSearch.Text = "Property box Transform Position"; await Settle();
            bx = Binding(boxId, "Transform.Position.X");
            var endRect = _animationDope!.KeyRect(bx.Identity, xEnd.Id); var startRect = _animationDope.KeyRect(bx.Identity, bx.Curve()!.Keys[0].Id);
            Check(endRect.Size.X > 0 && startRect.Size.X > 0, "visible independent property keys have pickable dope-sheet geometry");
            int history = _undo.Count; var down = endRect.GetCenter(); float pixelsPerSecond = (endRect.Position.X - startRect.Position.X) / 4;
            var moved = down - Vector2.Right * pixelsPerSecond;
            _animationDope._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = down });
            _animationDope._Input(new InputEventMouseMotion { Position = _animationDope.GetGlobalTransformWithCanvas() * moved, Relative = moved - down });
            _animationDope._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            Check(Near(Binding(boxId, "Transform.Position.X").Curve()!.Keys.Single(k => k.Id == xEnd.Id).Time, 3) && Binding(boxId, "Transform.Position.Y").Curve()!.Keys.Any(k => Near(k.Time, 2)), "actual dope-sheet mouse dragging retimes only the selected X key");
            Check(_undo.Count == history + 1, "one continuous dope-sheet drag creates one Undo item");
            Undo(); Check(Near(Binding(boxId, "Transform.Position.X").Curve()!.Keys.Single(k => k.Id == xEnd.Id).Time, 4), "Undo restores the independent key's original time");
            Redo(); Check(Near(Binding(boxId, "Transform.Position.X").Curve()!.Keys.Single(k => k.Id == xEnd.Id).Time, 3), "Redo reapplies the independent retime"); Undo();

            box = _entries.Single(e => e.Data.Id == boxId); Select(box); _animationSearch.Text = "Property box";
            _animationSelectedChannels.Clear(); _animationSelectedChannels.Add(Binding(boxId, "Transform.Position.X").Identity);
            SetAnimationWorkspaceMode("Graph"); await Settle(); _animationGraph!.FitCurves();
            bx = Binding(boxId, "Transform.Position.X"); var endKey = bx.Curve()!.Keys.Single(k => k.Id == xEnd.Id);
            double valueBefore = endKey.Value; history = _undo.Count;
            var point = _animationGraph.KeyScreenPoint(bx.Identity, xEnd.Id);
            _animationGraph._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point });
            _animationGraph._Input(new InputEventMouseMotion { Position = _animationGraph.GetGlobalTransformWithCanvas() * (point + new Vector2(0, -18)), Relative = new Vector2(0, -18) });
            _animationGraph._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            Check(Binding(boxId, "Transform.Position.X").Curve()!.Keys.Single(k => k.Id == xEnd.Id).Value > valueBefore && _undo.Count == history + 1, "dragging a real value-graph key changes the serialized value in one gesture");
            _animationGraphKind!.Select(1); _animationGraphKind.EmitSignal(OptionButton.SignalName.ItemSelected, 1L); await Settle();
            Check(_animationGraph.SpeedMode, "the graph toolbar switches to the signed speed graph");
            point = _animationGraph.KeyScreenPoint(bx.Identity, xEnd.Id);
            _animationGraph._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point });
            _animationGraph._Input(new InputEventMouseMotion { Position = _animationGraph.GetGlobalTransformWithCanvas() * (point + new Vector2(0, -14)), Relative = new Vector2(0, -14) });
            _animationGraph._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            Check(Binding(boxId, "Transform.Position.X").Curve()!.Keys.First().Interpolation == "Bezier", "speed-graph editing changes underlying interpolation handles instead of only its display");
            await Screenshot("01-speed-graph.png");
            int yKeys = Binding(boxId, "Transform.Position.Y").Curve()!.Keys.Count;
            _animationGraph._GuiInput(new InputEventKey { Keycode = Godot.Key.Delete, Pressed = true });
            Check(!Binding(boxId, "Transform.Position.X").Curve()!.Keys.Any(k => k.Id == xEnd.Id) && Binding(boxId, "Transform.Position.Y").Curve()!.Keys.Count == yKeys && _entries.Any(e => e.Data.Id == boxId), "graph Delete removes the selected key and preserves other axes and the scene object");
            Undo(); Check(Binding(boxId, "Transform.Position.X").Curve()!.Keys.Any(k => k.Id == xEnd.Id), "Undo restores a graph-deleted key with its identity");
            camera=_cameras.Single(c=>c.Id==cameraId);camera.LensKeys=new(){new(){Time=0,Value=20},new(){Time=4,Value=90}};
            SelectCamera(camera);_animationSearch.Text="Animation camera";_animationSelectedChannels.Clear();_animationSelectedChannels.Add(Binding(cameraId,"Lens").Identity);RefreshAnimationWorkspace();
            _animationGraphKind.Select(0);_animationGraphKind.EmitSignal(OptionButton.SignalName.ItemSelected,0L);await Settle();_animationGraph.FitCurves();
            var lensBinding=Binding(cameraId,"Lens");var lensKeys=lensBinding.Curve()!.Keys.ToArray();
            foreach(var lensKey in lensKeys)
            {
                point=_animationGraph.KeyScreenPoint(lensBinding.Identity,lensKey.Id);
                _animationGraph._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=point,CtrlPressed=lensKey!=lensKeys[0]});
                _animationGraph._Input(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false});
            }
            Check(_animationGraph.SelectedKeyCount==lensKeys.Length,"the lens graph supports additive selection of all authored keys");
            _animationGraph._GuiInput(new InputEventKey{Keycode=Godot.Key.Delete,Pressed=true});SetTime(2);
            Check(Binding(cameraId,"Lens").Curve()!.Keys.Count==0&&Near(_cameras.Single(c=>c.Id==cameraId).Rig.FocalLengthMm,camera.BaseLens),
                "deleting all lens keys holds the authored base lens and does not reactivate suppressed legacy lens keys");
            Undo();camera=_cameras.Single(c=>c.Id==cameraId);SetTime(1);
            Check(Binding(cameraId,"Lens").Curve()!.Keys.Count==lensKeys.Length&&Near(camera.Rig.FocalLengthMm,35),"Undo restores the deleted lens channel's actual animated focal length");
            box=_entries.Single(e=>e.Data.Id==boxId);Select(box);_animationSearch.Text="Property box";
            SetAnimationWorkspaceMode("Dope sheet"); ExpandAll(); await Screenshot("02-independent-axis-dope-sheet.png");

            RunExtendedPropertyChecks(Check);
            await RunAnimationLayoutChecks(folder,Check);
            string saved = Path.Combine(folder, "Animated properties.goprevis"); SaveTo(saved);
            var stored = JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(saved))!;
            Check(stored.Version == 14 && stored.Objects.Single(o => o.Id == boxId).PropertyAnimation.Channels.ContainsKey("Transform.Position.X") && stored.PropertyAnimation.Channels.ContainsKey("Scene.Brightness"), "schema 14 saves independent object and scene channels");
            LoadProjectFromPath(saved); SetTime(1);
            Check(Near(_lighting.Brightness, .5) && Near(_cameras.Single(c => c.Id == cameraId).Rig.FocalLengthMm, 35), "save/reopen restores animated scene and camera evaluation");
            Check(_entries.Single(e => e.Data.Id == personId).Data.MotionClips[0].PropertyAnimation.Channels.ContainsKey("Speed"), "portable project save/reopen retains animated motion-rate channels");

            var legacy = JsonSerializer.Deserialize<ProjectData>(JsonSerializer.Serialize(Snapshot(), _json))!;
            legacy.Version = 13; var legacyBox = legacy.Objects.Single(o => o.Id == boxId); legacyBox.Position = new float[] { 1, 2, 3 };
            TransformLayerRuntime.Upsert(legacyBox.TransformAnimation.Position, 0, Vector3.Zero); TransformLayerRuntime.Upsert(legacyBox.TransformAnimation.Position, 2, new Vector3(4, 0, 0));
            var legacyJson = JsonNode.Parse(JsonSerializer.Serialize(legacy, _json))!;
            void RemoveNewProperties(JsonNode node)
            {
                if (node is JsonObject obj) { obj.Remove("PropertyAnimation"); obj.Remove("Visible"); foreach (var child in obj.ToArray()) if (child.Value != null) RemoveNewProperties(child.Value); }
                else if (node is JsonArray array) foreach (var child in array) if (child != null) RemoveNewProperties(child);
            }
            RemoveNewProperties(legacyJson); string legacyPath = Path.Combine(folder, "Legacy grouped axes.goprevis"); File.WriteAllText(legacyPath, legacyJson.ToJsonString(_json));
            LoadProjectFromPath(legacyPath); SetTime(1);
            Check(_entries.Single(e => e.Data.Id == boxId).Node.Position.DistanceTo(new Vector3(3, 2, 3)) < .0001f, "an actual schema-13 file without new fields retains its grouped transform animation");
            LoadProjectFromPath(saved); SetTime(1);
            if (DisplayServer.GetName() != "headless")
            {
                var export = await ExportShotAsync(new() { OutputDirectory = Path.Combine(folder, "export"), Width = 128, Height = 128, Fps = 1, EndSeconds = 2, IncludeColor = true, IncludeDepth = true, IncludePose = true, ExportMp4 = false, ExportPngSequence = true });
                Check(export.FrameCount == 2 && File.Exists(Path.Combine(export.Directory, "depth", "000001.png")) && File.Exists(Path.Combine(export.Directory, "pose", "000001.json")), "actual whole-shot export evaluates scalar properties through synchronized color/depth/pose passes");
                using var first = Image.LoadFromFile(Path.Combine(export.Directory, "color", "000000.png")); using var second = Image.LoadFromFile(Path.Combine(export.Directory, "color", "000001.png"));
                Check(!first.GetData().SequenceEqual(second.GetData()), "animated transforms, materials and lighting produce different rendered frames");
            }
            passed = true; GD.Print("PROPERTY_ANIMATION_OK checks=" + checks + " output=" + folder); return checks;
        }
        catch (Exception error) { failure = error.ToString(); throw; }
        finally
        {
            comparison?.QueueFree(); if (_pilot) TogglePilot(); Restore(original, ""); _projectPath = originalPath; SetTime(originalTime); _editorCamera.Transform = editor;
            if (selected != null) Select(_entries.FirstOrDefault(e => e.Data.Id == selected)); else if (selectedCamera != null && _cameras.FirstOrDefault(c => c.Id == selectedCamera) is { } c) SelectCamera(c);
            if (pilot && !_pilot) TogglePilot(); _playing = playing; _autoKey = auto; SetProjectDirty(dirty);
            _undo.Clear(); _redo.Clear(); foreach (var item in undo.Reverse()) _undo.Push(item); foreach (var item in redo.Reverse()) _redo.Push(item);
            if (_animationSearch != null) _animationSearch.Text = search; if (_animationAnimatedOnly != null) _animationAnimatedOnly.ButtonPressed = animatedOnly; if (_animationSelectedOnly != null) _animationSelectedOnly.ButtonPressed = selectedOnly; SetAnimationWorkspaceMode(workspaceMode);
            window.Mode = windowMode; window.ContentScaleFactor = windowScale; window.Size = windowSize;
            File.WriteAllText(Path.Combine(folder, "report.json"), JsonSerializer.Serialize(new { status = passed ? "passed" : "failed", checks, failure, observations }, _json));
        }
    }
}
