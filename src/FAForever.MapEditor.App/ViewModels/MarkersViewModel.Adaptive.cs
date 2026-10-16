using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A group an adaptive map's script creates by a lobby option, as the Markers panel lists it.
/// </summary>
public sealed record OptionalGroupItem(OptionalGroup Group)
{
    public string Line => Group.Option == AdaptiveScript.PlayersOption && Group.Level is int players
        ? $"{Group.Group} ({Group.Army}): {MainViewModel.Units(Group.Units)}, when {players} or more players are in the game"
        : Group.Level is int level
        ? $"{Group.Group} ({Group.Army}): {MainViewModel.Units(Group.Units)}, from value {level} of optional_{Group.Option.ToLowerInvariant()}"
        : $"{Group.Group} ({Group.Army}): {MainViewModel.Units(Group.Units)}";
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
        RefreshAdaptiveFiles(document);
        if (document is not null && _armies is { } armies && armies == document.Armies)
        {
            return;
        }
        _armies = document?.Armies;
        OptionalGroups = document is null ? [] : [.. AdaptiveGroups.Find(document).Select(group => new OptionalGroupItem(group))];
    }

    private (object? Script, object? Armies, object? Resources, object? Scenario) _adaptiveOf;

    /// <summary>
    /// What the export writes of the adaptive map (user story R-5, <see cref="AdaptiveScript"/>): the
    /// lobby options, tables and script, or nothing because the map's own script decides.
    /// </summary>
    [ObservableProperty]
    public partial string AdaptiveFilesText { get; private set; } = "";

    /// <summary>
    /// Whether the export writes the adaptive map's options, tables and script.
    /// </summary>
    [ObservableProperty]
    public partial bool WritesAdaptiveFiles { get; private set; }

    private void RefreshAdaptiveFiles(MapDocument? document)
    {
        (object?, object?, object?, object?) of = (document?.Script, document?.Armies, document?.Markers.Resources, document?.Scenario);
        if (of.Equals(_adaptiveOf))
        {
            return;
        }
        _adaptiveOf = of;
        WritesAdaptiveFiles = document is not null && AdaptiveScript.Writes(document);
        AdaptiveFilesText = document is null || !AdaptiveScript.Adapts(document)
            ? "A map that adapts gets the lobby options, tables and script of FAF's adaptive maps on export: groups Optional_Option_n by option, Optional_Players_n by the number of players."
            : WritesAdaptiveFiles
                ? "The export writes the map's _options.lua (an option per group's option and dynamic_spawn), _tables.lua (the deposits per army) and the script that creates the groups and leaves out the deposits of armies no one plays."
                : "The map's own script decides what adapts: the export keeps it and writes no options or tables.";
    }
}
