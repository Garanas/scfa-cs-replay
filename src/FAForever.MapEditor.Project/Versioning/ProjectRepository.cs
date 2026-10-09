using System.Diagnostics;

namespace FAForever.MapEditor.Project.Versioning
{
    /// <summary>
    /// What turning version control on for a project sets up.
    /// </summary>
    public sealed record RepositorySettings
    {
        /// <summary>
        /// The author of the commits; empty leaves git's own (global) setting.
        /// </summary>
        public string Name { get; init; } = "";

        public string Email { get; init; } = "";

        /// <summary>
        /// Whether the images and the procedural sources go to Git LFS: a heightmap of 81 km is tens
        /// of megabytes, and every save of it would otherwise stay in the history.
        /// </summary>
        public bool Lfs { get; init; } = true;

        /// <summary>
        /// The address of a repository to share the project through (<c>origin</c>); empty for none.
        /// </summary>
        public string Remote { get; init; } = "";
    }

    /// <summary>
    /// Version control of a project as git sees it.
    /// </summary>
    /// <param name="GitInstalled">Whether <c>git</c> runs on this computer.</param>
    /// <param name="LfsInstalled">Whether Git LFS is installed.</param>
    /// <param name="IsRepository">Whether the project folder is a repository of its own.</param>
    /// <param name="UsesLfs">Whether its <c>.gitattributes</c> sends files to Git LFS.</param>
    /// <param name="Changes">The files that changed since the last commit.</param>
    /// <param name="Branch">The branch it is on.</param>
    /// <param name="LastCommit">The last commit: its short hash and subject.</param>
    /// <param name="Remote">The address of <c>origin</c>, if it has one.</param>
    public sealed record RepositoryStatus(
        bool GitInstalled,
        bool LfsInstalled,
        bool IsRepository,
        bool UsesLfs,
        int Changes,
        string? Branch,
        string? LastCommit,
        string? Remote);

    /// <summary>
    /// A project under version control: one git repository per project folder, set up by the map
    /// maker's choice (<see cref="Enable"/>), then a commit beside each save. Runs the <c>git</c>
    /// of the computer, so it works with every other tool for git.
    /// </summary>
    public static class ProjectRepository
    {
        /// <summary>
        /// The kinds of files that are binary: the project's images, raw heightmaps, textures and
        /// the sources of World Machine and Gaea.
        /// </summary>
        public static readonly IReadOnlyList<string> BinaryPatterns = ["*.png", "*.r16", "*.dds", "*.tmd", "*.terrain"];

        /// <summary>
        /// The <c>.gitignore</c> a repository of a project starts with.
        /// </summary>
        public const string GitIgnore =
            """
            # Written by the FAF map editor when version control was turned on; change it as you like.

            # What World Machine, Gaea and other tools build from procedural/sources.
            /procedural/temp/

            *.tmp
            *~

            """;

        /// <summary>
        /// The <c>.gitattributes</c> a repository of a project starts with: text with the same line
        /// endings on every computer, so that diffs of the JSON stay small; the binary files in Git
        /// LFS or as binary.
        /// </summary>
        public static string GitAttributes(bool lfs) =>
            "# Written by the FAF map editor when version control was turned on; change it as you like.\n"
            + "* text=auto\n"
            + "*.json text eol=lf\n"
            + "*.lua text eol=lf\n"
            + string.Concat(BinaryPatterns.Select(pattern => lfs ? $"{pattern} filter=lfs diff=lfs merge=lfs -text\n" : $"{pattern} binary\n"));

        /// <summary>
        /// The first commit's message.
        /// </summary>
        public const string FirstCommit = "Start the map project";

        /// <summary>
        /// How the project in <paramref name="folder"/> stands.
        /// </summary>
        public static RepositoryStatus Status(string folder)
        {
            bool git = TryRun(folder, "--version").Ok;
            bool lfs = git && TryRun(folder, "lfs", "version").Ok;
            if (!git || !IsRepository(folder))
            {
                return new RepositoryStatus(git, lfs, false, false, 0, null, null, null);
            }

            string attributes = Path.Combine(folder, ".gitattributes");
            bool usesLfs = File.Exists(attributes) && File.ReadAllText(attributes).Contains("filter=lfs", StringComparison.Ordinal);
            GitResult status = Run(folder, "status", "--porcelain", "--untracked-files=all");
            int changes = status.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            GitResult branch = TryRun(folder, "branch", "--show-current");
            GitResult last = TryRun(folder, "log", "-1", "--format=%h %s");
            GitResult remote = TryRun(folder, "remote", "get-url", "origin");
            return new RepositoryStatus(
                git,
                lfs,
                true,
                usesLfs,
                changes,
                branch.Ok && branch.Output.Length > 0 ? branch.Output : null,
                last.Ok && last.Output.Length > 0 ? last.Output : null,
                remote.Ok && remote.Output.Length > 0 ? remote.Output : null);
        }

