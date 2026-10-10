using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// What a water preset holds (user story L-2): how the water looks, not where it is. The
    /// elevations, whether the map has water, the wave generators (placed on the map's shores) and
    /// the painted masks stay the map's own.
    /// </summary>
    public sealed record WaterPresetSettings
    {
        public required float[] SurfaceColor { get; init; }

        public required float[] ColorLerp { get; init; }

        public float RefractionScale { get; init; }

        public float FresnelBias { get; init; }

        public float FresnelPower { get; init; }

        public float UnitReflection { get; init; }

        public float SkyReflection { get; init; }

        public float SunShininess { get; init; }

        public float SunStrength { get; init; }

        public required float[] SunDirection { get; init; }

        public required float[] SunColor { get; init; }

        public float SunReflection { get; init; }

        public float SunGlow { get; init; }

        public required string Cubemap { get; init; }

        public required string Ramp { get; init; }

        public required IReadOnlyList<WaveTextureDto> WaveTextures { get; init; }
    }

    /// <summary>
    /// Lighting and water saved as presets of the workspace (user stories L-1, L-2, W-1): the
    /// lighting of a biome, a look of the water, applied to another map.
    /// </summary>
    public static class AtmospherePresets
    {
        public const string LightingKind = "lighting";

        public const string WaterKind = "water";

        /// <summary>
        /// A lighting as a preset: every setting of it, as the project's <c>map.json</c> writes it.
        /// </summary>
        public static Preset OfLighting(string name, Lighting lighting) => Preset.Of(LightingKind, name, MapProjectMapping.ToDto(lighting));

        /// <exception cref="MapProjectException">The preset holds no lighting.</exception>
        public static Lighting ReadLighting(Preset preset) => MapProjectMapping.ToModel(preset.Read<LightingDto>());

        /// <summary>
        /// How the water looks, as a preset (<see cref="WaterPresetSettings"/>).
        /// </summary>
        public static Preset OfWater(string name, Water water) => Preset.Of(WaterKind, name, new WaterPresetSettings
        {
            SurfaceColor = Array(water.SurfaceColor),
            ColorLerp = [water.ColorLerp.X, water.ColorLerp.Y],
            RefractionScale = water.RefractionScale,
            FresnelBias = water.FresnelBias,
            FresnelPower = water.FresnelPower,
            UnitReflection = water.UnitReflection,
            SkyReflection = water.SkyReflection,
            SunShininess = water.SunShininess,
            SunStrength = water.SunStrength,
            SunDirection = Array(water.SunDirection),
            SunColor = Array(water.SunColor),
            SunReflection = water.SunReflection,
            SunGlow = water.SunGlow,
            Cubemap = water.Cubemap,
            Ramp = water.Ramp,
            WaveTextures = [.. water.WaveTextures.Select(MapProjectMapping.ToDto)],
        });

        /// <summary>
        /// The map's water with the look of a preset; its elevations, whether it has water, its
        /// wave generators and masks stay.
        /// </summary>
        /// <exception cref="MapProjectException">The preset holds no water.</exception>
        public static Water ApplyWater(Preset preset, Water water)
        {
            WaterPresetSettings settings = preset.Read<WaterPresetSettings>();
            return water with
            {
                SurfaceColor = Vector(settings.SurfaceColor, preset),
                ColorLerp = settings.ColorLerp.Length == 2 ? new Vector2(settings.ColorLerp[0], settings.ColorLerp[1]) : throw Wrong(preset, "colorLerp"),
                RefractionScale = settings.RefractionScale,
                FresnelBias = settings.FresnelBias,
                FresnelPower = settings.FresnelPower,
                UnitReflection = settings.UnitReflection,
                SkyReflection = settings.SkyReflection,
                SunShininess = settings.SunShininess,
                SunStrength = settings.SunStrength,
                SunDirection = Vector(settings.SunDirection, preset),
                SunColor = Vector(settings.SunColor, preset),
                SunReflection = settings.SunReflection,
                SunGlow = settings.SunGlow,
                Cubemap = settings.Cubemap,
                Ramp = settings.Ramp,
                WaveTextures = settings.WaveTextures.Count == 0 ? water.WaveTextures : [.. settings.WaveTextures.Select(MapProjectMapping.ToModel)],
            };
        }

        private static float[] Array(Vector3 v) => [v.X, v.Y, v.Z];

        private static Vector3 Vector(float[] values, Preset preset) =>
            values.Length == 3 ? new Vector3(values[0], values[1], values[2]) : throw Wrong(preset, "three numbers");

        private static MapProjectException Wrong(Preset preset, string what) => new MapProjectException($"The water preset {preset.Name} lacks {what}");
    }
}
