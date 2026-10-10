using System.Collections.Immutable;
using System.IO.Compression;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// Where each asset comes from, and what a map uses that the workspace lacks (user story F-8).
    /// </summary>
    [TestClass]
    public class AssetOriginsTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static string Archive(string folder, string name, params string[] files)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, name);
            using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (string file in files)
            {
                using StreamWriter writer = new StreamWriter(zip.CreateEntry(file).Open());
                writer.Write("x");
            }
            return path;
        }

        private static AssetEntry Entry(AssetKind kind, string key) => new AssetEntry
        {
            Kind = kind, Key = key, Keys = [key], Name = key, Code = key, Group = "", Category = "", Family = key, File = key,
        };

        [TestMethod]
        public void FafsArchivesLieOverTheGames()
        {
            string folder = ProjectFolders.New("archives");
            string scd = Archive(folder, "env.scd", "env/Evergreen/props/trees/Pine06_prop.bp", "env/evergreen/layers/eg_grass_albedo.dds");
            string nx2 = Archive(folder, "env.nx2", "env/evergreen/layers/eg_grass_albedo.dds", "env/evergreen/decals/new_albedo.dds");
            string units = Archive(folder, "units.scd", "units/UEC1101/UEC1101_unit.bp");

            AssetOrigins origins = AssetOrigins.Read([scd, nx2, units, Path.Combine(folder, "missing.nx2")]);

            Assert.AreEqual(4, origins.Count);
            Assert.AreEqual(new AssetSource(AssetOrigin.Game, "env.scd"), origins.Of(Entry(AssetKind.Prop, "env/evergreen/props/trees/pine06_prop.bp")));
            Assert.AreEqual(new AssetSource(AssetOrigin.Faf, "env.nx2", ReplacesGame: true), origins.Of(Entry(AssetKind.Texture, "env/evergreen/layers/eg_grass_albedo.dds")));
            Assert.AreEqual(new AssetSource(AssetOrigin.Faf, "env.nx2"), origins.Of(Entry(AssetKind.Decal, "env/evergreen/decals/new_albedo.dds")));
            Assert.AreEqual(new AssetSource(AssetOrigin.Game, "units.scd"), origins.Of(Entry(AssetKind.Unit, "uec1101")));
            Assert.AreEqual(AssetOrigin.Workspace, origins.Of(Entry(AssetKind.Brush, "brushes/crater.png")).Origin);
            Assert.AreEqual(AssetOrigin.Map, origins.Of("/maps/mine.v0001/env/decal_albedo.dds").Origin);
            Assert.AreEqual(AssetOrigin.Unknown, origins.Of("/env/other/x.dds").Origin);
            Assert.AreEqual("FAF (env.nx2), replacing the game's", origins.Of("/env/evergreen/layers/eg_grass_albedo.dds").Title);
        }

        [TestMethod]
        public void ListsWhatTheMapUsesAndTheWorkspaceLacks()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);

            IReadOnlyList<MissingAsset> missing = MissingAssets.Find(document, AssetCatalog.Empty);

            Assert.IsTrue(missing.Count > 0);
            Assert.IsTrue(missing.Where(asset => asset.Kind == AssetKind.Texture).All(asset => asset.Check == "missing-texture"));
            Assert.IsTrue(missing.Where(asset => asset.Kind == AssetKind.Prop).All(asset => asset.Check == "missing-model"));
            Assert.AreEqual(document.Props.Length, missing.Where(asset => asset.Kind == AssetKind.Prop).Sum(asset => asset.Uses), "every prop is counted");
            Assert.IsTrue(missing.Zip(missing.Skip(1)).All(pair => pair.First.Uses >= pair.Second.Uses), "most used first");

            // a stratum texture in the map's own files is not missing
            string own = "/maps/hardffa.v0001/own_albedo.dds";
            MapDocument withOwn = document with
            {
                Files = document.Files.Add("own_albedo.dds", ImmutableArray.Create<byte>(1)),
                Terrain = document.Terrain with { Layers = document.Terrain.Layers.SetItem(1, document.Terrain.Layers[1] with { AlbedoPath = own }) },
            };
            Assert.IsFalse(MissingAssets.Find(withOwn, AssetCatalog.Empty).Any(asset => asset.Path == AssetCatalog.KeyOf(own)));
        }
    }
}
