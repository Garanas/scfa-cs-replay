using System.Collections.Immutable;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// What a map template is made of (user story M-2).
    /// </summary>
    public enum MapTemplateKind
    {
        /// <summary>
        /// A small JSON file of the workspace's <c>templates</c> folder: the settings of a new map
        /// (<see cref="MapTemplateDto"/>), which the New map page fills its fields with.
        /// </summary>
        Settings,

        /// <summary>
        /// A map project in a folder of the workspace's <c>templates</c> folder: a whole map, its
        /// terrain, strata and things, which a new map copies.
        /// </summary>
        Project,

        /// <summary>
        /// The editor's own: the sandbox (<see cref="SandboxMap"/>), a small map with one of every
        /// kind of thing.
        /// </summary>
        Sandbox,
    }

    /// <summary>
    /// A template a new map can start from (user story M-2).
    /// </summary>
    /// <param name="Name">What the New map page lists; the file's or folder's name.</param>
    /// <param name="Path">The JSON file or the project's folder; null for the sandbox.</param>
    /// <param name="Summary">What it holds, in words: <c>10 km, 4 players, water</c>.</param>
    public sealed record MapTemplate(string Name, MapTemplateKind Kind, string? Path, string Summary)
    {
        public override string ToString() => Name;
    }

    /// <summary>
    /// The settings a JSON template holds: what a new map takes from it; each left out is the New
    /// map page's own.
    /// </summary>
    public sealed record MapTemplateDto
    {
        public int FormatVersion { get; init; } = MapTemplates.CurrentFormatVersion;

        public string? Description { get; init; }

        /// <summary>
        /// In ogrids, one of the game's sizes (<see cref="NewMap.Sizes"/>).
        /// </summary>
        public int? Size { get; init; }

        /// <summary>
        /// The starting height, in game units.
        /// </summary>
        public float? Height { get; init; }

        public bool? Water { get; init; }

        public float? WaterElevation { get; init; }

        public float? DeepElevation { get; init; }

        public float? AbyssElevation { get; init; }

        /// <summary>
        /// One of <see cref="NewMap.Biomes"/>; its strata unless <see cref="Strata"/> are given.
        /// </summary>
        public string? Biome { get; init; }

        /// <summary>
        /// The terrain's layers as a project's <c>map.json</c> writes them: the lower layer first,
        /// the strata, the upper layer last.
        /// </summary>
        public IReadOnlyList<TerrainLayerDto>? Strata { get; init; }

        public int? Players { get; init; }

        /// <summary>
        /// The armies besides the players', such as <c>ARMY_17</c> and <c>NEUTRAL_CIVILIAN</c>.
        /// </summary>
        public IReadOnlyList<string>? ExtraArmies { get; init; }

        public LightingDto? Lighting { get; init; }
    }

    /// <summary>
    /// The workspace's map templates (user story M-2): JSON files of a new map's settings and map
    /// projects in its <c>templates</c> folder, and the sandbox as the editor's own. Nothing is kept
    /// in memory: listing reads the folder.
    /// </summary>
    public static class MapTemplates
    {
        public const int CurrentFormatVersion = 1;

        /// <summary>
        /// The name the sandbox has as a template.
        /// </summary>
        public const string SandboxName = "Sandbox";

        /// <summary>
        /// The workspace's templates, by name, the sandbox last. A file or folder that cannot be read
        /// is left out and named in <paramref name="unreadable"/> when it is given.
        /// </summary>
        public static IReadOnlyList<MapTemplate> List(string templatesFolder, ICollection<string>? unreadable = null)
        {
            List<MapTemplate> templates = [];
            if (Directory.Exists(templatesFolder))
            {
                foreach (string file in Directory.EnumerateFiles(templatesFolder, "*.json"))
                {
                    try
                    {
                        templates.Add(new MapTemplate(System.IO.Path.GetFileNameWithoutExtension(file), MapTemplateKind.Settings, file, Summarize(ReadSettings(file))));
                    }
                    catch (Exception exception) when (exception is MapProjectException or IOException or UnauthorizedAccessException)
                    {
                        unreadable?.Add($"{file}: {exception.Message}");
                    }
                }
                foreach (string folder in Directory.EnumerateDirectories(templatesFolder))
                {
                    string map = System.IO.Path.Combine(folder, MapProjectLayout.Map);
                    if (!File.Exists(map))
                    {
                        continue;
                    }
                    try
                    {
                        MapProjectDto dto = ProjectJson.Read<MapProjectDto>(map);
                        templates.Add(new MapTemplate(System.IO.Path.GetFileName(folder), MapTemplateKind.Project, folder, $"a whole map: {dto.Scenario.Name}"));
                    }
                    catch (Exception exception) when (exception is MapProjectException or IOException or UnauthorizedAccessException)
                    {
                        unreadable?.Add($"{folder}: {exception.Message}");
                    }
                }
            }
            return [.. templates.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase),
                new MapTemplate(SandboxName, MapTemplateKind.Sandbox, null, "the editor's sandbox: a small map with one of every kind of thing")];
        }

        /// <summary>
        /// The template of that name, or null.
        /// </summary>
        public static MapTemplate? Find(string templatesFolder, string name) =>
            List(templatesFolder).FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <exception cref="MapProjectException">The file is no template.</exception>
        public static MapTemplateDto ReadSettings(string file)
        {
            MapTemplateDto dto = ProjectJson.Read<MapTemplateDto>(file);
            if (dto.FormatVersion > CurrentFormatVersion)
            {
                throw new MapProjectException($"The template {file} was made by a newer editor (format {dto.FormatVersion})");
            }
            return dto;
        }

        /// <summary>
        /// The settings of a new map from a JSON template, what it leaves out from <paramref name="defaults"/>.
        /// </summary>
        public static NewMapSettings Apply(MapTemplateDto template, NewMapSettings defaults) => defaults with
        {
            Size = template.Size ?? defaults.Size,
            Height = template.Height ?? defaults.Height,
            Water = template.Water ?? defaults.Water,
            WaterElevation = template.WaterElevation ?? defaults.WaterElevation,
            DeepElevation = template.DeepElevation ?? defaults.DeepElevation,
            AbyssElevation = template.AbyssElevation ?? defaults.AbyssElevation,
            Biome = template.Biome ?? defaults.Biome,
            Players = template.Players ?? defaults.Players,
            Layers = template.Strata is { Count: > 0 } strata ? [.. strata.Select(MapProjectMapping.ToModel)] : defaults.Layers,
            ExtraArmies = template.ExtraArmies is { } extra ? [.. extra] : defaults.ExtraArmies,
            Lighting = template.Lighting is LightingDto lighting ? MapProjectMapping.ToModel(lighting) : defaults.Lighting,
        };

        /// <summary>
        /// A new map from a template that holds a whole map (a project or the sandbox): a copy under
        /// a new name, at version 1, publishable, with the template's terrain, strata and things.
        /// </summary>
        /// <param name="catalog">The workspace's assets, for the sandbox.</param>
        /// <exception cref="ArgumentException">The template holds settings, not a map.</exception>
        /// <exception cref="MapProjectException">The template's project cannot be read.</exception>
        public static MapDocument Copy(MapTemplate template, string name, string author, AssetCatalog catalog)
        {
            MapDocument document = template.Kind switch
            {
                MapTemplateKind.Project => MapProjectStore.Load(template.Path!),
                MapTemplateKind.Sandbox => SandboxMap.Build(catalog),
                _ => throw new ArgumentException($"The template {template.Name} holds the settings of a new map, not a map", nameof(template)),
            };
            string title = name.Trim().Length == 0 ? "New map" : name.Trim();
            string folder = NewMap.FolderNameOf(title);
            return document with
            {
                Name = folder,
                FileName = folder,
                Version = 1,
                Scenario = document.Scenario with { Name = title, Author = author.Length > 0 ? author : document.Scenario.Author, Publishable = true },
            };
        }

        /// <summary>
        /// Writes the settings of a map as a JSON template: its size, its mean height, its water, its
        /// strata, the number of players, the extra armies and the lighting.
        /// </summary>
        /// <returns>The file.</returns>
        public static string SaveSettings(string templatesFolder, string name, MapDocument document, string? description = null)
        {
            Heightmap heights = document.Terrain.Heightmap;
            double sum = 0;
            for (int z = 0; z <= heights.Height; z++)
            {
                for (int x = 0; x <= heights.Width; x++)
                {
                    sum += heights.Samples[x, z];
                }
            }
            float mean = (float)(sum / ((heights.Width + 1.0) * (heights.Height + 1.0)) * heights.Scale);
            int size = Math.Max(document.Width, document.Height);
            MapTemplateDto dto = new MapTemplateDto
            {
                Description = description,
                Size = NewMap.Sizes.Contains(size) ? size : NewMap.Sizes.MinBy(s => Math.Abs(s - size)),
                Height = MathF.Round(mean, 2),
                Water = document.Water.HasWater,
                WaterElevation = document.Water.Elevation,
                DeepElevation = document.Water.ElevationDeep,
                AbyssElevation = document.Water.ElevationAbyss,
                Strata = [.. document.Terrain.Layers.Select(MapProjectMapping.ToDto)],
                Players = Math.Max(1, document.Scenario.PlayableArmies.Length),
                ExtraArmies = [.. document.Scenario.ExtraArmies],
                Lighting = MapProjectMapping.ToDto(document.Lighting),
            };
            string file = System.IO.Path.Combine(templatesFolder, FileName(name) + ".json");
            Directory.CreateDirectory(templatesFolder);
            ProjectJson.Write(file, dto);
            return file;
        }

        /// <summary>
        /// Writes a whole map as a template: a project in a folder of the templates folder, replacing
        /// one of that name.
        /// </summary>
        /// <returns>The folder.</returns>
        public static string SaveProject(string templatesFolder, string name, MapDocument document)
        {
            string folder = System.IO.Path.Combine(templatesFolder, FileName(name));
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
            MapProjectStore.Save(document, folder);
            return folder;
        }

        /// <summary>
        /// A name with the characters a file name cannot hold made dashes.
        /// </summary>
        public static string FileName(string name)
        {
            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            string cleaned = new string([.. name.Trim().Select(c => invalid.Contains(c) || c == '/' ? '-' : c)]);
            return cleaned.Length == 0 ? "Template" : cleaned;
        }

        private static string Summarize(MapTemplateDto dto)
        {
            List<string> parts = [];
            if (dto.Size is int size)
            {
                parts.Add($"{size * 5 / 256} km");
            }
            if (dto.Players is int players)
            {
                parts.Add(players == 1 ? "1 player" : $"{players} players");
            }
            if (dto.Water is bool water)
            {
                parts.Add(water ? "water" : "no water");
            }
            if (dto.Strata is { Count: > 0 })
            {
                parts.Add("its own strata");
            }
            else if (dto.Biome is string biome)
            {
                parts.Add(biome);
            }
            return dto.Description is { Length: > 0 } description ? $"{description} ({string.Join(", ", parts)})" : string.Join(", ", parts);
        }
    }
}
