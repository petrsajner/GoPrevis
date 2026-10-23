using System;
using Godot;

namespace Previs;

/// <summary>
/// Built-in, metre-scale blockout objects. All objects rest on Y = 0 and face -Z.
/// These are editable scene props, including the deliberately static dog placeholder.
/// </summary>
public static class PropFactory
{
    public static readonly string[] Kinds =
    {
        "box", "sphere", "cylinder", "wall", "floor", "table", "chair", "car", "tree", "dog"
    };

    private static readonly StandardMaterial3D Clay = Material("Clay", "#bdc7d7");
    private static readonly StandardMaterial3D LightClay = Material("Light clay", "#dce2e9");
    private static readonly StandardMaterial3D WallPaint = Material("Wall paint", "#a3aaba");
    private static readonly StandardMaterial3D FloorFinish = Material("Floor", "#737c8d");
    private static readonly StandardMaterial3D Wood = Material("Warm timber", "#b08a67");
    private static readonly StandardMaterial3D WoodEdge = Material("Timber edge", "#806a56");
    private static readonly StandardMaterial3D DarkMetal = Material("Dark metal", "#3c4451", 0.28f);
    private static readonly StandardMaterial3D CarPaint = Material("Vehicle blue", "#668cae", 0.18f);
    private static readonly StandardMaterial3D Glass = Material("Opaque blue glass", "#344d61", 0.12f);
    private static readonly StandardMaterial3D Rubber = Material("Tyre rubber", "#242a34");
    private static readonly StandardMaterial3D WheelMetal = Material("Wheel metal", "#9faab9", 0.5f);
    private static readonly StandardMaterial3D Headlight = Material("Headlight", "#eadfc1");
    private static readonly StandardMaterial3D Taillight = Material("Tail light", "#a15d60");
    private static readonly StandardMaterial3D Bark = Material("Tree bark", "#86715b");
    private static readonly StandardMaterial3D Leaves = Material("Leaf green", "#789c87");
    private static readonly StandardMaterial3D LeavesLight = Material("Leaf green light", "#93ad8e");
    private static readonly StandardMaterial3D Fur = Material("Dog coat", "#bb9977");
    private static readonly StandardMaterial3D FurLight = Material("Dog muzzle and paws", "#dcc5a8");
    private static readonly StandardMaterial3D FurDark = Material("Dog ears", "#79604d");
    private static readonly StandardMaterial3D Collar = Material("Dog collar", "#7c82bb");

    public static string DisplayName(string kind) => kind.ToLowerInvariant() switch
    {
        "box" => "Box",
        "sphere" => "Sphere",
        "cylinder" => "Cylinder",
        "wall" => "Wall",
        "floor" => "Floor",
        "table" => "Table",
        "chair" => "Chair",
        "car" => "Car",
        "tree" => "Tree",
        "dog" => "Dog (static)",
        _ => "Box"
    };

    public static Node3D Create(string kind)
    {
        kind = (kind ?? "box").ToLowerInvariant();
        if (Array.IndexOf(Kinds, kind) < 0)
            kind = "box";

        var root = new Node3D { Name = DisplayName(kind).Replace(" ", "_") };
        root.SetMeta("kind", kind);
        root.SetMeta("display_name", DisplayName(kind));
        root.SetMeta("static_prop", true);

        switch (kind)
        {
            case "sphere":
                Sphere(root, "Sphere", new Vector3(0, 0.5f, 0), 0.5f, Clay);
                break;
            case "cylinder":
                Cylinder(root, "Cylinder", new Vector3(0, 0.5f, 0), 0.45f, 0.45f, 1, Clay, 24);
                break;
            case "wall":
                Box(root, "Wall", new Vector3(0, 1.4f, 0), new Vector3(4, 2.8f, 0.16f), WallPaint);
                Box(root, "Baseboard", new Vector3(0, 0.06f, -0.1f), new Vector3(4, 0.12f, 0.04f), LightClay);
                break;
            case "floor":
                Box(root, "Floor", new Vector3(0, 0.025f, 0), new Vector3(4, 0.05f, 4), FloorFinish);
                break;
            case "table":
                BuildTable(root);
                break;
            case "chair":
                BuildChair(root);
                break;
            case "car":
                BuildCar(root);
                break;
            case "tree":
                BuildTree(root);
                break;
            case "dog":
                BuildDog(root);
                break;
            default:
                Box(root, "Box", new Vector3(0, 0.5f, 0), Vector3.One, Clay);
                break;
        }

        return root;
    }

