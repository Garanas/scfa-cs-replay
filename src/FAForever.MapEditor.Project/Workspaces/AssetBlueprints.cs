using System.Text.RegularExpressions;
using FAForever.FileFormats.Blueprints;
using FAForever.MapEditor.Project.Files.Serialized;
using FAForever.MapEditor.Project.Workspaces.Serialized;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// Turns the game's prop and unit blueprints into the editor's (<see cref="PropBlueprintDto"/>,
    /// <see cref="UnitBlueprintDto"/>): what the editor needs to place and show them, with the paths
    /// of their meshes and textures filled in as the game fills them in.
    /// </summary>
    internal static partial class AssetBlueprints
    {
        /// <param name="exists">Whether the archives hold a file (<see cref="GameArchives.Contains"/>).</param>
        /// <param name="meshes">The mesh blueprints of the archives by id, for props and units that name one.</param>
        public static PropBlueprintDto ToDto(BlueprintProp prop, Func<string, bool> exists, IReadOnlyDictionary<string, BlueprintMesh> meshes) => new PropBlueprintDto
        {
            Id = prop.BlueprintId,
            Description = WithoutLoc(prop.Description),
            Categories = [.. prop.Categories],
            Size = Size(prop),
            UniformScale = prop.Display.UniformScale,
            Health = prop.Defense.MaxHealth ?? prop.Defense.Health,
            ReclaimMass = prop.Economy.ReclaimMassMax,
            ReclaimEnergy = prop.Economy.ReclaimEnergyMax,
            ReclaimTime = prop.Economy.ReclaimTime,
            Lods = Lods(prop, prop.Display.Mesh, prop.Display.MeshBlueprint, exists, meshes),
        };

        /// <inheritdoc cref="ToDto(BlueprintProp, Func{string, bool}, IReadOnlyDictionary{string, BlueprintMesh})"/>
        public static UnitBlueprintDto ToDto(BlueprintUnit unit, Func<string, bool> exists, IReadOnlyDictionary<string, BlueprintMesh> meshes) => new UnitBlueprintDto
        {
            Id = unit.BlueprintId,
            Name = WithoutLoc(unit.Description),
            Faction = BlueprintIds.GetFaction(unit.BlueprintId),
            TechLevel = unit.TechLevel,
            Categories = [.. unit.Categories],
            StrategicIcon = unit.StrategicIconName,
            Size = Size(unit),
            Footprint = unit.Footprint is { SizeX: double x, SizeZ: double z } ? new FootprintDto { SizeX = x, SizeZ = z } : null,
            UniformScale = unit.Display.UniformScale,
            Health = unit.Defense.MaxHealth ?? unit.Defense.Health,
            MassCost = unit.Economy.BuildCostMass,
            EnergyCost = unit.Economy.BuildCostEnergy,
            WreckageMass = unit.Wreckage?.MassMult,
            Lods = Lods(unit, unit.Display.Mesh, unit.Display.MeshBlueprint, exists, meshes),
        };

        /// <summary>
        /// <c>&lt;LOC uel0001_desc&gt;Armored Command Unit</c> becomes <c>Armored Command Unit</c>.
        /// </summary>
        public static string? WithoutLoc(string? text) => text is null ? null : LocTag().Replace(text, "");

        private static Vector3Dto? Size(BlueprintEntity entity) => entity is { SizeX: double x, SizeY: double y, SizeZ: double z }
            ? new Vector3Dto { X = (float)x, Y = (float)y, Z = (float)z }
            : null;

        /// <summary>
        /// The levels of detail as the game reads them: from the mesh blueprint named by
        /// <c>Display.MeshBlueprint</c>, else from <c>Display.Mesh</c> (<c>ExtractMeshBlueprint</c>
        /// in <c>lua/system/Blueprints.lua</c>). A level without a mesh name uses
        /// <c>&lt;name&gt;_lod&lt;n&gt;.scm</c>, and the textures default to <c>&lt;name&gt;_albedo.dds</c>,
        /// <c>_normalsts.dds</c> and <c>_specteam.dds</c> next to the blueprint, as the engine
        /// (and the FAF map editor) fill them in. A name without a leading slash is relative to the
        /// blueprint's folder and may step out of it (<c>../Brch01_s1_albedo.dds</c>, as the tree
        /// groups name the textures of their trees). A level whose mesh is missing is left out; so
        /// is a texture that is missing.
        /// </summary>
        private static List<MeshLodDto> Lods(Blueprint blueprint, BlueprintDisplayMesh? mesh, string? meshBlueprint, Func<string, bool> exists, IReadOnlyDictionary<string, BlueprintMesh> meshes)
        {
            string source = blueprint.Source;
            IReadOnlyList<BlueprintMeshLod> lods = mesh?.LODs ?? [];
            string? meshId = meshBlueprint?.ToLowerInvariant();
            if (meshId is not null && meshId.EndsWith(".bp", StringComparison.Ordinal))
            {
                meshId = meshId[..^3];
            }
            if (meshId is { Length: > 0 } && meshes.TryGetValue(meshId, out BlueprintMesh? named))
            {
                source = named.Source;
                lods = named.LODs;
            }

            string normalized = GameArchives.Normalize(source);
            string folder = normalized[..(normalized.LastIndexOf('/') + 1)];
            string name = BlueprintSuffix().Replace(normalized[folder.Length..], "");

            string? Find(string? given, string fallback)
            {
                string path = Resolve(given is { Length: > 0 }
                    ? given.StartsWith('/') ? GameArchives.Normalize(given) : GameArchives.Normalize(folder + given)
                    : folder + fallback);
                return exists(path) ? "/" + WorkspaceLayout.AssetOf(path) : null;
            }

            List<MeshLodDto> result = [];
            int count = Math.Max(lods.Count, 1);
            for (int i = 0; i < count; i++)
            {
                BlueprintMeshLod? lod = i < lods.Count ? lods[i] : null;
                if (Find(lod?.MeshName, $"{name}_lod{i}.scm") is not string scm)
                {
                    continue;
                }
                result.Add(new MeshLodDto
                {
                    Cutoff = lod?.LODCutoff,
                    Mesh = scm,
                    Shader = lod?.ShaderName,
                    Albedo = Find(lod?.AlbedoName, $"{name}_albedo.dds"),
                    Normals = Find(lod?.NormalsName, $"{name}_normalsts.dds"),
                    Specular = Find(lod?.SpecularName, $"{name}_specteam.dds"),
                });
            }
            return result;
        }

        /// <summary>
        /// A path without its <c>.</c> and <c>..</c> steps; a <c>..</c> above the root is dropped.
        /// </summary>
        internal static string Resolve(string path)
        {
            if (!path.Contains("./", StringComparison.Ordinal))
            {
                return path;
            }
            List<string> parts = [];
            foreach (string part in path.Split('/'))
            {
                if (part == "..")
                {
                    if (parts.Count > 0)
                    {
                        parts.RemoveAt(parts.Count - 1);
                    }
                }
                else if (part is not ("." or ""))
                {
                    parts.Add(part);
                }
            }
            return string.Join('/', parts);
        }

        [GeneratedRegex("^<LOC [^>]*>")]
        private static partial Regex LocTag();

        /// <summary>
        /// The kind at the end of a blueprint's file name, as the game strips it: <c>_[a-z]+%.bp$</c>.
        /// </summary>
        [GeneratedRegex("_[a-z]+\\.bp$")]
        private static partial Regex BlueprintSuffix();
    }
}
