using System.Globalization;
using System.Text.Json.Nodes;
using CommandLine;
using ImageMagick;

namespace FAForever.MapEditor.Cli.Guide
{
    [Verb("guide", HelpText = "The editor's guide, made from its tours: 'guide build' plays them in a test editor and writes pictures, guide.json and the pages; 'guide check' plays the targets without pictures; 'guide render' writes the pages again from guide.json.")]
    public sealed class GuideOptions
    {
        [Value(0, MetaName = "action", Required = true, HelpText = "build, check or render.")]
        public string Action { get; set; } = "";

        [Option("out", Default = "docs/guide", HelpText = "The guide's folder: guide.json, the pages and images/.")]
        public string Out { get; set; } = "docs/guide";

        [Option('p', "port", Default = 5399, HelpText = "build and check: the port of the test editor it starts (never 5371, the person's own).")]
        public int Port { get; set; }

        [Option("settings", HelpText = "build and check: the settings file of the scratch workspace the editor runs in (faf-map workspace scratch --settings).")]
        public string? Settings { get; set; }

        [Option("tours", HelpText = "build and check: the ids of the tours to play, comma separated, in this order; by default every tour of the workspace.")]
        public string? Tours { get; set; }

        [Option("map", HelpText = "build and check: the project or map folder the editor opens; by default the workspace's sandbox.")]
        public string? Map { get; set; }

        [Option("size", Default = "1280x800", HelpText = "build: the window's size.")]
        public string Size { get; set; } = "1280x800";

        [Option("overview", Default = "window", HelpText = "build: the ids of the tours that get an overview picture of the window with numbered outlines, comma separated.")]
        public string Overview { get; set; } = "window";

        [Option("exe", HelpText = "build and check: the editor's executable; by default the one built beside this tool, or FAF_MAP_EDITOR_EXE.")]
        public string? Exe { get; set; }
    }

    /// <summary>
    /// <c>faf-map guide</c>. Everything it sends the editor is a command a person could give: it
    /// runs only in a test editor it starts itself, never uploads, and the tours' own actions go
    /// through the editor's refusals.
    /// </summary>
    public static class GuideCommand
    {
        /// <summary>The port of the person's own editor, which the guide never touches.</summary>
        private const int OwnEditorPort = 5371;

        public static int Run(GuideOptions o)
        {
            try
            {
                switch (o.Action)
                {
                    case "build":
                        return Build(o);
                    case "check":
                        return Check(o);
                    case "render":
                        return Render(o);
                    default:
                        Console.Error.WriteLine($"There is no guide action '{o.Action}': build, check or render");
                        return 2;
                }
            }
            catch (Exception exception) when (exception is GuideException or IOException or MagickException)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }

        private static int Render(GuideOptions o)
        {
            GuideManifest manifest = GuideManifest.Read(o.Out);
            GuidePages.Write(manifest, o.Out);
            Console.WriteLine($"Wrote the pages of {manifest.Tours.Count} tours into {Path.GetFullPath(o.Out)}");
            return 0;
        }

        /// <summary>
        /// Starts the test editor, answers what would be in the way (the offer of the tour, the
        /// generator), runs the action and stops the editor again.
        /// </summary>
        private static T WithEditor<T>(GuideOptions o, int width, int height, Func<EditorSession, string, T> action)
        {
            if (o.Port == OwnEditorPort)
            {
                throw new GuideException($"Port {OwnEditorPort} is the person's own editor: give a port of a test editor");
            }
            if (o.Settings is null || !File.Exists(o.Settings))
            {
                throw new GuideException("Give --settings: the settings file of a scratch workspace (faf-map workspace scratch <folder> --settings <file>)");
            }
            EditorOptions start = new EditorOptions { Command = "start", Port = o.Port, Settings = o.Settings, Map = o.Map, Exe = o.Exe, Timeout = 180, Force = true, RefreshTours = true };
            if (EditorProcess.Start(start) != 0)
            {
                throw new GuideException($"Could not start the test editor on port {o.Port}");
            }
            try
            {
                using EditorSession editor = new EditorSession(o.Port);
                JsonObject state = editor.Post("state");
                string workspace = state["workspace"]?.GetValue<string>() ?? throw new GuideException("The editor has no workspace");
                if (string.Equals(Path.GetFileName(workspace.TrimEnd('\\', '/')), "FAF Map Editor", StringComparison.OrdinalIgnoreCase))
                {
                    throw new GuideException($"{workspace} looks like a person's own workspace: use a scratch workspace");
                }
                editor.Post("window", new JsonObject { ["width"] = width, ["height"] = height });
                editor.Run("help.tourOffer", new JsonObject { ["answer"] = "never" });
                editor.Run("generate.automatic", new JsonObject { ["on"] = false });
                return action(editor, workspace);
            }
            finally
            {
                EditorProcess.Stop(new EditorOptions { Command = "stop", Port = o.Port, Timeout = 30, Force = true });
            }
        }