    /// <summary>
    /// Returns the visual bounds in root-local coordinates, including transformed parts.
    /// It works before the object is added to the scene tree. Transform the eight corners
    /// by root.GlobalTransform when a world-space selection box is needed.
    /// </summary>
    public static Aabb CalculateBounds(Node3D root)
    {
        var minimum = Vector3.Zero;
        var maximum = Vector3.Zero;
        var found = false;
        CollectBounds(root, Transform3D.Identity, ref minimum, ref maximum, ref found);
        return found ? new Aabb(minimum, maximum - minimum) : new Aabb(Vector3.Zero, Vector3.Zero);
    }

    private static void CollectBounds(Node node, Transform3D transform,
        ref Vector3 minimum, ref Vector3 maximum, ref bool found)
    {
        if (node is MeshInstance3D { Mesh: not null } instance)
        {
            var bounds = instance.GetAabb();
            for (var i = 0; i < 8; i++)
            {
                var p = transform * bounds.GetEndpoint(i);
                if (!found)
                {
                    minimum = maximum = p;
                    found = true;
                }
                else
                {
                    minimum = new Vector3(Mathf.Min(minimum.X, p.X), Mathf.Min(minimum.Y, p.Y), Mathf.Min(minimum.Z, p.Z));
                    maximum = new Vector3(Mathf.Max(maximum.X, p.X), Mathf.Max(maximum.Y, p.Y), Mathf.Max(maximum.Z, p.Z));
                }
            }
        }

        foreach (var child in node.GetChildren())
        {
            var childTransform = child is Node3D spatial ? transform * spatial.Transform : transform;
            CollectBounds(child, childTransform, ref minimum, ref maximum, ref found);
        }
    }

    private static void BuildTable(Node3D root)
    {
        // Dining / meeting table: 1.6 m wide, 0.85 m deep, 0.76 m high.
        Box(root, "Tabletop", new Vector3(0, 0.725f, 0), new Vector3(1.6f, 0.07f, 0.85f), Wood);
        Box(root, "Apron_front", new Vector3(0, 0.65f, -0.32f), new Vector3(1.38f, 0.09f, 0.045f), WoodEdge);
        Box(root, "Apron_back", new Vector3(0, 0.65f, 0.32f), new Vector3(1.38f, 0.09f, 0.045f), WoodEdge);
        foreach (var x in new[] { -0.67f, 0.67f })
        foreach (var z in new[] { -0.3f, 0.3f })
            Box(root, "Table_leg", new Vector3(x, 0.345f, z), new Vector3(0.075f, 0.69f, 0.075f), DarkMetal);
    }

    private static void BuildChair(Node3D root)
    {
        Box(root, "Seat", new Vector3(0, 0.455f, 0), new Vector3(0.48f, 0.065f, 0.48f), Wood);
        Box(root, "Backrest", new Vector3(0, 0.755f, 0.205f), new Vector3(0.48f, 0.44f, 0.055f), Wood);
        foreach (var x in new[] { -0.185f, 0.185f })
        {
            Box(root, "Front_leg", new Vector3(x, 0.2125f, -0.18f), new Vector3(0.045f, 0.425f, 0.045f), DarkMetal);
            Box(root, "Back_leg", new Vector3(x, 0.375f, 0.18f), new Vector3(0.045f, 0.75f, 0.045f), DarkMetal);
        }
    }

