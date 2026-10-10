using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The edits of markers beyond adding and removing one (user stories R-1 to R-6): spawns and
    /// their armies, links between path nodes, chains, snapping, the slope under a deposit, and
    /// picking per kind.
    /// </summary>
    [TestClass]
    public class MarkerEditsTest
    {
        private static MapDocument original = null!;

        [ClassInitialize]
        public static void Initialize(TestContext _) => original = MapImporter.ImportFolder(ProjectFolders.HardFfa);

        private static Spawn SpawnNamed(MapDocument document, string name) => document.Markers.Spawns.Single(spawn => spawn.Name == name);

        [TestMethod]
        public void ANewSpawnIsTheNextPlayableArmySkippingTheNamesOfExtraArmies()
        {
            CollectionAssert.Contains(original.Scenario.ExtraArmies.ToArray(), "ARMY_9", "HardFFA keeps ARMY_9 for the civilians");
            Assert.AreEqual("ARMY_10", original.NextSpawnName());

            MapDocument document = original.AddMarker(new Spawn { Id = 0, Name = "", Position = new Vector3(20, 0, 30) }, out Marker added);

            Assert.AreEqual("ARMY_10", added.Name);
            Assert.AreEqual("ARMY_10", document.Scenario.PlayableArmies[^1], "it is playable, after the others");
            Army army = document.Armies.Single(a => a.Name == "ARMY_10");
            Assert.AreEqual("INITIAL", army.Units.Groups.Single().Name, "an army as the map editors write it");
            Assert.AreEqual(original.Armies.Length + 1, document.Armies.Length);

            MapDocument copy = document.AddMarker(added, out Marker pasted);
            Assert.AreEqual("ARMY_11", pasted.Name, "a copy of a spawn is the next army, not a second ARMY_10");
            Assert.AreEqual("ARMY_11", copy.Scenario.PlayableArmies[^1]);
        }

        [TestMethod]
        public void RemovingASpawnRemovesItsArmyAndNumbersTheSpawnsAfterItDown()
        {
            Spawn third = SpawnNamed(original, "ARMY_3");
            Spawn fourth = SpawnNamed(original, "ARMY_4");
            ResourceMarker mass = original.Markers.Resources[0];
            MapDocument adaptive = original
                .UpdateResource(mass.Name, resource => resource with { SpawnWithArmy = "ARMY_4" })
                .UpdateResource(original.Markers.Resources[1].Name, resource => resource with { SpawnWithArmy = "ARMY_3" });

            MapDocument removed = adaptive.RemoveMarker(third.Id);

            CollectionAssert.AreEqual(new[] { "ARMY_1", "ARMY_2", "ARMY_3", "ARMY_4", "ARMY_5", "ARMY_6", "ARMY_7" }, removed.Scenario.PlayableArmies.ToArray());
            Assert.AreEqual("ARMY_3", removed.FindMarker(fourth.Id)!.Name, "the spawn after it takes its number");
            Assert.AreEqual("ARMY_3", removed.Markers.Resources[0].SpawnWithArmy, "its deposits go with it");
            Assert.IsNull(removed.Markers.Resources[1].SpawnWithArmy, "the deposits of the removed army no longer wait for it");
            Assert.IsFalse(removed.Armies.Any(army => army.Name == "ARMY_8"), "eight armies became seven");
            Assert.IsTrue(removed.Armies.Any(army => army.Name == "ARMY_9"), "the civilians' army stays as it is");
            CollectionAssert.AreEqual(original.Scenario.ExtraArmies.ToArray(), removed.Scenario.ExtraArmies.ToArray());
            Assert.AreEqual("ARMY_8", removed.NextSpawnName());
        }

        [TestMethod]
        public void RenamingAnArmyRenamesItEverywhereAndRefusesATakenName()
        {
            MapDocument document = original with { Scenario = original.Scenario with { ArmyFactions = original.Scenario.ArmyFactions.Add("ARMY_2", ["uef"]) } };

            MapDocument renamed = document.RenameArmy("ARMY_2", "ARMY_20");

            Assert.AreEqual("ARMY_20", renamed.FindMarker(SpawnNamed(original, "ARMY_2").Id)!.Name);
            Assert.AreEqual("ARMY_20", renamed.Scenario.PlayableArmies[1]);
            Assert.IsTrue(renamed.Armies.Any(army => army.Name == "ARMY_20"));
            Assert.IsTrue(renamed.Scenario.ArmyFactions.ContainsKey("ARMY_20"));
            Assert.ThrowsException<InvalidOperationException>(() => original.RenameArmy("ARMY_2", "ARMY_9"));
        }

        [TestMethod]
        public void PathNodesOfOneLayerLinkBothWaysAndALayerChangeDropsTheLinks()
        {
            MapDocument document = original
                .AddMarker(new PathNode { Id = 0, Name = "LandPN1", Layer = PathLayer.Land, Position = new Vector3(10, 0, 10) }, out Marker a)
                .AddMarker(new PathNode { Id = 0, Name = "LandPN2", Layer = PathLayer.Land, Position = new Vector3(20, 0, 10) }, out Marker b)
                .AddMarker(new PathNode { Id = 0, Name = "WaterPN1", Layer = PathLayer.Water, Position = new Vector3(30, 0, 10) }, out Marker water);

            MapDocument linked = document.LinkPathNodes(a.Id, b.Id);
            CollectionAssert.AreEqual(new[] { b.Id }, ((PathNode)linked.FindMarker(a.Id)!).AdjacentTo.ToArray());
            CollectionAssert.AreEqual(new[] { a.Id }, ((PathNode)linked.FindMarker(b.Id)!).AdjacentTo.ToArray());
            Assert.IsTrue(linked.AreLinked(b.Id, a.Id));
            CollectionAssert.AreEqual(linked.Markers.PathNodes.ToArray(), linked.LinkPathNodes(a.Id, b.Id).Markers.PathNodes.ToArray(), "linking again changes nothing");
            Assert.ThrowsException<InvalidOperationException>(() => linked.LinkPathNodes(a.Id, water.Id), "only nodes of one layer link");
            Assert.ThrowsException<InvalidOperationException>(() => linked.LinkPathNodes(a.Id, a.Id));

            MapDocument unlinked = linked.UnlinkPathNodes(b.Id, a.Id);
            Assert.IsFalse(unlinked.AreLinked(a.Id, b.Id));

            MapDocument moved = linked.SetPathLayer(a.Id, PathLayer.Amphibious);
            Assert.AreEqual(PathLayer.Amphibious, ((PathNode)moved.FindMarker(a.Id)!).Layer);
            Assert.IsFalse(moved.AreLinked(a.Id, b.Id), "a link across layers goes");
        }

        [TestMethod]
        public void ChainsAreAddedChangedRenamedAndRemovedByName()
        {
            int[] ids = [.. original.Markers.Resources.Take(3).Select(resource => resource.Id)];
            MapDocument document = original
                .AddChain(new Chain { Name = "", MarkerIds = [ids[0], ids[1], 9999] }, out Chain first)
                .AddChain(new Chain { Name = "Chain_1", MarkerIds = [ids[2]] }, out Chain second);

            Assert.AreEqual("Chain_1", first.Name);
            CollectionAssert.AreEqual(new[] { ids[0], ids[1] }, first.MarkerIds.ToArray(), "an id no marker has is left out");
            Assert.AreEqual("Chain_2", second.Name, "a name that is taken gets the next number");

            MapDocument reordered = document.UpdateChain("Chain_1", chain => chain with { MarkerIds = [ids[1], ids[0]] });
            CollectionAssert.AreEqual(new[] { ids[1], ids[0] }, reordered.Markers.Chains[0].MarkerIds.ToArray());
            MapDocument renamed = reordered.UpdateChain("Chain_1", chain => chain with { Name = "Patrol_Chain" });
            Assert.AreEqual("Patrol_Chain", renamed.Markers.Chains[0].Name);
            Assert.ThrowsException<InvalidOperationException>(() => renamed.UpdateChain("Patrol_Chain", chain => chain with { Name = "Chain_2" }));
            Assert.ThrowsException<InvalidOperationException>(() => renamed.UpdateChain("Patrol_Chain", chain => chain with { Name = "" }));

            MapDocument withoutMarker = renamed.RemoveMarker(ids[0]);
            CollectionAssert.AreEqual(new[] { ids[1] }, withoutMarker.Markers.Chains[0].MarkerIds.ToArray(), "a removed marker leaves its chains");
            MapDocument removed = renamed.RemoveChain("Patrol_Chain");
            Assert.AreEqual("Chain_2", removed.Markers.Chains.Single().Name);
            Assert.ThrowsException<KeyNotFoundException>(() => removed.RemoveChain("Patrol_Chain"));
        }

        [TestMethod]
        public void PlacesSnapToHalfOgridsOrToTheCentresOfOgrids()
        {
            Assert.AreEqual(10.37f, MarkerEdits.Snap(10.37f, MarkerSnap.Off));
            Assert.AreEqual(10.5f, MarkerEdits.Snap(10.37f, MarkerSnap.Half));
            Assert.AreEqual(10f, MarkerEdits.Snap(10.2f, MarkerSnap.Half));
            Assert.AreEqual(10.5f, MarkerEdits.Snap(10.99f, MarkerSnap.Centre), "deposits sit on an ogrid's centre");
            Assert.AreEqual(10.5f, MarkerEdits.Snap(10.01f, MarkerSnap.Centre));
        }

        [TestMethod]
        public void SteepGroundUnderADepositIsFound()
        {
            ResourceMarker mass = original.Markers.Resources.First(resource => resource.Kind == ResourceKind.Mass);
            Heightmap heights = original.Terrain.Heightmap;
            Assert.IsNull(MarkerEdits.Steepness(heights, ResourceKind.Mass, mass.Position.X, mass.Position.Z), "the map's deposits lie on flat ground");

            // a cliff of 20 game units through the place
            int x = (int)mass.Position.X;
            int z = (int)mass.Position.Z;
            TiledBuffer<ushort>.Builder builder = heights.Samples.ToBuilder();
            builder[x + 1, z] = (ushort)(heights.Samples[x + 1, z] + 20 / heights.Scale);
            Heightmap cliff = heights with { Samples = builder.ToBuffer() };
            Assert.AreEqual(20, MarkerEdits.Steepness(cliff, ResourceKind.Mass, mass.Position.X, mass.Position.Z)!.Value, 1);
        }

        [TestMethod]
        public void PickingAndBoxesLeaveOutTheHiddenKindsOfMarkers()
        {
            ResourceMarker mass = original.Markers.Resources.First(resource => resource.Kind == ResourceKind.Mass);
            Assert.AreEqual(ObjectRef.Marker(mass.Id), original.Nearest(mass.Position.X, mass.Position.Z, 0.5f, ObjectKinds.Markers));
            Assert.AreNotEqual(ObjectRef.Marker(mass.Id), original.Nearest(mass.Position.X, mass.Position.Z, 0.5f, ObjectKinds.Markers, MarkerKinds.All & ~MarkerKinds.Mass));

            ObjectRef[] all = [.. original.Inside(0, 0, original.Width, original.Height, ObjectKinds.Markers, MarkerKinds.Spawns)];
            Assert.AreEqual(original.Markers.Spawns.Length, all.Length, "only the spawns");
            Assert.AreEqual(original.Markers.Spawns.Length, original.All(ObjectKinds.Markers, MarkerKinds.Spawns).Count());
        }
    }
}
