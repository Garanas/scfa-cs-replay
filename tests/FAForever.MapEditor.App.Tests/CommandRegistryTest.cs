using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Input;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;

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
        public void EveryIncompleteFunctionNamesItsStory()
        {
            // the [Incomplete] functions of the whole editor, for the list of what is left
            (string Function, IncompleteAttribute Incomplete)[] incomplete = [.. typeof(MainViewModel).Assembly.GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Select(method => (Function: $"{type.Name}.{method.Name}", Incomplete: method.GetCustomAttribute<IncompleteAttribute>()))
                    .Where(found => found.Incomplete is not null))
                .Select(found => (found.Function, found.Incomplete!))];
            HashSet<string> stories = Stories();

            Assert.IsTrue(incomplete.Length > 40);
            foreach ((string function, IncompleteAttribute attribute) in incomplete)
            {
                Assert.IsTrue(stories.Contains(attribute.Story), $"{function} names {attribute.Story}, which is not in USER_STORIES.md");
                Assert.IsFalse(string.IsNullOrWhiteSpace(attribute.Missing), $"{function} says what is missing");
            }
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

            bool ran = await model.Commands.RunAsync("terrain.addLayer");

            Assert.IsFalse(ran);
            StringAssert.Contains(model.Status, "Not built yet: Terrain: Add a height layer");
            StringAssert.Contains(model.Status, "user story T-5");
            StringAssert.Contains(model.Console.Last().Message, "terrain.addLayer: not built yet, user story T-5");
        }

        [TestMethod]
        public async Task BuiltCommandsDoWhatTheMenuDid()
        {
            MainViewModel model = NewModel();

            Assert.IsTrue(await model.Commands.RunAsync("view.3d"));
            Assert.AreEqual(1, model.ViewIndex);
            Assert.IsTrue(await model.Commands.RunAsync("view.console"));
            Assert.AreEqual(3, model.DockIndex);
            Assert.IsFalse(await model.Commands.RunAsync("file.save"), "nothing to save without a map");
            Assert.ThrowsException<ArgumentException>(() => model.Commands.RunAsync("no.such").GetAwaiter().GetResult());
        }

        [TestMethod]
        public async Task ToolsAreCommandsAndFollowTheBrush()
        {
            MainViewModel model = NewModel();
            await model.OpenAsync("assets/maps/HardFFA.v0001");
            Assert.AreEqual("terrain.sculpt", model.ActiveTool!.Definition.Id, "the editor starts sculpting");
            Assert.IsTrue(model.CanPaint);

            await model.Commands.RunAsync("tool.terrain.curve");

            Assert.AreEqual("terrain", model.SelectedMode!.Definition.Id);
            Assert.IsTrue(model.IsToolIncomplete);
            Assert.IsFalse(model.CanPaint, "a tool not built yet does nothing in the views");

            model.SelectPaintTarget(1);
            Assert.AreEqual("paint.strata", model.ActiveTool!.Definition.Id, "painting a mask is the strata tool");
            Assert.AreEqual("paint", model.SelectedMode!.Definition.Id);

            model.SelectTool("terrain.sculpt");
            Assert.AreEqual(0, model.PaintedLayer, "sculpting paints the heights");
        }

        [TestMethod]
        public void ThePaletteFindsCommandsBuiltFirst()
        {
            MainViewModel model = NewModel();

            model.OpenPalette();
            model.PaletteQuery = "layer";

            Assert.IsTrue(model.IsPaletteOpen);
            Assert.IsTrue(model.PaletteResults.Count > 1);
            Assert.IsTrue(model.PaletteResults.All(c => c.FullTitle.Contains("layer", StringComparison.OrdinalIgnoreCase) || c.Id.Contains("layer", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(model.PaletteResults.SkipWhile(c => !c.IsIncomplete).Any(c => !c.IsIncomplete), "the commands that work come first");
            Assert.AreSame(model.PaletteResults[0], model.PaletteSelection);
        }
    }
}
