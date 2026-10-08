using CommandLine;
using FAForever.MapEditor.Project;

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
        [Value(0, MetaName = "command", Required = true, HelpText = "state, open, import, save, save-as, export, undo, redo, brush, stroke, height, fit, view, screenshot, close (help lists them).")]
        public string Command { get; set; } = "";

        [Value(1, MetaName = "arguments", HelpText = "name=value pairs; a value that reads as JSON (a number, [..], true) is sent as JSON, any other as text.")]
        public IEnumerable<string> Arguments { get; set; } = [];

        [Option('p', "port", Default = 5371, HelpText = "The port the editor listens on.")]
        public int Port { get; set; }
    }

    public static class Program
    {
        public static int Main(string[] args) =>
            Parser.Default.ParseArguments<UnpackOptions, PackOptions, EditorOptions>(args).MapResult(
                (UnpackOptions o) => Run(() => Unpack(o)),
                (PackOptions o) => o.Watch ? Watch(o) : Run(() => Pack(o)),
                (EditorOptions o) => EditorClient.Send(o.Port, o.Command, o.Arguments),
                _ => 2);

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
            string folder = EditedMap.OpenProject(o.ProjectFolder).Export(o.OutputFolder, o.Version);
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} Packed {folder}");
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
                        string folder = map.Export(o.OutputFolder, o.Version);
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
