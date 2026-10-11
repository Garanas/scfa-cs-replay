using FAForever.MapEditor.Project.Workspaces.Serialized;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// The folder with the user's map projects and the assets converted from the game's archives
    /// (<see cref="WorkspaceLayout"/>), and where the game, FAF and the vault are.
    /// </summary>
    public sealed record Workspace
    {
        public const int CurrentFormatVersion = 1;

        public required string Folder { get; init; }

        /// <summary>
        /// The game's installation, with <c>gamedata/env.scd</c> and <c>gamedata/units.scd</c>.
        /// </summary>
        public required string GameFolder { get; init; }

        /// <summary>
        /// FAF's <c>gamedata</c> folder (<c>env.nx2</c>, <c>units.nx2</c>); null without FAF.
        /// </summary>
        public string? FafDataFolder { get; init; }

        /// <summary>
        /// The vault on disk, e.g. <c>D:\faf-vault</c>.
        /// </summary>
        public required string VaultFolder { get; init; }

        /// <summary>
        /// The Procedural settings a map made or imported in the workspace starts with: what regenerates
        /// on its own and what the export makes (<c>workspace.json</c>'s <c>procedural</c>).
        /// </summary>
        public ProceduralDefaults Procedural { get; init; } = ProceduralDefaults.Default;

        public string ProjectsFolder => Path.Combine(Folder, WorkspaceLayout.Projects);

        public string AssetsFolder => Path.Combine(Folder, WorkspaceLayout.Assets);

        /// <summary>
        /// The map maker's brush shapes (greyscale PNGs), chosen per workspace.
        /// </summary>
        public string BrushesFolder => Path.Combine(Folder, WorkspaceLayout.Brushes);

        /// <summary>
        /// The saved settings to apply again (user story F-10), a folder per kind (<see cref="PresetKinds"/>).
        /// </summary>
        public string PresetsFolder => Path.Combine(Folder, WorkspaceLayout.Presets);

        /// <summary>
        /// The map templates a new map starts from (<see cref="MapTemplates"/>, user story M-2): the
        /// presets' <c>maps</c>.
        /// </summary>
        public string TemplatesFolder => Path.Combine(PresetsFolder, PresetKinds.Maps);

        /// <summary>
        /// The Lua plugins (user story F-15), a folder each with its <c>plugin.lua</c>.
        /// </summary>
        public string PluginsFolder => Path.Combine(Folder, WorkspaceLayout.Plugins);

        /// <summary>
        /// The presets of <see cref="PresetsFolder"/>.
        /// </summary>
        public PresetLibrary Presets => new PresetLibrary(PresetsFolder);

        /// <summary>
        /// Where maps are exported to: the vault's <c>maps</c> folder, where the game and the FAF
        /// client find them.
        /// </summary>
        public string MapsFolder => Path.Combine(VaultFolder, "maps");

        /// <summary>
        /// Whether <paramref name="folder"/> holds a workspace.
        /// </summary>
        public static bool Exists(string folder) => File.Exists(Path.Combine(folder, WorkspaceLayout.Settings));

        /// <exception cref="MapProjectException">The folder holds no readable workspace.</exception>
        public static Workspace Open(string folder)
        {
            WorkspaceDto dto = ProjectJson.Read<WorkspaceDto>(Path.Combine(folder, WorkspaceLayout.Settings));
            if (dto.FormatVersion > CurrentFormatVersion)
            {
                throw new MapProjectException($"The workspace in {folder} was made by a newer editor (format {dto.FormatVersion})");
            }
            return new Workspace
            {
                Folder = Path.GetFullPath(folder),
                GameFolder = dto.GameFolder,
                FafDataFolder = dto.FafDataFolder,
                VaultFolder = dto.VaultFolder,
                Procedural = ToModel(dto.Procedural),
            };
        }

        private static ProceduralDefaults ToModel(WorkspaceProceduralDto? dto) => dto is null
            ? ProceduralDefaults.Default
            : new ProceduralDefaults
            {
                AutoGenerate = dto.AutoGenerate is List<string> auto
                    ? System.Collections.Immutable.ImmutableSortedSet.CreateRange(StringComparer.Ordinal, auto)
                    : ProceduralDefaults.Default.AutoGenerate,
                OnExport = dto.OnExport is List<string> export
                    ? System.Collections.Immutable.ImmutableSortedSet.CreateRange(StringComparer.Ordinal, export)
                    : ProceduralDefaults.Default.OnExport,
            };

        private static WorkspaceProceduralDto? ToDto(ProceduralDefaults defaults) => defaults.IsDefault
            ? null
            : new WorkspaceProceduralDto
            {
                AutoGenerate = defaults.AutoGenerate.SetEquals(ProceduralDefaults.Default.AutoGenerate) ? null : [.. defaults.AutoGenerate],
                OnExport = defaults.OnExport.SetEquals(ProceduralDefaults.Default.OnExport) ? null : [.. defaults.OnExport],
            };

        /// <summary>
        /// Sets up a workspace in <paramref name="folder"/>, which must be absent, empty or a
        /// workspace already (whose settings are then replaced; its projects and assets stay), with
        /// its sandbox (<see cref="SandboxMap"/>) when that is missing and the example unit presets
        /// (<see cref="ExampleUnitPresets"/>).
        /// </summary>
        /// <exception cref="MapProjectException">The folder holds something else.</exception>
        public static Workspace Create(string folder, string gameFolder, string? fafDataFolder, string vaultFolder)
        {
            if (Directory.Exists(folder) && !Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            {
                throw new MapProjectException($"{folder} is not empty and holds no workspace");
            }

            Workspace workspace = new Workspace
            {
                Folder = Path.GetFullPath(folder),
                GameFolder = Path.GetFullPath(gameFolder),
                FafDataFolder = fafDataFolder is null ? null : Path.GetFullPath(fafDataFolder),
                VaultFolder = Path.GetFullPath(vaultFolder),
            };
            workspace.Save();
            SandboxMap.Ensure(workspace);
            ExampleUnitPresets.Ensure(workspace);
            return workspace;
        }

        /// <summary>
        /// Writes <c>workspace.json</c> and makes the folders and the <c>.gitignore</c> that are missing.
        /// </summary>
        public void Save()
        {
            Directory.CreateDirectory(Folder);
            foreach (string name in WorkspaceLayout.Folders)
            {
                Directory.CreateDirectory(Path.Combine(Folder, name));
            }

            string gitIgnore = Path.Combine(Folder, WorkspaceLayout.GitIgnore);
            if (!File.Exists(gitIgnore))
            {
                File.WriteAllText(gitIgnore, $"# converted from the game's archives: made again by the editor, and not ours to share\n/{WorkspaceLayout.Assets}/\n/{WorkspaceLayout.AssetsInProgress}/\n");
            }

            ProjectJson.Write(Path.Combine(Folder, WorkspaceLayout.Settings), new WorkspaceDto
            {
                FormatVersion = CurrentFormatVersion,
                GameFolder = GameFolder,
                FafDataFolder = FafDataFolder,
                VaultFolder = VaultFolder,
                Procedural = ToDto(Procedural),
            });
        }

        /// <summary>
        /// The archives the assets are converted from (<see cref="GameArchives.Find"/>).
        /// </summary>
        /// <exception cref="MapProjectException">The game folder lacks one of its archives.</exception>
        public IReadOnlyList<string> FindArchives() => GameArchives.Find(GameFolder, FafDataFolder);

        /// <summary>
        /// The converted file of a game path, e.g. <c>/env/Evergreen/layers/Eg_Grass001_albedo.dds</c>
        /// gives <c>assets/env/evergreen/layers/eg_grass001_albedo.png</c>; null when there is none.
        /// </summary>
        public string? FindAsset(string gamePath)
        {
            string path = Path.Combine(AssetsFolder, WorkspaceLayout.AssetOf(gamePath).Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// Whether the assets are there and converted from the archives as they are now, by this
        /// version of the editor.
        /// </summary>
        public AssetCheck CheckAssets() => AssetCheck.Of(this);
    }
}
