using System.Text.Json;
using System.Text.Json.Nodes;

namespace FAForever.MapEditor.Cli.Guide
{
    /// <summary>
    /// A tour file as the guide reads it: the fields it writes pages from and the steps it takes
    /// pictures of. The editor checks the file; this reads what it needs and nothing more.
    /// </summary>
    public sealed record TourFile(string Id, string Title, string Summary, int Order, List<TourFileStep> Steps);

    /// <param name="Number">The step's place in the file, from 1.</param>
    /// <param name="Skip">The step has <c>guide.skip</c>: no picture and no place in the guide.</param>
    /// <param name="DoItForMe">The step asks the person to act and offers to do it: the guide runs it and takes an after picture.</param>
    /// <param name="Until">The condition of the step, <c>path</c> and <c>value</c>.</param>
    public sealed record TourFileStep(
        int Number,
        string Id,
        string Title,
        string Text,
        string? More,
        string? Done,
        int? Margin,
        bool Skip,
        bool DoItForMe,
        bool? After,
        string? UntilPath,
        JsonNode? UntilValue);

    public static class GuideTours
    {
        private static readonly JsonDocumentOptions Options = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>
        /// Every <c>*.tour.json</c> of a folder that can be read, in the order of the list of tours
        /// (<c>order</c>, then id). A file that cannot be read is an error naming it.
        /// </summary>
        public static List<TourFile> ReadFolder(string folder)
        {
            if (!Directory.Exists(folder))
            {
                throw new GuideException($"The tours folder {folder} does not exist");
            }
            List<TourFile> tours = [];
            foreach (string file in Directory.GetFiles(folder, "*.tour.json"))
            {
                try
                {
                    tours.Add(Parse(File.ReadAllText(file)));
                }
                catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException)
                {
                    throw new GuideException($"{file}: {exception.Message}");
                }
            }
            return [.. tours.OrderBy(tour => tour.Order).ThenBy(tour => tour.Id, StringComparer.Ordinal)];
        }

        public static TourFile Parse(string json)
        {
            JsonNode root = JsonNode.Parse(json, documentOptions: Options) ?? throw new JsonException("the file is empty");
            string id = Text(root["id"]) ?? throw new JsonException("the tour has no id");
            List<TourFileStep> steps = [];
            int number = 0;
            foreach (JsonNode? step in root["steps"]?.AsArray() ?? [])
            {
                number++;
                JsonNode? guide = step?["guide"];
                JsonNode? until = step?["until"];
                steps.Add(new TourFileStep(
                    number,
                    Text(step?["id"]) ?? throw new JsonException($"step {number} of {id} has no id"),
                    Text(step?["title"]) ?? "",
                    Text(step?["text"]) ?? "",
                    Text(guide?["more"]),
                    Text(step?["done"]),
                    guide?["margin"]?.GetValue<int>(),
                    guide?["skip"]?.GetValue<bool>() == true,
                    step?["doItForMe"] is not null && until is not null,
                    guide?["after"]?.GetValue<bool>(),
                    Text(until?["path"]),
                    until?["value"]?.DeepClone()));
            }
            return new TourFile(id, Text(root["title"]) ?? id, Text(root["summary"]) ?? "", root["order"]?.GetValue<int>() ?? 1000, steps);
        }

        private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
    }
}