        private static List<TourFile> ChooseTours(GuideOptions o, string workspace)
        {
            List<TourFile> all = GuideTours.ReadFolder(Path.Combine(workspace, "tours"));
            if (o.Tours is null)
            {
                return all;
            }
            List<TourFile> chosen = [];
            foreach (string id in o.Tours.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                chosen.Add(all.FirstOrDefault(tour => tour.Id == id)
                    ?? throw new GuideException($"There is no tour '{id}' in {Path.Combine(workspace, "tours")}: {string.Join(", ", all.Select(tour => tour.Id))}"));
            }
            return chosen;
        }

        private static int Build(GuideOptions o)
        {
            (int width, int height) = GuideGeometry.ParseSize(o.Size);
            HashSet<string> overviews = [.. o.Overview.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            string folder = Path.GetFullPath(o.Out);
            string scratch = Path.Combine(Path.GetTempPath(), $"faf-map-guide-{Environment.ProcessId}");
            Directory.CreateDirectory(scratch);
            try
            {
                return WithEditor(o, width, height, (editor, workspace) =>
                {
                    GuideBuilder builder = new GuideBuilder(editor, folder, scratch, width, height);
                    List<GuideTour> tours = [];
                    foreach (TourFile tour in ChooseTours(o, workspace))
                    {
                        tours.Add(builder.BuildTour(tour, overviews.Contains(tour.Id)));
                    }
                    JsonObject build = editor.Post("state")["build"] as JsonObject ?? [];
                    GuideManifest manifest = new GuideManifest(
                        GuideManifest.CurrentFormat,
                        new GuideEditor(build["version"]?.GetValue<string>(), build["built"]?.ToString(), DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        "dark",
                        [width, height],
                        tours);
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, "guide.json"), manifest.ToJson());
                    GuidePages.Write(manifest, folder);
                    Console.WriteLine($"Wrote the guide of {tours.Count} tours ({tours.Sum(tour => tour.Steps.Count)} steps) into {folder}");
                    return builder.Missing == 0 ? 0 : 1;
                });
            }
            finally
            {
                try
                {
                    Directory.Delete(scratch, recursive: true);
                }
                catch (IOException)
                {
                    // temporary pictures left behind
                }
            }
        }

        private static int Check(GuideOptions o)
        {
            GuideManifest manifest = GuideManifest.Read(o.Out);
            return WithEditor(o, manifest.Window[0], manifest.Window[1], (editor, workspace) =>
            {
                Dictionary<string, List<LiveStep>> live = [];
                foreach (TourFile tour in ChooseTours(o, workspace))
                {
                    List<LiveStep> steps = [];
                    foreach (TourFileStep step in tour.Steps.Where(step => !step.Skip))
                    {
                        editor.Post("tour", new JsonObject { ["id"] = tour.Id, ["step"] = step.Number, ["card"] = false });
                        JsonObject target = GuideBuilder.Target(editor);
                        steps.Add(new LiveStep(step.Id, GuideBuilder.Found(target), GuideBuilder.Bounds(target), target["why"]?.GetValue<string>()));
                    }
                    editor.Run("help.tourEnd");
                    live[tour.Id] = steps;
                }
                GuideManifest compared = o.Tours is null
                    ? manifest
                    : manifest with { Tours = [.. manifest.Tours.Where(tour => live.ContainsKey(tour.Id))] };
                List<GuideFinding> findings = GuideCheck.Compare(compared, live);
                foreach (GuideFinding finding in findings)
                {
                    Console.WriteLine($"{(finding.IsError ? "ERROR" : "note ")} {finding.Message}");
                }
                int steps2 = live.Values.Sum(list => list.Count);
                int errors = findings.Count(finding => finding.IsError);
                Console.WriteLine($"Checked {live.Count} tours, {steps2} steps: {errors} targets missing, {findings.Count(finding => finding.Kind == GuideFindingKind.Moved)} moved, {findings.Count(finding => finding.Kind is GuideFindingKind.Added or GuideFindingKind.AddedTour)} added, {findings.Count(finding => finding.Kind is GuideFindingKind.Removed or GuideFindingKind.RemovedTour)} removed");
                return errors == 0 ? 0 : 1;
            });
        }
    }
}
