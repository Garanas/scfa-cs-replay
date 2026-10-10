using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// What a markers preset holds (user story F-10): markers as a project's <c>markers.json</c>
    /// writes them (links and chains by name), placed relative to their middle, each height above
    /// the terrain under it.
    /// </summary>
    public sealed record MarkerPresetSettings(MarkerSetDto Markers, int Count);

    /// <summary>
    /// Markers presets (kind <c>markers</c>): a set of markers, such as a spawn with its deposits or
    /// a piece of a path network, saved with their places relative to their middle and placed again
    /// elsewhere, on this map or another.
    /// </summary>
    public static class MarkerPresets
    {
        public const string Kind = "markers";

        /// <summary>
        /// The markers of those ids as a preset: path nodes keep their links among each other, a
        /// chain whose markers are all among them comes along, the army deposits of an adaptive map
        /// wait for is left out (another map has its own armies).
        /// </summary>
        /// <exception cref="ArgumentException">None of the ids is a marker of the map.</exception>
        public static MarkerPresetSettings Capture(MapDocument document, IEnumerable<int> ids)
        {
            HashSet<int> chosen = [.. ids];
            Marker[] markers = [.. document.Markers.All.Where(marker => chosen.Contains(marker.Id))];
            if (markers.Length == 0)
            {
                throw new ArgumentException("No marker to save: select markers first");
            }
            Vector2 middle = new Vector2(markers.Average(marker => marker.Position.X), markers.Average(marker => marker.Position.Z));
            Heightmap heights = document.Terrain.Heightmap;
            T Relative<T>(T marker) where T : Marker => marker with
            {
                Position = new Vector3(
                    marker.Position.X - middle.X,
                    marker.Position.Y - heights.HeightAt(marker.Position.X, marker.Position.Z),
                    marker.Position.Z - middle.Y),
            };
            MarkerSet source = document.Markers;
            MarkerSet set = new MarkerSet
            {
                Spawns = [.. source.Spawns.Where(m => chosen.Contains(m.Id)).Select(Relative)],
                Resources = [.. source.Resources.Where(m => chosen.Contains(m.Id)).Select(m => Relative(m) with { SpawnWithArmy = null })],
                PathNodes = [.. source.PathNodes.Where(m => chosen.Contains(m.Id)).Select(m => Relative(m) with { AdjacentTo = [.. m.AdjacentTo.Where(chosen.Contains)] })],
                AiMarkers = [.. source.AiMarkers.Where(m => chosen.Contains(m.Id)).Select(Relative)],
                WeatherDefinitions = [.. source.WeatherDefinitions.Where(m => chosen.Contains(m.Id)).Select(Relative)],
                WeatherGenerators = [.. source.WeatherGenerators.Where(m => chosen.Contains(m.Id)).Select(Relative)],
                Chains = [.. source.Chains.Where(chain => !chain.MarkerIds.IsEmpty && chain.MarkerIds.All(chosen.Contains))],
            };
            return new MarkerPresetSettings(MapProjectMapping.ToDto(set), markers.Length);
        }

        /// <summary>
        /// Places a preset's markers around a place, each as high above the terrain as it was saved,
        /// under names the map does not have (a spawn becomes the next playable army), with their
        /// links and chains.
        /// </summary>
        /// <param name="added">The markers added, in the preset's order.</param>
        public static MapDocument Place(MapDocument document, MarkerPresetSettings settings, float x, float z, out ImmutableArray<ObjectRef> added)
        {
            MarkerSet set = MapProjectMapping.ToModel(settings.Markers);
            Heightmap heights = document.Terrain.Heightmap;
            Marker[] markers = [.. set.All.Select(marker =>
            {
                float px = Math.Clamp(x + marker.Position.X, 0, document.Width);
                float pz = Math.Clamp(z + marker.Position.Z, 0, document.Height);
                return marker with { Position = new Vector3(px, heights.HeightAt(px, pz) + marker.Position.Y, pz) };
            })];
            document = document.Paste(new ObjectClipboard([.. markers.Cast<object>()], new Vector2(x, z)), 0, 0, out added);
            Dictionary<int, int> ids = [];
            for (int i = 0; i < markers.Length && i < added.Length; i++)
            {
                ids[markers[i].Id] = added[i].Id;
            }
            foreach (Chain chain in set.Chains)
            {
                document = document.AddChain(chain with { MarkerIds = [.. chain.MarkerIds.Where(ids.ContainsKey).Select(id => ids[id])] }, out _);
            }
            return document;
        }
    }
}
