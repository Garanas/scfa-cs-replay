using System.Globalization;
using System.Text.Json.Nodes;
using ImageMagick;

namespace FAForever.MapEditor.Cli.Guide
{
    /// <summary>
    /// Plays the tours in a running test editor and takes the pictures: per step the editor draws
    /// its dimming and ring (no card: the text goes on the page), a screenshot of the window is
    /// cropped round the target, and a step the person acts in is done for them and pictured again.
    /// </summary>
    public sealed class GuideBuilder
    {
        private readonly EditorSession editor;
        private readonly string folder;
        private readonly string scratch;
        private readonly int width;
        private readonly int height;
        private int shots;

        public GuideBuilder(EditorSession editor, string folder, string scratch, int width, int height)
        {
            this.editor = editor;
            this.folder = folder;
            this.scratch = scratch;
            this.width = width;
            this.height = height;
        }

        /// <summary>The steps whose target the editor did not find.</summary>
        public int Missing { get; private set; }

        /// <summary>
        /// <c>state.tour.target</c> after a step has been entered: the editor lays the target out on
        /// the next frame, so it is read once settled.
        /// </summary>
        public static JsonObject Target(EditorSession editor)
        {
            Thread.Sleep(120);
            JsonObject tour = editor.Post("state")["tour"] as JsonObject ?? [];
            return tour["target"] as JsonObject ?? [];
        }

        /// <summary>A step without a target has nothing to find.</summary>
        public static bool Found(JsonObject target) => target["named"] is null || target["found"]?.GetValue<bool>() == true;

        public static int[]? Bounds(JsonObject target) =>
            target["bounds"] is JsonArray array && array.Count == 4 ? [.. array.Select(item => (int)Math.Round(item!.GetValue<double>()))] : null;

        public GuideTour BuildTour(TourFile tour, bool overview)
        {
            Console.WriteLine($"Tour {tour.Id}: {tour.Steps.Count} steps");
            List<GuideStep> steps = [];
            foreach (TourFileStep step in tour.Steps.Where(step => !step.Skip))
            {
                steps.Add(BuildStep(tour, step));
            }
            string? overviewImage = null;
            if (overview)
            {
                overviewImage = BuildOverview(tour, steps);
            }
            else
            {
                editor.Run("help.tourEnd");
            }
            ResetMap();
            return new GuideTour(tour.Id, tour.Title, tour.Summary, tour.Order, overviewImage, steps);
        }

        /// <summary>
        /// Undoes what a tour's steps did to the map (a placed deposit, a unit, a layer, a stroke), so that the next
        /// tour's pictures show the map as the first tour found it.
        /// </summary>
        public void ResetMap()
        {
            for (int undone = 0; undone < MaxUndo && editor.Post("state")["undo"] is not null; undone++)
            {
                if (!editor.Run("edit.undo"))
                {
                    break;
                }
            }
        }

        private const int MaxUndo = 1000;

        private GuideStep BuildStep(TourFile tour, TourFileStep step)
        {
            editor.Post("tour", new JsonObject { ["id"] = tour.Id, ["step"] = step.Number, ["card"] = false });
            JsonObject state = Shot(out MagickImage window, out double scale, out int[]? view3d);
            using (window)
            {
                JsonObject tourState = state["tour"] as JsonObject ?? [];
                if (tourState["stepId"]?.GetValue<string>() != step.Id)
                {
                    throw new GuideException($"{tour.Id}/{step.Id}: the editor shows the step '{tourState["stepId"]}' instead");
                }
                JsonObject target = tourState["target"] as JsonObject ?? [];
                int[]? bounds = Bounds(target);
                bool found = Found(target);
                if (!found)
                {
                    Missing++;
                    Console.Error.WriteLine($"{tour.Id}/{step.Id}: target not found ({target["why"]?.GetValue<string>()})");
                }
                // a step without a target (a welcome, an end) is a card only: no picture of the window
                string? image = target["named"] is null ? null : Path.Combine("images", tour.Id, $"{step.Number:00}-{step.Id}{GuideImages.Extension}").Replace('\\', '/');
                if (image is not null)
                {
                    Save(window, scale, found ? bounds : null, null, step.Margin, image);
                }
                Console.WriteLine($"  {step.Number}. {step.Id}: {(found ? (bounds is null ? "no target" : $"[{string.Join(", ", bounds)}]") : "target NOT FOUND")}");

                string? after = null;
                if (image is not null && step.DoItForMe && step.After != false)
                {
                    after = BuildAfter(tour, step, bounds, image);
                }
                string named = target["named"]?.GetValue<string>() ?? "";
                return new GuideStep(
                    step.Number,
                    step.Id,
                    step.Title,
                    step.Text,
                    step.More,
                    step.Done,
                    named.Length == 0 ? null : new GuideTarget(named, found, bounds, target["why"]?.GetValue<string>()),
                    image,
                    after);
            }
        }

