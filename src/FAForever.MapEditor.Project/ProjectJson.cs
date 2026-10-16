using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// How the editor writes and reads its JSON files: projects, the workspace and its assets.
    /// Indented, camel case, nulls left out, enums as text.
    /// </summary>
    internal static class ProjectJson
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            // the files are read by people and their tools, never put in a web page: <LOC ...> stays as it is
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            RespectNullableAnnotations = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>
        /// The text of a file, ending with a new line.
        /// </summary>
        public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options) + "\n";

        /// <summary>
        /// Writes a JSON file unless it already holds this text.
        /// </summary>
        public static void Write<T>(string path, T value)
        {
            string text = Serialize(value);
            if (!File.Exists(path) || File.ReadAllText(path) != text)
            {
                File.WriteAllText(path, text);
            }
        }

        /// <exception cref="MapProjectException">The file is missing, empty or not what it should hold.</exception>
        public static T Read<T>(string path)
        {
            if (!File.Exists(path))
            {
                throw new MapProjectException($"{path} is missing");
            }
            try
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
                    ?? throw new MapProjectException($"{path} is empty");
            }
            catch (JsonException exception)
            {
                throw new MapProjectException($"{path}: {exception.Message}", exception);
            }
        }
    }
}
