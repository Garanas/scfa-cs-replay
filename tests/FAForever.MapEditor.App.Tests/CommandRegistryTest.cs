using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia.Input;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The controller's commands (user story F-1), and the bookkeeping of what is not built yet:
    /// every story they name exists in <c>USER_STORIES.md</c>.
    /// </summary>
    [TestClass]
    public sealed partial class CommandRegistryTest
    {
        private sealed class NoDialogs : IEditorDialogs
        {
            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult<string?>(null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Task.FromResult(SaveChoice.Discard);
        }

        private static MainViewModel NewModel() => new MainViewModel(new NoDialogs());

        private static HashSet<string> Stories()
        {
            string text = File.ReadAllText(Path.Combine(HeadlessApp.RepositoryFolder, "USER_STORIES.md"));
            return [.. StoryId().Matches(text).Select(match => match.Groups[1].Value)];
        }

        [GeneratedRegex(@"\*\*([A-Z]-\d+) ")]
        private static partial Regex StoryId();

        [TestMethod]
        public void EveryStoryNamedExists()
        {
            HashSet<string> stories = Stories();
            MainViewModel model = NewModel();
            List<string> named = [];
            named.AddRange(model.Commands.All.Where(c => c.Story is not null).Select(c => $"{c.Id}: {c.Story}"));
            named.AddRange(model.Commands.All.Where(c => c.Incomplete is not null).Select(c => $"{c.Id}: {c.Incomplete!.Story}"));
            foreach (ModeDefinition mode in EditorModes.All)
            {
                named.AddRange(mode.Tools.Select(tool => $"tool {tool.Id}: {tool.Story}"));
                named.AddRange(mode.Sections.Concat(mode.Inspector).OfType<PlannedSection>().Select(section => $"{mode.Id} {section.Title}: {section.Story}"));
            }
            named.AddRange(model.Overlays.Select(overlay => $"overlay {overlay.Id}: {overlay.Story}"));

            string[] unknown = [.. named.Where(entry => !stories.Contains(entry[(entry.LastIndexOf(' ') + 1)..]))];

            Assert.IsTrue(stories.Count > 60, "USER_STORIES.md was read");
            CollectionAssert.AreEqual(Array.Empty<string>(), unknown, "every story named in the editor is in USER_STORIES.md");
        }

        [TestMethod]
        public void EveryCommandNotBuiltNamesItsStory()
        {
            // the commands of the whole editor that are not built yet, for the list of what is left
            EditorCommand[] incomplete = [.. NewModel().Commands.All.Where(command => command.IsIncomplete)];
            HashSet<string> stories = Stories();

            int planned = typeof(MainViewModel).Assembly.GetTypes().Count(type => type.GetCustomAttribute<IncompleteAttribute>() is not null && type.GetCustomAttribute<EditorCommandAttribute>() is not null);
            Assert.IsTrue(incomplete.Length >= planned, "the planned commands are found, and the tools not built yet");
            foreach (EditorCommand command in incomplete)
            {
                Assert.IsTrue(stories.Contains(command.Incomplete!.Story), $"{command.Id} names {command.Incomplete.Story}, which is not in USER_STORIES.md");
                Assert.IsFalse(string.IsNullOrWhiteSpace(command.Incomplete.Missing), $"{command.Id} says what is missing");
            }
        }

        [TestMethod]
        public void EveryCommandBuiltHasAHandlerAndNoneNotBuiltHasOne()
        {
            Type[] types = typeof(MainViewModel).Assembly.GetTypes();
            HashSet<Type> handled = [.. types
                .SelectMany(type => type.GetInterfaces())
                .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() is Type definition
                    && (definition == typeof(IRequestHandler<>) || definition == typeof(IRequestHandler<,>)))
                .Select(contract => contract.GetGenericArguments()[0])];

            foreach (Type request in types.Where(type => type.GetCustomAttribute<EditorCommandAttribute>() is not null))
            {
                bool incomplete = request.GetCustomAttribute<IncompleteAttribute>() is not null;
                Assert.AreEqual(!incomplete, handled.Contains(request),
                    incomplete ? $"{request.Name} is not built yet and has a handler" : $"{request.Name} has no handler");
            }
        }

        [TestMethod]
        public void TheMenusHoldEveryCommandOnce()
        {
            MainViewModel model = NewModel();
            List<string> ids = [];
            void Collect(MenuLayout menu)
            {
                foreach (object item in menu.Items)
                {
                    if (item is MenuLayout submenu)
                    {
                        Collect(submenu);
                    }
                    else if (item is string id && id != Menus.Separator)
                    {
                        ids.Add(id);
                    }
                }
            }
            foreach (MenuLayout menu in Menus.Bar)
            {
                Collect(menu);
            }

            CollectionAssert.AllItemsAreUnique(ids, "a command shows in one menu");
            string[] outside = [.. model.Commands.All.Where(command => !ids.Contains(command.Id)).Select(command => command.Id).Order()];
            CollectionAssert.AreEqual(
                new[]
                {
                    "assets.drop", "assets.select", "assets.showFile", "brush.set", "camera.focus", "camera.look", "decals.addToGroup", "decals.deleteGroup",
                    "decals.lod", "decals.place", "decals.removeFromGroup", "decals.renameGroup", "decals.scale", "decals.selectGroup", "decals.showGroup", "decals.size",
                    "diagnostics.flush", "diagnostics.set", "diagnostics.upload", "edit.goTo", "edit.nudge", "edit.place", "file.enableVersionControl",
                    "file.restoreCommit", "file.showCommit", "file.showMapFiles", "lighting.set", "map.removeArea", "map.removeArmy", "map.renameArmy", "map.setAlliance",
                    "map.setArea", "map.setArmy", "map.setPlayableArea", "markers.addOptionalGroup", "markers.deleteChain", "markers.link", "markers.place",
                    "markers.renameChain", "markers.set", "markers.setChain", "markers.setWeather", "markers.showKind", "paint.addMaskRule", "paint.chooseMask",
                    "paint.moveMaskRule", "paint.moveStratum", "paint.removeMaskRule", "paint.removeStratum", "paint.setLayer", "paint.setMaskRule", "plugins.enable",
                    "plugins.setField", "presets.apply",
                    "presets.delete", "problems.show", "props.addRuleCondition", "props.chooseRule", "props.place", "props.removeFromPalette", "props.removeRule",
                    "props.removeRuleCondition", "props.scale", "props.set", "props.setRule", "props.setRuleCondition", "props.setRulePalette", "props.tilt",
                    "select.set", "terrain.addCurve", "terrain.chooseLayer", "terrain.moveLayer", "terrain.reloadLayer", "terrain.removeLayer", "terrain.setCurve",
                    "terrain.setLayer", "units.add", "units.moveGroup", "units.moveToArmy", "units.moveToGroup", "units.removeGroup", "units.renameGroup",
                    "view.addToToolbar", "view.gpuDebug", "view.removeFromToolbar", "view.resizePanel", "view.setToolbar", "view.togglePanel", "view.walkability",
                    "view.zoomTo", "water.removeWaveGenerator",
                    "water.set", "water.setWaveGenerator", "water.setWaveTexture", "workspace.convert", "workspace.setUp",
                }, outside,
                "the commands outside the menus are for automation and plugins: their buttons are on a page or panel");
            Assert.AreEqual("Terrain", model.Commands.Find("tool.terrain.curve")!.Menu, "a tool is in its mode's menu");
        }

        [TestMethod]
        public void IdsAndShortcutsAreUnique()
        {
            MainViewModel model = NewModel();

            string[] ids = [.. model.Commands.All.Select(c => c.Id)];
            CollectionAssert.AllItemsAreUnique(ids);

            KeyGesture[] gestures = [.. model.Commands.All.Where(c => c.Gesture is not null).Select(c => KeyGesture.Parse(c.Gesture!))];
            CollectionAssert.AllItemsAreUnique(gestures.Select(g => g.ToString()).ToArray(), "one command per shortcut");
            Assert.IsFalse(gestures.Any(g => g.KeyModifiers == KeyModifiers.None && g.Key is >= Key.A and <= Key.Z),
                "a letter alone would fire while typing in a text box");
        }

        [TestMethod]
        public async Task ACommandNotBuiltYetSaysWhatItIsToDo()
        {
            MainViewModel model = NewModel();

            bool ran = await model.Commands.RunAsync("terrain.saveBrush");

            Assert.IsFalse(ran);
            StringAssert.Contains(model.Status, "Not built yet: Terrain: Save the brush as a preset");
            StringAssert.Contains(model.Status, "user story T-2");
            StringAssert.Contains(model.Diagnostics.Console.Last().Message, "terrain.saveBrush: not built yet, user story T-2");
        }

        [TestMethod]
        public async Task BuiltCommandsDoWhatTheMenuDid()
        {
            MainViewModel model = NewModel();

            Assert.IsTrue(await model.Commands.RunAsync("view.3d"));
            Assert.AreEqual(MapViewTab.ThreeD, model.View);
            Assert.IsTrue(await model.Commands.RunAsync("view.console"));
            Assert.AreEqual(DockTab.Console, model.Dock);
            Assert.IsFalse(await model.Commands.RunAsync("file.save"), "nothing to save without a map");
            Assert.ThrowsException<ArgumentException>(() => model.Commands.RunAsync("no.such").GetAwaiter().GetResult());
        }

        [TestMethod]
        public async Task ToolsAreCommandsAndKeepTheirSettings()
        {
            MainViewModel model = NewModel();
            await model.OpenAsync("assets/maps/HardFFA.v0001");
            Assert.AreEqual("terrain.sculpt", model.ActiveTool!.Definition.Id, "the editor starts sculpting");
            Assert.IsInstanceOfType<SculptTool>(model.ActiveTool.Tool);
            Assert.AreEqual(0, model.ShownMask, "sculpting shows no mask");
            model.Tool<SculptTool>().Radius = 30;

            await model.Commands.RunAsync("tool.terrain.walkability");

            Assert.AreEqual("terrain", model.SelectedMode!.Definition.Id);
            Assert.IsTrue(model.IsToolIncomplete);
            Assert.IsNull(model.ActiveTool!.Tool, "a tool not built yet does nothing in the views");
            Assert.ThrowsException<InvalidOperationException>(() => model.Stroke([(50, 50)]));

            await model.Commands.RunAsync("tool.paint.strata");
            Assert.AreEqual("paint", model.SelectedMode!.Definition.Id);
            Assert.AreEqual(1, model.ShownMask, "the strata tool paints the first layer of a map that opens");
            model.StrataTool.Choose(3);
            Assert.AreEqual(3, model.ShownMask, "the top view shows the mask it paints");

            model.SelectTool("terrain.sculpt");
            Assert.AreEqual(30, model.Tool<SculptTool>().Radius, "a tool keeps its settings");
            Assert.AreEqual(12, model.StrataTool.Radius, "each tool has its own radius");
            Assert.AreEqual(3, model.StrataTool.Layer, "and the strata tool its layer");
        }

        [TestMethod]
        public void AToolIsBuiltWhenItCanBeMadeAndElseSaysWhatItWillDo()
        {
            foreach (ToolDefinition tool in EditorModes.All.SelectMany(mode => mode.Tools))
            {
                Assert.AreNotEqual(tool.Make is null, tool.Missing is null, $"{tool.Id}: Make or Missing, not both");
            }
        }

        [TestMethod]
        public async Task BrushSetChoosesTheToolOfALayerAndSetsTheActiveBrush()
        {
            MainViewModel model = NewModel();
            await model.OpenAsync("assets/maps/HardFFA.v0001");

            await model.Commands.RunAsync("brush.set", new JsonObject { ["layer"] = 2, ["radius"] = 20, ["erase"] = true }, interactive: false);
            Assert.AreEqual("paint.strata", model.ActiveTool!.Definition.Id);
            Assert.AreEqual(2, model.StrataTool.Layer);
            Assert.AreEqual(20, model.StrataTool.Radius);
            Assert.IsTrue(model.StrataTool.Erase);
            Assert.AreEqual(12, model.Tool<SculptTool>().Radius, "only the active tool's brush");

            await model.Commands.RunAsync("brush.set", new JsonObject { ["layer"] = 0, ["mode"] = "flatten", ["strength"] = 1 }, interactive: false);
            Assert.AreEqual("terrain.sculpt", model.ActiveTool!.Definition.Id);
            Assert.AreEqual(HeightBrushMode.Flatten, model.Tool<SculptTool>().Mode);
            Assert.AreEqual(1, model.Tool<SculptTool>().Strength);

            model.SelectTool("terrain.curve");
            Assert.ThrowsException<ArgumentException>(() => model.Commands.RunAsync("brush.set", new JsonObject { ["radius"] = 5 }, interactive: false).GetAwaiter().GetResult(),
                "a tool not built yet has no brush");
        }

        [TestMethod]
        public async Task ACommandTakesItsArgumentsByName()
        {
            MainViewModel model = NewModel();

            CommandRun opened = await model.Commands.RunAsync("file.open", new JsonObject { ["folder"] = "assets/maps/HardFFA.v0001" }, interactive: false);
            Assert.IsTrue(opened.Ran);
            Assert.IsTrue(model.HasMap);

            model.Stroke([(50, 50), (60, 60)]);
            CommandRun undone = await model.Commands.RunAsync("edit.undo", null, interactive: false);
            Assert.AreEqual(true, undone.Result, "a command answers");

            Assert.ThrowsException<ArgumentException>(() => model.Commands.RunAsync("edit.goTo", [], interactive: false).GetAwaiter().GetResult(),
                "an argument it needs is missing");
            Assert.ThrowsException<ArgumentException>(() => model.Commands.RunAsync("file.open", new JsonObject { ["path"] = "x" }, interactive: false).GetAwaiter().GetResult(),
                "an argument it does not take");
            Assert.AreEqual("file.open {folder?}", model.Commands.Find("file.open")!.Usage);
        }

        [TestMethod]
        public async Task ACommandWithoutAPersonAsksNothing()
        {
            MainViewModel model = NewModel();
            await model.OpenAsync("assets/maps/HardFFA.v0001");

            InvalidOperationException asked = Assert.ThrowsException<InvalidOperationException>(
                () => model.Commands.RunAsync("file.saveAs", null, interactive: false).GetAwaiter().GetResult());

            StringAssert.Contains(asked.Message, "Give the folder as an argument");
        }

        [TestMethod]
        public void ACommandWaitsForWhatItNeeds()
        {
            MainViewModel model = NewModel();

            Assert.IsFalse(model.Commands.CanRun(model.Commands.Find("file.save")!), "no map");
            Assert.IsFalse(model.Commands.CanRun(model.Commands.Find("edit.undo")!), "nothing to undo");
            Assert.IsFalse(model.Commands.CanRun(model.Commands.Find("file.commit")!), "no project");
            Assert.IsTrue(model.Commands.CanRun(model.Commands.Find("file.open")!));
            Assert.IsFalse(model.Commands.CanRun(model.Commands.Find("terrain.addLayer")!), "not built");
        }

        [TestMethod]
        public async Task TheWaterGoesOnAndOffAsOneStepOfUndo()
        {
            MainViewModel model = NewModel();
            await model.OpenAsync("assets/maps/HardFFA.v0001");
            bool before = model.Map!.Document.Water.HasWater;

            Assert.IsTrue(await model.Commands.RunAsync("water.toggle"));
            Assert.AreEqual(!before, model.Map.Document.Water.HasWater);
            Assert.AreEqual("Water on or off", model.Map.Session.UndoDescription);

            Assert.IsTrue((await model.Commands.RunAsync("water.toggle", new JsonObject { ["on"] = before }, interactive: false)).Ran);
            Assert.AreEqual(before, model.Map.Document.Water.HasWater);
            Assert.IsTrue(await model.Commands.RunAsync("edit.undo"));
            Assert.AreEqual(!before, model.Map.Document.Water.HasWater, "each run is one step");
        }

        [TestMethod]
        public void ACommandMadeAtRunTimeCanBeTakenAway()
        {
            MainViewModel model = NewModel();
            model.Commands.Register(new EditorCommand { Id = "plugin.example.hello", Title = "Hello", MakeRequest = _ => new ShowTopView() });

            Assert.IsTrue(model.Commands.Unregister("plugin.example.hello"));

            Assert.IsNull(model.Commands.Find("plugin.example.hello"));
            Assert.IsFalse(model.Commands.Unregister("plugin.example.hello"), "it is gone");
            Assert.ThrowsException<InvalidOperationException>(() => model.Commands.Unregister("file.save"), "a command of the menus stays");
        }

        [TestMethod]
        public void ThePaletteFindsCommandsBuiltFirst()
        {
            List<string> ran = [];
            PaletteViewModel palette = new PaletteViewModel(NewModel().Commands, id =>
            {
                ran.Add(id);
                return Task.CompletedTask;
            });

            palette.Open();
            palette.Query = "layer";

            Assert.IsTrue(palette.IsOpen);
            Assert.IsTrue(palette.Results.Count > 1);
            Assert.IsTrue(palette.Results.All(c => c.FullTitle.Contains("layer", StringComparison.OrdinalIgnoreCase) || c.Id.Contains("layer", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(palette.Results.SkipWhile(c => !c.IsIncomplete).Any(c => !c.IsIncomplete), "the commands that work come first");
            Assert.AreSame(palette.Results[0], palette.Selection);

            palette.RunSelectionAsync().GetAwaiter().GetResult();
            Assert.IsFalse(palette.IsOpen);
            CollectionAssert.AreEqual(new[] { palette.Results[0].Id }, ran);
        }

        [TestMethod]
        public async Task ACommandWithUndoStepIsOneStepAndLeavesNothingWhenItFails()
        {
            MainViewModel model = NewModel();
            await model.OpenAsync("assets/maps/HardFFA.v0001");
            CommandRegistry commands = CommandServices.Create(model, new DialogGuard(new NoDialogs()), typeof(CommandRegistryTest).Assembly);
            ushort before = model.Map!.Document.Terrain.Heightmap.Samples[40, 40];

            Assert.IsTrue(await commands.RunAsync("test.raise"));

            Assert.AreEqual(before + 100, model.Map.Document.Terrain.Heightmap.Samples[40, 40]);
            Assert.AreEqual("Raise a square", model.Map.Session.UndoDescription, "named after the command");
            Assert.AreEqual(1, model.Map.Session.History.Count);

            InvalidOperationException failed = Assert.ThrowsException<InvalidOperationException>(
                () => commands.RunAsync("test.raise", new JsonObject { ["fail"] = true }, interactive: false).GetAwaiter().GetResult());
            Assert.AreEqual("Failed halfway", failed.Message);
            Assert.AreEqual(before + 100, model.Map.Document.Terrain.Heightmap.Samples[40, 40], "the half-made edit is gone");
            Assert.AreEqual(1, model.Map.Session.History.Count, "and is no step of the history");

            model.ActiveTool!.Tool!.Press(10, 10);
            Assert.IsFalse(await commands.RunAsync("test.raise"), "a stroke holds the map's transaction");
            model.ActiveTool.Tool.Release();

            Assert.ThrowsException<InvalidOperationException>(() => commands.RunAsync("test.edit-without-step").GetAwaiter().GetResult(),
                "a command edits only with [UndoStep]");
        }
    }

    /// <summary>
    /// Raises a square of the heightmap by 100 as one step of undo; fails halfway when asked to.
    /// </summary>
    [UndoStep]
    [EditorCommand("test.raise", "Raise a square")]
    public sealed record RaiseSquare(bool Fail = false) : IRequest;

    public sealed class RaiseSquareHandler(CurrentEdit edit) : IRequestHandler<RaiseSquare>
    {
        public Task Handle(RaiseSquare request, CancellationToken cancellationToken)
        {
            var heights = edit.Transaction.Heightmap;
            for (int z = 40; z < 48; z++)
            {
                for (int x = 40; x < 48; x++)
                {
                    heights[x, z] = (ushort)(heights[x, z] + 100);
                }
            }
            return request.Fail ? throw new InvalidOperationException("Failed halfway") : Task.CompletedTask;
        }
    }

    [EditorCommand("test.edit-without-step", "Edit without a step")]
    public sealed record EditWithoutStep : IRequest;

    public sealed class EditWithoutStepHandler(CurrentEdit edit) : IRequestHandler<EditWithoutStep>
    {
        public Task Handle(EditWithoutStep request, CancellationToken cancellationToken)
        {
            _ = edit.Transaction;
            return Task.CompletedTask;
        }
    }
}
