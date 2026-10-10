using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class MarkerPresetsTest
    {
        [TestMethod]
        public void SavesMarkersAroundTheirMiddleAndPlacesThemElsewhereWithTheirLinks()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa) with { Markers = new MarkerSet() };
            Heightmap heights = document.Terrain.Heightmap;
            Vector3 On(float x, float z, float above = 0) => new Vector3(x, heights.HeightAt(x, z) + above, z);
            document = document
                .AddMarker(new ResourceMarker { Id = 0, Name = "Mass 01", Kind = ResourceKind.Mass, Position = On(40.5f, 40.5f), SpawnWithArmy = "ARMY_1" }, out Marker mass)
                .AddMarker(new PathNode { Id = 0, Name = "LandPN0", Layer = PathLayer.Land, Position = On(50, 40) }, out Marker a)
                .AddMarker(new PathNode { Id = 0, Name = "LandPN1", Layer = PathLayer.Land, Position = On(60, 40, 2) }, out Marker b)
                .AddMarker(new PathNode { Id = 0, Name = "LandPN2", Layer = PathLayer.Land, Position = On(70, 40) }, out Marker outside)
                .LinkPathNodes(a.Id, b.Id)
                .LinkPathNodes(b.Id, outside.Id)
                .AddChain(new Chain { Name = "Route", MarkerIds = [a.Id, b.Id] }, out _)
                .AddChain(new Chain { Name = "Longer", MarkerIds = [a.Id, outside.Id] }, out _);

            MarkerPresetSettings preset = MarkerPresets.Capture(document, [mass.Id, a.Id, b.Id]);
            Assert.AreEqual(3, preset.Count);
            Assert.IsNull(preset.Markers.Resources[0].SpawnWithArmy, "the army stays with the map");
            Assert.AreEqual(1, preset.Markers.Chains.Count, "only the chain wholly among them");

            MapDocument placed = MarkerPresets.Place(document, preset, 150, 100, out ImmutableArray<ObjectRef> added);

            Assert.AreEqual(3, added.Length);
            Marker[] markers = [.. added.Select(placed.Find).OfType<Marker>()];
            Vector2 middle = new Vector2(50.1667f, 40.1667f);
            Assert.AreEqual(150 + (40.5f - middle.X), markers[0].Position.X, 0.01, "around the place as around their middle");
            Assert.AreEqual("Mass 02", markers[0].Name, "a free name");
            PathNode[] nodes = [.. markers.OfType<PathNode>()];
            Assert.IsTrue(nodes[0].AdjacentTo.Contains(nodes[1].Id) && nodes[1].AdjacentTo.Contains(nodes[0].Id), "linked among each other");
            Assert.AreEqual(1, nodes[1].AdjacentTo.Length, "not to the node left out");
            Assert.AreEqual(heights.HeightAt(nodes[1].Position.X, nodes[1].Position.Z) + 2, nodes[1].Position.Y, 0.01, "as high above the terrain");
            Chain chain = placed.Markers.Chains.Single(c => c.MarkerIds.Contains(nodes[0].Id));
            CollectionAssert.AreEqual(new[] { nodes[0].Id, nodes[1].Id }, chain.MarkerIds.ToArray());
            Assert.AreEqual("Route 1", chain.Name);
        }

        [TestMethod]
        public void PlacesASpawnAsTheNextArmy()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            Spawn spawn = document.Markers.Spawns[0];
            MarkerPresetSettings preset = MarkerPresets.Capture(document, [spawn.Id]);

            MapDocument placed = MarkerPresets.Place(document, preset, 10, 10, out ImmutableArray<ObjectRef> added);

            Spawn copy = (Spawn)placed.Find(added[0])!;
            Assert.AreEqual(document.NextSpawnName(), copy.Name);
            CollectionAssert.Contains(placed.Scenario.PlayableArmies.ToArray(), copy.Name);
        }

        [TestMethod]
        public void RefusesNothing()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            Assert.ThrowsException<ArgumentException>(() => MarkerPresets.Capture(document, [-5]));
        }
    }

    [TestClass]
    public class AdaptiveGroupsTest
    {
        [TestMethod]
        public void FindsAndAddsTheGroupsOfTheAdaptiveScript()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            string army = AdaptiveGroups.CivilianArmy(document)!;
            Assert.IsNotNull(army);

            document = document.AddOptionalGroup(army, "Civilian_Defenses", 2, out string name)
                .AddOptionalGroup(army, "Optional_Wreckage", 3, out string wrecks)
                .AddOptionalGroup(army, "Civilians_Base", null, out _);

            Assert.AreEqual("Optional_Civilian_Defenses_2", name);
            Assert.AreEqual("Optional_Wreckage_3", wrecks);
            IReadOnlyList<OptionalGroup> groups = AdaptiveGroups.Find(document);
            CollectionAssert.AreEqual(
                new[] { ("Civilian_Defenses", (int?)2), ("Wreckage", 3), ("Civilians_Base", null) },
                groups.Select(group => (group.Option, group.Level)).ToArray());
            Assert.IsTrue(groups.All(group => group.Army == army && group.Units == 0));
            Assert.ThrowsException<InvalidOperationException>(() => document.AddOptionalGroup(army, "Civilian_Defenses", 2, out _));
            Assert.ThrowsException<ArgumentException>(() => AdaptiveGroups.GroupName("two words", 2));
            Assert.ThrowsException<ArgumentException>(() => AdaptiveGroups.GroupName("Wreckage", 1));
        }
    }

    [TestClass]
    public class WeatherStylesTest
    {
        [TestMethod]
        public void OffersTheGamesStylesAndTypes()
        {
            CollectionAssert.AreEqual(WeatherStyles.StyleNames.ToArray(), WeatherStyles.Styles.Keys.Order(StringComparer.Ordinal).ToArray());
            Assert.AreEqual(WeatherStyles.None, WeatherStyles.TypesOf("Evergreen")[0]);
            CollectionAssert.Contains(WeatherStyles.TypesOf("Evergreen").ToArray(), "RainClouds");
            CollectionAssert.DoesNotContain(WeatherStyles.TypesOf("Desert").ToArray(), "RainClouds");
            Assert.AreEqual(WeatherStyles.AllTypes, WeatherStyles.TypesOf("Martian"));
            Assert.AreEqual(4, WeatherStyles.NewDefinition("Weather Definition 01", Vector3.Zero).Types.Length);
            Assert.AreEqual(180, WeatherStyles.NewGenerator("Weather Generator 01", Vector3.Zero).CloudHeight);
        }
    }
}