        /// <summary>
        /// Does what the step asks for (Do it for me), waits for its condition and takes the picture
        /// of what the person sees then, with the 3D view when it shows.
        /// </summary>
        private string? BuildAfter(TourFile tour, TourFileStep step, int[]? bounds, string image)
        {
            if (!editor.Run("help.tourDo"))
            {
                Console.Error.WriteLine($"{tour.Id}/{step.Id}: Do it for me did not run");
                return null;
            }
            if (step.UntilPath is not null)
            {
                JsonObject wait = new JsonObject { ["path"] = step.UntilPath, ["value"] = step.UntilValue?.DeepClone(), ["timeout"] = 15 };
                if (editor.Post("wait", wait)["wait"]?["matched"]?.GetValue<bool>() != true)
                {
                    Console.Error.WriteLine($"{tour.Id}/{step.Id}: the condition {step.UntilPath} = {step.UntilValue} did not hold after Do it for me");
                }
            }
            JsonObject state = Shot(out MagickImage window, out double scale, out int[]? view3d);
            using (window)
            {
                int[]? now = Bounds((state["tour"]?["target"] as JsonObject) ?? []) ?? bounds;
                string after = image[..^GuideImages.Extension.Length] + "-after" + GuideImages.Extension;
                Save(window, scale, now, view3d, step.Margin, after);
                Console.WriteLine($"    after: {after}");
                return after;
            }
        }

        /// <summary>
        /// The overview: the tour ended, the window as it is then, with an outline and a number on
        /// each step's target (the step's number on the page).
        /// </summary>
        private string? BuildOverview(TourFile tour, List<GuideStep> steps)
        {
            editor.Run("help.tourEnd");
            JsonObject state = Shot(out MagickImage window, out double scale, out _, tourRunning: false);
            using (window)
            {
                List<(int, int[])> places = [];
                foreach (GuideStep step in steps)
                {
                    int[]? bounds = step.Target?.Found == true ? step.Target.Bounds : null;
                    if (bounds is not null && (double)bounds[2] * bounds[3] < 0.7 * width * height)
                    {
                        places.Add((step.Number, bounds));
                    }
                }
                GuideImages.DrawOverview(window, places, scale);
                string image = Path.Combine("images", tour.Id, "00-overview" + GuideImages.Extension).Replace('\\', '/');
                GuideImages.Save(window, Path.Combine(folder, image));
                Console.WriteLine($"  overview: {image} with {places.Count} numbered places");
                return image;
            }
        }

        /// <summary>
        /// A screenshot of the window, with the 3D view's frame put in where it shows, and the
        /// editor's state at that moment.
        /// </summary>
        private JsonObject Shot(out MagickImage window, out double scale, out int[]? view3d, bool tourRunning = true)
        {
            string path = Path.Combine(scratch, $"shot-{++shots}.png");
            editor.Post("screenshot", new JsonObject { ["path"] = path, ["settle"] = true });
            JsonObject state = editor.Post("state");
            window = new MagickImage(path);
            scale = window.Width / (double)width;
            view3d = null;
            if (state["views"]?["3d"] is JsonObject view && view["visible"]?.GetValue<bool>() == true && view["bounds"] is JsonArray array)
            {
                view3d = [.. array.Select(item => (int)Math.Round(item!.GetValue<double>()))];
                string gpu = Path.Combine(scratch, $"gpu-{shots}.png");
                try
                {
                    editor.Post("screenshot", new JsonObject { ["path"] = gpu, ["gpu"] = true, ["settle"] = true });
                    using MagickImage frame = new MagickImage(gpu);
                    int[]? target = tourRunning ? Bounds((state["tour"]?["target"] as JsonObject) ?? []) : null;
                    GuideImages.PutIn3D(window, frame, view3d, tourRunning, target, scale);
                }
                catch (GuideException exception)
                {
                    Console.Error.WriteLine($"The 3D view's frame could not be read, its place stays empty: {exception.Message}");
                }
            }
            return state;
        }

        private void Save(MagickImage window, double scale, int[]? bounds, int[]? alsoShow, int? margin, string image)
        {
            int[]? region = null;
            if (bounds is not null)
            {
                int[] shown = alsoShow is null ? bounds : GuideGeometry.Union(bounds, alsoShow);
                region = GuideGeometry.Crop(shown, margin ?? GuideGeometry.DefaultMargin, width, height);
            }
            string path = Path.Combine(folder, image);
            if (region is null)
            {
                GuideImages.Save(window, path);
            }
            else
            {
                using IMagickImage<ushort> crop = GuideImages.Crop(window, region, scale);
                GuideImages.Save(crop, path);
            }
        }
    }
}
