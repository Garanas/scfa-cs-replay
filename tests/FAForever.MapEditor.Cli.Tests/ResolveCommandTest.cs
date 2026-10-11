using System.Text.Json.Nodes;

namespace FAForever.MapEditor.Cli.Tests
{
    /// <summary>
    /// <c>faf-map dev resolve</c>: every asset a map names, resolved through a workspace's assets.
    /// </summary>
    [TestClass]
    public sealed class ResolveCommandTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        [TestMethod]
        public void SaysWhatAMapNamesThatTheAssetsLack()
        {
            string workspace = Path.Combine(Path.GetTempPath(), $"resolve-{Guid.NewGuid():N}");
            string png = Path.Combine(workspace, "assets", "env", "desert", "layers", "des_sandlight_albedo.png");
            Directory.CreateDirectory(Path.GetDirectoryName(png)!);
            File.WriteAllBytes(png, []);
            string report = Path.Combine(workspace, "report.json");
            try
            {
                Assert.AreEqual(1, ResolveCommand.Run([HardFfa], workspace, report), "most of HardFFA's assets are not there");

                JsonArray lookups = JsonNode.Parse(File.ReadAllText(report))![0]!["lookups"]!.AsArray();
                JsonNode sand = lookups.Single(lookup => lookup!["path"]!.GetValue<string>() == "/env/Desert/Layers/Des_sandLight_albedo.dds")!;
                Assert.AreEqual(png, sand["file"]!.GetValue<string>(), "found whatever the case the map writes it in");
                Assert.IsTrue(lookups.Where(lookup => lookup!["file"] is null).All(lookup => lookup!["reason"] is not null));
                Assert.AreEqual(2, ResolveCommand.Run([HardFfa], null, null), "no workspace");
            }
            finally
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }
}
