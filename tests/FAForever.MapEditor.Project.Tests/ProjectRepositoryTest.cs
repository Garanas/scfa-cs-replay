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

        private static string Rename(string project, string name, string message)
        {
            MapDocument document = MapProjectStore.Load(project);
            MapProjectStore.Save(document with { Scenario = document.Scenario with { Name = name } }, project, document);
            return ProjectRepository.Commit(project, message)!;
        }

        [TestMethod]
        public void TheHistoryListsTheCommitsWithTheirFiles()
        {
            string project = Project();
            ProjectRepository.Enable(project, Author);
            string rename = Rename(project, "Harder FFA", "Rename the map");

            IReadOnlyList<CommitInfo> history = ProjectRepository.History(project);

            CollectionAssert.AreEqual(new[] { "Rename the map", ProjectRepository.FirstCommit }, history.Select(commit => commit.Subject).ToArray(), "newest first");
            Assert.AreEqual(rename, history[0].ShortHash);
            Assert.AreEqual("Map maker", history[0].Author);
            Assert.IsTrue(DateTimeOffset.Now - history[0].Date < TimeSpan.FromMinutes(5));
            CollectionAssert.AreEqual(new[] { "changed map.json" }, ProjectRepository.FilesOf(project, rename).Select(file => $"{file.Change} {file.Path}").ToArray());
            IReadOnlyList<CommitFile> first = ProjectRepository.FilesOf(project, history[1].Hash);
            Assert.IsTrue(first.All(file => file.Change == "added") && first.Any(file => file.Path == "map.json"), "the first commit adds every file");
            Assert.ThrowsException<MapProjectException>(() => ProjectRepository.FilesOf(project, "--all"));
            Assert.ThrowsException<MapProjectException>(() => ProjectRepository.FilesOf(project, "0000000"));
        }

        [TestMethod]
        public void ReadsTheProjectAsACommitHoldsItWithoutTouchingItsFiles()
        {
            string project = Project();
            ProjectRepository.Enable(project, Author);
            string first = ProjectRepository.History(project)[0].Hash;
            Rename(project, "Harder FFA", "Rename the map");

            MapDocument old = ProjectRepository.ReadAt(project, first);

            Assert.AreNotEqual("Harder FFA", old.Scenario.Name);
            Assert.AreEqual("Harder FFA", MapProjectStore.Load(project).Scenario.Name, "the project's own files stay");
            Assert.AreEqual(0, ProjectRepository.Status(project).Changes);
            Assert.AreEqual("", Git(project, "worktree", "list", "--porcelain").Split('\n').Count(line => line.StartsWith("worktree ")) == 1 ? "" : "a worktree is left behind");
        }

        [TestMethod]
        public void PushesToAndPullsFromTheSharedRepository()
        {
            string shared = ProjectFolders.New("shared");
            Directory.CreateDirectory(shared);
            Git(shared, "init", "--bare", "--initial-branch=main");
            string mine = Project();
            ProjectRepository.Enable(mine, Author with { Remote = shared });
            Assert.IsNull(ProjectRepository.Status(mine).Ahead, "nothing pushed: no branch to follow yet");

            ProjectRepository.Push(mine);
            Assert.AreEqual(0, ProjectRepository.Status(mine).Ahead);

            // a second computer clones it, and commits a change
            string theirs = ProjectFolders.New("clone");
            Git(Path.GetDirectoryName(theirs)!, "clone", shared, theirs);
            Git(theirs, "config", "user.name", "Other maker");
            Git(theirs, "config", "user.email", "other@example.org");
            Rename(theirs, "Their FFA", "Their name");
            ProjectRepository.Push(theirs);

            File.AppendAllText(Path.Combine(mine, "map.json"), " ");
            Assert.ThrowsException<MapProjectException>(() => ProjectRepository.Pull(mine), "changes that are not committed stop a pull");
            Git(mine, "checkout", "--", ".");

            IReadOnlyList<string> changed = ProjectRepository.Pull(mine);

            CollectionAssert.AreEqual(new[] { "map.json" }, changed.ToArray());
            Assert.AreEqual("Their FFA", MapProjectStore.Load(mine).Scenario.Name);
            Assert.AreEqual(0, ProjectRepository.Status(mine).Behind);
        }

        [TestMethod]
        public void PushingAndPullingNeedASharedRepository()
        {
            string project = Project();
            ProjectRepository.Enable(project, Author);

            Assert.ThrowsException<MapProjectException>(() => ProjectRepository.Push(project));
            Assert.ThrowsException<MapProjectException>(() => ProjectRepository.Pull(project));
        }
    }
}
