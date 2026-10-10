using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Version 60 skies (<see cref="Skybox"/>): one made from a map's lighting for a map without one,
    /// and one read from the JSON the FAF map editor saves its skybox presets as.
    /// </summary>
    public static class Skyboxes
    {
        /// <summary>
        /// A plain sky for a map that has no skybox (versions 53 and 56, or a version 60 map without
        /// one): around the map's middle, as large as the FAF map editor makes it (about 2.3 times
        /// the map's larger side), the horizon in the fog's colour warmed by the sun, the zenith a
        /// deeper blue of the same brightness; no sky objects and no cirrus.
        /// </summary>
        public static Skybox FromLighting(MapDocument document)
        {
            Lighting lighting = document.Lighting;
            float size = Math.Max(document.Width, document.Height);
            Vector3 sun = Vector3.Clamp(lighting.SunColor, Vector3.Zero, new Vector3(2)) / Math.Max(1, Max(lighting.SunColor));
            Vector3 fog = Vector3.Clamp(lighting.FogColor, Vector3.Zero, Vector3.One);
            // a black fog (some maps) says nothing of the sky: the sun's colour then, dimmed
            Vector3 horizon = Max(fog) < 0.02f ? sun * 0.6f : Vector3.Lerp(fog, sun * Math.Max(0.5f, Max(fog)), 0.35f);
            horizon = Vector3.Clamp(horizon, new Vector3(0.05f), Vector3.One);
            float brightness = (horizon.X + horizon.Y + horizon.Z) / 3;
            Vector3 blue = new Vector3(0.32f, 0.5f, 0.85f) * Math.Clamp(brightness * 1.1f, 0.15f, 1);
            Vector3 zenith = Vector3.Clamp(Vector3.Lerp(horizon * 0.6f, blue, 0.65f), Vector3.Zero, Vector3.One);
            float scale = size * 2.29f;
            return new Skybox
            {
                Position = new Vector3(document.Width / 2f, 0, document.Height / 2f),
                Scale = scale,
                SubtractHeight = 1.25f,
                SubdivisionsAxis = 16,
                SubdivisionsHeight = 6,
                HorizonHeight = 0,
                ZenithHeight = scale * 0.4f,
                HorizonColor = horizon,
                ZenithColor = zenith,
                DecalGlowMultiplier = 0,
                Albedo = "",
                Glow = "",
                Planets = [],
                MidColor = default,
                CirrusMultiplier = 0,
                CirrusColor = Vector3.One,
                CirrusTexture = "",
                CirrusLayers = [],
                Clouds7 = 0,
            };
        }

        private static float Max(Vector3 value) => Math.Max(value.X, Math.Max(value.Y, value.Z));

        /// <summary>
        /// A skybox from the JSON the FAF map editor saves its skybox presets as: an object
        /// <c>Data</c> with <c>Position</c> (x, y, z), <c>Scale</c>, <c>HorizonColor</c> and the other
        /// colours as r, g, b (0 to 1), <c>MidRgbColor</c> as r, g, b, a (0 to 255), <c>Planets</c>
        /// (<c>Position</c>, <c>Rotation</c>, <c>Scale</c> x and y, <c>Uv</c> x, y, z, w) and
        /// <c>CirrusLayers</c> (<c>frequency</c> x and y, <c>Speed</c>, <c>Direction</c> x and y).
        /// A missing number is 0, a missing text empty.
        /// </summary>
        /// <param name="folder">The map's folder: a texture path into another map's folder
        /// (<c>/maps/blackstar.v0004/env/decals/aurora.dds</c>) is moved into this one when
        /// <paramref name="hasFile"/> says the map has that file; null to keep the paths.</param>
        /// <param name="hasFile">Whether the map has a file at a path in its folder (<c>env/decals/aurora.dds</c>).</param>
        /// <exception cref="JsonException">The text is no JSON object.</exception>
        public static Skybox ReadJson(string json, string? folder = null, Func<string, bool>? hasFile = null)
        {
            using JsonDocument parsed = JsonDocument.Parse(json);
            JsonElement root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("A skybox is a JSON object");
            }
            JsonElement data = Get(root, "Data") is { ValueKind: JsonValueKind.Object } inner ? inner : root;

            string Texture(string name)
            {
                string path = Text(data, name);
                if (folder is null || hasFile is null || !path.Replace('\\', '/').TrimStart('/').StartsWith("maps/", StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
                string key = path.Replace('\\', '/').TrimStart('/');
                int folderEnd = key.IndexOf('/', "maps/".Length);
                string inFolder = folderEnd < 0 ? "" : key[(folderEnd + 1)..];
                return inFolder.Length > 0 && hasFile(inFolder) ? $"/maps/{folder}/{inFolder}" : path;
            }

            Color32 mid = default;
            if (Get(data, "MidRgbColor") is { ValueKind: JsonValueKind.Object } midColor)
            {
                static byte Byte(JsonElement element, string name) => (byte)Math.Clamp((int)MathF.Round(Number(element, name)), 0, 255);
                mid = new Color32(Byte(midColor, "a"), Byte(midColor, "r"), Byte(midColor, "g"), Byte(midColor, "b"));
            }

            return new Skybox
            {
                Position = Vector(data, "Position"),
                Scale = Number(data, "Scale"),
                SubtractHeight = Number(data, "SubtractHeight"),
                SubdivisionsAxis = (int)Number(data, "SubdivisionsAxis"),
                SubdivisionsHeight = (int)Number(data, "SubdivisionsHeight"),
                HorizonHeight = Number(data, "HorizonHeight"),
                ZenithHeight = Number(data, "ZenithHeight"),
                HorizonColor = Color(data, "HorizonColor"),
                ZenithColor = Color(data, "ZenithColor"),
                DecalGlowMultiplier = Number(data, "DecalGlowMultiplier"),
                Albedo = Texture("Albedo"),
                Glow = Texture("Glow"),
                Planets = Items(data, "Planets").Select(planet => new Planet
                {
                    Position = Vector(planet, "Position"),
                    Rotation = Number(planet, "Rotation"),
                    Scale = Get(planet, "Scale") is { ValueKind: JsonValueKind.Object } scale ? new Vector2(Number(scale, "x"), Number(scale, "y")) : Vector2.Zero,
                    Uv = Get(planet, "Uv") is { ValueKind: JsonValueKind.Object } uv ? new Vector4(Number(uv, "x"), Number(uv, "y"), Number(uv, "z"), Number(uv, "w")) : Vector4.Zero,
                }).ToImmutableArray(),
                MidColor = mid,
                CirrusMultiplier = Number(data, "CirrusMultiplier"),
                CirrusColor = Color(data, "CirrusColor"),
                CirrusTexture = Texture("CirrusTexture"),
                CirrusLayers = Items(data, "CirrusLayers").Select(layer => new CirrusLayer
                {
                    Frequency = Get(layer, "frequency") is { ValueKind: JsonValueKind.Object } frequency ? new Vector2(Number(frequency, "x"), Number(frequency, "y")) : Vector2.Zero,
                    Speed = Number(layer, "Speed"),
                    Direction = Get(layer, "Direction") is { ValueKind: JsonValueKind.Object } direction ? new Vector2(Number(direction, "x"), Number(direction, "y")) : Vector2.Zero,
                }).ToImmutableArray(),
                Clouds7 = Number(data, "Clouds7"),
            };
        }

        /// <summary>
        /// A property by its name, any case.
        /// </summary>
        private static JsonElement? Get(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value;
                }
            }
            return null;
        }

        private static float Number(JsonElement element, string name) =>
            Get(element, name) is { ValueKind: JsonValueKind.Number } number ? (float)number.GetDouble() : 0;

        private static string Text(JsonElement element, string name) =>
            Get(element, name) is { ValueKind: JsonValueKind.String } text ? text.GetString() ?? "" : "";

        private static Vector3 Vector(JsonElement element, string name) =>
            Get(element, name) is { ValueKind: JsonValueKind.Object } vector ? new Vector3(Number(vector, "x"), Number(vector, "y"), Number(vector, "z")) : Vector3.Zero;

        private static Vector3 Color(JsonElement element, string name) =>
            Get(element, name) is { ValueKind: JsonValueKind.Object } color ? new Vector3(Number(color, "r"), Number(color, "g"), Number(color, "b")) : Vector3.Zero;

        private static IEnumerable<JsonElement> Items(JsonElement element, string name) =>
            Get(element, name) is { ValueKind: JsonValueKind.Array } items ? items.EnumerateArray().ToArray() : [];
    }
}
