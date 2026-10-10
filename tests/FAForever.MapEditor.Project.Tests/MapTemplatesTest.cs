using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// Map templates (user story M-2): settings and whole maps a new map starts from.
    /// </summary>
    [TestClass]
    public class MapTemplatesTest
    {
        [TestMethod]
        public void TheSettingsOfAMapAreATemplateANewMapTakesThemFrom()
        {
            string folder = ProjectFolders.New("templates");
            MapDocument hard = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            string file = MapTemplates.SaveSettings(folder, "Hard: copy", hard);
            Assert.AreEqual("Hard- copy.json", Path.GetFileName(file), "a name a file can have");

            IReadOnlyList<MapTemplate> templates = MapTemplates.List(folder);
            CollectionAssert.AreEqual(new[] { "Hard- copy", MapTemplates.SandboxName }, templates.Select(t => t.Name).ToArray(), "the sandbox last");
            MapTemplate template = templates[0];
            Assert.AreEqual(MapTemplateKind.Settings, template.Kind);
            StringAssert.Contains(template.Summary, $"{hard.Scenario.PlayableArmies.Length} players");

            MapTemplateDto dto = MapTemplates.ReadSettings(file);
            NewMapSettings settings = MapTemplates.Apply(dto, new NewMapSettings { Name = "From Hard" });
            MapDocument made = NewMap.Create(settings);
            Assert.AreEqual(hard.Width, made.Width);
            Assert.AreEqual(hard.Scenario.PlayableArmies.Length, made.Scenario.PlayableArmies.Length);
            CollectionAssert.AreEqual(hard.Scenario.ExtraArmies.ToArray(), made.Scenario.ExtraArmies.ToArray(), "its extra armies");
            Assert.AreEqual(hard.Terrain.Layers[1].AlbedoPath, made.Terrain.Layers[1].AlbedoPath, "its strata");
            Assert.AreEqual(NewMap.LayerCount, made.Terrain.Layers.Length, "as many as a new map's version holds");
            Assert.AreEqual(8, made.Terrain.StratumMasks.Layers.Length);
            Assert.AreEqual(hard.Terrain.Layers[^1].AlbedoPath, made.Terrain.Layers[^1].AlbedoPath, "the upper layer last");
            Assert.AreEqual(hard.Water.Elevation, made.Water.Elevation);
            Assert.AreEqual(hard.Lighting.SunColor, made.Lighting.SunColor);
            Assert.AreEqual(dto.Height!.Value, made.Terrain.Heightmap.GetHeight(10, 10), 0.01, "flat at the mean height");
            Assert.AreEqual("From Hard", made.Scenario.Name);
        }

        [TestMethod]
        public void EveryBiomeAndATemplateOfEachExportAsTheGameReadsThem()
        {
            string folder = ProjectFolders.New("templates");
            foreach (NewMapBiome biome in NewMap.Biomes)
            {
                MapDocument fromBiome = NewMap.Create(new NewMapSettings { Name = biome.Title, Biome = biome.Id, Players = 2 });
                AssertExports(fromBiome, biome.Id);

                string file = MapTemplates.SaveSettings(folder, $"From {biome.Title}", fromBiome);
                MapDocument fromTemplate = NewMap.Create(MapTemplates.Apply(MapTemplates.ReadSettings(file), new NewMapSettings { Name = $"Again {biome.Title}" }));
                CollectionAssert.AreEqual(fromBiome.Terrain.Layers.ToArray(), fromTemplate.Terrain.Layers.ToArray(), $"the template of {biome.Id} keeps its strata");
                AssertExports(fromTemplate, $"a template of {biome.Id}");
            }

            // a version 53 map holds four strata: its template gets the four empty ones a new map adds
            MapDocument hard = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            MapDocument fromHard = NewMap.Create(MapTemplates.Apply(MapTemplates.ReadSettings(MapTemplates.SaveSettings(folder, "Hard", hard)), new NewMapSettings { Name = "From Hard" }));
            Assert.AreEqual(hard.Terrain.Layers[^1], fromHard.Terrain.Layers[^1], "the upper layer last");
            AssertExports(fromHard, "a template of a version 53 map");

            // a project saved with six layers of a version 56 map opens with the four strata it lacks
            MapDocument six = fromHard with { Terrain = fromHard.Terrain with { Layers = [.. fromHard.Terrain.Layers.Take(5), fromHard.Terrain.Layers[^1]] } };
            string project = ProjectFolders.New("six-layers");
            MapProjectStore.Save(six, project);
            MapDocument opened = MapProjectStore.Load(project);
            Assert.AreEqual(fromHard.Terrain.Layers[^1], opened.Terrain.Layers[^1]);
            AssertExports(opened, "a project of six layers");

            foreach (MapTemplate template in MapTemplates.List(folder).Where(t => t.Kind == MapTemplateKind.Project))
            {
                AssertExports(MapTemplates.Copy(template, "Copy", "", AssetCatalog.Load(Path.Combine(folder, "none"))), template.Name);
            }
        }

        private static void AssertExports(MapDocument document, string what)
        {
            Assert.AreEqual(NewMap.ScmapVersion, document.Terrain.ScmapVersion, what);
            Assert.AreEqual(NewMap.LayerCount, document.Terrain.Layers.Length, what);
            IReadOnlyDictionary<string, byte[]> files = MapExporter.ToBytes(document);
            byte[] scmap = files.Single(f => f.Key.EndsWith(".scmap", StringComparison.Ordinal)).Value;
            Assert.AreEqual(NewMap.LayerCount, FileFormats.Map.ScmapParser.Parse(scmap).Layers.Count, what);
        }

        [TestMethod]
        public void AWholeMapIsATemplateThatANewMapCopies()
        {
            string folder = ProjectFolders.New("templates");
            MapDocument hard = MapImporter.ImportFolder(ProjectFolders.HardFfa) with { Version = 7 };
            MapTemplates.SaveProject(folder, "Hard", hard);

            MapTemplate template = MapTemplates.Find(folder, "hard")!;
            Assert.AreEqual(MapTemplateKind.Project, template.Kind);
            MapDocument copy = MapTemplates.Copy(template, "My Hard", "Jip", AssetCatalog.Load(Path.Combine(folder, "none")));
            Assert.AreEqual(("My Hard", "my_hard", 1, "Jip"), (copy.Scenario.Name, copy.Name, copy.Version, copy.Scenario.Author));
            Assert.AreEqual(hard.Props.Length, copy.Props.Length, "its things");
            Assert.AreEqual(hard.Terrain.Heightmap.GetHeight(100, 100), copy.Terrain.Heightmap.GetHeight(100, 100));

            MapDocument sandbox = MapTemplates.Copy(MapTemplates.Find(folder, MapTemplates.SandboxName)!, "Practice", "", AssetCatalog.Load(Path.Combine(folder, "none")));
            Assert.IsTrue(sandbox.IsPublishable, "a map of its own, which may go to the vault");
            Assert.AreEqual("Practice", sandbox.Scenario.Name);
            Assert.ThrowsException<ArgumentException>(() => MapTemplates.Copy(new MapTemplate("x", MapTemplateKind.Settings, "x.json", ""), "x", "", AssetCatalog.Load(folder)));
        }

        [TestMethod]
        public void AFileThatIsNoTemplateIsLeftOut()
        {
            string folder = ProjectFolders.New("templates");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "broken.json"), "{ not json");
            File.WriteAllText(Path.Combine(folder, "small.json"), """{ "size": 256, "players": 4, "biome": "lava", "water": false }""");
            List<string> unreadable = [];

            IReadOnlyList<MapTemplate> templates = MapTemplates.List(folder, unreadable);

            CollectionAssert.AreEqual(new[] { "small", MapTemplates.SandboxName }, templates.Select(t => t.Name).ToArray());
            Assert.AreEqual("5 km, 4 players, no water, lava", templates[0].Summary);
            Assert.AreEqual(1, unreadable.Count);
            NewMapSettings settings = MapTemplates.Apply(MapTemplates.ReadSettings(templates[0].Path!), new NewMapSettings());
            Assert.AreEqual((256, 4, "lava", false), (settings.Size, settings.Players, settings.Biome, settings.Water));
        }
    }
}