        /// <summary>
        /// Whether the project folder is a repository of its own (and not a folder inside another one).
        /// </summary>
        public static bool IsRepository(string folder) =>
            Directory.Exists(Path.Combine(folder, ".git")) || File.Exists(Path.Combine(folder, ".git"));

        /// <summary>
        /// The author git uses on this computer, to suggest.
        /// </summary>
        public static (string Name, string Email) DefaultAuthor(string folder)
        {
            GitResult name = TryRun(folder, "config", "--global", "user.name");
            GitResult email = TryRun(folder, "config", "--global", "user.email");
            return (name.Ok ? name.Output : "", email.Ok ? email.Output : "");
        }

        /// <summary>
        /// Makes the project folder a repository: <c>main</c>, Git LFS if chosen, the
        /// <c>.gitignore</c> and <c>.gitattributes</c> unless the folder has them, the author and
        /// the remote; then commits everything as it is on disk.
        /// </summary>
        /// <exception cref="MapProjectException">Git or Git LFS is missing, the folder is a repository
        /// already, or git fails.</exception>
        public static void Enable(string folder, RepositorySettings settings)
        {
            RepositoryStatus status = Status(folder);
            if (!status.GitInstalled)
            {
                throw new MapProjectException("Git is not installed: install it from git-scm.com");
            }
            if (settings.Lfs && !status.LfsInstalled)
            {
                throw new MapProjectException("Git LFS is not installed: install it from git-lfs.com, or turn LFS off");
            }
            if (status.IsRepository)
            {
                throw new MapProjectException($"{folder} is under version control already");
            }

            Run(folder, "init", "--initial-branch=main");
            // a project's paths are long on Windows: a workspace deep in the user's folders, assets' names
            Run(folder, "config", "core.longpaths", "true");
            if (settings.Lfs)
            {
                Run(folder, "lfs", "install", "--local");
            }
            WriteWhenMissing(Path.Combine(folder, ".gitignore"), GitIgnore);
            WriteWhenMissing(Path.Combine(folder, ".gitattributes"), GitAttributes(settings.Lfs));
            if (settings.Name.Trim() is { Length: > 0 } name)
            {
                Run(folder, "config", "user.name", name);
            }
            if (settings.Email.Trim() is { Length: > 0 } email)
            {
                Run(folder, "config", "user.email", email);
            }
            if (settings.Remote.Trim() is { Length: > 0 } remote)
            {
                Run(folder, "remote", "add", "origin", remote);
            }
            Commit(folder, FirstCommit);
        }

        /// <summary>
        /// Commits everything that changed in the project folder.
        /// </summary>
        /// <returns>The commit's short hash, or <c>null</c> when nothing changed.</returns>
        /// <exception cref="MapProjectException">The folder is no repository, or git fails (no author set, ...).</exception>
        public static string? Commit(string folder, string message)
        {
            if (!IsRepository(folder))
            {
                throw new MapProjectException($"{folder} is not under version control");
            }
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new MapProjectException("Say what the commit changes");
            }
            Run(folder, "add", "--all");
            if (Run(folder, "status", "--porcelain").Output.Length == 0)
            {
                return null;
            }
            Run(folder, "commit", "--quiet", "--message", message.Trim());
            return Run(folder, "rev-parse", "--short", "HEAD").Output;
        }

        private static void WriteWhenMissing(string path, string text)
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path, text.ReplaceLineEndings("\n"));
            }
        }

        private readonly record struct GitResult(bool Ok, string Output, string Error);

        /// <exception cref="MapProjectException">Git fails, with what it said.</exception>
        private static GitResult Run(string folder, params string[] arguments)
        {
            GitResult result = TryRun(folder, arguments);
            if (!result.Ok)
            {
                string said = result.Error.Length > 0 ? result.Error : result.Output;
                throw new MapProjectException($"git {arguments[0]} failed: {said}");
            }
            return result;
        }

        private static GitResult TryRun(string folder, params string[] arguments)
        {
            ProcessStartInfo start = new ProcessStartInfo("git")
            {
                WorkingDirectory = Directory.Exists(folder) ? folder : Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }
            // never wait for a password or an editor nobody sees
            start.Environment["GIT_TERMINAL_PROMPT"] = "0";
            start.Environment["GIT_EDITOR"] = "true";
            try
            {
                using Process process = Process.Start(start)!;
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                return new GitResult(process.ExitCode == 0, output.Result.Trim(), error.Result.Trim());
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                return new GitResult(false, "", exception.Message);
            }
        }
    }
}
