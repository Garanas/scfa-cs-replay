using System.IO.Compression;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Asset discovery (user story F-8): where each asset comes from, and what the map uses that
    /// the workspace lacks.
    /// </summary>
    [TestClass]
    public sealed class AssetDiscoveryTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        /// <summary>
        /// Archives in which FAF's adds the oak and replaces the rock of the game's.
        /// </summary>
        private static AssetOrigins Origins()
        {
            string folder = Path.Combine(AppContext.BaseDirectory, "archives", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string Archive(string name, params string[] files)
            {
                string path = Path.Combine(folder, name);
                using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
                foreach (string file in files)
                {
                    zip.CreateEntry(file);
                }
                return path;
            }
            return AssetOrigins.Read([
                Archive("env.scd", "env/evergreen/props/trees/pine06_prop.bp", "env/evergreen/props/trees/pine06_s2_prop.bp", "env/evergreen/props/trees/pine06_v1_prop.bp", "env/evergreen/props/rocks/rock01_prop.bp"),
                Archive("env.nx2", "env/evergreen/props/trees/oak01_prop.bp", "env/evergreen/props/rocks/rock01_prop.bp"),
            ]);
        }

        [TestMethod]
        public void TheShelfSaysWhereEachAssetComesFromAndFiltersByIt()
        {
            AssetsViewModel shelf = new AssetsViewModel(NullLogger.Instance);
            shelf.Use(TestAssets.Catalog());
            shelf.Scope = AssetScope.Everything;
            shelf.UseOrigins(Origins());

            CollectionAssert.AreEquivalent(new[] { "Game 3", "FAF 2", "Unknown 1" }, shelf.Sources.Select(source => source.Text).ToArray(), "tund_rock01 is in neither archive");
            shelf.Source = "FAF";
            CollectionAssert.AreEquivalent(new[] { "oak01", "rock01" }, shelf.Tiles.Select(tile => tile.Title).ToArray());

            shelf.Select("env/evergreen/props/rocks/rock01_prop.bp");
            Assert.AreEqual("FAF (env.nx2), replacing the game's", shelf.ShownSource);
            shelf.Kind = AssetKind.Brush;
            Assert.AreEqual("Workspace", shelf.Sources.Single().Value, "the brushes are the workspace's own");
        }

        [TestMethod]
        public async Task TheMissingScopeListsWhatTheMapUsesAndTheWorkspaceLacks()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            model.Assets.Use(TestAssets.Catalog());
            await model.OpenAsync(HardFfa);

            Assert.IsTrue(model.Assets.MissingCount > 0, "HardFFA's props and textures are not in the test assets");
            JsonObject shelf = (JsonObject)(await model.Commands.RunAsync("assets.browse", new JsonObject { ["kind"] = "texture", ["scope"] = "missing" }, interactive: false)).Result!;
            Assert.AreEqual(AssetScope.Missing, model.Assets.Scope);
            Assert.AreEqual(0, model.Assets.Tiles.Count, "the scope lists paths, no tiles");
            Assert.IsTrue(model.Assets.Missing.Count > 0);
            Assert.IsTrue(model.Assets.Missing.All(missing => missing.Kind == AssetKind.Texture && missing.Check == "missing-texture"));
            Assert.AreEqual(model.Assets.Missing.Count, ((JsonArray)shelf["missing"]!).Count);

            // its problem: the Problems tab, the line of that texture
            model.Problems.Run();
            MissingAsset first = model.Assets.Missing[0];
            int line = model.ShowMissingAsset(first);
            Assert.AreEqual(DockTab.Problems, model.Dock);
            Assert.IsTrue(line >= 0, "the check missing-texture names the same path");
            StringAssert.Contains(model.Problems.Items[line].Problem.Message.ToLowerInvariant(), first.Path);
        }
    }
}
