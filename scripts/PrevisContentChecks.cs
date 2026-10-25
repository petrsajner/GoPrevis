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
    /// <summary>Exercises the actual library drop, animation, layering and portable-save paths.</summary>
    public async Task RunContentLibraryChecks()
    {
        var original = Snapshot();
        string originalPath = _projectPath;
        double originalTime = _time;
        bool wasPlaying = _playing;
        var undo = _undo.ToArray();
        var redo = _redo.ToArray();
        int checks = 0;
        int modelCount = 0, motionCount = 0, animalModelsLoaded = 0;
        bool completed = false;
        string? failure = null;
        var observations = new List<object>();
        var boundsComparisons = new List<object>();
        void Check(bool valid, string message)
        {
            observations.Add(new { check=message, passed=valid });
            Require(valid, "Content library: " + message);
            checks++;
        }
        try
        {
            _playing = false;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            modelCount = ContentCatalog.All.Count;
            motionCount = MotionCatalog.All.Count;
            Check(ContentCatalog.All.Count == 284, "284 usable model descriptors are available");
            Check(MotionCatalog.All.Count == 457, "457 motion descriptors are available");
            Check(ContentCatalog.All.Select(m => m.Id).Distinct().Count() == 284 && MotionCatalog.All.Select(m => m.Id).Distinct().Count() == 457, "all model and motion IDs are unique");
            Check(ContentCatalog.ForCategory("People").Count() == 6 && ContentCatalog.ForCategory("Animals").Count() == 13, "six human variants and thirteen animal rigs are available");
            Check(PropFactory.Kinds.All(kind => ContentCatalog.Find(kind)?.BuiltinKind == kind), "all original blockout shapes remain placeable");
            Check(MotionCatalog.All.Count(m => m.Id.StartsWith("cmu-", StringComparison.Ordinal)) == 300, "300 recorded CMU takes retain their identities");
            Check(ContentCatalog.All.Where(m => !m.IsBuiltin).All(m => m.Scale > 0 && !string.IsNullOrEmpty(m.License) && !string.IsNullOrEmpty(m.SourceUrl)), "external model scale and provenance are present");
            Check(MotionCatalog.All.All(m => m.Duration > 0 && m.SourceScale > 0 && !string.IsNullOrEmpty(m.License)), "all motions have positive timing, units and licensing");

            ClearObjects();
            SetTime(0);
            string[] props = { "kenney-furniture-chair", "kenney-cars-sedan", "kenney-buildings-building-sample-house-a", "kenney-nature-tree-default", "kenney-furniture-laptop" };
            for (int i = 0; i < props.Length; i++)
            {
                var model = ContentCatalog.Find(props[i])!;
                DropAsset(model.Id, new Vector3(i * 3, 0, -5));
                var entry = _selected!;
                var bounds = PropFactory.CalculateBounds(entry.Node);
                Check(entry.Data.LibraryModelId == model.Id && entry.Data.ModelGrounded && Math.Abs(entry.Data.ModelUnitScale - model.Scale) < .00001, model.Name + " drop preserves source units");
                Check(Descendants(entry.Node).OfType<MeshInstance3D>().Any(m => m.Mesh != null) && bounds.Size.Length() > .05f && Math.Abs(bounds.Position.Y) < .005f, model.Name + " has visible geometry resting on the ground");
            }
            var chair = ContentCatalog.Find(props[0])!;
            string cached = ContentCatalog.MaterializeSource(chair);
            var written = File.GetLastWriteTimeUtc(cached);
            Check(ContentCatalog.MaterializeSource(chair) == cached && File.GetLastWriteTimeUtc(cached) == written && Path.GetExtension(cached) == ".glb", "raw GLB materialization reuses its cache and original extension");

            var humans = new List<Entry>();
            foreach (var model in ContentCatalog.ForCategory("People"))
            {
                DropAsset(model.Id, new Vector3(humans.Count * 1.5f, 0, 0));
                var entry = _selected!;
                humans.Add(entry);
                Check(entry.Node is RiggedActor && entry.Data.LibraryModelId == model.Id && entry.Node.Scale.IsEqualApprox(Vector(model.BodyScale, Vector3.One)), model.Name + " creates the prepared body proportions");
            }
            var human = humans.First();
            human.Data.MotionClips.Clear();
            RebuildTrack(human);
            int beforeHumanDrop = _entries.Count;
            DropMotion("cmu-02-01", human, human.Node.Position);
            Check(_entries.Count == beforeHumanDrop && human.Data.MotionClips.Count == 1 && human.Data.MotionClips[0].SourceOffset > 0 && !human.Data.MotionClips[0].Loop, "recorded motion drops onto an existing human and skips the calibration frame");
            SetTime(.25);
            var humanPose = CaptureLibraryBonePose(human);
            SetTime(1.25);
            Check(LibraryPoseChanged(humanPose, CaptureLibraryBonePose(human)), "recorded human take changes the retargeted bone pose");
            SetTime(0);
            DropMotion("cmu-02-02", null, new Vector3(-3, 0, 1));
            Check(_entries.Count == beforeHumanDrop + 1 && _selected?.Node is RiggedActor && _selected.Data.ActorFamily == "human", "empty-ground human motion drop creates a matching prepared actor");
            DropMotion("cmu-02-02", human, human.Node.Position);
            Check(human.Data.MotionClips.Count == 2 && human.Data.MotionClips[1].BlendIn > 0 && human.Data.MotionClips[1].Start < human.Data.MotionClips[0].Start + human.Data.MotionClips[0].Duration, "recorded human clips sequence with a real overlap");

            var animalEntries = new List<Entry>();
            foreach (var model in ContentCatalog.All.Where(m => m.Id.StartsWith("animal-", StringComparison.Ordinal)))
            {
                SetTime(0);
                DropAsset(model.Id, new Vector3(animalEntries.Count * 1.8f, 0, 4));
                var entry = _selected!;
                animalEntries.Add(entry);
                Check(entry.NativeTrack != null && entry.Player != null && entry.Data.ActorFamily == model.ActorFamily, model.Name + " imports its real rig and native animations");
                animalModelsLoaded++;
                entry.Data.MotionClips.Clear();
                RebuildTrack(entry);
                var motion = MotionCatalog.ForFamily(model.ActorFamily).First(m => (m.SourceAnimation ?? "").Contains("Walk", StringComparison.OrdinalIgnoreCase));
                int before = _entries.Count;
                DropMotion(motion.Id, entry, entry.Node.Position);
                Check(_entries.Count == before && entry.Data.MotionClips.Count == 1 && entry.Data.MotionClips[0].SourceAnimation == motion.SourceAnimation, model.Name + " motion attaches to the same matching actor");
                SetTime(motion.Duration * .15);
                var pose = CaptureLibraryBonePose(entry);
                SetTime(motion.Duration * .55);
                Check(LibraryPoseChanged(pose, CaptureLibraryBonePose(entry)), model.Name + " native bones animate between source samples");
                int humanClips = human.Data.MotionClips.Count;
                DropMotion(motion.Id, human, human.Node.Position);
                Check(_entries.Count == before && human.Data.MotionClips.Count == humanClips, model.Name + " motion rejects an incompatible human rig");
                SetTime(0);
                DropMotion(motion.Id, null, new Vector3(-4, 0, 4));
                var spawned = _selected!;
                Check(_entries.Count == before + 1 && spawned.Data.LibraryModelId == model.Id && spawned.Data.ActorFamily == model.ActorFamily && spawned.NativeTrack != null, model.Name + " empty-ground motion drop creates its matching animal");
                Check(spawned.Data.ModelPath == entry.Data.ModelPath, model.Name + " clips share the cached source model");
                _entries.Remove(spawned);
                _world.RemoveChild(spawned.Node);
                spawned.Node.QueueFree();
                Select(entry);
                RefreshObjects();
            }
            Check(animalEntries.Count == 12, "every new animal species completed the placement and motion workflow");

            var animal = animalEntries.First();
            animal.Data.MotionClips[0].Duration = 3;
            RebuildTrack(animal);
            var run = MotionCatalog.ForFamily(animal.Data.ActorFamily).First(m => m.SourceAnimation == "Gallop");
            DropMotion(run.Id, animal, animal.Node.Position);
            Check(animal.Data.MotionClips.Count == 2 && animal.NativeTrack!.ClipCount == 2 && animal.Data.MotionClips[1].BlendIn > 0, "new animal clips blend on the actual native track");
            animal.Data.Path = new ActorPathData { Enabled=true, Duration=8, OrientToPath=true, Points=new() { new float[]{-2,0,2}, new float[]{0,0,3}, new float[]{3,0,1} } };
            animal.Data.PathOffset = new float[] { .2f, .1f, 0 };
            animal.Data.Rotation = new float[] { 0, 25, 0 };
            animal.Data.Scale = new float[] { 1.2f, 1.2f, 1.2f };
            TransformLayerRuntime.Upsert(animal.Data.TransformAnimation.Rotation, 0, new Vector3(0, 10, 0));
            TransformLayerRuntime.Upsert(animal.Data.TransformAnimation.Rotation, 8, new Vector3(0, 50, 0));
            TransformLayerRuntime.Upsert(animal.Data.TransformAnimation.Position, 0, Vector3.Zero);
            TransformLayerRuntime.Upsert(animal.Data.TransformAnimation.Position, 8, new Vector3(.4f, .2f, 0));
            var overlap = animal.Data.MotionClips[1];
            for (int i = 0; i <= 4; i++)
            {
                double time = overlap.Start + overlap.BlendIn * i / 4;
                SetTime(time);
                var path = ActorPathRuntime.Evaluate(animal.Data.Path, time);
                var expectedPosition = path.Position + Vector(animal.Data.PathOffset) + new Vector3(.4f, .2f, 0) * (float)(time / 8);
                var facing = animal.Node.Basis * animal.NativeTrack!.AnimatedForward;
                facing.Y = 0;
                var expectedFacing = new Basis(Vector3.Up, Mathf.DegToRad(35 + 40 * (float)(time / 8))) * path.Tangent;
                expectedFacing.Y = 0;
                Check(animal.Node.Position.DistanceTo(expectedPosition) < .0001f && facing.Normalized().Dot(expectedFacing.Normalized()) > .998f && animal.Node.Scale.IsEqualApprox(Vector3.One * 1.2f), "path and manual corrections remain above animal crossfade sample " + i);
            }
            double roundtripTime = overlap.Start + overlap.BlendIn * .5;
            SetTime(roundtripTime);
            var finalTransform = animal.Node.Transform;
            SetTime(0);
            SetTime(roundtripTime);
            Check(animal.Node.Transform.IsEqualApprox(finalTransform), "crossfade, path and manual transforms seek deterministically");

            // BoneAttachment3D updates its visual child transforms after skeleton
            // evaluation. Compare both sides at the same settled frame, rather
            // than comparing freshly sampled bones with last-frame attachments.
            for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var expected = _entries.ToDictionary(e => e.Data.Id, e => (e.Data.LibraryModelId, e.Data.ModelUnitScale, e.Data.ModelGrounded,
                Scale: Vector(e.Data.Scale, Vector3.One), Bounds: PropFactory.CalculateBounds(e.Node).Size,
                OuterTransform: e.Node.Transform, BonePose: CaptureLibraryBonePose(e)));
            string directory = ProjectSettings.GlobalizePath("user://content-checks");
            Directory.CreateDirectory(directory);
            string project = Path.Combine(directory, "library-roundtrip.previs");
            SaveTo(project);
            var saved = JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(project))!;
            Check(saved.Version == 9 && saved.Objects.Count == expected.Count, "content saves without changing project format 9");
            LoadProjectFromPath(project);
            SetTime(roundtripTime);
            for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(_entries.Count == expected.Count, "all placed content reopens from the portable project");
            foreach (var entry in _entries)
            {
                var before = expected[entry.Data.Id];
                Check(entry.Data.LibraryModelId == before.LibraryModelId && Math.Abs(entry.Data.ModelUnitScale - before.ModelUnitScale) < .00001 && entry.Data.ModelGrounded == before.ModelGrounded && Vector(entry.Data.Scale, Vector3.One).IsEqualApprox(before.Scale), entry.Data.Name + " retains identity, source units, grounding and user scale after reopen");
                var actualBounds = PropFactory.CalculateBounds(entry.Node).Size;
                boundsComparisons.Add(new { id=entry.Data.Id, model=entry.Data.LibraryModelId, name=entry.Data.Name,
                    expected=Array3(before.Bounds), actual=Array3(actualBounds), deltaMetres=actualBounds.DistanceTo(before.Bounds) });
                Check(actualBounds.DistanceTo(before.Bounds) < .005f, entry.Data.Name + " visual dimensions survive save/reopen");
                Check(entry.Node.Transform.IsEqualApprox(before.OuterTransform), entry.Data.Name + " final outer transform survives save/reopen");
                Check(LibraryPoseEqual(before.BonePose, CaptureLibraryBonePose(entry)), entry.Data.Name + " source bone pose survives save/reopen");
                if (entry.Data.ModelPath != null) Check(File.Exists(entry.Data.ModelPath), entry.Data.Name + " portable source file exists");
            }
            var reopenedAnimal = _entries.First(e => e.Data.Id == animal.Data.Id);
            Check(reopenedAnimal.Node.Transform.IsEqualApprox(finalTransform) && reopenedAnimal.Data.MotionClips.Count == 2 && reopenedAnimal.Data.Path.Enabled, "animal crossfade and final path/manual layer survive portable save/reopen");
            completed = true;
            GD.Print($"CONTENT_LIBRARY_OK models={ContentCatalog.All.Count} motions={MotionCatalog.All.Count} animals={animalEntries.Count} checks={checks}");
        }
        catch (Exception error)
        {
            failure = error.ToString();
            throw;
        }
        finally
        {
            string reportPath = ProjectSettings.GlobalizePath("res://output/content-workflow-report.json");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                File.WriteAllText(reportPath, JsonSerializer.Serialize(new
                {
                    status=completed ? "passed" : "failed", timestampUtc=DateTime.UtcNow,
                    modelCount, motionCount, animalModelsLoaded, checksPassed=checks,
                    failure, observations, boundsComparisons
                }, new JsonSerializerOptions { WriteIndented=true }));
                GD.Print("CONTENT_WORKFLOW_REPORT " + reportPath);
            }
            catch (IOException error) { GD.PushWarning("Could not write content workflow report: " + error.Message); }
            Restore(original, "");
            _projectPath = originalPath;
            SetTime(originalTime);
            _playing = wasPlaying;
            _undo.Clear();
            _redo.Clear();
            foreach (var item in undo.Reverse()) _undo.Push(item);
            foreach (var item in redo.Reverse()) _redo.Push(item);
        }
    }

    static Transform3D[] CaptureLibraryBonePose(Entry entry) => Descendants(entry.Node).OfType<Skeleton3D>()
        .SelectMany(skeleton => Enumerable.Range(0, skeleton.GetBoneCount()).Select(skeleton.GetBonePose)).ToArray();

    static bool LibraryPoseChanged(Transform3D[] before, Transform3D[] after) => before.Length > 0 && before.Length == after.Length
        && before.Zip(after).Any(pair => !pair.First.IsEqualApprox(pair.Second));

    static bool LibraryPoseEqual(Transform3D[] before, Transform3D[] after) => before.Length == after.Length
        && before.Zip(after).All(pair => pair.First.IsEqualApprox(pair.Second));
}
