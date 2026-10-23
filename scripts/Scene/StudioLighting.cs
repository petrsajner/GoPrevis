using Godot;

namespace Previs;

/// <summary>
/// Neutral, shadow-free studio illumination for arranging a shot. The hemisphere
/// environment describes diffuse fill, not a hidden directional light. Its upper
/// half is brighter so curved objects retain readable form without clipped whites.
/// </summary>
public partial class StudioLighting : Node3D
{
    private DirectionalLight3D? _directional;
    private bool _directionalEnabled;

    public Godot.Environment Environment { get; } = CreateEnvironment();

    /// <summary>
    /// The optional artistic key light. It is absent in the neutral default setup.
    /// Toggling it repeatedly reuses one light and does not accumulate illumination.
    /// </summary>
    public bool DirectionalEnabled
    {
        get => _directionalEnabled;
        set
        {
            _directionalEnabled = value;
            if (value)
                EnsureDirectionalLight();
            if (_directional != null)
                _directional.Visible = value;
        }
    }

    public override void _Ready()
    {
        if (_directionalEnabled)
            EnsureDirectionalLight();
    }

    /// <summary>
    /// Use the same lighting in the working scene and built-in asset thumbnails.
    /// Linear mapping and sub-white sky radiance preserve authored albedo colors;
    /// no glow, exposure boost, sun disk, or shadow setup is required by default.
    /// </summary>
    public static Godot.Environment CreateEnvironment()
    {
        var hemisphere = new ProceduralSkyMaterial
        {
            ResourceName = "Neutral studio hemisphere",
            SkyTopColor = new Color("c4c4c4"),
            SkyHorizonColor = new Color("969696"),
            GroundHorizonColor = new Color("969696"),
            GroundBottomColor = new Color("505050"),
            SkyCurve = 0.45f,
            GroundCurve = 0.45f,
            SkyEnergyMultiplier = 1,
            GroundEnergyMultiplier = 1,
            EnergyMultiplier = 1
        };
        return new Godot.Environment
        {
            ResourceName = "Neutral blockout environment",
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color("252529"),
            BackgroundEnergyMultiplier = 1,
            Sky = new Sky { SkyMaterial = hemisphere },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 1,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            TonemapExposure = 1,
            GlowEnabled = false,
            AdjustmentEnabled = false,
            FogEnabled = false
        };
    }

    private void EnsureDirectionalLight()
    {
        if (_directional != null)
            return;
        _directional = new DirectionalLight3D
        {
            Name = "Directional_light",
            RotationDegrees = new Vector3(-48, -28, 0),
            LightColor = Colors.White,
            LightEnergy = 0.2f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 60,
            SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly
        };
        AddChild(_directional);
    }
}
