using CommandLine;

using System.Xml.Serialization;

namespace FAForever.Replay.Sandbox
{
    public class Program
    {

        public static void Main(string[] args)
        {
            Parser.Default.ParseArguments<ProgramArguments>(args)
                .WithParsed<ProgramArguments>(o =>
                {
                    if (!Directory.Exists(o.OutputDirectory))
                    { Directory.CreateDirectory(o.OutputDirectory); }

                    if (o.Interactive)
                    {
                        Interactive(o).Wait();
                        return;
                    }

                    Programmatic(o).Wait();
                });
        }

        internal static async Task<Replay> LoadReplay(ProgramArguments o)
        {
            if (o.FilePath != string.Empty)
            {
                return await LoadReplayFromDisk(o.FilePath);
            }
            else if (o.URL != string.Empty)
            {
                return await LoadReplayFromURL(o.URL);
            }
            else
            {
                throw new ArgumentException("No valid arguments provided.");
            }   
        }

        /// <summary>
        /// Loads a replay from disk. Uses the file extension to determine if it is a compressed replay from FAForever or a standard Supreme Commander: Forged Alliance replay.
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        internal static async Task<Replay> LoadReplayFromDisk(string file)
        {
            bool isFAForeverReplay = (Path.GetExtension(file) == ".fafreplay");
            byte[] content = await File.ReadAllBytesAsync(file);
            using MemoryStream replayStream = new MemoryStream(content);
            {
                if (isFAForeverReplay)
                {
                    IProgress<ReplayLoadProgression> progress = new Progress<ReplayLoadProgression>((s) => Console.WriteLine($" - {s} at {DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond}"));
                    return ReplayLoader.LoadFAFReplayFromMemory(replayStream, progress);
                }
                else
                {
                    IProgress<ReplayLoadProgression> progress = new Progress<ReplayLoadProgression>((s) => Console.WriteLine($" - {s} at {DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond}"));
                    return ReplayLoader.LoadSCFAReplayFromStream(replayStream, progress);
                }
            }
        }

        /// <summary>
        /// Loads a replay from a URL via GET HTTP request. Assumes that the replay is a compressed replay from FAForever.
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        internal static async Task<Replay> LoadReplayFromURL(string url)
        {
            HttpClient httpClient = new HttpClient();
            HttpResponseMessage response = await httpClient.GetAsync(url);
            byte[] content = await response.Content.ReadAsByteArrayAsync();
            using MemoryStream replayStream = new MemoryStream(content);
            {
                IProgress<ReplayLoadProgression> progress = new Progress<ReplayLoadProgression>((s) => Console.WriteLine($" - {s} at {DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond}"));
                return ReplayLoader.LoadFAFReplayFromMemory(replayStream, progress);
            }
        }

        /// <summary>
        /// Interactive mode that allows the user to provide URLs and/or files to process via the console.
        /// </summary>
        /// <param name="o"></param>
        /// <returns></returns>
        internal static async Task Interactive(ProgramArguments o)
        {
            while (true)
            {
                Console.Write("Provide a replay file: ");
                string? path = Console.ReadLine();
                if (path == null)
                {
                    continue;
                }

                if (path == string.Empty)
                {
                    continue;
                }

                if (path.Contains("https"))
                {
                    Replay replay = await LoadReplayFromURL(path);
                    using FileStream fs = new FileStream(Path.Combine(o.OutputDirectory, Path.GetFileNameWithoutExtension(path) + ".xml"), FileMode.Create);
                    XmlSerializer serializer = new XmlSerializer(typeof(Replay));
                    serializer.Serialize(fs, replay);
                    Console.WriteLine($"Replay from URL has {replay.Body.UserInput.Count} events.");
                }
                else
                {
                    Replay replay = await LoadReplayFromDisk(path);
                    XmlSerializer serializer = new XmlSerializer(typeof(Replay));
                    using FileStream fs = new FileStream(Path.Combine(o.OutputDirectory, Path.GetFileNameWithoutExtension(path) + ".xml"), FileMode.Create);
                    serializer.Serialize(fs, replay);
                    Console.WriteLine($"Replay from disk has {replay.Body.UserInput.Count} events.");
                }
            }
        }

        /// <summary>
        /// Programmatic mode that allows the user to interact with the application via the command line.
        /// </summary>
        /// <param name="o"></param>
        /// <returns></returns>
        internal static async Task Programmatic(ProgramArguments o)
        {
            Replay replay = await LoadReplay(o);
            Console.WriteLine(replay.Body.UserInput.Count);
            Dictionary<string, int> inputCount = ReplaySemantics.CountInputTypes(replay);

            foreach (KeyValuePair<string, int> kvp in inputCount)
            {
                Console.WriteLine($"{kvp.Key}: {kvp.Value}");
            }

        }
    }
}
