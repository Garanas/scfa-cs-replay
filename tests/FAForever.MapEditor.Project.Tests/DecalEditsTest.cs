using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// Decals (user stories D-1, D-2): a new decal from a texture, sizes, distances, the order and
    /// the groups.
    /// </summary>
    [TestClass]
    public class DecalEditsTest
    {
        private static MapDocument original = null!;

        [ClassInitialize]
        public static void Initialize(TestContext _) => original = MapImporter.ImportFolder(ProjectFolders.HardFfa) with { Decals = [], DecalGroups = [] };

        /// <summary>
        /// The map with four decals, a, b, c, d from the bottom up.
        /// </summary>
        private static MapDocument WithFour(out ObjectRef[] decals)
        {
            Heightmap heights = original.Terrain.Heightmap;
            MapDocument document = original;
            List<ObjectRef> refs = [];
            foreach ((string name, float x) in new[] { ("a", 40f), ("b", 60f), ("c", 80f), ("d", 100f) })
            {
                document = document.PlaceDecal(DecalEdits.NewDecal(heights, $"env/common/decals/{name}_albedo.dds", x, 50, 8, 8, 0), out ObjectRef added);
                refs.Add(added);
            }
            decals = [.. refs];
            return document;
        }

        private static string Order(MapDocument document) =>
            string.Concat(document.Decals.Select(decal => Path.GetFileNameWithoutExtension(decal.Textures[0])[0]));

        [TestMethod]
        public void ANewDecalLiesCentredOnThePlaceWithItsTypeFromItsTexture()
        {
            Heightmap heights = original.Terrain.Heightmap;
            Decal albedo = DecalEdits.NewDecal(heights, "env/evergreen/decals/e_mud001_albedo.dds", 100, 120, 16, 8, MathF.PI / 2);
            Decal normals = DecalEdits.NewDecal(heights, "/env/common/decals/erosion018_normals.dds", 100, 120, 10, 10, 0, cutOffLod: 800);

            Assert.AreEqual(DecalType.Albedo, albedo.Type);
            CollectionAssert.AreEqual(new[] { "/env/evergreen/decals/e_mud001_albedo.dds", "" }, albedo.Textures.ToArray(), "the game's path, the second slot empty");
            Assert.AreEqual(new Vector3(16, 16, 8), albedo.Scale, "y follows x, as maps write it");
            Vector3 centre = MapObjects.DecalCentre(albedo);
            Assert.AreEqual(100, centre.X, 1e-3);
            Assert.AreEqual(120, centre.Z, 1e-3);
            Assert.AreEqual(heights.HeightAt(albedo.Position.X, albedo.Position.Z), albedo.Position.Y, 1e-3, "on the terrain at its corner");
            Assert.AreEqual(DecalEdits.DefaultCutOffLod, albedo.CutOffLod);
            Assert.AreEqual(-1, albedo.OwnerArmy);

            Assert.AreEqual(DecalType.Normals, normals.Type);
            Assert.AreEqual("/env/common/decals/erosion018_normals.dds", normals.Textures[0], "a path with its slash stays as it is");
            Assert.AreEqual(800, normals.CutOffLod);
        }

        [TestMethod]
        public void ScalingAndSizingKeepEachDecalsCentre()
        {
            MapDocument document = WithFour(out ObjectRef[] decals);
            Vector3 before = document.PlaceOf(decals[0])!.Value;

            MapDocument scaled = document.ScaleDecals([decals[0], ObjectRef.Prop(1)], 2, 1);
            Decal wide = (Decal)scaled.Find(decals[0])!;
            Assert.AreEqual(new Vector3(16, 16, 8), wide.Scale, "the width only");
            Assert.AreEqual(before.X, scaled.PlaceOf(decals[0])!.Value.X, 1e-3);
            Assert.AreEqual(before.Z, scaled.PlaceOf(decals[0])!.Value.Z, 1e-3);
            Assert.AreSame(document.Decals[1], scaled.Decals[1], "the others stay");

            MapDocument sized = scaled.SizeDecals([decals[0], decals[1]], null, 3);
            Assert.AreEqual(new Vector3(16, 16, 3), ((Decal)sized.Find(decals[0])!).Scale);
            Assert.AreEqual(new Vector3(8, 8, 3), ((Decal)sized.Find(decals[1])!).Scale);
            Assert.AreEqual(before.Z, sized.PlaceOf(decals[0])!.Value.Z, 1e-3);

            Assert.AreEqual(DecalEdits.SmallestSide, ((Decal)document.ScaleDecals([decals[2]], 0, 0).Find(decals[2])!).Scale.X, "never nothing");
        }

        [TestMethod]
        public void TheDistancesAreSetForManyDecalsAtOnce()
        {
            MapDocument document = WithFour(out ObjectRef[] decals);

            MapDocument changed = document.SetDecalLods(decals[..2], 1250, null).SetDecalLods([decals[1]], null, 20);

            Assert.AreEqual((1250f, 0f), (((Decal)changed.Find(decals[0])!).CutOffLod, ((Decal)changed.Find(decals[0])!).NearCutOffLod));
            Assert.AreEqual((1250f, 20f), (((Decal)changed.Find(decals[1])!).CutOffLod, ((Decal)changed.Find(decals[1])!).NearCutOffLod));
            Assert.AreEqual(DecalEdits.DefaultCutOffLod, ((Decal)changed.Find(decals[2])!).CutOffLod);
        }

        [TestMethod]
        public void TheOrderMovesDecalsUpAndDownTogether()
        {
            MapDocument document = WithFour(out ObjectRef[] d);
            Assert.AreEqual("abcd", Order(document));

            Assert.AreEqual("bacd", Order(document.OrderDecals([d[0]], DecalOrder.Forward)));
            Assert.AreEqual("cabd", Order(document.OrderDecals([d[0], d[1]], DecalOrder.Forward)), "a block goes up together");
            Assert.AreEqual("bdac", Order(document.OrderDecals([d[0], d[2]], DecalOrder.Forward).OrderDecals([d[0], d[2]], DecalOrder.Forward)));
            Assert.AreEqual("acbd", Order(document.OrderDecals([d[2]], DecalOrder.Backward)));
            Assert.AreEqual("bdac", Order(document.OrderDecals([d[2], d[0]], DecalOrder.ToFront)), "keeping their own order");
            Assert.AreEqual("bdac", Order(document.OrderDecals([d[0], d[2]], DecalOrder.ToFront)));
            Assert.AreEqual("bdac", Order(document.OrderDecals([d[1], d[3]], DecalOrder.ToBack)));
            Assert.AreSame(document, document.OrderDecals([d[3]], DecalOrder.Forward), "the top one stays on top");
            Assert.AreSame(document, document.OrderDecals([d[0]], DecalOrder.ToBack));
            Assert.AreSame(document, document.OrderDecals([ObjectRef.Prop(1)], DecalOrder.ToFront));
        }

        [TestMethod]
        public void GroupsAreMadeRenamedFilledEmptiedAndRemoved()
        {
            MapDocument document = WithFour(out ObjectRef[] d);

            document = document.AddDecalGroup("Roads", [d[0], d[1], ObjectRef.Prop(1), ObjectRef.Decal(999)], out DecalGroup roads);
            document = document.AddDecalGroup("Roads", [d[2]], out DecalGroup second);
            Assert.AreEqual("Roads", roads.Name);
            Assert.AreEqual("Roads 1", second.Name, "names stay apart");
            Assert.AreNotEqual(roads.Id, second.Id);
            CollectionAssert.AreEqual(new[] { d[0].Id, d[1].Id }, roads.DecalIds.ToArray(), "decals the map has, nothing else");

            document = document.AddToDecalGroup(roads.Id, [d[1], d[3]]).RemoveFromDecalGroup(roads.Id, [d[0]]);
            CollectionAssert.AreEqual(new[] { d[1], d[3] }, document.DecalsOf(document.FindDecalGroup(roads.Id)!).ToArray());

            document = document.RenameDecalGroup(second.Id, " Rivers ");
            Assert.AreEqual("Rivers", document.FindDecalGroup(second.Id)!.Name);
            Assert.ThrowsException<ArgumentException>(() => document.RenameDecalGroup(second.Id, " "));

            // removing a decal takes it out of its groups; removing a group keeps its decals
            document = document.RemoveObjects([d[3]]);
            CollectionAssert.AreEqual(new[] { d[1].Id }, document.FindDecalGroup(roads.Id)!.DecalIds.ToArray());
            document = document.RemoveDecalGroup(roads.Id);
            Assert.IsNull(document.FindDecalGroup(roads.Id));
            Assert.AreEqual(3, document.Decals.Length);
            Assert.ThrowsException<KeyNotFoundException>(() => document.RemoveDecalGroup(roads.Id));
        }

        [TestMethod]
        public void APickLeavesOutWhatIsNotIncluded()
        {
            MapDocument document = WithFour(out ObjectRef[] d);
            // b over a, in the middle of both
            document = document.PlaceDecal(DecalEdits.NewDecal(original.Terrain.Heightmap, "env/common/decals/e_albedo.dds", 40, 50, 8, 8, 0), out ObjectRef top);

            Assert.AreEqual(top, document.Nearest(40, 50, 0.5f, ObjectKinds.Decals), "the one on top");
            Assert.AreEqual(d[0], document.Nearest(40, 50, 0.5f, ObjectKinds.Decals, thing => thing != top), "the one under it, when the top one is hidden");
        }
    }
}
