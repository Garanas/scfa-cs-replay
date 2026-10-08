using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class MapWorkspaceTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static MapWorkspace Saved(string map = ProjectFolders.HardFfa)
        {
            MapWorkspace workspace = MapWorkspace.ImportMap(map);
            workspace.SaveAs(ProjectFolders.New("project"));
            return workspace;
        }

        private static string Of(MapWorkspace workspace, string file) => Path.Combine(workspace.ProjectFolder!, file);

        private static void Raise(MapWorkspace workspace, int x, int z)
        {
            using EditTransaction stroke = workspace.Session.Begin("Raise terrain");
            new HeightBrush { Radius = 4, Strength = 5 }.Apply(stroke.Heightmap, workspace.Document.Terrain.Heightmap.Scale, x, z);
            stroke.Commit();
        }

        [TestMethod]
        public void SavesAnImportedMapAsAProject()
        {
            MapWorkspace workspace = MapWorkspace.ImportMap(ProjectFolders.HardFfa);
            Assert.IsNull(workspace.ProjectFolder);
            Assert.ThrowsException<InvalidOperationException>(workspace.Save);

            string project = ProjectFolders.New("project");
            workspace.SaveAs(project);

            Assert.AreEqual(Path.GetFullPath(project), workspace.ProjectFolder);
            Assert.IsFalse(workspace.Session.IsModified);
            CollectionAssert.AreEqual(
                MapExporter.ToBytes(workspace.Document)["HardFFA.scmap"],
                MapExporter.ToBytes(MapWorkspace.OpenProject(project).Document)["HardFFA.scmap"]);
        }

        [TestMethod]
        public void SavesOnlyWhatChanged()
        {
            MapWorkspace workspace = Saved();
            File.SetLastWriteTimeUtc(Of(workspace, MapProjectLayout.Heightmap), new DateTime(2000, 1, 1));
            File.SetLastWriteTimeUtc(Of(workspace, MapProjectLayout.StratumMask(1)), new DateTime(2000, 1, 1));
            File.SetLastWriteTimeUtc(Of(workspace, MapProjectLayout.Markers), new DateTime(2000, 1, 1));

            Raise(workspace, 100, 100);
            workspace.Save();

            Assert.AreNotEqual(new DateTime(2000, 1, 1), File.GetLastWriteTimeUtc(Of(workspace, MapProjectLayout.Heightmap)));
            Assert.AreEqual(new DateTime(2000, 1, 1), File.GetLastWriteTimeUtc(Of(workspace, MapProjectLayout.StratumMask(1))), "an unchanged image is not written");
            Assert.AreEqual(new DateTime(2000, 1, 1), File.GetLastWriteTimeUtc(Of(workspace, MapProjectLayout.Markers)), "an unchanged JSON file is not written");
            Assert.AreEqual(workspace.Document.Terrain.Heightmap.Samples[100, 100], MapWorkspace.OpenProject(workspace.ProjectFolder!).Document.Terrain.Heightmap.Samples[100, 100]);
        }

        [TestMethod]
        public void ReloadsAnImageAnotherToolChanged()
        {
            MapWorkspace workspace = Saved();
            MapDocument before = workspace.Document;
            ushort[] heights = before.Terrain.Heightmap.Samples.ToArray();
            heights[0] = 4096;
            ProjectImages.WriteGray16(Of(workspace, MapProjectLayout.Heightmap), 257, 257, heights);

            Assert.IsTrue(workspace.Reload([MapProjectLayout.Heightmap]));

            Assert.AreEqual(4096, workspace.Document.Terrain.Heightmap.Samples[0, 0]);
            Assert.AreSame(before.Terrain.Types, workspace.Document.Terrain.Types, "only the heightmap is read again");
            Assert.AreSame(before.Markers, workspace.Document.Markers);
            Assert.AreEqual("Reload heightmap.png", workspace.Session.UndoDescription);
            Assert.IsFalse(workspace.Session.IsModified, "the document is what the folder holds");

            workspace.Session.Undo();
            Assert.AreSame(before, workspace.Document);
        }

        [TestMethod]
        public void MakesNoEditOfItsOwnSave()
        {
            MapWorkspace workspace = Saved();
            Raise(workspace, 50, 50);
            workspace.Save();
            string[] files = [.. Directory.GetFiles(workspace.ProjectFolder!, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(workspace.ProjectFolder!, file))];

            Assert.IsFalse(workspace.Reload(files));
            Assert.AreEqual("Raise terrain", workspace.Session.UndoDescription);
        }

        [TestMethod]
        public async Task KeepsEditingDuringASaveInTheBackground()
        {
            MapWorkspace workspace = Saved();
            Raise(workspace, 50, 50);

            Task save = workspace.SaveAsync();
            Raise(workspace, 150, 150);
            await save;

            Assert.IsTrue(workspace.Session.IsModified, "the second stroke came after the save started");
            Assert.IsFalse(workspace.Reload([MapProjectLayout.Heightmap]), "the files of the save are no change from another tool");
            ushort raised = workspace.Document.Terrain.Heightmap.Samples[150, 150];
            Assert.AreNotEqual(raised, MapWorkspace.OpenProject(workspace.ProjectFolder!).Document.Terrain.Heightmap.Samples[150, 150], "the save wrote the version from when it started");

            workspace.Save();
            Assert.IsFalse(workspace.Session.IsModified);
        }

        [TestMethod]
        public void ReloadsTheMarkers()
        {
            MapWorkspace workspace = Saved();
            MapDocument before = workspace.Document;
            string markers = Of(workspace, MapProjectLayout.Markers);
            File.WriteAllText(markers, File.ReadAllText(markers).Replace("\"ARMY_1\"", "\"ARMY_12\""));

            Assert.IsTrue(workspace.Reload([MapProjectLayout.Markers]));

            Assert.IsTrue(workspace.Document.Markers.Spawns.Any(spawn => spawn.Army == "ARMY_12"));
            Assert.IsTrue(before.Props == workspace.Document.Props, "the props are the same array");
            Assert.AreSame(before.Terrain, workspace.Document.Terrain);
        }

        [TestMethod]
        public void ReloadsTheOtherFilesAndTheScript()
        {
            MapWorkspace workspace = Saved();
            Directory.CreateDirectory(Of(workspace, "files/env"));
            File.WriteAllBytes(Of(workspace, "files/env/rock.dds"), [1, 2, 3]);
            File.WriteAllText(Of(workspace, MapProjectLayout.Script), "-- script");

            Assert.IsTrue(workspace.Reload(["files/env/rock.dds", MapProjectLayout.Script]));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, workspace.Document.Files["env/rock.dds"].ToArray());
            Assert.IsNotNull(workspace.Document.Script);

            File.Delete(Of(workspace, "files/env/rock.dds"));
            Assert.IsTrue(workspace.Reload(["files\\env\\rock.dds"]));
            Assert.IsFalse(workspace.Document.Files.ContainsKey("env/rock.dds"));
        }

        [TestMethod]
        public void RefusesToChangeTheSizeOnReload()
        {
            MapWorkspace workspace = Saved();
            string map = Of(workspace, MapProjectLayout.Map);
            File.WriteAllText(map, File.ReadAllText(map).Replace("\"width\": 256", "\"width\": 512"));

            MapProjectException exception = Assert.ThrowsException<MapProjectException>(() => workspace.Reload([MapProjectLayout.Map]));

            StringAssert.Contains(exception.Message, "512 by 256");
            Assert.IsFalse(workspace.Session.CanUndo);
        }

        [TestMethod]
        public void ExportsUnderAnotherVersion()
        {
            MapWorkspace workspace = MapWorkspace.ImportMap(ProjectFolders.HardFfa);

            string folder = workspace.Export(ProjectFolders.New("output"), version: 3);

            Assert.AreEqual("HardFFA.v0003", Path.GetFileName(folder));
            Assert.AreEqual(1, workspace.Document.Version, "the document keeps its own version");
        }

        [TestMethod]
        public void HearsAToolSaveAFile()
        {
            MapWorkspace workspace = Saved();
            using ProjectWatcher watcher = new ProjectWatcher(workspace.ProjectFolder!, TimeSpan.FromMilliseconds(100));
            TaskCompletionSource<IReadOnlyCollection<string>> heard = new TaskCompletionSource<IReadOnlyCollection<string>>();
            watcher.Changed += changed => heard.TrySetResult(changed);

            File.WriteAllText(Of(workspace, MapProjectLayout.Script), "-- script");

            Assert.IsTrue(heard.Task.Wait(TimeSpan.FromSeconds(10)), "the watcher reports the change");
            CollectionAssert.Contains(heard.Task.Result.ToList(), MapProjectLayout.Script);
        }
    }
}
