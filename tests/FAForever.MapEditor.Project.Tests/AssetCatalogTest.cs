using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public sealed class AssetCatalogTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        /// <summary>
        /// A workspace's assets folder with a few of each kind, as the conversion writes them.
        /// </summary>
        private static string Assets()
        {
            string assets = ProjectFolders.New("assets");
            void Write(string path, string text = "")
            {
                string file = Path.Combine(assets, path);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, text);
            }
            const string pine = """{ "id": "/env/evergreen/props/trees/pine06_prop.bp", "size": { "x": 1, "y": 9, "z": 1 }, "reclaimMass": 0, "reclaimEnergy": 30 }""";
            Write("env/evergreen/props/trees/pine06_prop.json", pine);
            Write("env/evergreen/props/trees/pine06_s2_prop.json", pine);
            Write("env/evergreen/props/trees/pine06_v1_s3_prop.json", pine);
            Write("env/evergreen/props/rocks/rock01_prop.json", """
                { "id": "/env/evergreen/props/rocks/rock01_prop.bp", "reclaimMass": 20, "lods": [
                    { "mesh": "/env/evergreen/props/rocks/rock01_lod0.glb", "shader": "NormalMappedAlpha", "albedo": "/env/evergreen/props/rocks/rock01_albedo.png", "normals": "/env/evergreen/props/rocks/rock01_normalsts.png" },
                    { "mesh": "/env/evergreen/props/rocks/rock01_lod1.glb" } ] }
                """);
            Write("env/evergreen/props/broken_prop.json", "{ not json");
            Write("env/evergreen/decals/e_mud001_albedo.png");
            Write("env/evergreen/decals/e_dl001_normals.png");
            Write("env/evergreen/splats/e_splat01.png");
            Write("env/tundra/layers/tund_rock_albedo.png");
            Write("env/tundra/layers/tund_rock_normal.png");
            Write("env/tundra/layers/macroice_albedo.png");
            Write("env/tundra/layers/tund_none_normal.png");
            Write("units/uec1101/uec1101_unit.json", """{ "id": "UEC1101", "name": "UEF Residential Building", "faction": "Uef", "techLevel": 1, "categories": ["UEF", "STRUCTURE", "CIVILIAN"] }""");
            Write("units/uel0101/uel0101_unit.json", """{ "id": "UEL0101", "name": "Snoop", "faction": "Uef", "techLevel": 1, "categories": ["UEF", "LAND", "MOBILE"] }""");
            Write("units/uel0101/uel0101_albedo.png");
            return assets;
        }

        [TestMethod]
        public void ReadsEachKindWithItsGroupAndCategory()
        {
            string assets = Assets();
            AssetCatalog catalog = AssetCatalog.Load(assets);

            AssetEntry pine = catalog.Find("/env/Evergreen/Props/Trees/Pine06_prop.bp")!;
            Assert.AreEqual(AssetKind.Prop, pine.Kind);
            Assert.AreEqual(("evergreen", "trees", "pine06"), (pine.Group, pine.Category, pine.Family));
            Assert.AreEqual("pine06", catalog.Find("env/evergreen/props/trees/pine06_v1_s3_prop.bp")!.Family);
            Assert.AreEqual("rocks", catalog.Find("env/evergreen/props/rocks/rock01_prop.bp")!.Category);
            // the most detailed mesh is drawn as its picture
            Assert.AreEqual(
                new AssetModel(
                    Path.Combine(assets, "env/evergreen/props/rocks/rock01_lod0.glb"),
                    Path.Combine(assets, "env/evergreen/props/rocks/rock01_albedo.png"),
                    Path.Combine(assets, "env/evergreen/props/rocks/rock01_normalsts.png"),
                    null,
                    "NormalMappedAlpha"),
                catalog.Find("env/evergreen/props/rocks/rock01_prop.bp")!.Model);
            Assert.IsNull(pine.Model, "a blueprint without levels of detail");
            Assert.IsTrue(pine.Facts.Any(fact => fact.Label == "Reclaim" && fact.Value == "0 mass, 30 energy"));
            Assert.AreEqual(4, catalog.Entries.Count(entry => entry.Kind == AssetKind.Prop), "the broken blueprint is left out");

            Assert.AreEqual("albedo", catalog.Find("/env/evergreen/decals/e_mud001_albedo.dds")!.Category);
            Assert.AreEqual("e_mud001", catalog.Find("/env/evergreen/decals/e_mud001_albedo.dds")!.Name);
            Assert.AreEqual("normals", catalog.Find("/env/evergreen/decals/e_dl001_normals.dds")!.Category);
            Assert.AreEqual("splats", catalog.Find("/env/evergreen/splats/e_splat01.dds")!.Category);

            AssetEntry rock = catalog.Find("/env/tundra/layers/tund_rock_albedo.dds")!;
            Assert.AreEqual((AssetKind.Texture, "stratum", "tund_rock"), (rock.Kind, rock.Category, rock.Name));
            Assert.AreSame(rock, catalog.Find("/env/tundra/layers/tund_rock_normal.dds"), "the normal map is part of its texture");
            Assert.IsNotNull(rock.NormalImage);
            Assert.AreEqual("macro", catalog.Find("/env/tundra/layers/macroice_albedo.dds")!.Category);
            Assert.AreEqual("normal map only", catalog.Find("/env/tundra/layers/tund_none_normal.dds")!.Category);

            AssetEntry house = catalog.Find("UEC1101")!;
            Assert.AreEqual((AssetKind.Unit, "UEF", "structure", "UEF Residential Building"), (house.Kind, house.Group, house.Category, house.Name));
            Assert.IsTrue(house.IsCivilian);
            Assert.AreEqual("land", catalog.Find("uel0101")!.Category);
            Assert.IsFalse(catalog.Find("uel0101")!.IsCivilian);
        }

        [TestMethod]
        public void ReadsTheWorkspacesBrushesInTheirFolders()
        {
            string brushes = ProjectFolders.New("brushes");
            Directory.CreateDirectory(Path.Combine(brushes, "Craters"));
            // a PNG of one grey pixel, 8 bits
            byte[] pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAAAAAA6fptVAAAACklEQVR4nGNgAAAAAgABSK+kcQAAAABJRU5ErkJggg==");
            File.WriteAllBytes(Path.Combine(brushes, "Craters", "Crater01.png"), pixel);
            File.WriteAllBytes(Path.Combine(brushes, "ridge.png"), pixel);
            File.WriteAllText(Path.Combine(brushes, "notes.txt"), "not a brush");

            AssetCatalog catalog = AssetCatalog.Load(ProjectFolders.New("no-assets"), brushes);

            Assert.AreEqual(2, catalog.Entries.Count);
            AssetEntry crater = catalog.Find("brushes/craters/crater01.png")!;
            Assert.AreEqual((AssetKind.Brush, "craters", "Crater01"), (crater.Kind, crater.Group, crater.Name));
            Assert.IsTrue(crater.Facts.Contains(new AssetFact("Size", "1 × 1 pixels")));
            Assert.IsTrue(crater.Facts.Contains(new AssetFact("Values", "8-bit grey")));
            Assert.AreEqual("brushes", catalog.Find("brushes/ridge.png")!.Group, "a brush directly in the folder");
            Assert.AreEqual(Path.Combine(brushes, "ridge.png"), catalog.Find("brushes/ridge.png")!.Image);
        }

        [TestMethod]
        public void AnAbsentFolderHasNoAssets()
        {
            Assert.AreEqual(0, AssetCatalog.Load(ProjectFolders.New("none")).Entries.Count);
        }

        [TestMethod]
        public void CountsWhatAMapUses()
        {
            MapDocument document = EditedMap.ImportMap(ProjectFolders.HardFfa).Document;

            IReadOnlyDictionary<string, int> uses = AssetUse.Count(document);

            string albedo = AssetCatalog.KeyOf(document.Terrain.Layers[0].AlbedoPath);
            Assert.IsTrue(uses[albedo] >= 1);
            Assert.AreEqual(document.Props.Length, uses.Where(use => use.Key.EndsWith("_prop.bp", StringComparison.Ordinal)).Sum(use => use.Value));
            Assert.IsTrue(AssetUse.Biomes(uses).Count > 0);
        }
    }
}
