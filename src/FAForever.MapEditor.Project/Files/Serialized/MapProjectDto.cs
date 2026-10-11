namespace FAForever.MapEditor.Project.Files.Serialized
{
    /// <summary>
    /// <c>map.json</c>: the map's name, its scenario, and the settings of its terrain, light, water
    /// and sky. The images, props, decals, markers and armies have files of their own
    /// (<see cref="MapProjectLayout"/>).
    /// </summary>
    public sealed record MapProjectDto
    {
        /// <summary>
        /// 2: the scenario's teams and whether the map may go to the vault, and the project's
        /// <c>masks/</c> for tools. 3: the playable area by name, the rule a prop was placed by,
        /// the rules of masks (<c>masks/NAME.rules.json</c>) and props (<c>prop-rules.json</c>), and the
        /// height layers (<c>layers/</c>). 4: the map's symmetry and a curve's symmetry. 5: the walkability
        /// marked by hand (<c>walkability-marks.png</c>). 6: stacks of layers (<c>stacks/&lt;target&gt;.json</c>
        /// with the layers' PNGs in <c>stacks/&lt;target&gt;/</c>, <c>LayerStackDto</c>), the heightmap's
        /// as <c>stacks/heightmap.json</c> in place of <c>layers/</c>; the composite is still written
        /// (<c>heightmap.png</c>). A project of format 3 to 5 opens unchanged: its <c>layers/</c> becomes
        /// the heightmap's stack in memory, and its next save writes format 6. 7: a file the editor
        /// makes from other things is named so (<c>.gen</c> before the extension,
        /// <see cref="MapProjectLayout.Generated"/>): the grid of a target with a stack
        /// (<c>heightmap.gen.png</c>, <c>stratum-masks/stratum-2.gen.png</c>, <c>masks/forest.gen.png</c>),
        /// a rules or decal-masks layer's file, the draft for World Machine and Gaea; an image layer of a
        /// stack of coverage reads its file in the project and keeps no copy. A project of format 6
        /// opens unchanged (each file is read under either name) and its next save renames them. 8: the
        /// skybox as the game reads it: the cumulus clouds' three textures (<c>cumulusLightRamp</c>,
        /// <c>cumulusDispersionRamp</c>, <c>cumulusTexture</c>) where format 7 had <c>midColor</c>, no
        /// <c>clouds7</c>, and always four cirrus layers. A project of format 7 opens: its
        /// <c>midColor</c> and <c>clouds7</c> are left out and its cirrus layers made four. An editor
        /// reads every version up to its own. Optional additions to format 8, which a project may lack
        /// (no new version: an older project reads as if it held none of them): a unit group's
        /// <c>instance</c>, the unit preset it was placed from (<c>PresetInstanceDto</c>, user story U-9).
        /// </summary>
        public const int CurrentFormatVersion = 8;

        /// <summary>
        /// The layout of the project; <see cref="CurrentFormatVersion"/> for projects this library writes.
        /// </summary>
        public required int FormatVersion { get; init; }

        /// <summary>
        /// The name of the map's folder without its version, e.g. <c>theta_passage</c>.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// The name the map's files start with: <c>&lt;FileName&gt;.scmap</c>, <c>&lt;FileName&gt;_save.lua</c>, ...
        /// </summary>
        public required string FileName { get; init; }

        /// <summary>
        /// The version of the map; the folder gets <c>.v0004</c> from it.
        /// </summary>
        public int? Version { get; init; }

        public required ScenarioInfoDto Scenario { get; init; }

        public required TerrainDto Terrain { get; init; }

        public required LightingDto Lighting { get; init; }

        public required WaterDto Water { get; init; }

        public required SkyDto Sky { get; init; }

        /// <summary>
        /// The colours of the minimap; absent in version 53.
        /// </summary>
        public MinimapDto? Minimap { get; init; }

        /// <summary>
        /// The symmetry the editor mirrors edits by; absent when none was chosen.
        /// </summary>
        public SymmetryDto? Symmetry { get; init; }

        /// <summary>
        /// How the generated results are made (the Procedural mode); absent for the defaults.
        /// </summary>
        public ProceduralDto? Procedural { get; init; }
    }
}
