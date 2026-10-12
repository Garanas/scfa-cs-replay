using System.Text.Json;
using System.Text.Json.Serialization;

namespace FAForever.MapEditor.Cli.Guide
{
    /// <summary>
    /// <c>guide.json</c>: everything the pages are written from and the check compares with. Bounds
    /// are <c>[x, y, width, height]</c> in the window's pixels.
    /// </summary>
    public sealed record GuideManifest(
        int Format,
        GuideEditor Editor,
        string Theme,
        int[] Window,
        List<GuideTour> Tours)
    {
        public const int CurrentFormat = 1;

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public string ToJson() => JsonSerializer.Serialize(this, Options);

        public static GuideManifest FromJson(string json) =>
            JsonSerializer.Deserialize<GuideManifest>(json, Options) ?? throw new JsonException("guide.json is empty");

        public static GuideManifest Read(string folder)
        {
            string file = Path.Combine(folder, "guide.json");
            if (!File.Exists(file))
            {
                throw new GuideException($"{file} does not exist: make the guide first (faf-map guide build)");
            }
            return FromJson(File.ReadAllText(file));
        }
    }

    /// <summary>
    /// The editor a guide was made with.
    /// </summary>
    public sealed record GuideEditor(string? Version, string? Built, string Generated);

    /// <summary>
    /// A tour: its steps without the ones the guide skips, and the picture of the whole window with
    /// each step's place numbered, when the tour has one.
    /// </summary>
    public sealed record GuideTour(string Id, string Title, string Summary, int Order, string? Overview, List<GuideStep> Steps);

    /// <summary>
    /// A step. <see cref="Number"/> counts from 1 in the tour's file (a skipped step keeps its
    /// number), <see cref="Image"/> and <see cref="After"/> are paths below the guide's folder.
    /// </summary>
    public sealed record GuideStep(
        int Number,
        string Id,
        string Title,
        string Text,
        string? More,
        string? Done,
        GuideTarget? Target,
        string? Image,
        string? After);

    /// <summary>
    /// What a step points at, as the editor named it, whether it found it, and where.
    /// </summary>
    public sealed record GuideTarget(string Named, bool Found, int[]? Bounds, string? Why);

    /// <summary>
    /// Something the guide's tools report to the person: a message, no stack trace.
    /// </summary>
    public sealed class GuideException(string message) : Exception(message);
}
