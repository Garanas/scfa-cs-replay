using System.IO.Compression;
using System.Net;
using System.Text.Json;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Vault;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The upload to the vault (user story M-8) against a fake API on the loopback address: never
    /// the real vault.
    /// </summary>
    [TestClass]
    public class VaultUploadTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static MapDocument Map(string name = "Upload Test") =>
            NewMap.Create(new NewMapSettings { Size = 256, Name = name }) is var document
                ? document with { Name = VaultRules.FolderName(name), FileName = VaultRules.FolderName(name), Scenario = document.Scenario with { Name = name, Description = "A map to test with" } }
                : throw new InvalidOperationException();

        private static string ExportedFolder(MapDocument document, int version) =>
            MapExporter.WriteFolder(document with { Version = version }, ProjectFolders.New("upload"));

        [TestMethod]
        public void TheArchiveHoldsTheFolderInLowerCase()
        {
            string folder = ExportedFolder(Map(), 3);
            string upper = Path.Combine(Path.GetDirectoryName(folder)!, "Upload_Test.v0003");
            Directory.Move(folder, upper);
            byte[] zip = VaultArchive.Create(upper);
            using ZipArchive archive = new ZipArchive(new MemoryStream(zip));
            string[] names = [.. archive.Entries.Select(e => e.FullName)];
            CollectionAssert.Contains(names, "upload_test.v0003/");
            Assert.IsTrue(names.All(n => n.StartsWith("upload_test.v0003/", StringComparison.Ordinal)), string.Join(", ", names));
            foreach (string ending in VaultArchive.RequiredEndings)
            {
                Assert.IsTrue(names.Any(n => n.EndsWith(ending, StringComparison.Ordinal)), ending);
            }
            Assert.AreEqual("upload_test.v0003.zip", VaultArchive.FileName(upper));
            Assert.AreEqual(0, VaultArchive.MissingFiles(upper, adaptive: false).Count);
            CollectionAssert.AreEquivalent(new[] { "_options.lua", "_tables.lua" }, VaultArchive.MissingFiles(upper, adaptive: true).ToArray());
        }

        [TestMethod]
        public void TheRulesAreTheApis()
        {
            Assert.AreEqual(0, VaultRules.Check(Map(), 1).Count);
            Assert.AreEqual("theta_passage", VaultRules.FolderName("Theta Passage"));
            Assert.AreNotEqual(0, VaultRules.Check(Map("Map!"), 1).Count, "a character the vault does not take");
            Assert.AreNotEqual(0, VaultRules.Check(Map("abc"), 1).Count, "too short");
            Assert.AreNotEqual(0, VaultRules.Check(Map("1 Map"), 1).Count, "starts with a digit");
            Assert.AreNotEqual(0, VaultRules.Check(Map("a-b-c-d-e"), 1).Count, "four minus signs");
            Assert.AreNotEqual(0, VaultRules.Check(Map(), 10000).Count, "version");
            MapDocument other = Map() with { Name = "something_else" };
            Assert.IsTrue(VaultRules.Check(other, 1).Any(r => r.Contains("upload_test", StringComparison.Ordinal)));
            MapDocument undescribed = Map() with { Scenario = Map().Scenario with { Description = "" } };
            Assert.AreNotEqual(0, VaultRules.Check(undescribed, 1).Count);
        }

        [TestMethod]
        public async Task TheUploadIsMultipartWithTheZipAndTheMetadata()
        {
            using FakeVaultServer server = new FakeVaultServer();
            string folder = ExportedFolder(Map(), 2);
            byte[] zip = VaultArchive.Create(folder);
            List<double> progress = [];
            using HttpClient http = new HttpClient();
            await MapUploadClient.UploadAsync(http, server.Url, "token-1", zip, VaultArchive.FileName(folder), new MapUploadMetadata(false),
                new SynchronousProgress(progress.Add), CancellationToken.None);

            Assert.AreEqual(1, server.Requests.Count);
            FakeVaultServer.Received request = server.Requests.Single();
            Assert.AreEqual("POST", request.Method);
            Assert.AreEqual("/maps/upload", request.Path);
            Assert.AreEqual("Bearer token-1", request.Authorization);
            StringAssert.StartsWith(request.ContentType, "multipart/form-data");
            Dictionary<string, (string Headers, byte[] Content)> parts = FakeVaultServer.Parts(request);
            CollectionAssert.AreEquivalent(new[] { "file", "metadata" }, parts.Keys.ToArray());
            StringAssert.Contains(parts["file"].Headers, "filename=upload_test.v0002.zip");
            StringAssert.Contains(parts["file"].Headers, "application/zip");
            CollectionAssert.AreEqual(zip, parts["file"].Content);
            StringAssert.Contains(parts["metadata"].Headers, "application/json");
            using JsonDocument metadata = JsonDocument.Parse(parts["metadata"].Content);
            Assert.IsFalse(metadata.RootElement.GetProperty("isRanked").GetBoolean());
            Assert.IsFalse(metadata.RootElement.TryGetProperty("licenseId", out _), "the vault's default licence");
            Assert.AreEqual(1, progress[^1], 1e-9);
        }

        [TestMethod]
        public async Task TheApisErrorsAreReadBack()
        {
            using FakeVaultServer server = new FakeVaultServer
            {
                UploadAnswer = (422, """{"requestId":"r1","errors":[{"status":"422","title":"Map version exists","detail":"A map with this version already exists.","code":"107","meta":{"args":[]}}]}"""),
            };
            using HttpClient http = new HttpClient();
            VaultUploadException exception = await Assert.ThrowsExceptionAsync<VaultUploadException>(() =>
                MapUploadClient.UploadAsync(http, server.Url, "t", [1, 2, 3], "a.v0001.zip", new MapUploadMetadata(false), null, CancellationToken.None));
            Assert.AreEqual((HttpStatusCode)422, exception.Status);
            Assert.AreEqual("107", exception.Errors.Single().Code);
            StringAssert.Contains(exception.Message, "already exists");

            server.UploadAnswer = (401, "");
            exception = await Assert.ThrowsExceptionAsync<VaultUploadException>(() =>
                MapUploadClient.UploadAsync(http, server.Url, "t", [1], "a.v0001.zip", new MapUploadMetadata(false), null, CancellationToken.None));
            StringAssert.Contains(exception.Message, "sign in again");
        }

        [TestMethod]
        public void TheRealVaultIsKnown()
        {
            Assert.IsTrue(MapUploadClient.IsRealVault(MapUploadClient.FafApi));
            Assert.IsTrue(MapUploadClient.IsRealVault(new Uri("https://API.faforever.com")));
            Assert.IsFalse(MapUploadClient.IsRealVault(new Uri("http://127.0.0.1:5000/")));
            Assert.IsFalse(MapUploadClient.IsRealVault(new Uri("https://notfaforever.com/")));
            Assert.AreEqual("http://127.0.0.1:5000/api/maps/upload", MapUploadClient.UploadUrl(new Uri("http://127.0.0.1:5000/api")).ToString());
        }

        [TestMethod]
        public async Task SigningInExchangesTheCodeWithTheVerifier()
        {
            using FakeVaultServer server = new FakeVaultServer();
            FafOAuthSettings settings = new FafOAuthSettings(new Uri(server.Url, "oauth2/auth"), new Uri(server.Url, "oauth2/token"), "client-1", "openid upload_map");
            using HttpClient http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            Uri? opened = null;
            Task<FafTokens> signIn = FafSignIn.SignInAsync(settings, http, url =>
            {
                opened = url;
                // the browser: Hydra sends it back to the loopback listener with a code
                System.Collections.Specialized.NameValueCollection query = System.Web.HttpUtility.ParseQueryString(url.Query);
                string redirect = query["redirect_uri"]!;
                _ = Task.Run(() => http.GetAsync($"{redirect}/?code=code-1&state={Uri.EscapeDataString(query["state"]!)}"));
            }, new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token);
            FafTokens tokens = await signIn;

            Assert.AreEqual("fake-access", tokens.AccessToken);
            Assert.AreEqual("fake-refresh", tokens.RefreshToken);
            Assert.IsTrue(tokens.IsValid(DateTimeOffset.UtcNow));
            System.Collections.Specialized.NameValueCollection asked = System.Web.HttpUtility.ParseQueryString(opened!.Query);
            Assert.AreEqual("S256", asked["code_challenge_method"]);
            StringAssert.StartsWith(asked["redirect_uri"], "http://127.0.0.1:");
            Assert.AreEqual("openid upload_map", asked["scope"]);
            FakeVaultServer.Received exchange = server.Requests.Single(r => r.Path == "/oauth2/token");
            System.Collections.Specialized.NameValueCollection form = System.Web.HttpUtility.ParseQueryString(exchange.Text);
            Assert.AreEqual("authorization_code", form["grant_type"]);
            Assert.AreEqual("code-1", form["code"]);
            Assert.AreEqual(asked["code_challenge"], FafSignIn.Challenge(form["code_verifier"]!));
        }

        [TestMethod]
        public async Task ASignInWithAnotherStateIsRefused()
        {
            using FakeVaultServer server = new FakeVaultServer();
            FafOAuthSettings settings = new FafOAuthSettings(new Uri(server.Url, "oauth2/auth"), new Uri(server.Url, "oauth2/token"), "client-1", "openid");
            using HttpClient http = new HttpClient();
            await Assert.ThrowsExceptionAsync<FafSignInException>(() => FafSignIn.SignInAsync(settings, http, url =>
            {
                string redirect = System.Web.HttpUtility.ParseQueryString(url.Query)["redirect_uri"]!;
                _ = Task.Run(() => http.GetAsync($"{redirect}/?code=code-1&state=forged"));
            }, new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token));
            Assert.IsFalse(server.Requests.Any(r => r.Path == "/oauth2/token"), "no exchange");
        }

        /// <summary>
        /// A progress that reports at once, on the reporting thread.
        /// </summary>
        private sealed class SynchronousProgress(Action<double> report) : IProgress<double>
        {
            public void Report(double value) => report(value);
        }
    }
}
