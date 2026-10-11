using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The Procedural settings a workspace gives the maps made or imported in it (<c>workspace.json</c>'s
    /// <c>procedural</c>).
    /// </summary>
    [TestClass]
    public sealed class ProceduralDefaultsTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        [TestMethod]
        public void WithoutABlockWalkabilityConnectedRegionsAndMasksRegenerateOnTheirOwn()
        {
            ProceduralDefaults defaults = ProceduralDefaults.Default;

            CollectionAssert.AreEquivalent(new[] { "walkability", "navigationMesh", "masksFromRules" }, defaults.AutoGenerate.ToArray());
            Assert.IsFalse(defaults.RegeneratesOnItsOwn(PathNetworkGenerator.GeneratorId), "path networks only when asked");
            CollectionAssert.AreEqual(new[] { "normalMap" }, defaults.OnExport.ToArray());
        }

        [TestMethod]
        public void AWorkspaceKeepsItsOwnAndWritesNothingForTheDefaults()
        {
            string folder = ProjectFolders.New("workspace-procedural");
            Workspace workspace = Workspace.Create(folder, "C:/game", null, "D:/faf-vault");
            Assert.IsFalse(File.ReadAllText(Path.Combine(folder, WorkspaceLayout.Settings)).Contains("procedural", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(ProceduralDefaults.Default, Workspace.Open(folder).Procedural);

            ProceduralDefaults own = ProceduralDefaults.Default.WithAutoGenerate(PathNetworkGenerator.GeneratorId, true).WithOnExport(NormalMapGenerator.GeneratorId, false);
            (workspace with { Procedural = own }).Save();

            Workspace opened = Workspace.Open(folder);
            Assert.AreEqual(own, opened.Procedural);
            Assert.IsTrue(opened.Procedural.RegeneratesOnItsOwn("pathNetwork"));
            Assert.AreEqual(0, opened.Procedural.OnExport.Count, "an empty list is none, not the defaults");
        }

        [TestMethod]
        public void AppliedToAMapAndTakenFromOne()
        {
            ProceduralDefaults defaults = new ProceduralDefaults
            {
                AutoGenerate = [WalkabilityGenerator.GeneratorId],
                OnExport = [PathNetworkGenerator.GeneratorId],
            };
            ProceduralSettings settings = ProceduralSettings.Default with { WriteDraftOnChange = true };

            ProceduralSettings applied = defaults.ApplyTo(settings);

            CollectionAssert.AreEqual(new[] { "walkability" }, applied.AutoGenerate.ToArray());
            Assert.IsFalse(applied.GeneratesOnExport(NormalMapGenerator.GeneratorId));
            Assert.IsTrue(applied.PathNodes.WritesOnExport);
            Assert.IsTrue(applied.WriteDraftOnChange, "the rest of the map's settings stay");
            Assert.AreEqual(defaults, ProceduralDefaults.Of(applied));

            // a map that never chose about the path networks keeps "the first Generate turns it on"
            Assert.IsNull(ProceduralDefaults.Default.ApplyTo(ProceduralSettings.Default).PathNodes.OnExport);
        }

        [TestMethod]
        public void TheSandboxAndAnImportedMapStartWithTheWorkspacesSettings()
        {
            string folder = ProjectFolders.New("workspace-procedural-sandbox");
            Workspace workspace = Workspace.Create(folder, "C:/game", null, "D:/faf-vault") with
            {
                Procedural = new ProceduralDefaults { AutoGenerate = [NavigationMeshGenerator.GeneratorId] },
            };
            workspace.Save();
            Directory.Delete(SandboxMap.FolderOf(workspace), recursive: true);

            SandboxMap.Ensure(workspace);

            MapDocument sandbox = EditedMap.OpenProject(SandboxMap.FolderOf(workspace)).Document;
            CollectionAssert.AreEqual(new[] { "navigationMesh" }, sandbox.Procedural.AutoGenerate.ToArray());
            EditedMap imported = EditedMap.ImportMap(ProjectFolders.HardFfa, workspace.Procedural);
            CollectionAssert.AreEqual(new[] { "navigationMesh" }, imported.Document.Procedural.AutoGenerate.ToArray());
            Assert.IsFalse(imported.Session.IsModified, "the map as imported");
            Assert.IsTrue(EditedMap.ImportMap(ProjectFolders.HardFfa).Document.Procedural.IsDefault, "without a workspace: the editor's own");
        }
    }
}
