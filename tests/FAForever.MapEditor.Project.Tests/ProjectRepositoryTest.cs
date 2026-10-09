using System.Diagnostics;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Versioning;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// Version control of a project with the computer's git (and Git LFS, when installed).
    /// </summary>
    [TestClass]
    public class ProjectRepositoryTest
    {
        private static readonly RepositorySettings Author = new RepositorySettings { Name = "Map maker", Email = "maker@example.org", Lfs = false };

        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static string Project()
        {
            string project = ProjectFolders.New("versioned");
            MapProjectStore.Save(MapImporter.ImportFolder(ProjectFolders.HardFfa), project);
            return project;
        }

        private static string Git(string folder, params string[] arguments)
        {
            ProcessStartInfo start = new ProcessStartInfo("git") { WorkingDirectory = folder, RedirectStandardOutput = true, UseShellExecute = false };
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }
            using Process process = Process.Start(start)!;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output.Trim();
        }

        [TestMethod]
        public void EnablingCommitsTheProjectAsItIs()
        {
            string project = Project();
            File.WriteAllText(Path.Combine(project, "procedural", "sources", "hard.tmd"), "source");
            File.WriteAllText(Path.Combine(project, "procedural", "temp", "height.r16"), "built");
            Assert.IsFalse(ProjectRepository.Status(project).IsRepository);

            ProjectRepository.Enable(project, Author);

            RepositoryStatus status = ProjectRepository.Status(project);
            Assert.IsTrue(status.IsRepository);
            Assert.IsFalse(status.UsesLfs);
            Assert.AreEqual(0, status.Changes);
            Assert.AreEqual("main", status.Branch);
            StringAssert.EndsWith(status.LastCommit, ProjectRepository.FirstCommit);
            string[] tracked = Git(project, "ls-files").Split('\n');
            CollectionAssert.Contains(tracked, "heightmap.png");
            CollectionAssert.Contains(tracked, "procedural/sources/hard.tmd");
            CollectionAssert.Contains(tracked, ".gitattributes");
            CollectionAssert.DoesNotContain(tracked, "procedural/temp/height.r16", "what the tools build is not versioned");
            Assert.AreEqual("Map maker", Git(project, "log", "-1", "--format=%an"));
        }

        [TestMethod]
        public void CommitsWhatChangedSinceTheLastCommit()
        {
            string project = Project();
            ProjectRepository.Enable(project, Author);
            MapDocument document = MapProjectStore.Load(project);

            MapProjectStore.Save(document with { Scenario = document.Scenario with { Name = "Harder FFA" } }, project, document);
            Assert.AreEqual(1, ProjectRepository.Status(project).Changes);
            string? commit = ProjectRepository.Commit(project, "Rename the map");

            Assert.IsNotNull(commit);
            Assert.AreEqual($"{commit} Rename the map", ProjectRepository.Status(project).LastCommit);
            Assert.IsNull(ProjectRepository.Commit(project, "Nothing"), "nothing changed");
        }

        [TestMethod]
        public void RefusesWhatCannotBeDone()
        {
            string project = Project();
            Assert.ThrowsException<MapProjectException>(() => ProjectRepository.Commit(project, "Not yet"));

            ProjectRepository.Enable(project, Author);

            Assert.ThrowsException<MapProjectException>(() => ProjectRepository.Enable(project, Author));
            Assert.ThrowsException<MapProjectException>(() => ProjectRepository.Commit(project, "  "));
        }

        [TestMethod]
        public void KeepsTheImagesInGitLfs()
        {
            string project = Project();
            if (!ProjectRepository.Status(project).LfsInstalled)
            {
                Assert.Inconclusive("Git LFS is not installed");
            }

            ProjectRepository.Enable(project, Author with { Lfs = true });

            Assert.IsTrue(ProjectRepository.Status(project).UsesLfs);
            StringAssert.StartsWith(Git(project, "show", "HEAD:heightmap.png"), "version https://git-lfs.github.com/spec/v1");
            StringAssert.StartsWith(Git(project, "show", "HEAD:map.json"), "{");
        }
    }
}
