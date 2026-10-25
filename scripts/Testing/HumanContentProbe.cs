using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace GenPrevis;

/// <summary>
/// Validates every bundled CMU take with the application's strict BVH reader and
/// prepared humanoid track; optionally renders actual retargeted-pose thumbnails.
/// Run with --script res://scripts/Testing/HumanContentProbe.cs; a GPU display
/// driver is required for thumbnails. -- --skip-thumbnails supports headless CI.
/// </summary>
public partial class HumanContentProbe : SceneTree
{
    private sealed class ContentEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string SourcePath { get; set; } = "";
        public float SourceScale { get; set; }
        public double SourceOffset { get; set; }
        public double Duration { get; set; }
        public int FrameCount { get; set; }
        public int Channels { get; set; }
        public double FrameTime { get; set; }
    }

    private sealed class ClipResult
    {
        public string Id { get; set; } = "";
        public bool Passed { get; set; }
        public string? Error { get; set; }
        public int Frames { get; set; }
        public int Channels { get; set; }
        public int SourceAnimatedJoints { get; set; }
        public int SourceJointsIncludingEndSites { get; set; }
        public int PreparedBones { get; set; }
        public double SourceDuration { get; set; }
        public double CaptureDuration { get; set; }
        public double SourceOffset { get; set; }
        public int SampledPoses { get; set; }
        public int ChangedPreparedBones { get; set; }
        public bool Deterministic { get; set; }
        public bool InPlace { get; set; }
        public double ThumbnailTime { get; set; }
        public string? ThumbnailPath { get; set; }
        public int ThumbnailForegroundPixels { get; set; }
        public double ElapsedSeconds { get; set; }
    }

    public override async void _Initialize()
    {
        var clock = Stopwatch.StartNew();
        var results = new List<ClipResult>();
        string reportPath = ProjectSettings.GlobalizePath("res://output/content-human-validation.json");
        if (OS.GetCmdlineUserArgs().Contains("--extras-only"))
        {
            await RenderExtras(reportPath);
            return;
        }
        SubViewport? viewport = null;
        try
        {
            string[] arguments = OS.GetCmdlineUserArgs();
            bool thumbnails = !arguments.Contains("--skip-thumbnails");
            int limit = int.TryParse(arguments.FirstOrDefault(value => value.StartsWith("--limit="))?[8..], out int parsed) ? parsed : int.MaxValue;
            var entries = JsonSerializer.Deserialize<ContentEntry[]>(File.ReadAllText(ProjectSettings.GlobalizePath("res://assets/content/manifests/human-motions.json")))
                ?? throw new InvalidDataException("The human motion manifest is empty.");
            if (thumbnails && DisplayServer.GetName() == "headless")
                throw new InvalidOperationException("GPU thumbnails require a display driver. Use -- --skip-thumbnails for headless validation.");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            Directory.CreateDirectory(ProjectSettings.GlobalizePath("res://assets/content/thumbnails"));
            viewport = new SubViewport
            {
                Size = new Vector2I(256, 208), OwnWorld3D = true,
                RenderTargetUpdateMode = thumbnails ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled
            };
            Root.AddChild(viewport);
            var stage = new Node3D { Name = "NeutralHumanContentStage" };
            viewport.AddChild(stage);
            stage.AddChild(new WorldEnvironment { Environment = StudioLighting.CreateEnvironment() });
            var camera = new Camera3D { Current = true, Projection = Camera3D.ProjectionType.Orthogonal, Size = 2.4f };
            stage.AddChild(camera);

            foreach (ContentEntry entry in entries.Take(limit))
            {
                var timer = Stopwatch.StartNew();
                var result = new ClipResult { Id = entry.Id, CaptureDuration = entry.Duration, SourceOffset = entry.SourceOffset };
                RiggedActor? actor = null;
                try
                {
                    string path = ProjectSettings.GlobalizePath(entry.SourcePath);
                    // Explicit reader pass checks every declared source channel value;
                    // SetMotionClips independently uses the real application preparation.
                    BvhMotion source = BvhMotion.FromFile(path);
                    Require(source.FrameCount == entry.FrameCount && source.ChannelCount == entry.Channels && Math.Abs(source.FrameTime - entry.FrameTime) < 1e-10,
                        "Source frame metadata differs from the manifest.");
                    result.Frames = source.FrameCount;
                    result.Channels = source.ChannelCount;
                    result.SourceAnimatedJoints = source.AnimatedJointCount;
                    result.SourceJointsIncludingEndSites = source.Joints.Count;
                    result.SourceDuration = source.DurationSeconds;
                    Require(entry.SourceOffset >= source.FrameTime && entry.Duration > 0, "The converter calibration frame must be skipped.");
                    actor = new RiggedActor { Name = "ContentActor" };
                    stage.AddChild(actor);
                    actor.SetMotionClips(new[] { new MotionClipData
                    {
                        Name = entry.Name, MotionPath = path, SourceScale = entry.SourceScale,
                        SourceOffset = entry.SourceOffset, Duration = entry.Duration, InPlace = true,
                        Speed = 1, Loop = false, BlendIn = 0, Start = 0
                    } });
                    var skeleton = actor.GetNode<Skeleton3D>("PreparedHumanoid");
                    result.PreparedBones = skeleton.GetBoneCount();
                    Require(actor.MotionTrackClipCount == 1 && actor.MotionTrackPreparedSourceCount == 1 && result.PreparedBones >= 21, "The application did not prepare a complete humanoid track.");
                    Transform3D[]? first = null;
                    var moved = new HashSet<int>();
                    foreach (double fraction in new[] { 0.0, .19, .41, .67, .93, 1.0 })
                    {
                        actor.EvaluateTimeline(entry.Duration * fraction, true);
                        ValidatePose(skeleton, actor);
                        Transform3D[] pose = Capture(skeleton);
                        first ??= pose;
                        for (int bone = 0; bone < pose.Length; bone++)
                            if (!pose[bone].IsEqualApprox(first[bone])) moved.Add(bone);
                        result.SampledPoses++;
                    }
                    result.ChangedPreparedBones = moved.Count;
                    Require(moved.Count > 0, "Retargeted prepared bones do not move across the recorded take.");
                    double thumbnailTime = entry.Duration * .41;
                    actor.EvaluateTimeline(thumbnailTime, true);
                    Transform3D[] expected = Capture(skeleton);
                    Vector3 expectedRoot = actor.RootMotionOffset;
                    actor.EvaluateTimeline(entry.Duration * .82, true);
                    actor.EvaluateTimeline(0, true);
                    actor.EvaluateTimeline(thumbnailTime, true);
                    Require(expectedRoot.IsEqualApprox(actor.RootMotionOffset) && expected.Zip(Capture(skeleton)).All(pair => pair.First.IsEqualApprox(pair.Second)), "Random-access sampling changes the retargeted pose.");
                    result.Deterministic = true;
                    result.InPlace = Math.Abs(actor.RootMotionOffset.X) < .00001 && Math.Abs(actor.RootMotionOffset.Z) < .00001;
                    Require(result.InPlace, "In-place sampling retains horizontal root displacement.");
                    result.ThumbnailTime = thumbnailTime;
                    if (thumbnails)
                    {
                        await ToSignal(this, SignalName.ProcessFrame);
                        await ToSignal(this, SignalName.ProcessFrame);
                        FrameCamera(actor, camera);
                        viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        await ToSignal(this, SignalName.ProcessFrame);
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        using Image image = viewport.GetTexture().GetImage();
                        Require(image.GetWidth() == 256 && image.GetHeight() == 208, "GPU thumbnail readback is empty.");
                        int foreground = 0;
                        Color background = image.GetPixel(0, 0);
                        for (int y = 0; y < image.GetHeight(); y++)
                        for (int x = 0; x < image.GetWidth(); x++)
                        {
                            Color pixel = image.GetPixel(x, y);
                            if (Math.Abs(pixel.R - background.R) + Math.Abs(pixel.G - background.G) + Math.Abs(pixel.B - background.B) > .09) foreground++;
                        }
                        Require(foreground > 600 && foreground < image.GetWidth() * image.GetHeight() * .9, "Thumbnail is blank or its actor fills the whole image.");
                        string resourcePath = "res://assets/content/thumbnails/" + entry.Id + ".png";
                        Require(image.SavePng(ProjectSettings.GlobalizePath(resourcePath)) == Error.Ok, "Could not save the rendered thumbnail.");
                        result.ThumbnailPath = resourcePath;
                        result.ThumbnailForegroundPixels = foreground;
                    }
                    result.Passed = true;
                }
                catch (Exception exception)
                {
                    result.Error = exception.ToString();
                    GD.PushError("HUMAN_CONTENT_CLIP_FAILED " + entry.Id + " " + exception.Message);
                }
                finally
                {
                    actor?.Free();
                    timer.Stop();
                    result.ElapsedSeconds = timer.Elapsed.TotalSeconds;
                    results.Add(result);
                }
                if (results.Count % 20 == 0)
                {
                    GD.Print($"HUMAN_CONTENT_PROGRESS clips={results.Count}/{Math.Min(entries.Length, limit)} passed={results.Count(item => item.Passed)} elapsed={clock.Elapsed.TotalSeconds:0.0}s");
                    GC.Collect();
                    await ToSignal(this, SignalName.ProcessFrame);
                }
            }
            var summary = new
            {
                RunUtc = DateTime.UtcNow.ToString("O"), Engine = Engine.GetVersionInfo()["string"].ToString(),
                DisplayDriver = DisplayServer.GetName(), VideoAdapter = RenderingServer.GetVideoAdapterName(),
                NeutralEnvironment = "StudioLighting.CreateEnvironment; no DirectionalLight3D",
                Expected = entries.Length, Validated = results.Count, Passed = results.Count(item => item.Passed),
                Failed = results.Count(item => !item.Passed), Thumbnails = results.Count(item => item.ThumbnailPath != null),
                SourceReader = "BvhMotion.FromFile", Retargeter = "RiggedActor.SetMotionClips/EvaluateTimeline",
                ElapsedSeconds = clock.Elapsed.TotalSeconds, Clips = results
            };
            File.WriteAllText(reportPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }) + "\n");
            GD.Print($"HUMAN_CONTENT_COMPLETE validated={summary.Validated} passed={summary.Passed} failed={summary.Failed} thumbnails={summary.Thumbnails} seconds={summary.ElapsedSeconds:0.0}");
            viewport.Free(); viewport = null;
            Quit(summary.Failed == 0 ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError("HUMAN_CONTENT_PROBE_FAILED " + exception);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Error = exception.ToString(), Clips = results }, new JsonSerializerOptions { WriteIndented = true }));
            viewport?.Free();
            Quit(1);
        }
    }

    private async Task RenderExtras(string reportPath)
    {
        SubViewport? viewport = null;
        try
        {
            Require(DisplayServer.GetName() != "headless", "GPU thumbnails require a display driver.");
            viewport = new SubViewport { Size = new Vector2I(256, 208), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            Root.AddChild(viewport);
            var stage = new Node3D(); viewport.AddChild(stage);
            stage.AddChild(new WorldEnvironment { Environment = StudioLighting.CreateEnvironment() });
            var camera = new Camera3D { Current = true, Projection = Camera3D.ProjectionType.Orthogonal, Size = 2.4f };
            stage.AddChild(camera);
            var extras = new List<object>();
            foreach (LibraryModel descriptor in ContentCatalog.ForFamily("human").Where(model => model.BuiltinKind == "actor"))
            {
                var actor = new RiggedActor(); stage.AddChild(actor);
                actor.Scale = new Vector3(descriptor.BodyScale[0], descriptor.BodyScale[1], descriptor.BodyScale[2]);
                actor.SetBodyColor(new Color(descriptor.Color[0], descriptor.Color[1], descriptor.Color[2]));
                string path = await SaveExtraThumbnail(actor, camera, viewport, descriptor.Id);
                extras.Add(new { descriptor.Id, Kind = "Prepared human model", descriptor.BodyScale, descriptor.Color, ThumbnailPath = path });
                actor.Free();
            }
            foreach (string identifier in new[] { "human-walk-gesture", "human-cmu-walk" })
            {
                LibraryMotion descriptor = MotionCatalog.Find(identifier) ?? throw new InvalidDataException("Missing original motion " + identifier);
                var actor = new RiggedActor(); stage.AddChild(actor);
                actor.SetMotionClips(new[] { new MotionClipData
                {
                    Name = descriptor.Name, MotionPath = MotionCatalog.MaterializeSource(descriptor),
                    SourceScale = descriptor.SourceScale, SourceOffset = descriptor.SourceOffset,
                    Duration = descriptor.Duration, Loop = descriptor.Loop, InPlace = true, BlendIn = 0
                } });
                actor.EvaluateTimeline(descriptor.Duration * .41, true);
                ValidatePose(actor.GetNode<Skeleton3D>("PreparedHumanoid"), actor);
                string path = await SaveExtraThumbnail(actor, camera, viewport, descriptor.Id);
                extras.Add(new { descriptor.Id, Kind = "Original human motion", descriptor.SourcePath, descriptor.SourceOffset, ThumbnailPath = path });
                actor.Free();
            }
            foreach (string kind in PropFactory.Kinds)
            {
                Node3D prop = PropFactory.Create(kind); stage.AddChild(prop);
                string path = await SaveExtraThumbnail(prop, camera, viewport, kind);
                extras.Add(new { Id = kind, Kind = "Original procedural prop", ThumbnailPath = path });
                prop.Free();
            }
            LibraryModel foxDescriptor = ContentCatalog.Find("fox_actor") ?? throw new InvalidDataException("Missing the original fox model.");
            foreach (string identifier in new[] { "fox_actor", "fox-walk", "fox-run", "fox-survey" })
            {
                LibraryMotion? motionDescriptor = identifier == "fox_actor" ? null : MotionCatalog.Find(identifier);
                Require(identifier == "fox_actor" || motionDescriptor != null, "Missing the original fox motion " + identifier);
                using var document = new GltfDocument();
                using var state = new GltfState();
                Error error = document.AppendFromFile(ContentCatalog.MaterializeSource(foxDescriptor), state);
                Require(error == Error.Ok, "Could not import the original fox model: " + error);
                var fox = document.GenerateScene(state) as Node3D ?? throw new InvalidDataException("The original fox has no 3D root.");
                stage.AddChild(fox);
                fox.Scale *= foxDescriptor.Scale;
                var player = Descendants(fox).OfType<AnimationPlayer>().FirstOrDefault() ?? throw new InvalidDataException("The original fox has no animation player.");
                string animation = motionDescriptor?.SourceAnimation ?? foxDescriptor.DefaultAnimation ?? "";
                Require(player.HasAnimation(animation), "Missing original fox animation " + animation);
                double duration = motionDescriptor?.Duration ?? player.GetAnimation(animation).Length;
                var track = new NativeMotionTrack(fox, player);
                track.SetClips(new[] { new MotionClipData
                {
                    Name = animation, SourceAnimation = animation, Duration = duration,
                    SourceScale = 1, Loop = true, InPlace = true, BlendIn = 0
                } });
                track.Evaluate(duration * .41, true);
                Require(Descendants(fox).OfType<Skeleton3D>().All(skeleton => Capture(skeleton).All(pose => pose.Origin.IsFinite() && pose.Basis.IsFinite())), "The original fox produced a non-finite skeleton pose.");
                string path = await SaveExtraThumbnail(fox, camera, viewport, identifier);
                extras.Add(new { Id = identifier, Kind = motionDescriptor == null ? "Original fox model" : "Original fox motion", foxDescriptor.SourcePath, SourceAnimation = animation, ThumbnailPath = path });
                fox.Free();
            }
            if (File.Exists(reportPath))
            {
                JsonNode report = JsonNode.Parse(File.ReadAllText(reportPath)) ?? throw new InvalidDataException("The runtime validation report is empty.");
                report["ExtraThumbnails"] = JsonSerializer.SerializeToNode(extras);
                File.WriteAllText(reportPath, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            }
            viewport.Free(); viewport = null;
            GD.Print($"HUMAN_EXTRA_THUMBNAILS_OK count={extras.Count} models=17 motions=5");
            Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError("HUMAN_EXTRA_THUMBNAILS_FAILED " + exception);
            viewport?.Free();
            Quit(1);
        }
    }

    private async Task<string> SaveExtraThumbnail(Node3D actor, Camera3D camera, SubViewport viewport, string identifier)
    {
        await ToSignal(this, SignalName.ProcessFrame);
        await ToSignal(this, SignalName.ProcessFrame);
        FrameCamera(actor, camera);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(this, SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = viewport.GetTexture().GetImage();
        Require(image.GetWidth() == 256 && image.GetHeight() == 208, "GPU thumbnail readback is empty.");
        string resourcePath = "res://assets/content/thumbnails/" + identifier + ".png";
        Require(image.SavePng(ProjectSettings.GlobalizePath(resourcePath)) == Error.Ok, "Could not save " + resourcePath);
        return resourcePath;
    }

    private static Transform3D[] Capture(Skeleton3D skeleton)
        => Enumerable.Range(0, skeleton.GetBoneCount()).Select(skeleton.GetBonePose).ToArray();

    private static void ValidatePose(Skeleton3D skeleton, RiggedActor actor)
    {
        Require(actor.RootMotionOffset.IsFinite() && actor.AnimatedForward.IsFinite(), "Retargeted root/facing is not finite.");
        Require(Math.Abs(actor.AnimatedForward.Length() - 1) < .0001, "Retargeted facing is not normalized.");
        for (int bone = 0; bone < skeleton.GetBoneCount(); bone++)
        {
            Transform3D pose = skeleton.GetBonePose(bone);
            Require(pose.Origin.IsFinite() && pose.Basis.IsFinite(), "Non-finite prepared bone pose.");
            Require(Math.Abs(skeleton.GetBonePoseRotation(bone).Length() - 1) < .0001, "Prepared bone quaternion is not normalized.");
        }
    }

    private static void FrameCamera(Node3D actor, Camera3D camera)
    {
        var endpoints = Descendants(actor).OfType<MeshInstance3D>().Where(mesh => mesh.Mesh != null)
            .SelectMany(mesh => Enumerable.Range(0, 8).Select(index => mesh.GlobalTransform * mesh.GetAabb().GetEndpoint(index))).ToArray();
        Require(endpoints.Length > 0, "Prepared actor has no renderable geometry.");
        Vector3 minimum = endpoints.Aggregate((left, right) => left.Min(right));
        Vector3 maximum = endpoints.Aggregate((left, right) => left.Max(right));
        Vector3 center = (minimum + maximum) * .5f;
        Vector3 forward = actor is RiggedActor human ? human.AnimatedForward.Normalized() : Vector3.Forward;
        Vector3 side = forward.Rotated(Vector3.Up, Mathf.Pi * .5f);
        Vector3 offset = forward * 3.6f + side * 2.1f + Vector3.Up * 1.1f;
        camera.Position = center + offset;
        camera.LookAt(center, Vector3.Up);
        Vector3 right = camera.GlobalBasis.X.Normalized(), up = camera.GlobalBasis.Y.Normalized();
        float horizontal = endpoints.Max(point => Math.Abs((point - center).Dot(right)));
        float vertical = endpoints.Max(point => Math.Abs((point - center).Dot(up)));
        camera.Size = Math.Max(vertical * 2, horizontal * 2 / (256f / 208f)) * 1.23f;
        camera.Near = .01f;
        camera.Far = 100;
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        yield return node;
        foreach (Node child in node.GetChildren())
            foreach (Node descendant in Descendants(child)) yield return descendant;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
