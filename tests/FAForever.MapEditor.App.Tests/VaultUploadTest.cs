using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Checks;
using FAForever.MapEditor.Project.Tests;
using FAForever.MapEditor.Project.Vault;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// <c>file.upload</c> (user story M-8) against a fake vault on the loopback address: what it
    /// refuses, what it asks, what it sends, and the vault's errors. Nothing goes to FAForever.
    /// </summary>
    [TestClass]
    public class VaultUploadTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        /// <summary>
        /// The questions the upload asks, answered by the test.
        /// </summary>
        private sealed class Answers : IEditorDialogs
        {
            public bool Yes { get; set; }

            public List<string> Questions { get; } = [];

            public List<Uri> Opened { get; } = [];

            /// <summary>
            /// What the browser does with a page that opens: the fake sign-in.
            /// </summary>
            public Action<Uri>? Browser { get; set; }

            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult<string?>(null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Task.FromResult(SaveChoice.Cancel);

            public Task<bool> ConfirmAsync(string title, string message, string action)
            {
                Questions.Add(message);
                return Task.FromResult(Yes);
            }

            public Task OpenInBrowserAsync(Uri page)
            {
                Opened.Add(page);
                Browser?.Invoke(page);
                return Task.CompletedTask;
            }
        }

        private static async Task<(MainViewModel Model, Answers Answers)> Open(bool script = true)
        {
            Answers answers = new Answers();
            MainViewModel model = new MainViewModel(answers);
            await model.OpenAsync(HardFfa);
            if (script)
            {
                // the test map has no script, which the vault wants
                model.EditMap("Add the script", document => document with { Script = [.. System.Text.Encoding.Latin1.GetBytes(NewMap.DefaultScript)] });
            }
            return (model, answers);
        }

        /// <summary>
        /// Runs the upload expecting a refusal, and answers why.
        /// </summary>
        private static async Task<string> Refused(MainViewModel model, JsonObject arguments)
        {
            Exception exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Run(model, arguments));
            return exception.Message;
        }

        private static Task<CommandRun> Run(MainViewModel model, JsonObject arguments, bool interactive = false) =>
            model.Commands.RunAsync("file.upload", arguments, interactive);

        [TestMethod]
        public async Task UploadsTheNextVersionToTheServerAndRaisesTheMapsVersion()
        {
            (MainViewModel model, _) = await Open();
            using FakeVaultServer server = new FakeVaultServer();
            int steps = model.History.Count;

            CommandRun run = await Run(model, new JsonObject { ["server"] = server.Url.ToString(), ["confirm"] = true, ["token"] = "test-token" });

            Assert.IsTrue(run.Ran, run.Why ?? model.Status);
            JsonObject result = (JsonObject)run.Result!;
            Assert.IsTrue(result["uploaded"]!.GetValue<bool>());
            Assert.AreEqual(2, result["version"]!.GetValue<int>());
            Assert.AreEqual("hardffa.v0002.zip", result["file"]!.GetValue<string>());
            FakeVaultServer.Received request = server.Requests.Single();
            Assert.AreEqual("/maps/upload", request.Path);
            Assert.AreEqual("Bearer test-token", request.Authorization);
            Dictionary<string, (string Headers, byte[] Content)> parts = FakeVaultServer.Parts(request);
            StringAssert.Contains(parts["file"].Headers, "filename=hardffa.v0002.zip");
            using (ZipArchive zip = new ZipArchive(new MemoryStream(parts["file"].Content)))
            {
                Assert.IsTrue(zip.Entries.All(e => e.FullName.StartsWith("hardffa.v0002/", StringComparison.Ordinal)), "one folder in lower case");
                ZipArchiveEntry scenario = zip.Entries.Single(e => e.FullName.EndsWith("_scenario.lua", StringComparison.Ordinal));
                using StreamReader reader = new StreamReader(scenario.Open());
                StringAssert.Contains(reader.ReadToEnd(), "map_version = 2");
            }
            using JsonDocument metadata = JsonDocument.Parse(parts["metadata"].Content);
            Assert.IsFalse(metadata.RootElement.GetProperty("isRanked").GetBoolean());

            Assert.AreEqual(2, model.Map!.Document.Version, "the map goes on from the uploaded version");
            // the raised version, and the preview the export renders (HardFFA's own differs from the editor's)
            Assert.AreEqual(steps + 2, model.History.Count);
            Assert.AreEqual("Uploaded", model.Upload.Stage);
            StringAssert.StartsWith(model.Status, "Uploaded HardFFA version 2");
        }

        [TestMethod]
        public async Task WithoutAPersonItNeedsATestServerAndConfirmation()
        {
            (MainViewModel model, _) = await Open();
            using FakeVaultServer server = new FakeVaultServer();

            string noServer = await Refused(model, new JsonObject { ["confirm"] = true, ["token"] = "t" });
            StringAssert.Contains(noServer, "never uploads to FAForever's vault");

            string real = await Refused(model, new JsonObject { ["server"] = "https://api.faforever.com", ["confirm"] = true, ["token"] = "t" });

            string unconfirmed = await Refused(model, new JsonObject { ["server"] = server.Url.ToString(), ["token"] = "t" });
            StringAssert.Contains(unconfirmed, "confirm");

            string noToken = await Refused(model, new JsonObject { ["server"] = server.Url.ToString(), ["confirm"] = true });

            Assert.AreEqual(0, server.Requests.Count, "nothing was sent");
            Assert.AreEqual(1, model.Map!.Document.Version);
        }

        [TestMethod]
        public async Task TheSandboxIsNeverUploaded()
        {
            (MainViewModel model, _) = await Open();
            using FakeVaultServer server = new FakeVaultServer();
            model.EditMap("Make it the sandbox", document => document with { Scenario = document.Scenario with { Publishable = false } });

            string run = await Refused(model, new JsonObject { ["server"] = server.Url.ToString(), ["confirm"] = true, ["token"] = "t" });
            StringAssert.Contains(run, "sandbox");
            Assert.AreEqual(0, server.Requests.Count);
        }

        [TestMethod]
        public async Task TheMapsErrorsStopIt()
        {
            (MainViewModel model, _) = await Open();
            using FakeVaultServer server = new FakeVaultServer();
            model.Problems.Checks.Register(new ProblemCheck("test-error", "A test", _ => [new Problem("test-error", ProblemSeverity.Error, "A test error")]));

            string run = await Refused(model, new JsonObject { ["server"] = server.Url.ToString(), ["confirm"] = true, ["token"] = "t" });
            StringAssert.Contains(run, "A test error");
            Assert.AreEqual(DockTab.Problems, model.Dock, "the problems show");
            Assert.AreEqual(0, server.Requests.Count);
        }

        [TestMethod]
        public async Task TheVaultsRulesAreCheckedFirst()
        {
            (MainViewModel model, _) = await Open();
            using FakeVaultServer server = new FakeVaultServer();
            model.EditMap("Rename", document => document with { Scenario = document.Scenario with { Name = "HardFFA!" } });

            string run = await Refused(model, new JsonObject { ["server"] = server.Url.ToString(), ["confirm"] = true, ["token"] = "t" });
            StringAssert.Contains(run, "only letters");
            Assert.AreEqual(0, server.Requests.Count);
        }

        [TestMethod]
        public async Task APersonIsAskedWithWhatGoesAndNoKeepsTheMap()
        {
            (MainViewModel model, Answers answers) = await Open();
            using FakeVaultServer server = new FakeVaultServer();
            answers.Yes = false;

            CommandRun run = await Run(model, new JsonObject { ["server"] = server.Url.ToString(), ["token"] = "t" }, interactive: true);

            Assert.IsTrue(run.Ran, run.Why);
            Assert.IsFalse(((JsonObject)run.Result!)["uploaded"]!.GetValue<bool>());
            string question = answers.Questions.Single();
            StringAssert.Contains(question, "HardFFA, version 2 (hardffa.v0002.zip, ");
            StringAssert.Contains(question, server.Url.ToString());
            Assert.AreEqual(0, server.Requests.Count);
            Assert.AreEqual(1, model.Map!.Document.Version, "unchanged");
            Assert.AreEqual("Cancelled", model.Upload.Stage);
        }

        [TestMethod]
        public async Task APersonSignsInInTheBrowserOnce()
        {
            (MainViewModel model, Answers answers) = await Open();
            using FakeVaultServer server = new FakeVaultServer();
            model.Upload.OAuth = new FafOAuthSettings(new Uri(server.Url, "oauth2/auth"), new Uri(server.Url, "oauth2/token"), "client", "openid upload_map");
            using HttpClient browser = new HttpClient();
            answers.Yes = true;
            answers.Browser = page =>
            {
                System.Collections.Specialized.NameValueCollection query = System.Web.HttpUtility.ParseQueryString(page.Query);
                _ = Task.Run(() => browser.GetAsync($"{query["redirect_uri"]}/?code=c&state={Uri.EscapeDataString(query["state"]!)}"));
            };

            CommandRun first = await Run(model, new JsonObject { ["server"] = server.Url.ToString() }, interactive: true);
            CommandRun second = await Run(model, new JsonObject { ["server"] = server.Url.ToString() }, interactive: true);

            Assert.IsTrue(first.Ran, first.Why);
            Assert.IsTrue(second.Ran, second.Why);
            Assert.AreEqual(1, answers.Opened.Count, "the sign-in is kept in memory");
            FakeVaultServer.Received[] uploads = [.. server.Requests.Where(r => r.Path == "/maps/upload")];
            Assert.AreEqual(2, uploads.Length);
            Assert.IsTrue(uploads.All(u => u.Authorization == "Bearer fake-access"));
            Assert.AreEqual(3, model.Map!.Document.Version, "version 2, then 3");
        }

        [TestMethod]
        public async Task TheVaultsErrorShowsAndTheMapStays()
        {
            (MainViewModel model, _) = await Open();
            using FakeVaultServer server = new FakeVaultServer
            {
                UploadAnswer = (422, """{"errors":[{"status":"422","title":"Not the original author","detail":"Only the original author can upload new versions of this map.","code":"106"}]}"""),
            };

            VaultUploadException exception = await Assert.ThrowsExceptionAsync<VaultUploadException>(() =>
                Run(model, new JsonObject { ["server"] = server.Url.ToString(), ["confirm"] = true, ["token"] = "t" }));
            StringAssert.Contains(exception.Message, "Only the original author");
            StringAssert.Contains(model.Status, "Only the original author");
            Assert.AreEqual("Failed", model.Upload.Stage);
            Assert.AreEqual("106", model.Upload.LastErrors.Single().Code);
            Assert.AreEqual(1, model.Map!.Document.Version);
            JsonObject state = model.Upload.Describe();
            Assert.AreEqual("106", state["lastErrors"]![0]!["code"]!.GetValue<string>());
        }

        [TestMethod]
        public async Task AMapWithoutAFileTheVaultWantsIsRefused()
        {
            (MainViewModel model, _) = await Open(script: false);
            using FakeVaultServer server = new FakeVaultServer();

            string why = await Refused(model, new JsonObject { ["server"] = server.Url.ToString(), ["confirm"] = true, ["token"] = "t" });

            StringAssert.Contains(why, "HardFFA_script.lua");
            Assert.AreEqual(0, server.Requests.Count);
        }

        [TestMethod]
        public async Task ConfirmationAndTheBrowserAreRefusedWithoutAPerson()
        {
            DialogGuard guard = new DialogGuard(new Answers { Yes = true }) { Quiet = true };
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => guard.ConfirmAsync("Upload", "?", "Upload"));
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => guard.OpenInBrowserAsync(new Uri("https://example.org")));
            await Task.CompletedTask;
        }
    }
}
