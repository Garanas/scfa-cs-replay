using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class EditedMapTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static EditedMap Saved(string mapFolder = ProjectFolders.HardFfa)
        {
            EditedMap map = EditedMap.ImportMap(mapFolder);
            map.SaveAs(ProjectFolders.New("project"));
            return map;
        }

        private static string Of(EditedMap map, string file) => Path.Combine(map.ProjectFolder!, file);

        private static void Raise(EditedMap map, int x, int z)
        {
            using EditTransaction stroke = map.Session.Begin("Raise terrain");
            new HeightBrush { Radius = 4, Strength = 5 }.Apply(stroke.Heightmap, map.Document.Terrain.Heightmap.Scale, x, z);
            stroke.Commit();
        }

        [TestMethod]
        public void SavesAnImportedMapAsAProject()
        {
            EditedMap map = EditedMap.ImportMap(ProjectFolders.HardFfa);
            Assert.IsNull(map.ProjectFolder);
            Assert.ThrowsException<InvalidOperationException>(map.Save);

            string project = ProjectFolders.New("project");
            map.SaveAs(project);

            Assert.AreEqual(Path.GetFullPath(project), map.ProjectFolder);
            Assert.IsFalse(map.Session.IsModified);
            CollectionAssert.AreEqual(
                MapExporter.ToBytes(map.Document)["HardFFA.scmap"],
                MapExporter.ToBytes(EditedMap.OpenProject(project).Document)["HardFFA.scmap"]);
        }

        [TestMethod]
        public void SavesOnlyWhatChanged()
        {
            EditedMap map = Saved();
            File.SetLastWriteTimeUtc(Of(map, MapProjectLayout.Heightmap), new DateTime(2000, 1, 1));
            File.SetLastWriteTimeUtc(Of(map, MapProjectLayout.StratumMask(1)), new DateTime(2000, 1, 1));
            File.SetLastWriteTimeUtc(Of(map, MapProjectLayout.Markers), new DateTime(2000, 1, 1));

            Raise(map, 100, 100);
            map.Save();

            Assert.AreNotEqual(new DateTime(2000, 1, 1), File.GetLastWriteTimeUtc(Of(map, MapProjectLayout.Heightmap)));
            Assert.AreEqual(new DateTime(2000, 1, 1), File.GetLastWriteTimeUtc(Of(map, MapProjectLayout.StratumMask(1))), "an unchanged image is not written");
            Assert.AreEqual(new DateTime(2000, 1, 1), File.GetLastWriteTimeUtc(Of(map, MapProjectLayout.Markers)), "an unchanged JSON file is not written");
            Assert.AreEqual(map.Document.Terrain.Heightmap.Samples[100, 100], EditedMap.OpenProject(map.ProjectFolder!).Document.Terrain.Heightmap.Samples[100, 100]);
        }

        [TestMethod]
        public void ReloadsAnImageAnotherToolChanged()
        {
            EditedMap map = Saved();
            MapDocument before = map.Document;
            ushort[] heights = before.Terrain.Heightmap.Samples.ToArray();
            heights[0] = 4096;
            ProjectImages.WriteGray16(Of(map, MapProjectLayout.Heightmap), 257, 257, heights);

            Assert.IsTrue(map.Reload([MapProjectLayout.Heightmap]));

            Assert.AreEqual(4096, map.Document.Terrain.Heightmap.Samples[0, 0]);
            Assert.AreSame(before.Terrain.Types, map.Document.Terrain.Types, "only the heightmap is read again");
            Assert.AreSame(before.Markers, map.Document.Markers);
            Assert.AreEqual("Reload heightmap.png", map.Session.UndoDescription);
            Assert.IsFalse(map.Session.IsModified, "the document is what the folder holds");

            map.Session.Undo();
            Assert.AreSame(before, map.Document);
        }

        [TestMethod]
        public void MakesNoEditOfItsOwnSave()
        {
            EditedMap map = Saved();
            Raise(map, 50, 50);
            map.Save();
            string[] files = [.. Directory.GetFiles(map.ProjectFolder!, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(map.ProjectFolder!, file))];

            Assert.IsFalse(map.Reload(files));
            Assert.AreEqual("Raise terrain", map.Session.UndoDescription);
        }

        [TestMethod]
        public async Task KeepsEditingDuringASaveInTheBackground()
        {
            EditedMap map = Saved();
            Raise(map, 50, 50);

            Task save = map.SaveAsync();
            Raise(map, 150, 150);
            await save;

            Assert.IsTrue(map.Session.IsModified, "the second stroke came after the save started");
            Assert.IsFalse(map.Reload([MapProjectLayout.Heightmap]), "the files of the save are no change from another tool");
            ushort raised = map.Document.Terrain.Heightmap.Samples[150, 150];
            Assert.AreNotEqual(raised, EditedMap.OpenProject(map.ProjectFolder!).Document.Terrain.Heightmap.Samples[150, 150], "the save wrote the version from when it started");

            map.Save();
            Assert.IsFalse(map.Session.IsModified);
        }

        [TestMethod]
        public void ReloadsTheMarkers()
        {
            EditedMap map = Saved();
            MapDocument before = map.Document;
            string markers = Of(map, MapProjectLayout.Markers);
            File.WriteAllText(markers, File.ReadAllText(markers).Replace("\"ARMY_1\"", "\"ARMY_12\""));

            Assert.IsTrue(map.Reload([MapProjectLayout.Markers]));

            Assert.IsTrue(map.Document.Markers.Spawns.Any(spawn => spawn.Army == "ARMY_12"));
            Assert.IsTrue(before.Props == map.Document.Props, "the props are the same array");
            Assert.AreSame(before.Terrain, map.Document.Terrain);
        }

        [TestMethod]
        public void ReloadsTheOtherFilesAndTheScript()
        {
            EditedMap map = Saved();
            Directory.CreateDirectory(Of(map, "files/env"));
            File.WriteAllBytes(Of(map, "files/env/rock.dds"), [1, 2, 3]);
            File.WriteAllText(Of(map, MapProjectLayout.Script), "-- script");

            Assert.IsTrue(map.Reload(["files/env/rock.dds", MapProjectLayout.Script]));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, map.Document.Files["env/rock.dds"].ToArray());
            Assert.IsNotNull(map.Document.Script);

            File.Delete(Of(map, "files/env/rock.dds"));
            Assert.IsTrue(map.Reload(["files\\env\\rock.dds"]));
            Assert.IsFalse(map.Document.Files.ContainsKey("env/rock.dds"));
        }

        [TestMethod]
        public void RefusesToChangeTheSizeOnReload()
        {
            EditedMap map = Saved();
            string json = Of(map, MapProjectLayout.Map);
            File.WriteAllText(json, File.ReadAllText(json).Replace("\"width\": 256", "\"width\": 512"));

            MapProjectException exception = Assert.ThrowsException<MapProjectException>(() => map.Reload([MapProjectLayout.Map]));

            StringAssert.Contains(exception.Message, "512 by 256");
            Assert.IsFalse(map.Session.CanUndo);
        }

        [TestMethod]
        public void ExportsUnderAnotherVersion()
        {
            EditedMap map = EditedMap.ImportMap(ProjectFolders.HardFfa);

            string folder = map.Export(ProjectFolders.New("output"), version: 3);

            Assert.AreEqual("HardFFA.v0003", Path.GetFileName(folder));
            Assert.AreEqual(1, map.Document.Version, "the document keeps its own version");
        }

        [TestMethod]
        public void HearsAToolSaveAFile()
        {
            EditedMap map = Saved();
            using ProjectWatcher watcher = new ProjectWatcher(map.ProjectFolder!, TimeSpan.FromMilliseconds(100));
            TaskCompletionSource<IReadOnlyCollection<string>> heard = new TaskCompletionSource<IReadOnlyCollection<string>>();
            watcher.Changed += changed => heard.TrySetResult(changed);

            File.WriteAllText(Of(map, MapProjectLayout.Script), "-- script");

            Assert.IsTrue(heard.Task.Wait(TimeSpan.FromSeconds(10)), "the watcher reports the change");
            CollectionAssert.Contains(heard.Task.Result.ToList(), MapProjectLayout.Script);
        }
    }
}
