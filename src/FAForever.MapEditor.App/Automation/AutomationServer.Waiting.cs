using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Automation;

/// <summary>
/// Automation's reads for a driver that waits and asks by name: <c>wait {path, value, timeout?}</c>
/// polls the state until a value matches, things are found by their name (<c>get {name}</c>,
/// <c>objects {name}</c>), and the state says which build of the editor runs (<c>build</c>).
/// </summary>
public sealed partial class AutomationServer
{
    /// <summary>
    /// The longest <c>wait</c> may take, in seconds.
    /// </summary>
    private const double LongestWait = 120;

    /// <summary>
    /// <c>wait {path, value, timeout?}</c>: reads the state every tenth of a second until the value
    /// at <paramref name="arguments"/>' <c>path</c> (keys joined by dots, a number for an item of a
    /// list: <c>selection.count</c>, <c>history.0</c>) matches <c>value</c>, or <c>timeout</c>
    /// seconds (10 when left out, at most 120) passed. A number matches a number of the same
    /// value, anything else matches by its text (<c>"Ready"</c>, <c>true</c>, <c>null</c>).
    /// </summary>
    /// <returns><c>matched</c>, the value last read, how long it took and how many reads.</returns>
    private async Task<JsonObject> WaitAsync(JsonObject arguments)
    {
        string path = Text(arguments, "path");
        JsonNode? expected = arguments.ContainsKey("value") ? arguments["value"] : throw new ArgumentException("value is missing: the value the path is to have");
        double timeout = Math.Clamp(Number(arguments, "timeout") ?? 10, 0, LongestWait);
        Stopwatch clock = Stopwatch.StartNew();
        int reads = 0;
        JsonNode? found;
        bool matched;
        while (true)
        {
            reads++;
            found = At(State(), path);
            matched = Matches(found, expected);
            if (matched || clock.Elapsed.TotalSeconds >= timeout)
            {
                break;
            }
            await Task.Delay(100);
        }
        return new JsonObject
        {
            ["path"] = path,
            ["matched"] = matched,
            ["value"] = found?.DeepClone(),
            ["milliseconds"] = Math.Round(clock.Elapsed.TotalMilliseconds),
            ["reads"] = reads,
        };
    }

    /// <summary>
    /// The value at a path of keys joined by dots in a JSON value, a number for an item of a list;
    /// null where the path leads nowhere.
    /// </summary>
    internal static JsonNode? At(JsonNode? node, string path)
    {
        foreach (string part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            node = node switch
            {
                JsonObject obj => obj[part],
                JsonArray list when int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index < list.Count => list[index],
                _ => null,
            };
            if (node is null)
            {
                return null;
            }
        }
        return node;
    }

    /// <summary>
    /// Whether a value read matches the one waited for: numbers by value, the rest by their text.
    /// </summary>
    internal static bool Matches(JsonNode? found, JsonNode? expected)
    {
        if (found is null)
        {
            return expected is null || TextOf(expected) == "null";
        }
        if (expected is null)
        {
            return false;
        }
        if (NumberOf(found) is double a && NumberOf(expected) is double b)
        {
            return Math.Abs(a - b) < 1e-9;
        }
        return TextOf(found) == TextOf(expected);
    }

    private static double? NumberOf(JsonNode node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.Number ? double.Parse(value.ToJsonString(), CultureInfo.InvariantCulture) : null;

    private static string TextOf(JsonNode node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : node.ToJsonString();

    /// <summary>
    /// The thing of a name: a marker, unit or area named so (ignoring case); props and decals have no names.
    /// </summary>
    /// <exception cref="ArgumentException">Nothing has that name.</exception>
    private static ObjectRef Named(MapDocument document, string name)
    {
        foreach (ObjectRef thing in document.All(ObjectKinds.Markers | ObjectKinds.PathNodes | ObjectKinds.Units | ObjectKinds.Areas))
        {
            if (NameOf(document, thing) is string named && string.Equals(named, name, StringComparison.OrdinalIgnoreCase))
            {
                return thing;
            }
        }
        throw new ArgumentException($"Nothing on the map is named '{name}': markers, units and areas have names (objects name=... finds them by part of it)");
    }

    /// <summary>
    /// What a thing is called, as <c>objects</c> lists it: a marker's, unit's or area's name, a
    /// prop's or unit's blueprint, a decal's texture.
    /// </summary>
    private static string? WhatOf(MapDocument document, ObjectRef thing) => document.Find(thing) switch
    {
        Prop prop => prop.Blueprint,
        PlacedUnit unit => unit.Unit.Blueprint,
        Marker marker => marker.Name,
        Decal decal => decal.Textures.FirstOrDefault(texture => texture.Length > 0),
        Area area => area.Name,
        _ => null,
    };

    private static string? NameOf(MapDocument document, ObjectRef thing) => document.Find(thing) switch
    {
        PlacedUnit unit => unit.Unit.Name,
        Marker marker => marker.Name,
        Area area => area.Name,
        _ => null,
    };

    /// <summary>
    /// Which build of the editor runs: its version and when its assembly was built (the file's
    /// time), so a driver can tell an editor started before a build from one after.
    /// </summary>
    private static JsonObject BuildState()
    {
        Assembly assembly = typeof(AutomationServer).Assembly;
        string? version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString();
        DateTime? built = null;
        try
        {
            built = string.IsNullOrEmpty(assembly.Location) ? null : File.GetLastWriteTimeUtc(assembly.Location);
        }
        catch (IOException)
        {
            // the file went away: no time
        }
        return new JsonObject
        {
            ["version"] = version,
            ["built"] = built?.ToString("O", CultureInfo.InvariantCulture),
        };
    }
}
