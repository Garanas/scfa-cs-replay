using System.Diagnostics;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Versioning;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Version control's history, going back to a commit, pushing and pulling (user story F-18).
    /// </summary>
    [TestClass]
    public sealed class VersionControlTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "versioned");

        [ClassCleanup]
        public static void Cleanup()
        {
            if (Directory.Exists(Root))
            {
                foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(Root, recursive: true);
            }
        }

        private static string NewFolder() => Path.Combine(Root, Guid.NewGuid().ToString("N"));

        private static void Git(string folder, params string[] arguments)
        {
            ProcessStartInfo start = new ProcessStartInfo("git") { WorkingDirectory = folder, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }
            using Process process = Process.Start(start)!;
            process.WaitForExit();
        }

        /// <summary>
        /// HardFFA as a project under version control, with <paramref name="remote"/> to share through.
        /// </summary>
        private static async Task<MainViewModel> Versioned(string remote = "")
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            await model.SaveAsAsync(NewFolder());
            await model.Commands.RunAsync("file.enableVersionControl",
                new JsonObject { ["name"] = "Map maker", ["email"] = "maker@example.org", ["lfs"] = false, ["remote"] = remote }, interactive: false);
            return model;
        }

        private static void Rename(MainViewModel model, string name) =>
            model.EditMap($"Rename to {name}", document => document with { Scenario = document.Scenario with { Name = name } });

        [TestMethod]
        public async Task GoingBackToACommitIsOneStepOfUndo()
        {
            MainViewModel model = await Versioned();
            string original = model.Map!.Document.Scenario.Name;
            Rename(model, "Renamed FFA");
            await model.VersionControl.CommitAsync("Rename the map");

            IReadOnlyList<CommitInfo> history = (IReadOnlyList<CommitInfo>)(await model.Commands.RunAsync("file.history", null, interactive: false)).Result!;
            CollectionAssert.AreEqual(new[] { "Rename the map", ProjectRepository.FirstCommit }, history.Select(commit => commit.Subject).ToArray());
            Assert.IsTrue(model.VersionControl.IsOpen, "the page shows the history");

            IReadOnlyList<CommitFile> files = (IReadOnlyList<CommitFile>)(await model.Commands.RunAsync("file.showCommit", new JsonObject { ["commit"] = history[0].ShortHash }, interactive: false)).Result!;
            Assert.AreEqual(history[0], model.VersionControl.ChosenCommit);
            CollectionAssert.Contains(files.Select(file => file.Path).ToArray(), "map.json");

            int steps = model.Map.Session.History.Count;
            await model.Commands.RunAsync("file.restoreCommit", new JsonObject { ["commit"] = history[1].Hash }, interactive: false);

            Assert.AreEqual(original, model.Map.Document.Scenario.Name, "the map is as the first commit holds it");
            Assert.AreEqual(steps + 1, model.Map.Session.History.Count, "one step of undo");
            Assert.IsTrue(model.Map.Session.IsModified, "not saved: the files on disk stay");
            Assert.AreEqual(0, model.VersionControl.Repository!.Changes);
            model.Undo();
            Assert.AreEqual("Renamed FFA", model.Map.Document.Scenario.Name, "nothing is lost");
        }

        [TestMethod]
        public async Task GoingBackFromThePageUsesTheChosenCommit()
        {
            MainViewModel model = await Versioned();
            string original = model.Map!.Document.Scenario.Name;
            Rename(model, "Renamed FFA");
            await model.VersionControl.CommitAsync("Rename the map");
            await model.VersionControl.OpenAsync();

            model.VersionControl.ChosenCommit = model.VersionControl.History[1];
            await model.VersionControl.GoBackCommand.ExecuteAsync(null);

            Assert.IsNull(model.VersionControl.Error);
            Assert.AreEqual(original, model.Map.Document.Scenario.Name);
        }

        [TestMethod]
        public async Task PushesAndPullsThroughTheSharedRepository()
        {
            string shared = NewFolder();
            Directory.CreateDirectory(shared);
            Git(shared, "init", "--bare", "--initial-branch=main");
            MainViewModel mine = await Versioned(shared);
            Assert.IsTrue(mine.VersionControl.IsShared);
            await mine.Commands.RunAsync("file.push", null, interactive: false);
            Assert.AreEqual(0, mine.VersionControl.Repository!.Ahead);

            // another computer clones it and pushes a change
            string theirs = NewFolder();
            Git(Root, "clone", shared, theirs);
            Git(theirs, "config", "user.name", "Other maker");
            Git(theirs, "config", "user.email", "other@example.org");
            MainViewModel other = new MainViewModel(new MainViewModelTest.Answers());
            await other.OpenAsync(theirs);
            Rename(other, "Their FFA");
            await other.VersionControl.CommitAsync("Their name");
            await other.VersionControl.PushAsync();

            Rename(mine, "Unsaved FFA");
            await Assert.ThrowsExceptionAsync<Project.MapProjectException>(() => mine.VersionControl.PullAsync(), "unsaved changes stop a pull");
            mine.Undo();

            IReadOnlyList<string> changed = (IReadOnlyList<string>)(await mine.Commands.RunAsync("file.pull", null, interactive: false)).Result!;

            CollectionAssert.Contains(changed.ToArray(), "map.json");
            Assert.AreEqual("Their FFA", mine.Map!.Document.Scenario.Name, "what was pulled is read again");
            Assert.IsFalse(mine.Map.Session.IsModified);
            StringAssert.EndsWith(mine.VersionControl.Repository!.LastCommit, "Their name");
        }
    }
}
