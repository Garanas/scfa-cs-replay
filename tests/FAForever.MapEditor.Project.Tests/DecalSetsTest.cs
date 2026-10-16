using System.Collections.Immutable;
using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using ImageMagick;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// Decal sets (user story D-1): grouped by name and suffix, placed as one decal, written as one
    /// decal per texture; and the decal-masks layers that draw their masks.
    /// </summary>
    [TestClass]
    public class DecalSetsTest
    {
        private static string assets = null!;

        [ClassInitialize]
        public static void Initialize(TestContext _)
        {
            assets = ProjectFolders.New("decal-assets");
            string decals = Path.Combine(assets, "env", "rocky", "decals");
            Directory.CreateDirectory(decals);
            foreach (string name in new[] { "rock01_albedo", "rock01_normals", "rock01_spec", "rock01_mask", "rock01_mask_moss", "lone_albedo", "pebbles_normals", "glow_speckels2" })
            {
                using MagickImage image = new MagickImage(MagickColors.White, 8, 4);
                File.WriteAllBytes(Path.Combine(decals, name + ".png"), image.ToByteArray(MagickFormat.Png));
            }
        }

        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static MapDocument Flat() => NewMap.Create(new NewMapSettings { Name = "Sets", Size = 256 });

        private static DecalSetAssets Rock() => DecalSets.Find(AssetCatalog.Load(assets).Entries, "env/rocky/decals/rock01")!;

        [TestMethod]
        [DataRow("env/a/decals/rock01_albedo.dds", "env/a/decals/rock01", DecalSetPart.Albedo, null)]
        [DataRow("/env/a/decals/rock01_Normals.dds", "/env/a/decals/rock01", DecalSetPart.Normals, null)]
        [DataRow("rock01_normal.png", "rock01", DecalSetPart.Normals, null)]
        [DataRow("rock01_spec.dds", "rock01", DecalSetPart.Specular, null)]
        [DataRow("rock01_mask.png", "rock01", DecalSetPart.Mask, "main")]
        [DataRow("rock01_mask_moss.png", "rock01", DecalSetPart.Mask, "moss")]
        [DataRow("iansclutch_h2omask_a.png", "iansclutch_h2omask_a", DecalSetPart.Plain, null)]
        [DataRow("glow_speckels2.dds", "glow_speckels2", DecalSetPart.Plain, null)]
        public void ReadsTheSuffix(string path, string name, DecalSetPart part, string? mask)
        {
            Assert.AreEqual(new DecalSetFile(name, part, mask), DecalSets.PartOf(path));
        }

        [TestMethod]
        public void GroupsTheCataloguesDecalsByNameAndSuffix()
        {
            AssetCatalog catalog = AssetCatalog.Load(assets);

            IReadOnlyList<DecalSetAssets> sets = DecalSets.Of(catalog.Entries);

            DecalSetAssets rock = sets.Single();
            Assert.AreEqual("env/rocky/decals/rock01", rock.Name);
            Assert.AreEqual("env/rocky/decals/rock01_albedo.dds", rock.Albedo);
            Assert.AreEqual("env/rocky/decals/rock01_normals.dds", rock.Normals);
            Assert.AreEqual("env/rocky/decals/rock01_spec.dds", rock.Specular);
            CollectionAssert.AreEqual(new[] { "main", "moss" }, rock.Masks.Keys.ToArray());
            Assert.AreEqual("albedo, normals, specular, masks main and moss", rock.Summary);
            // the shelf groups a set by its family; a lone texture is a family of its own
            Assert.AreEqual(5, catalog.Entries.Count(entry => entry.Family == "env/rocky/decals/rock01".Split('/')[^1] || entry.Family.EndsWith("rock01", StringComparison.Ordinal)));
            Assert.AreEqual("masks", catalog.Find("env/rocky/decals/rock01_mask_moss.dds")!.Category);
        }

        [TestMethod]
        public void FindsTheSetsOfTheMapsOwnFiles()
        {
            MapDocument document = Flat();
            byte[] png = File.ReadAllBytes(Path.Combine(assets, "env", "rocky", "decals", "rock01_albedo.png"));
            document = document with
            {
                Files = document.Files.SetItem("env/decals/cliff_albedo.dds", [.. png]).SetItem("env/decals/cliff_mask_moss.png", [.. png]).SetItem("env/decals/lone_albedo.dds", [.. png]),
            };

            DecalSetAssets cliff = DecalSets.OfMap(document).Single();

            Assert.AreEqual("/maps/sets.v0001/env/decals/cliff", cliff.Name);
            Assert.AreEqual("/maps/sets.v0001/env/decals/cliff_albedo.dds", cliff.Albedo);
            Assert.AreEqual("/maps/sets.v0001/env/decals/cliff_mask_moss.png", cliff.Masks["moss"]);
            Assert.AreEqual(cliff, DecalSets.Find([], document, "maps/sets.v0001/env/decals/cliff"), "found with or without the leading slash");
            Assert.IsNull(DecalSets.Find([], document, "/maps/sets.v0001/env/decals/lone"), "one texture is no set");
        }

        [TestMethod]
        public void PlacesASetAsOneDecalCarryingItsOtherTextures()
        {
            MapDocument document = Flat();

            Decal decal = DecalEdits.NewDecalSet(document.Terrain.Heightmap, Rock(), 100, 100, 20, 10, 0);

            Assert.AreEqual(DecalType.Albedo, decal.Type);
            CollectionAssert.AreEqual(new[] { "/env/rocky/decals/rock01_albedo.dds", "/env/rocky/decals/rock01_spec.dds" }, decal.Textures.ToArray());
            Assert.AreEqual("/env/rocky/decals/rock01", decal.Set!.Name);
            Assert.AreEqual("/env/rocky/decals/rock01_normals.dds", decal.Set.Normals);
            Assert.AreEqual("/env/rocky/decals/rock01_mask_moss.dds", decal.Set.Masks["moss"]);
            Assert.AreEqual(new Vector3(100, 0, 100), MapObjects.DecalCentre(decal) with { Y = 0 });
        }

        [TestMethod]
        public void WritesOneDecalPerTextureWithTheSamePlaceInTheSameGroups()
        {
            MapDocument document = Flat();
            document = document.PlaceDecal(DecalEdits.NewDecalSet(document.Terrain.Heightmap, Rock(), 100, 100, 20, 10, 0.5f), out ObjectRef set);
            document = document.PlaceDecal(DecalEdits.NewDecal(document.Terrain.Heightmap, "env/rocky/decals/lone_albedo.dds", 50, 50, 8, 8, 0), out ObjectRef lone);
            document = document.AddDecalGroup("Rocks", [set], out DecalGroup group);

            MapGameFiles files = MapExporter.Export(document);

            IReadOnlyList<ScmapDecal> decals = files.Scmap.Decals;
            Assert.AreEqual(3, decals.Count);
            ScmapDecal albedo = decals[0], normals = decals[1];
            Assert.AreEqual((int)DecalType.Albedo, albedo.Type);
            Assert.AreEqual((int)DecalType.Normals, normals.Type);
            CollectionAssert.AreEqual(new[] { "/env/rocky/decals/rock01_normals.dds", "" }, normals.Textures.ToArray());
            Assert.AreEqual(albedo.Position, normals.Position);
            Assert.AreEqual(albedo.Rotation, normals.Rotation);
            Assert.AreEqual(albedo.Scale, normals.Scale);
            Assert.AreEqual(albedo.CutOffLod, normals.CutOffLod);
            Assert.AreNotEqual(albedo.Id, normals.Id);
            Assert.AreEqual(lone.Id, decals[2].Id, "the others keep their ids");
            CollectionAssert.AreEqual(new[] { albedo.Id, normals.Id }, files.Scmap.DecalGroups.Single(g => g.Id == group.Id).DecalIds.ToArray());
            Assert.AreEqual(document.Decals.Max(decal => decal.Id) + 2, files.Scmap.NextDecalId, "the counter after the new id");
        }

        [TestMethod]
        public void KeepsASetInTheProject()
        {
            MapDocument document = Flat();
            document = document.PlaceDecal(DecalEdits.NewDecalSet(document.Terrain.Heightmap, Rock(), 100, 100, 20, 10, 0), out _);
            string project = ProjectFolders.New("sets");

            MapProjectStore.Save(document, project);
            MapDocument loaded = MapProjectStore.Load(project);

            Assert.AreEqual(document.Decals.Single().Set, loaded.Decals.Single().Set);
            Assert.IsNull(MapProjectStore.Load(project).Decals.Single().Set!.Normals is null ? "missing" : null);
            CollectionAssert.AreEqual(MapExporter.ToBytes(document).OrderBy(file => file.Key).SelectMany(file => file.Value).ToArray(),
                MapExporter.ToBytes(loaded).OrderBy(file => file.Key).SelectMany(file => file.Value).ToArray());
        }

        [TestMethod]
        public void MovesASetAsOneThing()
        {
            MapDocument document = Flat();
            document = document.PlaceDecal(DecalEdits.NewDecalSet(document.Terrain.Heightmap, Rock(), 100, 100, 20, 10, 0), out ObjectRef set);

            MapDocument moved = document.MoveObjects([set], 10, 5);

            Decal decal = moved.Decals.Single();
            Assert.AreEqual(document.Decals.Single().Set, decal.Set, "its textures go with it");
            Assert.AreEqual(new Vector3(110, 0, 105), MapObjects.DecalCentre(decal) with { Y = 0 });
        }

        #region Decal masks

        /// <summary>
        /// Mask pictures by path: the main mask of rock01 full in its left half, the moss mask empty.
        /// </summary>
        private sealed class Pictures : IDecalMaskPictures
        {
            public int Reads;

            public MaskPicture? Read(string gamePath, MapDocument document)
            {
                Reads++;
                return gamePath.EndsWith("rock01_mask.dds", StringComparison.Ordinal)
                    ? new MaskPicture(4, 1, [255, 255, 0, 0])
                    : gamePath.EndsWith("_moss.dds", StringComparison.Ordinal) ? new MaskPicture(1, 1, [0]) : null;
            }
        }

        private static (MapDocument Document, ObjectRef Set) WithRock(float x, float z)
        {
            MapDocument document = Flat();
            document = document.PlaceDecal(DecalEdits.NewDecalSet(document.Terrain.Heightmap, Rock(), x, z, 32, 32, 0), out ObjectRef set);
            return (document, set);
        }

        private static LayerStack StratumStack(MapDocument document, IDecalMaskPictures pictures, DecalMasksSource source)
        {
            LayerStack stack = LayerStack.Empty(LayerTarget.Stratum(1));
            Layer layer = new Layer { Name = "Rock shapes", Source = source, Combine = LayerCombine.Maximum };
            stack = LayerStackEdits.Add(stack, layer);
            return stack.With(0, DecalMaskLayers.Draw(document, stack, stack.Layers[0], pictures));
        }

        /// <summary>
        /// The stratum's value at a place of the map (its masks are half the map's size).
        /// </summary>
        private static byte At(TiledBuffer<byte> mask, MapDocument document, float x, float z) =>
            mask[(int)(x * mask.Width / document.Width), (int)(z * mask.Height / document.Height)];

        [TestMethod]
        public void DrawsASetsMaskAtItsPlaceTurnAndSizeIntoAStratum()
        {
            (MapDocument document, _) = WithRock(100, 100);

            LayerStack stack = StratumStack(document, new Pictures(), DecalMasksSource.Every);
            TiledBuffer<byte> stratum = LayerStacks.CompositeCoverage(document, stack, document.Terrain.StratumMasks.Layers[0]);

            // the decal spans 84 to 116; its mask is full in its left half
            Assert.AreEqual(255, At(stratum, document, 90, 100));
            Assert.AreEqual(0, At(stratum, document, 110, 100));
            Assert.AreEqual(0, At(stratum, document, 60, 100), "beyond the decal");
        }

        [TestMethod]
        public void DrawsOnlyTheNamedMaskOfTheChosenSets()
        {
            (MapDocument document, _) = WithRock(100, 100);

            LayerStack moss = StratumStack(document, new Pictures(), new DecalMasksSource("moss", []));
            LayerStack other = StratumStack(document, new Pictures(), new DecalMasksSource(DecalSets.MainMask, ["/env/rocky/decals/other01"]));

            Assert.IsTrue(moss.Layers[0].Coverage!.ToArray().All(value => value == 0));
            Assert.IsTrue(other.Layers[0].Coverage!.ToArray().All(value => value == 0));
        }

        [TestMethod]
        public void TheStratumFollowsAMovedDecal()
        {
            (MapDocument document, ObjectRef set) = WithRock(100, 100);
            Pictures pictures = new Pictures();
            LayerStack stack = StratumStack(document, pictures, DecalMasksSource.Every);

            MapDocument moved = document.MoveObjects([set], 60, 0);
            LayerStack followed = DecalMaskLayers.Follow(document, moved, stack, pictures);
            TiledBuffer<byte> stratum = LayerStacks.CompositeCoverage(moved, followed, moved.Terrain.StratumMasks.Layers[0]);

            Assert.AreEqual(0, At(stratum, moved, 90, 100), "gone from the old place");
            Assert.AreEqual(255, At(stratum, moved, 150, 100), "at the new place");
            Assert.AreSame(stack, DecalMaskLayers.Follow(moved, moved with { Props = [] }, stack, pictures), "the decals did not change");
        }

        [TestMethod]
        public void TheHeightmapFollowsAMovedDecalInAnEditSession()
        {
            (MapDocument document, ObjectRef set) = WithRock(100, 100);
            Pictures pictures = new Pictures();
            EditSession session = new EditSession(document) { DecalPictures = pictures };
            session.Edit("Raise the rocks", d =>
            {
                LayerStack stack = HeightLayerEdits.Start(d).HeightStack;
                Layer layer = new Layer { Name = "Rocks", Source = new DecalMasksSource(DecalSets.MainMask, [], Height: 8) };
                stack = LayerStackEdits.Add(stack, layer);
                stack = stack.With(stack.Count - 1, DecalMaskLayers.Draw(d, stack, stack.Layers[^1], pictures));
                return d with { HeightStack = stack };
            });
            float flat = document.Terrain.Heightmap.HeightAt(90, 100);
            Assert.AreEqual(flat + 8, session.Document.Terrain.Heightmap.HeightAt(90, 100), 0.05f);

            session.Edit("Move the rock", d => d.MoveObjects([set], 60, 0));

            Assert.AreEqual(flat, session.Document.Terrain.Heightmap.HeightAt(90, 100), 0.05f);
            Assert.AreEqual(flat + 8, session.Document.Terrain.Heightmap.HeightAt(150, 100), 0.05f);
            session.Undo();
            Assert.AreEqual(flat + 8, session.Document.Terrain.Heightmap.HeightAt(90, 100), 0.05f);
        }

        [TestMethod]
        public void KeepsADecalMasksLayerInTheProject()
        {
            (MapDocument document, _) = WithRock(100, 100);
            Pictures pictures = new Pictures();
            LayerStack stack = HeightLayerEdits.Start(document).HeightStack;
            stack = LayerStackEdits.Add(stack, new Layer { Name = "Rocks", Source = new DecalMasksSource("moss", ["/env/rocky/decals/rock01"], Height: 4) });
            stack = stack.With(stack.Count - 1, DecalMaskLayers.Draw(document, stack, stack.Layers[^1], pictures));
            document = HeightLayers.Follow(document, document with { HeightStack = stack }, null);
            string project = ProjectFolders.New("decal-masks");

            MapProjectStore.Save(document, project);
            MapDocument loaded = MapProjectStore.Load(project);

            Assert.AreEqual(document.HeightStack.Layers[^1].Source, loaded.HeightStack.Layers[^1].Source);
            Assert.AreEqual(LayerSources.DecalMasksName, LayerSources.NameOf(loaded.HeightStack.Layers[^1].Source));
        }

        #endregion
    }
}
