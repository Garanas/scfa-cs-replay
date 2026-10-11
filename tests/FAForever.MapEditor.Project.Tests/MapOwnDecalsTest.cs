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
    }
}
