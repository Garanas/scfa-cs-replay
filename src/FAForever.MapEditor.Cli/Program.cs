using CommandLine;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Files.InMemory;

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

    public static class Program
    {
        public static int Main(string[] args) =>
            Parser.Default.ParseArguments<UnpackOptions, PackOptions>(args).MapResult(
                (UnpackOptions o) => Run(() => Unpack(o)),
                (PackOptions o) => o.Watch ? Watch(o) : Run(() => Pack(o)),
                _ => 2);

        private static void Unpack(UnpackOptions o)
        {
            if (Directory.Exists(o.ProjectFolder) && Directory.EnumerateFileSystemEntries(o.ProjectFolder).Any())
            {
                throw new MapProjectException($"The project folder {o.ProjectFolder} is not empty");
            }
            MapProjectStore.Save(MapImporter.ImportFolder(o.MapFolder), o.ProjectFolder);
            Console.WriteLine($"Unpacked {o.MapFolder} into {o.ProjectFolder}");
        }

        private static void Pack(PackOptions o)
        {
            MapDocument document = MapProjectStore.Load(o.ProjectFolder);
            document = document with { Version = o.Version ?? document.Version };
            string folder = MapExporter.WriteFolder(document, o.OutputFolder);
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
        /// Packs, then packs again after every change to the project. Tools save in bursts (a temporary
        /// file, then a rename), so a pack waits until the project has been quiet for a moment.
        /// </summary>
        private static int Watch(PackOptions o)
        {
            object gate = new object();
            void PackAgain()
            {
                lock (gate)
                {
                    try
                    {
                        Run(() => Pack(o));
                    }
                    catch (IOException exception)
                    {
                        // a tool still holds a file; its next save brings another change
                        Console.Error.WriteLine(exception.Message);
                    }
                }
            }

            PackAgain();
            using Timer timer = new Timer(_ => PackAgain());
            using FileSystemWatcher watcher = new FileSystemWatcher(o.ProjectFolder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            void Changed(object sender, FileSystemEventArgs e) => timer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
            watcher.Changed += Changed;
            watcher.Created += Changed;
            watcher.Deleted += Changed;
            watcher.Renamed += Changed;
            watcher.EnableRaisingEvents = true;

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
