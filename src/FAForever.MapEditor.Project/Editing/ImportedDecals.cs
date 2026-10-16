using System.Collections.Immutable;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// What an imported map's decals were in the editor (user story D-1): the decals stretched over the
    /// whole map, laid out as Kaali (kaali.v0004) and the editor's export lay them, become the map-wide
    /// slots (<see cref="MapDocument.WideDecals"/>); an albedo decal followed by the normals decal of the
    /// same set at the same place becomes one decal carrying the set (<see cref="Decal.Set"/>), as the
    /// export wrote it (<see cref="DecalSets.Expand"/>). So a map exported by the editor comes back as
    /// it was made, and Kaali's three decals fill the three slots.
    /// </summary>
    public static class ImportedDecals
    {
        /// <summary>
        /// The document with its map-wide decals in the slots and its decal sets as one decal each; the
        /// document itself when it has none of either.
        /// </summary>
        public static MapDocument Recognise(MapDocument document) => Sets(MapWide(document));

        /// <summary>
        /// Whether a decal lies over the whole map as a map-wide decal does: at the map's corner,
        /// unturned, as large as the map, drawn from any distance, its texture one of the map's own files.
        /// </summary>
        public static bool IsMapWide(MapDocument document, Decal decal) =>
            MathF.Abs(decal.Position.X) < 0.5f && MathF.Abs(decal.Position.Z) < 0.5f
            && MathF.Abs(decal.Rotation.X) < 1e-3f && MathF.Abs(decal.Rotation.Y) < 1e-3f && MathF.Abs(decal.Rotation.Z) < 1e-3f
            && MathF.Abs(decal.Scale.X - document.Width) < 1 && MathF.Abs(decal.Scale.Z - document.Height) < 1
            && decal.CutOffLod >= MapWideDecalsExport.CutOffLod / 2
            && decal.Textures.Length > 0 && OwnFile(document, decal.Textures[0]) is not null;

        /// <summary>
        /// The map-wide decals in the slots: the normals decal the normal slot, an albedo decal named for
        /// lighting or shadow (<c>map_lighting</c>) the shadow slot, the other albedo decal the albedo slot
        /// with its second texture as the specular. Each slot takes the first that fits; the slots name
        /// the map's own files, and the decals leave the list and their groups.
        /// </summary>
        private static MapDocument MapWide(MapDocument document)
        {
            if (!document.WideDecals.IsNone)
            {
                return document;
            }
            MapWideDecals wide = MapWideDecals.None;
            HashSet<int> taken = [];
            foreach (Decal decal in document.Decals)
            {
                if (!IsMapWide(document, decal))
                {
                    continue;
                }
                string texture = decal.Textures[0];
                string name = Path.GetFileNameWithoutExtension(texture).ToLowerInvariant();
                if (decal.Type == DecalType.Normals)
                {
                    if (wide.Normal.Source == MapWideSource.None)
                    {
                        wide = wide with { Normal = MapWideSlot.Of(texture) };
                        taken.Add(decal.Id);
                    }
                }
                else if (decal.Type == DecalType.Albedo && (name.Contains("lighting", StringComparison.Ordinal) || name.Contains("shadow", StringComparison.Ordinal)))
                {
                    if (wide.Shadow.Source == MapWideSource.None)
                    {
                        wide = wide with { Shadow = MapWideSlot.Of(texture) };
                        taken.Add(decal.Id);
                    }
                }
                else if (decal.Type == DecalType.Albedo && wide.Albedo.Source == MapWideSource.None)
                {
                    string? specular = decal.Textures.Length > 1 && decal.Textures[1].Length > 0 && OwnFile(document, decal.Textures[1]) is not null ? decal.Textures[1] : null;
                    if (decal.Textures.Length > 1 && decal.Textures[1].Length > 0 && specular is null)
                    {
                        // a specular of the game's: the slot could not name it, so the decal stays as it is
                        continue;
                    }
                    wide = wide with { Albedo = MapWideSlot.Of(texture), Specular = specular };
                    taken.Add(decal.Id);
                }
            }
            if (taken.Count == 0)
            {
                return document;
            }
            return Without(document, taken) with { WideDecals = wide };
        }

        /// <summary>
        /// Each albedo decal of a set (<c>rock01_albedo</c>) directly followed by its normals decal
        /// (<c>rock01_normals</c>) at the same place, turn, size and distances as one decal carrying the
        /// set, with the masks the map's own files have for it (never exported, so found beside it).
        /// </summary>
        private static MapDocument Sets(MapDocument document)
        {
            List<Decal> decals = new List<Decal>(document.Decals.Length);
            HashSet<int> merged = [];
            for (int i = 0; i < document.Decals.Length; i++)
            {
                Decal decal = document.Decals[i];
                if (i + 1 < document.Decals.Length && decal.Set is null && SetOf(document, decal, document.Decals[i + 1]) is DecalSet set)
                {
                    decals.Add(decal with { Set = set });
                    merged.Add(document.Decals[i + 1].Id);
                    i++;
                    continue;
                }
                decals.Add(decal);
            }
            if (merged.Count == 0)
            {
                return document;
            }
            return document with
            {
                Decals = [.. decals],
                DecalGroups = [.. document.DecalGroups.Select(group => group.DecalIds.Any(merged.Contains) ? group with { DecalIds = [.. group.DecalIds.Where(id => !merged.Contains(id))] } : group)],
            };
        }

        /// <summary>
        /// The set an albedo decal and the normals decal after it make, or null when they make none.
        /// </summary>
        private static DecalSet? SetOf(MapDocument document, Decal albedo, Decal normals)
        {
            if (albedo.Type != DecalType.Albedo || normals.Type != DecalType.Normals || albedo.Textures.Length == 0 || normals.Textures.Length == 0
                || albedo.Position != normals.Position || albedo.Rotation != normals.Rotation || albedo.Scale != normals.Scale
                || albedo.CutOffLod != normals.CutOffLod || albedo.NearCutOffLod != normals.NearCutOffLod || albedo.OwnerArmy != normals.OwnerArmy)
            {
                return null;
            }
            DecalSetFile a = DecalSets.PartOf(albedo.Textures[0]);
            DecalSetFile n = DecalSets.PartOf(normals.Textures[0]);
            if (a.Part != DecalSetPart.Albedo || n.Part != DecalSetPart.Normals || !string.Equals(a.Name, n.Name, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            // the masks of a set of the map's own files lie beside its textures in the project
            ImmutableSortedDictionary<string, string> masks = ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal);
            if (MapOwnAssets.InFolder(a.Name) is string own)
            {
                string folder = a.Name[..^own.Length];
                foreach (string file in document.Files.Keys)
                {
                    DecalSetFile part = DecalSets.PartOf(file);
                    if (part.Part == DecalSetPart.Mask && string.Equals(part.Name, own, StringComparison.OrdinalIgnoreCase) && !masks.ContainsKey(part.MaskName!))
                    {
                        masks = masks.Add(part.MaskName!, folder + file);
                    }
                }
            }
            return new DecalSet { Name = DecalEdits.TexturePath(a.Name), Normals = normals.Textures[0], Masks = masks };
        }

        private static MapDocument Without(MapDocument document, HashSet<int> ids) => document with
        {
            Decals = [.. document.Decals.Where(decal => !ids.Contains(decal.Id))],
            DecalGroups = [.. document.DecalGroups.Select(group => group.DecalIds.Any(ids.Contains) ? group with { DecalIds = [.. group.DecalIds.Where(id => !ids.Contains(id))] } : group)],
        };

        /// <summary>
        /// The map's own file a game path names (its path in the map's folder), or null.
        /// </summary>
        private static string? OwnFile(MapDocument document, string gamePath) =>
            MapOwnAssets.InFolder(gamePath) is string own ? document.Files.Keys.FirstOrDefault(file => string.Equals(file, own, StringComparison.OrdinalIgnoreCase)) : null;
    }
}
