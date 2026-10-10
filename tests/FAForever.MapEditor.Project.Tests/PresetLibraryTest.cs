using System.Text.Json.Nodes;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class PresetLibraryTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private enum Mode
        {
            Raise,
            Flatten,
        }

        private sealed record BrushSettings(Mode Mode, double Radius, string? Shape = null);

        [TestMethod]
        public void SavesListsReadsAndDeletesPresetsOfAKind()
        {
            Workspace workspace = Workspace.Create(ProjectFolders.New("workspace"), "C:/game", null, "D:/vault");
            Assert.IsTrue(Directory.Exists(workspace.PresetsFolder), "a new workspace has the folder");
            PresetLibrary presets = workspace.Presets;

            string file = presets.Save(Preset.Of("brush", "Big crater", new BrushSettings(Mode.Flatten, 40, "craters/crater01.png")));
            presets.Save(Preset.Of("brush", "a small hill", new BrushSettings(Mode.Raise, 6)));
            presets.Save(new Preset("lighting", "Evergreen dusk", new JsonObject { ["sunColor"] = "#ffcc8844" }));

            Assert.AreEqual(Path.Combine(workspace.PresetsFolder, "brush", "Big crater.json"), file);
            StringAssert.Contains(File.ReadAllText(file), "\"mode\": \"Flatten\"", "the editor's JSON: camel case, enums as text");
            CollectionAssert.AreEqual(new[] { "brush", "lighting" }, presets.Kinds().ToArray());
            CollectionAssert.AreEqual(new[] { "a small hill", "Big crater" }, presets.List("brush").Select(preset => preset.Name).ToArray());
            Assert.AreEqual(new BrushSettings(Mode.Flatten, 40, "craters/crater01.png"), presets.Find("brush", "Big crater")!.Read<BrushSettings>());
            Assert.IsNull(presets.Find("brush", "nothing"));
            Assert.AreEqual(0, presets.List("water").Count);

            presets.Save(Preset.Of("brush", "Big crater", new BrushSettings(Mode.Raise, 41)));
            Assert.AreEqual(41, presets.Find("brush", "Big crater")!.Read<BrushSettings>().Radius, "saved again under its name");
            Assert.AreEqual("Big crater 2", presets.FreeName("brush", "Big crater"));
            Assert.AreEqual("Water", presets.FreeName("brush", "Water"));

            Assert.IsTrue(presets.Delete("brush", "Big crater"));
            Assert.IsFalse(presets.Delete("brush", "Big crater"));
            Assert.AreEqual(1, presets.List("brush").Count);
        }

        [TestMethod]
        public void NamesThatAreNoFileNamesStillWork()
        {
            PresetLibrary presets = new PresetLibrary(ProjectFolders.New("presets"));

            string file = presets.Save(Preset.Of("brush", "Ridge: 50/50?", new BrushSettings(Mode.Raise, 10)));

            Assert.AreEqual("Ridge- 50-50-.json", Path.GetFileName(file));
            Assert.AreEqual("Ridge: 50/50?", presets.List("brush").Single().Name, "the name is the file's, not its file name");
            Assert.IsNotNull(presets.Find("brush", "Ridge: 50/50?"));
            Assert.ThrowsException<ArgumentException>(() => presets.Save(Preset.Of("Brush", "x", new BrushSettings(Mode.Raise, 1))), "kinds are lower case");
            Assert.ThrowsException<ArgumentException>(() => presets.Save(Preset.Of("../brush", "x", new BrushSettings(Mode.Raise, 1))));
            Assert.ThrowsException<ArgumentException>(() => presets.Save(Preset.Of("brush", " ", new BrushSettings(Mode.Raise, 1))));
        }

        [TestMethod]
        public void LeavesOutAFileItCannotRead()
        {
            PresetLibrary presets = new PresetLibrary(ProjectFolders.New("presets"));
            presets.Save(Preset.Of("brush", "Good", new BrushSettings(Mode.Raise, 10)));
            File.WriteAllText(Path.Combine(presets.Folder, "brush", "broken.json"), "{ not json");
            List<string> unreadable = [];

            IReadOnlyList<Preset> listed = presets.List("brush", unreadable);

            Assert.AreEqual("Good", listed.Single().Name);
            StringAssert.Contains(unreadable.Single(), "broken.json");
            Assert.ThrowsException<MapProjectException>(() => new Preset("brush", "Wrong", new JsonObject { ["radius"] = "far" }).Read<BrushSettings>());
        }
    }
}
