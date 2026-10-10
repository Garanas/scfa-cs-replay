using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project.Files
{
    // The scenario, the armies and the markers, which differ in memory and on disk: in memory
    // markers have ids and refer to each other by them, on disk by name.
    internal static partial class MapProjectMapping
    {
        public static ScenarioInfoDto ToDto(ScenarioInfo source) => new ScenarioInfoDto
        {
            Name = source.Name,
            Description = source.Description,
            Author = source.Author,
            Type = source.Type,
            PreviewPath = source.PreviewPath,
            NoRushRadius = source.NoRushRadius,
            AdaptiveMap = source.AdaptiveMap,
            Reclaim = source.Reclaim is null ? null : ToDto(source.Reclaim),
            PlayableArmies = [.. source.PlayableArmies],
            ExtraArmies = [.. source.ExtraArmies],
            ArmyFactions = source.ArmyFactions.IsEmpty
                ? null
                : source.ArmyFactions.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<string>)[.. entry.Value]),
            Teams = source.Teams.IsEmpty ? null : [.. source.Teams.Select(team => new TeamDto { Name = team.Name, Armies = [.. team.Armies] })],
            Publishable = source.Publishable ? null : false,
        };

        public static ScenarioInfo ToModel(ScenarioInfoDto source) => new ScenarioInfo
        {
            Name = source.Name,
            Description = source.Description,
            Author = source.Author,
            Type = source.Type,
            PreviewPath = source.PreviewPath,
            NoRushRadius = source.NoRushRadius,
            AdaptiveMap = source.AdaptiveMap,
            Reclaim = source.Reclaim is null ? null : ToModel(source.Reclaim),
            PlayableArmies = [.. source.PlayableArmies],
            ExtraArmies = [.. source.ExtraArmies],
            ArmyFactions = (source.ArmyFactions ?? []).ToImmutableSortedDictionary(entry => entry.Key, entry => entry.Value.ToImmutableArray(), StringComparer.Ordinal),
            Teams = [.. (source.Teams ?? []).Select(team => new Team { Name = team.Name, Armies = [.. team.Armies] })],
            Publishable = source.Publishable ?? true,
        };

        public static ArmyDto ToDto(Army source) => new ArmyDto
        {
            Name = source.Name,
            Plans = source.Plans,
            Color = source.Color,
            Faction = source.Faction,
            Alliances = new Dictionary<string, string>(source.Alliances),
            Units = ToDto(source.Units),
        };

        public static Army ToModel(ArmyDto source) => new Army
        {
            Name = source.Name,
            Plans = source.Plans,
            Color = source.Color,
            Faction = source.Faction,
            Alliances = source.Alliances.ToImmutableSortedDictionary(StringComparer.Ordinal),
            Units = ToModel(source.Units),
        };

        /// <summary>
        /// The markers as the file names them: links and chains by the names of the markers.
        /// </summary>
        public static MarkerSetDto ToDto(MarkerSet source)
        {
            Dictionary<int, string> names = source.All.ToDictionary(marker => marker.Id, marker => marker.Name);
            IReadOnlyList<string> Names(IEnumerable<int> ids) => [.. ids.Where(names.ContainsKey).Select(id => names[id])];

            return new MarkerSetDto
            {
                Spawns = [.. source.Spawns.Select(spawn => new SpawnDto
                {
                    Army = spawn.Name,
                    Position = Dto(spawn.Position),
                    Orientation = Optional(spawn.Orientation),
                    NoRushOffset = spawn.NoRushOffset is Vector2 offset ? Dto(offset) : null,
                })],
                Resources = [.. source.Resources.Select(resource => new ResourceMarkerDto
                {
                    Name = resource.Name,
                    Kind = resource.Kind,
                    Position = Dto(resource.Position),
                    Orientation = Optional(resource.Orientation),
                    SpawnWithArmy = resource.SpawnWithArmy,
                })],
                PathNodes = [.. source.PathNodes.Select(node => new PathNodeDto
                {
                    Name = node.Name,
                    Layer = node.Layer,
                    Position = Dto(node.Position),
                    Orientation = Optional(node.Orientation),
                    AdjacentTo = Names(node.AdjacentTo),
                })],
                AiMarkers = [.. source.AiMarkers.Select(marker => new AiMarkerDto
                {
                    Name = marker.Name,
                    Type = marker.Type,
                    Position = Dto(marker.Position),
                    Orientation = Optional(marker.Orientation),
                })],
                WeatherDefinitions = [.. source.WeatherDefinitions.Select(weather => new WeatherDefinitionDto
                {
                    Name = weather.Name,
                    Position = Dto(weather.Position),
                    Orientation = Optional(weather.Orientation),
                    MapStyle = weather.MapStyle,
                    Description = weather.Description,
                    DriftDirection = Dto(weather.DriftDirection),
                    Types = [.. weather.Types.Select(chance => ToDto(chance))],
                })],
                WeatherGenerators = [.. source.WeatherGenerators.Select(generator => new WeatherGeneratorDto
                {
                    Name = generator.Name,
                    Position = Dto(generator.Position),
                    Orientation = Optional(generator.Orientation),
                    ForceType = generator.ForceType,
                    SpawnChance = generator.SpawnChance,
                    CloudCount = generator.CloudCount,
                    CloudCountRange = generator.CloudCountRange,
                    CloudEmitterScale = generator.CloudEmitterScale,
                    CloudEmitterScaleRange = generator.CloudEmitterScaleRange,
                    CloudHeight = generator.CloudHeight,
                    CloudHeightRange = generator.CloudHeightRange,
                    CloudSpread = generator.CloudSpread,
                })],
                Areas = [.. source.Areas.Select(area => ToDto(area))],
                Chains = [.. source.Chains.Select(chain => new ChainDto { Name = chain.Name, Markers = Names(chain.MarkerIds) })],
            };
        }

        /// <summary>
        /// The markers of the file, numbered from 1 in the order of the file, with their links and
        /// chains turned from names into ids. A name no marker has is left out.
        /// </summary>
        public static MarkerSet ToModel(MarkerSetDto source)
        {
            int next = 1;
            ImmutableArray<Spawn> spawns = [.. source.Spawns.Select(spawn => new Spawn
            {
                Id = next++,
                Name = spawn.Army,
                Position = Model(spawn.Position),
                Orientation = spawn.Orientation is null ? Vector3.Zero : Model(spawn.Orientation),
                NoRushOffset = spawn.NoRushOffset is null ? null : Model(spawn.NoRushOffset),
            })];
            ImmutableArray<ResourceMarker> resources = [.. source.Resources.Select(resource => new ResourceMarker
            {
                Id = next++,
                Name = resource.Name,
                Kind = resource.Kind,
                Position = Model(resource.Position),
                Orientation = resource.Orientation is null ? Vector3.Zero : Model(resource.Orientation),
                SpawnWithArmy = resource.SpawnWithArmy,
            })];
            // the ids of the nodes first, then their links
            int firstNode = next;
            next += source.PathNodes.Count;
            ImmutableArray<AiMarker> aiMarkers = [.. source.AiMarkers.Select(marker => new AiMarker
            {
                Id = next++,
                Name = marker.Name,
                Type = marker.Type,
                Position = Model(marker.Position),
                Orientation = marker.Orientation is null ? Vector3.Zero : Model(marker.Orientation),
            })];
            ImmutableArray<WeatherDefinition> weatherDefinitions = [.. source.WeatherDefinitions.Select(weather => new WeatherDefinition
            {
                Id = next++,
                Name = weather.Name,
                Position = Model(weather.Position),
                Orientation = weather.Orientation is null ? Vector3.Zero : Model(weather.Orientation),
                MapStyle = weather.MapStyle,
                Description = weather.Description,
                DriftDirection = Model(weather.DriftDirection),
                Types = [.. weather.Types.Select(chance => ToModel(chance))],
            })];
            ImmutableArray<WeatherGenerator> weatherGenerators = [.. source.WeatherGenerators.Select(generator => new WeatherGenerator
            {
                Id = next++,
                Name = generator.Name,
                Position = Model(generator.Position),
                Orientation = generator.Orientation is null ? Vector3.Zero : Model(generator.Orientation),
                ForceType = generator.ForceType,
                SpawnChance = generator.SpawnChance,
                CloudCount = generator.CloudCount,
                CloudCountRange = generator.CloudCountRange,
                CloudEmitterScale = generator.CloudEmitterScale,
                CloudEmitterScaleRange = generator.CloudEmitterScaleRange,
                CloudHeight = generator.CloudHeight,
                CloudHeightRange = generator.CloudHeightRange,
                CloudSpread = generator.CloudSpread,
            })];

            Dictionary<string, int> ids = new Dictionary<string, int>(StringComparer.Ordinal);
            void Name(string name, int id) => ids.TryAdd(name, id);
            foreach (Spawn spawn in spawns) Name(spawn.Name, spawn.Id);
            foreach (ResourceMarker resource in resources) Name(resource.Name, resource.Id);
            for (int i = 0; i < source.PathNodes.Count; i++) Name(source.PathNodes[i].Name, firstNode + i);
            foreach (AiMarker marker in aiMarkers) Name(marker.Name, marker.Id);
            foreach (WeatherDefinition weather in weatherDefinitions) Name(weather.Name, weather.Id);
            foreach (WeatherGenerator generator in weatherGenerators) Name(generator.Name, generator.Id);
            ImmutableArray<int> Ids(IEnumerable<string> names) => [.. names.Where(ids.ContainsKey).Select(name => ids[name])];

            return new MarkerSet
            {
                Spawns = spawns,
                Resources = resources,
                PathNodes = [.. source.PathNodes.Select((node, index) => new PathNode
                {
                    Id = firstNode + index,
                    Name = node.Name,
                    Layer = node.Layer,
                    Position = Model(node.Position),
                    Orientation = node.Orientation is null ? Vector3.Zero : Model(node.Orientation),
                    AdjacentTo = Ids(node.AdjacentTo),
                })],
                AiMarkers = aiMarkers,
                WeatherDefinitions = weatherDefinitions,
                WeatherGenerators = weatherGenerators,
                Areas = [.. source.Areas.Select(area => ToModel(area))],
                Chains = [.. source.Chains.Select(chain => new Chain { Name = chain.Name, MarkerIds = Ids(chain.Markers) })],
            };
        }
    }
}
