using System.Numerics;
using System.Text.Json;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The skybox as the game reads it (project format 8): the cumulus clouds' three textures where
    /// format 7 had a middle colour, no <c>clouds7</c>, and always four cirrus layers, also in the
    /// map's file the export writes.
    /// </summary>
    [TestClass]
    public class SkyboxFormatTest
    {
        [TestMethod]
        public void ASkyboxOfFormat7ReadsWithoutItsMiddleColourAndWithFourLayers()
        {
            const string json = """
                {"position": {"x": 256, "y": 0, "z": 256}, "horizonHeight": -50, "scale": 1171.6, "subtractHeight": 1.2566,
                 "subdivisionsAxis": 16, "subdivisionsHeight": 6, "zenithHeight": 256,
                 "horizonColor": {"x": 0.6, "y": 0.4, "z": 0.3}, "zenithColor": {"x": 0, "y": 0, "z": 0}, "decalGlowMultiplier": 0.1,
                 "albedo": "", "glow": "", "planets": [], "midColor": "#ff102030", "cirrusMultiplier": 1.8,
                 "cirrusColor": {"x": 1, "y": 1, "z": 1}, "cirrusTexture": "/textures/environment/cirrus000.dds",
                 "cirrusLayers": [{"frequency": {"x": 0.001, "y": 0.002}, "speed": 0.5, "direction": {"x": 1, "y": 0}}], "clouds7": 0}
                """;

            SkyboxDto dto = JsonSerializer.Deserialize<SkyboxDto>(json, ProjectJson.Options)!;
            Skybox skybox = MapProjectMapping.ToModel(dto);

            Assert.AreEqual("", skybox.CumulusLightRamp);
            Assert.AreEqual("", skybox.CumulusTexture);
            Assert.AreEqual(4, skybox.CirrusLayers.Length);
            Assert.AreEqual(new Vector2(0.001f, 0.002f), skybox.CirrusLayers[0].Frequency, "the layer it had stays first");
            Assert.AreEqual(Skyboxes.DefaultCirrusLayers[1], skybox.CirrusLayers[1], "the missing ones are the game's");
            string written = ProjectJson.Serialize(MapProjectMapping.ToDto(skybox));
            StringAssert.Contains(written, "\"cumulusTexture\": \"\"");
            Assert.IsFalse(written.Contains("midColor") || written.Contains("clouds7"), written);
        }

        [TestMethod]
        public void TheExportWritesFourLayersAndTheMapReadsBackTheSame()
        {
            EditedMap map = EditedMap.ImportMap(ProjectFolders.ThetaPassage);
            Skybox skybox = map.Document.Sky.Skybox!;
            Assert.AreEqual(4, skybox.CirrusLayers.Length);
            MapDocument document = map.Document with
            {
                Sky = map.Document.Sky with { Skybox = skybox with { CirrusLayers = [skybox.CirrusLayers[0]], CumulusTexture = "/textures/x.dds" } },
            };

            Scmap scmap = MapExporter.Export(document).Scmap;
            Scmap read = ScmapParser.Parse(ScmapWriter.Write(scmap));

            Assert.AreEqual(4, read.Skybox!.CirrusLayers.Count);
            Assert.AreEqual("/textures/x.dds", read.Skybox.CumulusTexture);
            Assert.AreEqual(skybox.CirrusLayers[0].Frequency, read.Skybox.CirrusLayers[0].Frequency);
            Assert.AreEqual(Skyboxes.DefaultCirrusLayers[3].Speed, read.Skybox.CirrusLayers[3].Speed);
        }
    }
}