    private static void BuildCar(Node3D root)
    {
        // Four-seat hatchback facing -Z. Opaque glazing keeps blockout masks reliable.
        Box(root, "Chassis", new Vector3(0, 0.38f, 0), new Vector3(1.62f, 0.25f, 3.95f), DarkMetal);
        Box(root, "Body", new Vector3(0, 0.66f, 0), new Vector3(1.78f, 0.5f, 4.05f), CarPaint);
        Box(root, "Bonnet", new Vector3(0, 0.935f, -1.31f), new Vector3(1.7f, 0.08f, 1.28f), CarPaint);
        var cabinMesh = TaperedBox(new Vector3(1.64f, 0.68f, 2.17f), new Vector2(1.38f, 1.42f), -0.04f);
        AddPart(root, "Cabin_windows", cabinMesh,
            new ConvexPolygonShape3D { Points = CabinPoints(new Vector3(1.64f, 0.68f, 2.17f), new Vector2(1.38f, 1.42f), -0.04f) },
            new Vector3(0, 1.22f, 0.12f), Glass);
        Box(root, "Roof", new Vector3(0, 1.58f, 0.08f), new Vector3(1.42f, 0.065f, 1.47f), CarPaint);
        foreach (var x in new[] { -0.835f, 0.835f })
        {
            Box(root, "Window_pillar", new Vector3(x, 1.18f, 0.13f), new Vector3(0.04f, 0.58f, 0.075f), CarPaint);
            Box(root, "Door_handle", new Vector3(x * 1.075f, 0.78f, 0.4f), new Vector3(0.025f, 0.045f, 0.18f), DarkMetal);
            Box(root, "Mirror", new Vector3(x * 1.14f, 1.02f, -0.74f), new Vector3(0.19f, 0.13f, 0.24f), CarPaint);
        }
        foreach (var x in new[] { -0.88f, 0.88f })
        foreach (var z in new[] { -1.28f, 1.28f })
        {
            var wheel = Cylinder(root, "Wheel", new Vector3(x, 0.34f, z), 0.34f, 0.34f, 0.22f, Rubber, 20);
            wheel.Rotation = new Vector3(0, 0, Mathf.Pi / 2);
            var hub = Cylinder(root, "Wheel_hub", new Vector3(x + Math.Sign(x) * 0.12f, 0.34f, z), 0.205f, 0.205f, 0.025f, WheelMetal, 12);
            hub.Rotation = wheel.Rotation;
        }
        Box(root, "Front_bumper", new Vector3(0, 0.47f, -2.045f), new Vector3(1.65f, 0.1f, 0.065f), DarkMetal);
        Box(root, "Rear_bumper", new Vector3(0, 0.47f, 2.045f), new Vector3(1.65f, 0.1f, 0.065f), DarkMetal);
        foreach (var x in new[] { -0.61f, 0.61f })
        {
            Box(root, "Headlight", new Vector3(x, 0.78f, -2.035f), new Vector3(0.38f, 0.15f, 0.025f), Headlight);
            Box(root, "Tail_light", new Vector3(x, 0.79f, 2.035f), new Vector3(0.34f, 0.13f, 0.025f), Taillight);
        }
    }

    private static void BuildTree(Node3D root)
    {
        Cylinder(root, "Trunk", new Vector3(0, 0.85f, 0), 0.14f, 0.21f, 1.7f, Bark, 9);
        var branch = Cylinder(root, "Branch", new Vector3(0.22f, 1.5f, 0), 0.055f, 0.09f, 0.7f, Bark, 7);
        branch.Rotation = new Vector3(0, 0, -0.65f);
        FacetedSphere(root, "Crown", new Vector3(0, 2.32f, 0), new Vector3(1.02f, 1.17f, 0.95f), Leaves);
        FacetedSphere(root, "Crown_left", new Vector3(-0.66f, 2.05f, 0.12f), new Vector3(0.7f, 0.8f, 0.7f), LeavesLight);
        FacetedSphere(root, "Crown_right", new Vector3(0.65f, 2.2f, -0.1f), new Vector3(0.66f, 0.83f, 0.73f), Leaves);
    }

    private static void BuildDog(Node3D root)
    {
        // A medium-sized dog for scene scale and composition. No animation or rig.
        var body = Capsule(root, "Body", new Vector3(0, 0.72f, 0.02f), 0.235f, 0.98f, Fur);
        body.Rotation = new Vector3(Mathf.Pi / 2, 0, 0);
        Capsule(root, "Chest", new Vector3(0, 0.72f, -0.3f), 0.215f, 0.64f, Fur);
        foreach (var x in new[] { -0.17f, 0.17f })
        foreach (var z in new[] { -0.3f, 0.3f })
        {
            Capsule(root, "Leg", new Vector3(x, 0.325f, z), 0.072f, 0.57f, Fur);
            Box(root, "Paw", new Vector3(x, 0.05f, z - 0.035f), new Vector3(0.15f, 0.1f, 0.21f), FurLight);
        }
        var neck = Capsule(root, "Neck", new Vector3(0, 0.97f, -0.38f), 0.15f, 0.47f, Fur);
        neck.Rotation = new Vector3(-0.25f, 0, 0);
        var collar = Cylinder(root, "Collar", new Vector3(0, 1.01f, -0.39f), 0.158f, 0.158f, 0.07f, Collar, 16);
        collar.Rotation = neck.Rotation;
        Sphere(root, "Head", new Vector3(0, 1.16f, -0.49f), 0.205f, Fur);
        Box(root, "Muzzle", new Vector3(0, 1.105f, -0.705f), new Vector3(0.23f, 0.16f, 0.27f), FurLight);
        Box(root, "Nose", new Vector3(0, 1.13f, -0.85f), new Vector3(0.12f, 0.085f, 0.045f), Rubber);
        foreach (var x in new[] { -0.17f, 0.17f })
        {
            var ear = Capsule(root, "Ear", new Vector3(x, 1.14f, -0.445f), 0.072f, 0.29f, FurDark);
            ear.Rotation = new Vector3(-0.12f, 0, Math.Sign(x) * 0.2f);
            Sphere(root, "Eye", new Vector3(x * 0.79f, 1.215f, -0.65f), 0.024f, Rubber);
        }
        var tail = Capsule(root, "Tail", new Vector3(0, 0.88f, 0.64f), 0.045f, 0.5f, Fur);
        tail.Rotation = new Vector3(0.82f, 0, 0);
    }

