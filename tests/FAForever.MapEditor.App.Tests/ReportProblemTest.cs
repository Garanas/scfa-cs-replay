using System.IO.Compression;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// A problem report (user story F-17): the recent sessions' telemetry, the editor and system,
    /// and the open map's project when chosen, in one zip; nothing is sent.
    /// </summary>
    [TestClass]
    public sealed class ReportProblemTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static string NewFolder() => Path.Combine(AppContext.BaseDirectory, "reports", Guid.NewGuid().ToString("N"));

        private static string[] Entries(string zip)
        {
            using ZipArchive archive = ZipFile.OpenRead(zip);
            return [.. archive.Entries.Select(entry => entry.FullName)];
        }

        private static JsonObject Report(string zip)
        {
            using ZipArchive archive = ZipFile.OpenRead(zip);
            using Stream stream = archive.GetEntry("report.json")!.Open();
            return JsonNode.Parse(stream)!.AsObject();
        }

        [TestMethod]
        public async Task BundlesTheSessionsAndTheChosenProject()
        {
            string telemetry = NewFolder();
            using Telemetry session = new Telemetry(telemetry, background: false);
            MainViewModelTest.Answers answers = new MainViewModelTest.Answers();
            MainViewModel model = new MainViewModel(answers, session);
            model.Logger("Editor").LogWarning("Something odd happened");
            await model.OpenAsync(HardFfa);
            string project = NewFolder();
            await model.SaveAsAsync(project);
            string? shown = null;
            model.ShowFolderRequested += folder => shown = folder;

            string output = NewFolder();
            string zip = (string)(await model.Commands.RunAsync("help.reportProblem",
                new JsonObject { ["folder"] = output, ["description"] = "The brush froze", ["includeMap"] = true }, interactive: false)).Result!;

            Assert.AreEqual(output, Path.GetDirectoryName(zip));
            Assert.AreEqual(output, shown, "the report shows in the file manager");
            string[] entries = Entries(zip);
            CollectionAssert.Contains(entries, "report.json");
            CollectionAssert.Contains(entries, "telemetry/" + Path.GetFileName(session.LogsFile));
            CollectionAssert.Contains(entries, "project/map.json");
            JsonObject report = Report(zip);
            Assert.AreEqual("The brush froze", report["description"]!.GetValue<string>());
            Assert.AreEqual(Project.Workspaces.AssetConverter.EditorVersion, report["editorVersion"]!.GetValue<string>());
            Assert.IsFalse(string.IsNullOrEmpty(report["os"]!.GetValue<string>()));
            using (ZipArchive archive = ZipFile.OpenRead(zip))
            using (StreamReader logs = new StreamReader(archive.GetEntry("telemetry/" + Path.GetFileName(session.LogsFile))!.Open()))
            {
                StringAssert.Contains(logs.ReadToEnd(), "Something odd happened", "the session is written before it is bundled");
            }

            string without = (string)(await model.Commands.RunAsync("help.reportProblem", new JsonObject { ["folder"] = NewFolder() }, interactive: false)).Result!;
            Assert.IsFalse(Entries(without).Any(entry => entry.StartsWith("project/")), "the project goes along only when chosen");
        }

        [TestMethod]
        public void TakesTheNewestSessions()
        {
            string telemetry = NewFolder();
            Directory.CreateDirectory(telemetry);
            for (int day = 1; day <= 7; day++)
            {
                File.WriteAllText(Path.Combine(telemetry, $"2026100{day}-120000-abcdef0{day}.logs.jsonl"), "{}");
                File.WriteAllText(Path.Combine(telemetry, $"2026100{day}-120000-abcdef0{day}.metrics.jsonl"), "{}");
            }
            File.WriteAllText(Path.Combine(telemetry, "20261007-120000-abcdef07.logs.jsonl.sent"), "3");

            string[] files = [.. ProblemReport.SessionFiles(telemetry, 5).Select(Path.GetFileName)!];

            Assert.AreEqual(10, files.Length, "two files a session, and no .sent");
            StringAssert.StartsWith(files[0], "20261007");
            Assert.IsFalse(files.Any(file => file!.StartsWith("20261002")), "the two oldest are left out");
        }

        [TestMethod]
        public async Task ThePageAsksForTheFolder()
        {
            MainViewModelTest.Answers answers = new MainViewModelTest.Answers();
            using Telemetry session = new Telemetry(NewFolder(), background: false);
            MainViewModel model = new MainViewModel(answers, session);
            await model.OpenAsync(HardFfa);

            await model.Commands.RunAsync("help.reportProblem");
            Assert.IsTrue(model.Report.IsOpen);
            Assert.IsFalse(model.Report.CanIncludeProject, "an imported map is no project");

            string output = NewFolder();
            answers.Folders.Enqueue(output);
            model.Report.Description = "It crashed";
            await model.Report.MakeCommand.ExecuteAsync(null);

            Assert.IsFalse(model.Report.IsOpen);
            Assert.IsNotNull(model.Report.LastReport);
            Assert.IsTrue(File.Exists(model.Report.LastReport));
            Assert.AreEqual("It crashed", Report(model.Report.LastReport)["description"]!.GetValue<string>());
        }
    }
}
