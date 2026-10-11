using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using ImageMagick;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// A map maker's own decals and decal sets (user story D-1): the map's files on the shelf and in the
    /// picker under This map, added from the window, placed on the map only; the decal-masks layer's
    /// mask and sets chosen in a stratum's, mask's or water map's stack.
    /// </summary>
    [TestClass]
    public class MapOwnDecalsShelfTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "own-decals");

        /// <summary>
        /// The window's questions: the files picked come from <see cref="Files"/>.
        /// </summary>
        private sealed class Dialogs : IEditorDialogs
        {
            public Queue<IReadOnlyList<string>> Files { get; } = new Queue<IReadOnlyList<string>>();

            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult<string?>(null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Task.FromResult(SaveChoice.Discard);

            public Task<IReadOnlyList<string>> PickFilesAsync(string title, IReadOnlyList<string> extensions, string? startIn = null) =>
                Task.FromResult(Files.Count > 0 ? Files.Dequeue() : (IReadOnlyList<string>)[]);
        }

        private static async Task<MainViewModel> Open(IEditorDialogs? dialogs = null)
        {
            MainViewModel model = new MainViewModel(dialogs ?? new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            return model;
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        private static byte[] Png(MagickColor colour, uint size = 16)
        {
            using MagickImage image = new MagickImage(colour, size, size);
            return image.ToByteArray(MagickFormat.Png);
        }

        /// <summary>
        /// Puts the decal set <c>crag</c> into the map's own files: albedo, normals, a main and a moss mask.
        /// </summary>
        private static void AddCrag(MainViewModel model) => model.EditMap("Add a decal set", document => document with
        {
            Files = document.Files
                .SetItem("env/decals/crag_albedo.png", [.. Png(MagickColors.Gray)])
                .SetItem("env/decals/crag_normals.png", [.. Png(MagickColors.Blue)])
                .SetItem("env/decals/crag_mask.png", [.. Png(MagickColors.White)])
                .SetItem("env/decals/crag_mask_moss.png", [.. Png(MagickColors.Black)]),
        });

        private static string CragName(MainViewModel model) => $"maps/{model.ShownDocument!.Folder}/env/decals/crag";

        [TestMethod]
        public async Task TheShelfListsTheMapsOwnSetAsOneTileUnderThisMap()
        {
            MainViewModel model = await Open();
            AddCrag(model);
            AssetsViewModel assets = model.Assets;
            assets.Kind = AssetKind.Decal;

            Assert.AreEqual(4, assets.MapEntries.Count);
            AssetTile tile = assets.Tiles.Single(t => t.Title == "crag");
            Assert.IsTrue(tile.IsSet, "in the map's biomes, whatever they are");
            Assert.AreEqual("set of 4", tile.Detail);
            Assert.IsTrue(File.Exists(tile.Image), "its picture on disk for the tile");
            AssetChoice source = assets.Sources.Single(choice => choice.Value == "Map");
            Assert.AreEqual("This map", source.Title);
            assets.Source = "Map";
            Assert.AreEqual(1, assets.Tiles.Count, "only the map's own");
            assets.Select(tile.Entry.Key);
            Assert.AreEqual("In the map's folder", assets.ShownKeyLabel);

            assets.ChooseTileCommand.Execute(assets.Tiles[0]);
            Assert.AreEqual(CragName(model), model.DecalPlaceTool.Decal, "the place tool places the whole set");
            Assert.AreEqual("crag (set)", model.DecalPlaceTool.Title);

            // the files gone (undone): so is the tile
            model.Undo();
            Assert.AreEqual(0, assets.MapEntries.Count);
        }

        [TestMethod]
        public async Task ThePickerChoosesTheMapsOwnSetWhole()
        {
            MainViewModel model = await Open();
            AddCrag(model);

            Task<string?> picked = model.Picker.PickAsync(AssetKind.Decal);
            model.Picker.Query = "crag";
            Assert.AreEqual(5, model.Picker.Tiles.Count, "the set and its four textures");
            Assert.IsTrue(model.Picker.Selected!.IsSet, "the set first");
            model.Picker.Choose();

            Assert.AreEqual("/" + CragName(model), "/" + (await picked)!.TrimStart('/'));
            CommandRun chosen = await Run(model, "decals.choose", new JsonObject { ["key"] = CragName(model) });
            Assert.AreEqual(CragName(model), chosen.Result);
            Assert.AreEqual("Chose the decal set crag (albedo, normals, masks main and moss)", model.Status);
            Assert.IsNotNull(model.Assets.ShownSet, "the shelf shows the whole set");
            Assert.AreEqual(CragName(model), model.DecalPlaceTool.Decal);
        }

        [TestMethod]
        public async Task AddsFilesToTheMapsOwnFilesAsOneStep()
        {
            Dialogs dialogs = new Dialogs();
            MainViewModel model = await Open(dialogs);
            Directory.CreateDirectory(Root);
            string albedo = Path.Combine(Root, $"{Guid.NewGuid():N}_albedo.png");
            string script = Path.Combine(Root, $"{Guid.NewGuid():N}.lua");
            File.WriteAllBytes(albedo, Png(MagickColors.Olive));
            File.WriteAllText(script, "-- a script");
            int steps = model.History.Count;

            CommandRun run = await Run(model, "map.addFiles", new JsonObject { ["files"] = new JsonArray(albedo, script) });

            string name = Path.GetFileName(albedo);
            CollectionAssert.AreEqual(new[] { $"env/decals/{name}", Path.GetFileName(script) }, ((IEnumerable<string>)run.Result!).ToArray(), "pictures into env/decals, the rest at the top");
            CollectionAssert.AreEqual(File.ReadAllBytes(albedo), model.ShownDocument!.Files[$"env/decals/{name}"].ToArray());
            Assert.AreEqual(steps + 1, model.History.Count);
            Assert.IsTrue(model.Assets.MapEntries.Any(entry => entry.Code == name), "the shelf lists it");
            StringAssert.StartsWith(model.Status, "Added 2 files to the map's own files");

            await Run(model, "map.addFiles", new JsonObject { ["files"] = new JsonArray(albedo), ["folder"] = "env/rocks/" });
            Assert.IsTrue(model.ShownDocument!.Files.ContainsKey($"env/rocks/{name}"));
            Assert.IsFalse((await model.Commands.TryRunAsync("map.addFiles", new JsonObject { ["files"] = new JsonArray(albedo), ["folder"] = "../outside" }, interactive: false)).Ran);

            // with a person: the dialog's files
            dialogs.Files.Enqueue([albedo]);
            await model.Commands.RunAsync("map.addFiles", [], interactive: true);
            Assert.AreEqual(steps + 3, model.History.Count);
            model.Undo();
            model.Undo();
            model.Undo();
            Assert.IsFalse(model.ShownDocument!.Files.ContainsKey($"env/decals/{name}"), "undone");
        }

        [TestMethod]
        public async Task ADecalIsPlacedOnTheMapOnly()
        {
            MainViewModel model = await Open();
            AddCrag(model);
            int decals = model.ShownDocument!.Decals.Length;

            Assert.IsFalse((await model.Commands.TryRunAsync("decals.place", new JsonObject { ["key"] = CragName(model), ["x"] = -38.7, ["z"] = 100 }, interactive: false)).Ran);

            model.SelectMode("decals");
            model.DecalPlaceTool.Decal = CragName(model);
            model.DecalPlaceTool.Press(-38.7f, 100);
            model.DecalPlaceTool.Release();
            Assert.AreEqual(decals, model.ShownDocument!.Decals.Length, "a click beside the map places nothing");
            StringAssert.Contains(model.Status, "outside the map");

            // dragged beyond the edge: its middle stays on the map
            model.DecalPlaceTool.Press(4, 100);
            model.DecalPlaceTool.Drag(-60, 100);
            model.DecalPlaceTool.Release();
            Decal placed = model.ShownDocument!.Decals[^1];
            Assert.IsTrue(MapObjectsCentre(placed).X >= 0, "clamped to the map's edge");
        }

        private static System.Numerics.Vector3 MapObjectsCentre(Decal decal) => FAForever.MapEditor.Project.Editing.MapObjects.DecalCentre(decal);

        [TestMethod]
        public async Task TheDecalMasksLayerOfAStratumChoosesItsMaskAndSets()
        {
            MainViewModel model = await Open();
            AddCrag(model);
            await Run(model, "decals.place", new JsonObject { ["key"] = CragName(model), ["x"] = 100, ["z"] = 100, ["width"] = 32 });
            model.SelectMode("paint");
            await Run(model, "layers.add", new JsonObject { ["target"] = "stratum:2", ["source"] = "decals" });
            await Run(model, "layers.choose", new JsonObject { ["target"] = "stratum:2", ["layer"] = "Decal masks" });
            CoverageStackViewModel stack = model.StratumStack;

            Assert.IsTrue(stack.IsDecalMasks);
            ChoiceSetting mask = (ChoiceSetting)stack.DecalMasks.Settings.Single(setting => setting.Label == "Mask");
            ChoiceSetting sets = (ChoiceSetting)stack.DecalMasks.Settings.Single(setting => setting.Label == "Decal sets");
            CollectionAssert.AreEqual(new[] { "main", "moss" }, mask.Choices.ToArray());
            CollectionAssert.AreEqual(new[] { CoverageStackViewModel.EverySet, "crag" }, sets.Choices.ToArray());
            Assert.AreEqual("main", mask.Chosen);
            Assert.AreEqual(CoverageStackViewModel.EverySet, sets.Chosen);

            mask.Chosen = "moss";
            sets.Chosen = "crag";
            for (int i = 0; i < 50 && ((DecalMasksSource)Layer(model).Source).Sets.IsEmpty; i++)
            {
                await Task.Delay(20);
            }
            DecalMasksSource source = (DecalMasksSource)Layer(model).Source;
            Assert.AreEqual("moss", source.Mask);
            CollectionAssert.AreEqual(new[] { "/" + CragName(model) }, source.Sets.ToArray(), "the placed set's short name names it");

            await Run(model, "layers.setDecalMasks", new JsonObject { ["target"] = "stratum:2", ["sets"] = CoverageStackViewModel.EverySet });
            Assert.IsTrue(((DecalMasksSource)Layer(model).Source).Sets.IsEmpty);
            Assert.IsFalse((await model.Commands.TryRunAsync("layers.setDecalMasks", new JsonObject { ["target"] = "stratum:2", ["sets"] = "pebbles" }, interactive: false)).Ran,
                "no placed set of that name");
        }

        private static Layer Layer(MainViewModel model) =>
            model.ShownDocument!.CoverageStacks["stratum-2"].Layers.Single(layer => layer.Source is DecalMasksSource);

        [TestMethod]
        public Task TheShelfsButtonAddsFilesAndItsTileShowsTheSet() => HeadlessApp.Run(async () =>
        {
            Dialogs dialogs = new Dialogs();
            MainViewModel model = new MainViewModel(dialogs);
            await model.OpenAsync(HardFfa);
            MainWindow window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
            window.Show();
            model.SelectMode("decals");
            await model.Commands.RunAsync("assets.browse", new JsonObject { ["kind"] = "decal" }, interactive: false);
            Dispatcher.UIThread.RunJobs();
            Directory.CreateDirectory(Root);
            string prefix = $"stone{Guid.NewGuid():N}"[..12];
            string[] files = [.. new[] { "albedo", "normals", "mask" }.Select(part => Path.Combine(Root, $"{prefix}_{part}.png"))];
            foreach (string file in files)
            {
                File.WriteAllBytes(file, Png(MagickColors.Gray));
            }
            dialogs.Files.Enqueue(files);

            Button add = window.GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyVisible && button.Content as string == "Add files to the map...");
            // below the fold of the shelf's left column: scrolled to, as the wheel does
            add.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            Click(window, add);
            for (int i = 0; i < 100 && model.Assets.MapEntries.Count < 3; i++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(20);
            }

            Assert.AreEqual(3, model.Assets.MapEntries.Count, model.Status);
            Dispatcher.UIThread.RunJobs();
            Button tile = window.GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyVisible
                && Avalonia.Automation.AutomationProperties.GetName(button)?.StartsWith($"{prefix}, set of 3", StringComparison.Ordinal) == true);
            Click(window, tile);
            Assert.AreEqual($"maps/{model.ShownDocument!.Folder}/env/decals/{prefix}", model.DecalPlaceTool.Decal);
        });

        [TestMethod]
        [DataRow("desert boulder", true)]
        [DataRow("BOULDER desert", true)]
        [DataRow("desert rocks", true)]
        [DataRow("des_ 01", true)]
        [DataRow("desert bush", false)]
        public void EachWordOfASearchMatchesTheNameBiomeOrCategoryOnItsOwn(string query, bool matches)
        {
            AssetEntry boulder = new AssetEntry
            {
                Kind = AssetKind.Prop,
                Key = "env/desert/props/rocks/des_boulder01_prop.bp",
                Keys = ["env/desert/props/rocks/des_boulder01_prop.bp"],
                Name = "des_boulder01",
                Code = "des_boulder01",
                Group = "desert",
                Category = "rocks",
                Family = "des_boulder01",
                File = "des_boulder01_prop.json",
            };

            Assert.AreEqual(matches, AssetSearch.Matches(boulder, AssetSearch.Words(query)));
        }

        private static void Click(MainWindow window, Control control)
        {
            Point at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
