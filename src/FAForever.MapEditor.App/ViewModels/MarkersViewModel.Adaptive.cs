using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A group an adaptive map's script creates by a lobby option, as the Markers panel lists it.
/// </summary>
public sealed record OptionalGroupItem(OptionalGroup Group)
{
    public string Line => Group.Level is int level
        ? $"{Group.Group} ({Group.Army}): {Group.Units} units, from value {level} of optional_{Group.Option.ToLowerInvariant()}"
        : $"{Group.Group} ({Group.Army}): {Group.Units} units";
}

/// <summary>
/// The adaptive map's groups of civilians and wrecks (user story R-5): those the map has, and a
/// new one named by the convention of FAF's adaptive script (<see cref="AdaptiveGroups"/>).
/// </summary>
public sealed partial class MarkersViewModel
{
    private System.Collections.Immutable.ImmutableArray<Army>? _armies;

    /// <summary>
    /// The groups named <c>Optional_...</c> directly under an army's units.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<OptionalGroupItem> OptionalGroups { get; private set; } = [];

    /// <summary>
    /// The option of a new group, e.g. <c>Civilian_Defenses</c>.
    /// </summary>
    [ObservableProperty]
    public partial string OptionName { get; set; } = "Civilian_Defenses";

    /// <summary>
    /// The option's value from which on the script creates the new group, as typed; empty for none.
    /// </summary>
    [ObservableProperty]
    public partial string OptionLevel { get; set; } = "2";

    /// <summary>
    /// Adds the group named by the option and value, with the selected units in it.
    /// </summary>
    [RelayCommand]
    private Task AddOptionalGroup()
    {
        JsonObject arguments = new JsonObject { ["option"] = OptionName.Trim(), ["fromSelection"] = true };
        if (int.TryParse(OptionLevel.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int level))
        {
            arguments["level"] = level;
        }
        return _run("markers.addOptionalGroup", arguments);
    }

    /// <summary>
    /// Lists the optional groups of a version of the document, when its armies changed.
    /// </summary>
    internal void RefreshOptionalGroups(MapDocument? document)
    {
        if (document is not null && _armies is { } armies && armies == document.Armies)
        {
            return;
        }
        _armies = document?.Armies;
        OptionalGroups = document is null ? [] : [.. AdaptiveGroups.Find(document).Select(group => new OptionalGroupItem(group))];
    }
}