    private static StandardMaterial3D Material(string name, string color, float metallic = 0)
        => new()
        {
            ResourceName = name,
            AlbedoColor = Color.FromHtml(color),
            Roughness = metallic > 0 ? 0.6f : 0.88f,
            Metallic = metallic
        };

    private static Node3D Box(Node3D root, string name, Vector3 center, Vector3 size, Material material)
        => AddPart(root, name, new BoxMesh { Size = size }, new BoxShape3D { Size = size }, center, material);

    private static Node3D Sphere(Node3D root, string name, Vector3 center, float radius, Material material)
        => AddPart(root, name,
            new SphereMesh { Radius = radius, Height = radius * 2, RadialSegments = 24, Rings = 12 },
            new SphereShape3D { Radius = radius }, center, material);

    private static Node3D Capsule(Node3D root, string name, Vector3 center, float radius, float height, Material material)
        => AddPart(root, name,
            new CapsuleMesh { Radius = radius, Height = height, RadialSegments = 16, Rings = 6 },
            new CapsuleShape3D { Radius = radius, Height = height }, center, material);

    private static Node3D Cylinder(Node3D root, string name, Vector3 center,
        float topRadius, float bottomRadius, float height, Material material, int segments = 16)
        => AddPart(root, name,
            new CylinderMesh { TopRadius = topRadius, BottomRadius = bottomRadius, Height = height, RadialSegments = segments },
            new CylinderShape3D { Radius = Mathf.Max(topRadius, bottomRadius), Height = height }, center, material);

    private static void FacetedSphere(Node3D root, string name, Vector3 center, Vector3 radius, Material material)
    {
        var part = AddPart(root, name,
            new SphereMesh { Radius = 1, Height = 2, RadialSegments = 9, Rings = 5 },
            new BoxShape3D { Size = radius * 2 }, center, material);
        // Scale only the visual: physics shapes must keep a uniform node scale.
        part.GetNode<MeshInstance3D>("Visual").Scale = radius;
    }

    private static Node3D AddPart(Node3D root, string name, Mesh mesh, Shape3D shape,
        Vector3 center, Material material)
    {
        var part = new Node3D { Name = name, Position = center };
        part.AddChild(new MeshInstance3D { Name = "Visual", Mesh = mesh, MaterialOverride = material });
        var body = new StaticBody3D { Name = "PickBody", CollisionLayer = 1, CollisionMask = 0 };
        body.SetMeta("scene_root", root);
        body.AddChild(new CollisionShape3D { Name = "Shape", Shape = shape });
        part.AddChild(body);
        root.AddChild(part);
        return part;
    }

    private static Vector3[] CabinPoints(Vector3 size, Vector2 roof, float roofOffsetZ)
    {
        var b = size * 0.5f;
        return new[]
        {
            new Vector3(-b.X, -b.Y, -b.Z), new Vector3(b.X, -b.Y, -b.Z),
            new Vector3(b.X, -b.Y, b.Z), new Vector3(-b.X, -b.Y, b.Z),
            new Vector3(-roof.X / 2, b.Y, -roof.Y / 2 + roofOffsetZ),
            new Vector3(roof.X / 2, b.Y, -roof.Y / 2 + roofOffsetZ),
            new Vector3(roof.X / 2, b.Y, roof.Y / 2 + roofOffsetZ),
            new Vector3(-roof.X / 2, b.Y, roof.Y / 2 + roofOffsetZ)
        };
    }

    private static ArrayMesh TaperedBox(Vector3 size, Vector2 roof, float roofOffsetZ)
    {
        var points = CabinPoints(size, roof, roofOffsetZ);
        // Godot uses clockwise front-face winding. Surface normals point outwards.
        int[][] faces =
        {
            new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 },
            new[] { 3, 0, 4, 7 }, new[] { 4, 5, 6, 7 }, new[] { 3, 2, 1, 0 }
        };
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var face in faces)
        {
            var a = points[face[0]];
            var b = points[face[1]];
            var c = points[face[2]];
            var d = points[face[3]];
            var normal = (c - a).Cross(b - a).Normalized();
            foreach (var vertex in new[] { a, b, c, a, c, d })
            {
                tool.SetNormal(normal);
                tool.AddVertex(vertex);
            }
        }
        return tool.Commit();
    }
}
