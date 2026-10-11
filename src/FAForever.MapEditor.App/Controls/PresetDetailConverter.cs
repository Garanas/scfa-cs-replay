using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia.Data.Converters;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The detail of a preset's button in a panel, as the entries of the list modes have one: how many
/// units a unit preset places (<c>14 units</c>, with the units of its groups), how many markers a
/// marker preset places; nothing for the other kinds.
/// </summary>
public sealed class PresetDetailConverter : IValueConverter
{
    public static PresetDetailConverter Instance { get; } = new PresetDetailConverter();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is Preset preset ? Detail(preset) : null;

    public static string? Detail(Preset preset)
    {
        if (preset.Settings["group"] is JsonObject group)
        {
            return MainViewModel.Units(CountUnits(group));
        }
        if (preset.Settings["count"] is JsonValue count && count.TryGetValue(out int markers) && preset.Settings["markers"] is JsonObject)
        {
            return markers == 1 ? "1 marker" : $"{markers.ToString(CultureInfo.InvariantCulture)} markers";
        }
        return null;
    }

    // the units of a preset's group and of the groups in it
    private static int CountUnits(JsonObject group) =>
        ((group["units"] as JsonArray)?.Count ?? 0) + ((group["groups"] as JsonArray)?.OfType<JsonObject>().Sum(CountUnits) ?? 0);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
