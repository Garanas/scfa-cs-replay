using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// How the JSON files of a project are written: camel case, indented, enums by name and vectors
    /// as arrays (<c>[x, y, z]</c>). Floats are written as the shortest text that reads back to the
    /// same value, so a map survives unpacking and packing unchanged.
    /// </summary>
    public static class MapProjectJson
    {
        public static JsonSerializerOptions Options { get; } = CreateOptions();

        private static JsonSerializerOptions CreateOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            options.Converters.Add(new JsonStringEnumConverter());
            options.Converters.Add(new Vector2Converter());
            options.Converters.Add(new Vector3Converter());
            options.Converters.Add(new Vector4Converter());
            options.Converters.Add(new ByteTripleConverter());
            options.MakeReadOnly(populateMissingResolver: true);
            return options;
        }

        public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

        /// <summary>
        /// Reads a JSON file of a project.
        /// </summary>
        /// <exception cref="MapProjectException">The file is missing or is not what was expected.</exception>
        public static T Read<T>(string path)
        {
            if (!File.Exists(path))
            {
                throw new MapProjectException($"{Path.GetFileName(path)} is missing ({path})");
            }
            try
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
                    ?? throw new MapProjectException($"{Path.GetFileName(path)} is empty");
            }
            catch (JsonException exception)
            {
                throw new MapProjectException($"{Path.GetFileName(path)}: {exception.Message}", exception);
            }
        }

        public static void Write<T>(string path, T value) => File.WriteAllText(path, Serialize(value) + "\n");

        private static float[] ReadFloats(ref Utf8JsonReader reader, int count, JsonSerializerOptions options)
        {
            float[] values = JsonSerializer.Deserialize<float[]>(ref reader, options)
                ?? throw new JsonException("Expected an array of numbers");
            if (values.Length != count)
            {
                throw new JsonException($"Expected {count} numbers, found {values.Length}");
            }
            return values;
        }

        private static void WriteFloats(Utf8JsonWriter writer, params ReadOnlySpan<float> values)
        {
            writer.WriteStartArray();
            foreach (float value in values)
            {
                if (float.IsFinite(value))
                {
                    writer.WriteNumberValue(value);
                }
                else
                {
                    writer.WriteStringValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            writer.WriteEndArray();
        }

        private sealed class Vector2Converter : JsonConverter<Vector2>
        {
            public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                float[] v = ReadFloats(ref reader, 2, options);
                return new Vector2(v[0], v[1]);
            }

            public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options) =>
                WriteFloats(writer, value.X, value.Y);
        }

        private sealed class Vector3Converter : JsonConverter<Vector3>
        {
            public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                float[] v = ReadFloats(ref reader, 3, options);
                return new Vector3(v[0], v[1], v[2]);
            }

            public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options) =>
                WriteFloats(writer, value.X, value.Y, value.Z);
        }

        private sealed class Vector4Converter : JsonConverter<Vector4>
        {
            public override Vector4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                float[] v = ReadFloats(ref reader, 4, options);
                return new Vector4(v[0], v[1], v[2], v[3]);
            }

            public override void Write(Utf8JsonWriter writer, Vector4 value, JsonSerializerOptions options) =>
                WriteFloats(writer, value.X, value.Y, value.Z, value.W);
        }

        /// <summary>
        /// A colour of three bytes (<see cref="FileFormats.Map.ScmapSkybox.MidColor"/>) as <c>[red, green, blue]</c>.
        /// </summary>
        private sealed class ByteTripleConverter : JsonConverter<(byte, byte, byte)>
        {
            public override (byte, byte, byte) Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                byte[] values = JsonSerializer.Deserialize<int[]>(ref reader, options) is { Length: 3 } ints
                    ? ints.Select(i => checked((byte)i)).ToArray()
                    : throw new JsonException("Expected three numbers from 0 to 255");
                return (values[0], values[1], values[2]);
            }

            public override void Write(Utf8JsonWriter writer, (byte, byte, byte) value, JsonSerializerOptions options)
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(value.Item1);
                writer.WriteNumberValue(value.Item2);
                writer.WriteNumberValue(value.Item3);
                writer.WriteEndArray();
            }
        }
    }

    /// <summary>
    /// A project that cannot be packed or a map that cannot be unpacked, with a message for the map
    /// maker: which file, and what is wrong with it.
    /// </summary>
    public sealed class MapProjectException(string message, Exception? inner = null) : Exception(message, inner);
}
