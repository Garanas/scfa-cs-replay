using CommandLine;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Cli
{
    [Verb("unpack", HelpText = "Turn a map folder into a project of JSON and PNG files.")]
    public sealed class UnpackOptions
    {
        [Value(0, MetaName = "map", Required = true, HelpText = "The map folder, e.g. maps/theta_passage.v0004.")]
        public string MapFolder { get; set; } = "";

        [Value(1, MetaName = "project", Required = true, HelpText = "The project folder to create; it must be empty or absent.")]
        public string ProjectFolder { get; set; } = "";
    }

    [Verb("pack", HelpText = "Build a map folder from a project.")]
    public sealed class PackOptions
    {
        [Value(0, MetaName = "project", Required = true, HelpText = "The project folder.")]
        public string ProjectFolder { get; set; } = "";

        [Value(1, MetaName = "output", Required = true, HelpText = "Where to put the map folder, e.g. the game's maps folder.")]
        public string OutputFolder { get; set; } = "";

        [Option('v', "version", HelpText = "The version to give the map (map_version and the folder's .vNNNN).")]
        public int? Version { get; set; }

        [Option('w', "watch", HelpText = "Pack again whenever a file of the project changes.")]
        public bool Watch { get; set; }
    }

    [Verb("editor", HelpText = "Send a command to a running editor started with --automation, e.g. 'editor stroke points=[[10,10],[50,50]]'.")]
    public sealed class EditorOptions
    {
        [Value(0, MetaName = "command", Required = true, HelpText = "start, stop or restart a test editor (restart: with the arguments of its last start, the newest build); or a command for a running one: state, mouse, key, open, save, export, undo, stroke, screenshot, close (help lists them).")]
        public string Command { get; set; } = "";

        [Value(1, MetaName = "arguments", HelpText = "name=value pairs; a value that reads as JSON (a number, [..], true) is sent as JSON, any other as text.")]
        public IEnumerable<string> Arguments { get; set; } = [];

        [Option('p', "port", Default = 5371, HelpText = "The port the editor listens on.")]
        public int Port { get; set; }

        [Option("settings", HelpText = "start: the editor's settings file (FAF_MAP_EDITOR_SETTINGS), which names its workspace.")]
        public string? Settings { get; set; }

        [Option("map", HelpText = "start: the project or map folder to open; without one the editor opens the workspace's sandbox.")]
        public string? Map { get; set; }

        [Option("exe", HelpText = "start: the editor's executable; by default the one built beside this tool, or FAF_MAP_EDITOR_EXE.")]
        public string? Exe { get; set; }

        [Option("no-wait", HelpText = "start: answer once the process runs, without waiting until it is ready.")]
        public bool NoWait { get; set; }

        [Option("timeout", Default = 120, HelpText = "start: seconds to wait until it is ready; stop: until it exits.")]
        public int Timeout { get; set; }

        [Option("force", HelpText = "stop: end the process when it does not exit in time.")]
        public bool Force { get; set; }

        [Option("shadow", HelpText = "start: the folder the editor runs from, a copy of its build kept up to date (by default faf-map-editor-shadow/<port> in the temporary folder), so dotnet build works while it runs.")]
        public string? Shadow { get; set; }

        [Option("no-shadow", HelpText = "start: run the build output itself, without a copy (a build then needs the editor stopped).")]
        public bool NoShadow { get; set; }

        [Option("log", HelpText = "start: the file the editor's own output goes to; by default editor-<port>.log beside the settings file (else in the temporary folder).")]
        public string? Log { get; set; }
    }

    [Verb("workspace", HelpText = "Set up the workspace, convert the game's assets into it, show whether they are current, or make a scratch workspace for tests.")]
    public sealed class WorkspaceOptions
    {
        [Value(0, MetaName = "action", Required = true, HelpText = "init, convert, status, scratch or remove-scratch.")]
        public string Action { get; set; } = "";

        [Value(1, MetaName = "folder", HelpText = "The workspace; by default the one the editor remembers.")]
        public string? Folder { get; set; }

        [Option("game", HelpText = "init: the game's installation; by default the one FAF's fa_path.lua names.")]
        public string? GameFolder { get; set; }

        [Option("faf", HelpText = "init: FAF's gamedata folder; by default the one next to fa_path.lua.")]
        public string? FafDataFolder { get; set; }

        [Option("vault", HelpText = "init: the vault on disk, where maps are exported to; by default the one fa_path.lua names.")]
        public string? VaultFolder { get; set; }

        [Option("from", HelpText = "scratch: the workspace whose converted assets it links to; by default the one the editor remembers.")]
        public string? From { get; set; }

        [Option("settings", HelpText = "init and scratch: the settings file to write that names the workspace, for FAF_MAP_EDITOR_SETTINGS; init without it writes the user's own.")]
        public string? Settings { get; set; }

        [Option("convert-into", HelpText = "scratch: a folder of its own for the assets (made when absent, shared by several scratch workspaces), converted from the game unless current: for a change of the assets' format, without touching --from's assets.")]
        public string? ConvertInto { get; set; }
    }

    [Verb("dev", HelpText = "Tools for working on the editor's source, e.g. 'dev replace <file> --old-file a.txt --new-file b.txt': replaces text keeping the file's line endings and UTF-8.")]
    public sealed class DevOptions
    {
        [Value(0, MetaName = "action", Required = true, HelpText = "replace.")]
        public string Action { get; set; } = "";

        [Value(1, MetaName = "file", Required = true, HelpText = "The file to change.")]
        public string File { get; set; } = "";

        [Option("old", HelpText = "replace: the text to replace, on one line (--old-file for several lines).")]
        public string? Old { get; set; }

        [Option("new", HelpText = "replace: the text to put in its place.")]
        public string? New { get; set; }

        [Option("old-file", HelpText = "replace: a UTF-8 file holding the text to replace.")]
        public string? OldFile { get; set; }

        [Option("new-file", HelpText = "replace: a UTF-8 file holding the text to put in its place.")]
        public string? NewFile { get; set; }

        [Option("count", Default = 1, HelpText = "replace: how many times the old text must occur.")]
        public int Count { get; set; }

        [Option("all", HelpText = "replace: every place the old text occurs, however many.")]
        public bool All { get; set; }
    }

    public static class Program
    {
        public static int Main(string[] args) =>
            Parser.Default.ParseArguments<UnpackOptions, PackOptions, EditorOptions, WorkspaceOptions, DevOptions>(args).MapResult(
                (UnpackOptions o) => Run(() => Unpack(o)),
                (PackOptions o) => o.Watch ? Watch(o) : Run(() => Pack(o)),
                (EditorOptions o) => o.Command switch
                {
                    "start" => EditorProcess.Start(o),
                    "stop" => EditorProcess.Stop(o),
                    "restart" => EditorProcess.Restart(o),
                    _ => EditorClient.Send(o.Port, o.Command, o.Arguments),
                },
                (WorkspaceOptions o) => Run(() => WorkspaceCommand.Run(o)),
                (DevOptions o) => Dev(o),
                _ => 2);

        /// <summary>
        /// <c>dev replace</c>: see <see cref="TextEdit"/>.
        /// </summary>
        private static int Dev(DevOptions o)
        {
            if (o.Action != "replace")
            {
                Console.Error.WriteLine($"There is no dev action '{o.Action}': replace");
                return 2;
            }
            try
            {
                string old = o.OldFile is string oldFile ? TextEdit.ReadText(oldFile) : o.Old ?? throw new ArgumentException("Give --old or --old-file");
                string replacement = o.NewFile is string newFile ? TextEdit.ReadText(newFile) : o.New ?? throw new ArgumentException("Give --new or --new-file");
                int places = TextEdit.ReplaceInFile(o.File, old, replacement, o.All ? null : o.Count);
                Console.WriteLine($"Replaced {places} {(places == 1 ? "place" : "places")} in {o.File}");
                return 0;
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or System.Text.DecoderFallbackException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"{o.File}: {exception.Message}");
                return 1;
            }
        }

        private static void Unpack(UnpackOptions o)
        {
            if (Directory.Exists(o.ProjectFolder) && Directory.EnumerateFileSystemEntries(o.ProjectFolder).Any())
            {
                throw new MapProjectException($"The project folder {o.ProjectFolder} is not empty");
            }
            EditedMap.ImportMap(o.MapFolder).SaveAs(o.ProjectFolder);
            Console.WriteLine($"Unpacked {o.MapFolder} into {o.ProjectFolder}");
        }

        private static void Pack(PackOptions o)
        {
            string folder = EditedMap.OpenProject(o.ProjectFolder).Export(o.OutputFolder, o.Version, wideDecals: true, ddsTextures: true, converted: ReportConverted);
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} Packed {folder}");
        }

        /// <summary>
        /// Says which of the map's own textures a pack converted to DDS.
        /// </summary>
        private static void ReportConverted(IReadOnlyList<ConvertedTexture> converted)
        {
            if (converted.Count == 0)
            {
                return;
            }
            int cached = converted.Count(texture => texture.Cached);
            Console.WriteLine($"Converted {converted.Count} texture{(converted.Count == 1 ? "" : "s")} to DDS{(cached > 0 ? $" ({cached} as before)" : "")}:");
            foreach (ConvertedTexture texture in converted)
            {
                Console.WriteLine($"  {texture.Source} -> {texture.Target} ({texture.Format})");
            }
        }

        private static int Run(Action action)
        {
            try
            {
                action();
                return 0;
            }
            catch (MapProjectException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }

        /// <summary>
        /// Packs, then packs again after every change to the project: the project is opened once,
        /// and a change reads only the files that changed.
        /// </summary>
        private static int Watch(PackOptions o)
        {
            object gate = new object();
            EditedMap? map = null;
            void PackAgain(IReadOnlyCollection<string>? changed)
            {
                lock (gate)
                {
                    try
                    {
                        if (map is null || changed is null)
                        {
                            map = EditedMap.OpenProject(o.ProjectFolder);
                        }
                        else if (!map.Reload(changed))
                        {
                            return;
                        }
                        string folder = map.Export(o.OutputFolder, o.Version, wideDecals: true, ddsTextures: true, converted: ReportConverted);
                        Console.WriteLine($"{DateTime.Now:HH:mm:ss} Packed {folder}");
                    }
                    catch (Exception exception) when (exception is MapProjectException or IOException)
                    {
                        // a broken file, or one a tool still holds: the next save brings another change
                        Console.Error.WriteLine(exception.Message);
                    }
                }
            }

            PackAgain(null);
            using ProjectWatcher watcher = new ProjectWatcher(o.ProjectFolder);
            watcher.Changed += PackAgain;

            Console.WriteLine($"Watching {o.ProjectFolder}; press Ctrl+C to stop.");
            using ManualResetEventSlim stop = new ManualResetEventSlim();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                stop.Set();
            };
            stop.Wait();
            return 0;
        }
    }
}
