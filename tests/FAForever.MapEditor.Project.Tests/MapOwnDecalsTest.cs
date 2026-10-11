using System.Collections.Immutable;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using ImageMagick;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// A map maker's own decals and decal sets among the map's files (user story D-1): listed as assets,
    /// their masks drawn by decal-masks layers, and never exported.
    /// </summary>
    [TestClass]
    public class MapOwnDecalsTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static MapDocument Flat() => NewMap.Create(new NewMapSettings { Name = "Own", Size = 256 });

        /// <summary>
        /// A PNG of one colour: grey (one channel, 8 bits) or with colour and alpha.
        /// </summary>
        private static ImmutableArray<byte> Png(byte value, bool grey, int width = 16, int height = 16)
        {
            using MagickImage image = new MagickImage(MagickColor.FromRgb(value, value, value), (uint)width, (uint)height);
            if (grey)
            {
                image.ColorType = ColorType.Grayscale;
                image.Depth = 8;
            }
            else
            {
                image.ColorType = ColorType.TrueColorAlpha;
            }
            return [.. image.ToByteArray(MagickFormat.Png)];
        }

        /// <summary>
        /// A map with its own decal set <c>crag</c> (albedo, normals, a grey main mask full white) and a plain decal.
        /// </summary>
        private static MapDocument WithOwnSet()
        {
            MapDocument document = Flat();
            return document with
            {
                Files = document.Files
                    .SetItem("env/decals/crag_albedo.png", Png(128, grey: false))
                    .SetItem("env/decals/crag_normals.png", Png(128, grey: false))
                    .SetItem("env/decals/crag_mask.png", Png(255, grey: true))
                    .SetItem("env/decals/crag_mask_moss.png", Png(0, grey: true))
                    .SetItem("env/decals/puddle.png", Png(60, grey: false))
                    .SetItem("env/layers/rock_albedo.png", Png(90, grey: false))
                    .SetItem("readme.txt", [.. "hello"u8.ToArray()]),
            };
        }

        [TestMethod]
        public void ListsThePicturesOfTheMapsDecalsFolderAsDecals()
        {
            MapDocument document = WithOwnSet();

            IReadOnlyList<AssetEntry> entries = MapOwnAssets.Decals(document, (path, _) => "picture of " + path);

            CollectionAssert.AreEquivalent(
                new[] { "maps/own.v0001/env/decals/crag_albedo.png", "maps/own.v0001/env/decals/crag_mask.png", "maps/own.v0001/env/decals/crag_mask_moss.png", "maps/own.v0001/env/decals/crag_normals.png", "maps/own.v0001/env/decals/puddle.png" },
                entries.Select(entry => entry.Key).ToArray(), "a stratum's texture and other files are no decals");
            Assert.IsTrue(entries.All(entry => entry.Kind == AssetKind.Decal && entry.Group == MapOwnAssets.Group));
            Assert.AreEqual("masks", entries.Single(entry => entry.Name == "crag_mask").Category);
            Assert.AreEqual("normals", entries.Single(entry => entry.Name == "crag_normals").Category);
            Assert.AreEqual("picture of env/decals/puddle.png", entries.Single(entry => entry.Name == "puddle").Image);
            Assert.AreEqual("maps/own.v0001/env/decals/crag", DecalSets.Of(entries).Single().Name, "the set is found among them");
            Assert.AreEqual("puddle", MapOwnAssets.Find(entries, "/Maps/Own.v0001/env/decals/PUDDLE.png")!.Name, "found by its game path in any case");
        }

        [TestMethod]
        public void ADecalOfTheMapNamingAPictureOutsideTheDecalsFolderListsIt()
        {
            MapDocument document = WithOwnSet();
            document = document.PlaceDecal(DecalEdits.NewDecal(document.Terrain.Heightmap, "/maps/own.v0001/env/layers/rock_albedo.png", 50, 50, 10, 10, 0), out _);

            CollectionAssert.Contains(MapOwnAssets.DecalFiles(document).ToArray(), "env/layers/rock_albedo.png");
        }

        [TestMethod]
        public void DrawsTheGreyMaskOfAnOwnSetIntoAStratum()
        {
            MapDocument document = WithOwnSet();
            DecalSetAssets set = DecalSets.Of(MapOwnAssets.Decals(document, (_, _) => null)).Single();
            document = document.PlaceDecal(DecalEdits.NewDecalSet(document.Terrain.Heightmap, set, 100, 100, 32, 32, 0), out _);

            MaskPicture? read = new GameMaskPictures(() => null).Read(document.Decals.Last().Set!.Masks["main"], document);
            Assert.IsNotNull(read, "read " + document.Decals.Last().Set!.Masks["main"]);
            Assert.AreEqual(255, read.Pixels[0], "white");
            Assert.AreEqual(128, MaskPicture.Of(GameTextures.Decode([.. Png(128, grey: true)])).Pixels[0], "a mid grey stays");
            LayerStack stack = LayerStack.Empty(LayerTarget.Stratum(1));
            stack = LayerStackEdits.Add(stack, new Layer { Name = "Crags", Source = DecalMasksSource.Every, Combine = LayerCombine.Maximum });
            Layer drawn = DecalMaskLayers.Draw(document, stack, stack.Layers[0], new GameMaskPictures(() => null));

            TiledBuffer<byte> coverage = drawn.Coverage!;
            Assert.AreEqual(255, coverage[(int)(100 * coverage.Width / document.Width), (int)(100 * coverage.Height / document.Height)], "full at the decal's middle");
            Assert.AreEqual(0, coverage[(int)(40 * coverage.Width / document.Width), (int)(100 * coverage.Height / document.Height)], "nothing beside it");
        }

        [TestMethod]
        public void NeverExportsADecalSetsMasks()
        {
            MapDocument document = WithOwnSet();
            DecalSetAssets set = DecalSets.Of(MapOwnAssets.Decals(document, (_, _) => null)).Single();
            document = document.PlaceDecal(DecalEdits.NewDecalSet(document.Terrain.Heightmap, set, 100, 100, 32, 32, 0), out _);

            IReadOnlyDictionary<string, byte[]> files = MapExporter.ToBytes(document);
            IReadOnlyDictionary<string, TextureRole> converted = TextureExport.Pending(document);

            Assert.IsTrue(files.ContainsKey("env/decals/crag_albedo.png") && files.ContainsKey("env/decals/crag_normals.png"));
            Assert.IsFalse(files.Keys.Any(path => path.Contains("_mask", StringComparison.Ordinal)), "the masks stay in the project");
            Assert.IsFalse(converted.Keys.Any(path => path.Contains("_mask", StringComparison.Ordinal)), "nor are they converted");
            Assert.IsTrue(converted.ContainsKey("env/decals/crag_albedo.png"));
        }

        [TestMethod]
        public void ExportsAMaskTheMapsScriptNames()
        {
            MapDocument document = WithOwnSet();
            document = document with { Files = document.Files.SetItem("lua/rocks.lua", [.. System.Text.Encoding.UTF8.GetBytes("local m = '/maps/own.v0001/env/decals/crag_mask_moss.png'")]) };

            CollectionAssert.AreEquivalent(new[] { "env/decals/crag_mask.png" }, DecalSets.UnexportedMasks(document).ToArray());
        }

        /// <summary>
        /// A map of 256 with Kaali's three decals over the whole map, of its own files, and a decal of the game's.
        /// </summary>
        private static MapDocument KaaliLayout()
        {
            MapDocument document = Flat();
            string root = $"/maps/{document.Folder}/env/decals/";
            Decal Wide(int id, DecalType type, string first, string second) => new Decal
            {
                Id = id,
                Type = type,
                Textures = [first, second],
                Scale = new System.Numerics.Vector3(256, 256, 256),
                Position = new System.Numerics.Vector3(0, 16.5f, 0),
                CutOffLod = 10000,
                OwnerArmy = -1,
            };
            document = document with
            {
                WideDecals = MapWideDecals.None,
                Files = document.Files.SetItem("env/decals/map_albedo.dds", Png(1, false)).SetItem("env/decals/map_specular.dds", Png(2, false))
                    .SetItem("env/decals/map_lighting.dds", Png(3, false)).SetItem("env/decals/map_normals.dds", Png(4, false)),
                Decals =
                [
                    Wide(0, DecalType.Albedo, root + "map_albedo.dds", root + "map_specular.dds"),
                    Wide(1, DecalType.Albedo, root + "map_lighting.dds", ""),
                    Wide(2, DecalType.Normals, root + "map_normals.dds", ""),
                ],
                DecalGroups = [new DecalGroup { Id = 1, Name = "Base", DecalIds = [0, 1, 2] }],
            };
            return document.PlaceDecal(DecalEdits.NewDecal(document.Terrain.Heightmap, "/env/evergreen/decals/e_mud001_albedo.dds", 50, 50, 10, 10, 0), out _);
        }

        [TestMethod]
        public void ImportsKaalisDecalsIntoTheMapWideSlots()
        {
            MapDocument document = KaaliLayout();
            string root = $"/maps/{document.Folder}/env/decals/";

            MapDocument imported = ImportedDecals.Recognise(document);

            Assert.AreEqual(MapWideSlot.Of(root + "map_albedo.dds"), imported.WideDecals.Albedo);
            Assert.AreEqual(root + "map_specular.dds", imported.WideDecals.Specular);
            Assert.AreEqual(MapWideSlot.Of(root + "map_lighting.dds"), imported.WideDecals.Shadow);
            Assert.AreEqual(MapWideSlot.Of(root + "map_normals.dds"), imported.WideDecals.Normal);
            Assert.AreEqual("/env/evergreen/decals/e_mud001_albedo.dds", imported.Decals.Single().Textures[0], "the others stay decals");
            Assert.IsTrue(imported.DecalGroups.Single().DecalIds.IsEmpty, "the slots leave the groups");

            // exported again, they come back in Kaali's layout (at the height of the map's corner)
            MapDocument exported = MapWideDecalsExport.Apply(imported, _ => null, out _);
            CollectionAssert.AreEqual(document.Decals.Take(3).Select(decal => (decal.Type, decal.Textures[0], decal.Textures[1], decal.Position with { Y = 0 }, decal.Scale, decal.CutOffLod)).ToArray(),
                exported.Decals.Take(3).Select(decal => (decal.Type, decal.Textures[0], decal.Textures[1], decal.Position with { Y = 0 }, decal.Scale, decal.CutOffLod)).ToArray());
        }

        [TestMethod]
        public void LeavesADecalOfTheGamesOverTheWholeMapAsItIs()
        {
            MapDocument document = KaaliLayout();
            document = document with { Decals = document.Decals.SetItem(2, document.Decals[2] with { Textures = ["/env/common/decals/normals_big.dds", ""] }) };

            MapDocument imported = ImportedDecals.Recognise(document);

            Assert.AreEqual(MapWideSource.None, imported.WideDecals.Normal.Source, "the slot names the map's own files only");
            Assert.AreEqual(2, imported.Decals.Length);
        }

        [TestMethod]
        public void ImportsTheEditorsExportWithItsSlotsAndSets()
        {
            MapDocument document = WithOwnSet();
            DecalSetAssets set = DecalSets.Of(MapOwnAssets.Decals(document, (_, _) => null)).Single();
            document = document.PlaceDecal(DecalEdits.NewDecalSet(document.Terrain.Heightmap, set, 100, 100, 32, 32, 0.5f), out _)
                .PlaceDecal(DecalEdits.NewDecal(document.Terrain.Heightmap, "/env/evergreen/decals/e_mud001_albedo.dds", 50, 50, 10, 10, 0), out _);
            document = document with { WideDecals = MapWideDecals.NewMap with { Albedo = MapWideSlot.Of($"/maps/{document.Folder}/env/decals/puddle.png") } };
            string folder = ProjectFolders.New("own-export");
            EditedMap map = EditedMap.New(document);
            string exported = map.Export(folder, wideDecals: true, ddsTextures: true);

            MapDocument back = EditedMap.ImportMap(exported).Document;

            Assert.AreEqual(MapWideSource.File, back.WideDecals.Albedo.Source);
            Assert.AreEqual(MapWideSource.File, back.WideDecals.Normal.Source, "a generated decal comes back as its file");
            Assert.AreEqual(MapWideSource.File, back.WideDecals.Shadow.Source);
            Decal crag = back.Decals.Single(decal => decal.Set is not null);
            StringAssert.EndsWith(crag.Set!.Name, "/env/decals/crag");
            StringAssert.EndsWith(crag.Set.Normals, "/env/decals/crag_normals.dds");
            Assert.AreEqual(0.5f, crag.Rotation.Y, 1e-5f);
            Assert.AreEqual(2, back.Decals.Length, "the set and the game's decal");
            Assert.IsFalse(back.Files.Keys.Any(file => file.Contains("_mask", StringComparison.Ordinal)), "the masks were never exported");
        }
    }
}
