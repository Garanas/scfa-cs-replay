using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// What players see of the map (user story M-1): its name, description, author, version, type,
/// preview, no-rush radius and whether it adapts to the number of players; each field changes the
/// map through <c>map.information</c>, one step of undo per field. The reclaim the lobby shows is
/// read only (<see cref="ReclaimAtExport"/>): the export writes the props' totals. The Map panel's
/// Information entry shows them in the inspector in three groups (<see cref="About"/>,
/// <see cref="Game"/>, <see cref="PreviewSettings"/>), with the map's folder, size, armies and the
/// preview the <c>.scmap</c> starts with.
/// </summary>
public sealed partial class MapInformationViewModel : ObservableObject
{
    private ScenarioInfo? _shown;
    private TiledBuffer<Color32>? _shownPreview;

    public MapInformationViewModel(Func<string, JsonObject, Task> run)
    {
        const string command = "map.information";
        Settings = new SettingGroup("Information",
        [
            new TextSetting("Name", command, "name", d => d.Scenario.Name, run) { Help = "The name players see in the lobby" },
            new TextSetting("Description", command, "description", d => d.Scenario.Description, run) { IsLong = true },
            new TextSetting("Author", command, "author", d => d.Scenario.Author, run),
            new NumberSetting("Version", command, "version", d => d.Version ?? 0, run)
            {
                Help = "The map's version in the vault; the folder is named after it (.v0004)",
            },
            new ChoiceSetting("Type", command, "type", ["skirmish", "campaign_coop"], d => TypeName(d.Scenario.Type), run),
            new TextSetting("Image file", command, "preview", d => d.Scenario.PreviewPath, run)
            {
                Help = "The path of a preview image the scenario names, usually empty: the lobby then shows the .scmap's picture below",
            },
            new NumberSetting("No-rush radius", command, "noRushRadius", d => d.Scenario.NoRushRadius, run)
            {
                Minimum = 0, Maximum = 200, HasSlider = true,
                Help = "In game units: how far around each start position others may not come while no rush lasts",
            },
            new BoolSetting("Adaptive map", command, "adaptiveMap", d => d.Scenario.AdaptiveMap, run)
            {
                Help = "The map adapts its resources to the number of players and the lobby's options",
            },
        ]);
        About = Pick("About", "name", "description", "author", "version");
        Game = Pick("Game", "type", "noRushRadius", "adaptiveMap");
        PreviewSettings = Pick("Preview", "preview");
    }

    /// <summary>
    /// Every field, refreshed together; the inspector shows them in <see cref="About"/>,
    /// <see cref="Game"/> and <see cref="PreviewSettings"/>.
    /// </summary>
    public SettingGroup Settings { get; }

    /// <summary>
    /// The reclaim the export writes into the scenario for the lobby, from the props: e.g.
    /// <c>Reclaim: 52,400 mass · 410,000 energy, written at export</c>; empty without a map.
    /// </summary>
    [ObservableProperty]
    public partial string ReclaimAtExport { get; private set; } = "";

    /// <summary>
    /// Shows the props' reclaim as the export will write it; null without a map.
    /// </summary>
    public void ShowReclaim(ReclaimTotal? total) =>
        ReclaimAtExport = total is null ? "" : total.Props == 0
            ? "Reclaim: none, the map has no props"
            : string.Create(CultureInfo.InvariantCulture, $"Reclaim: {Math.Round(total.Mass):#,0} mass · {Math.Round(total.Energy):#,0} energy, written at export");

    /// <summary>
    /// The name, description, author and version.
    /// </summary>
    public SettingGroup About { get; }

    /// <summary>
    /// The type, no-rush radius and whether the map adapts.
    /// </summary>
    public SettingGroup Game { get; }

    /// <summary>
    /// The path of a preview image the scenario names.
    /// </summary>
    public SettingGroup PreviewSettings { get; }

    private SettingGroup Pick(string title, params string[] arguments) =>
        new SettingGroup(title, [.. arguments.Select(argument => Settings.Settings.First(setting => setting.Argument == argument))]);

    /// <summary>
    /// The Map panel's line beside Information: <c>v3 · 4 players</c>.
    /// </summary>
    [ObservableProperty]
    public partial string Summary { get; private set; } = "";

    /// <summary>
    /// E.g. <c>20 km, 1024 by 1024 ogrids</c>; empty without a map.
    /// </summary>
    [ObservableProperty]
    public partial string Size { get; private set; } = "";

    /// <summary>
    /// E.g. <c>8 players, 2 other armies</c>.
    /// </summary>
    [ObservableProperty]
    public partial string Players { get; private set; } = "";

    /// <summary>
    /// <c>2 players, 1 other army</c>, <c>1 player, 3 other armies</c>.
    /// </summary>
    public static string PlayersText(int players, int others) => string.Create(CultureInfo.InvariantCulture,
        $"{players} {(players == 1 ? "player" : "players")}, {others} other {(others == 1 ? "army" : "armies")}");

    /// <summary>
    /// The map's folder, e.g. <c>theta_passage.v0004</c>.
    /// </summary>
    [ObservableProperty]
    public partial string Folder { get; private set; } = "";

    /// <summary>
    /// The preview the <c>.scmap</c> starts with, 256 by 256 pixels; null without a map.
    /// </summary>
    [ObservableProperty]
    public partial TiledBuffer<Color32>? Preview { get; private set; }

    /// <summary>
    /// The text of a type as the scenario writes it.
    /// </summary>
    public static string TypeName(ScenarioType type) => type == ScenarioType.CampaignCoop ? "campaign_coop" : "skirmish";

    /// <summary>
    /// The size of a map in kilometres, as the lobby names it: 256 ogrids are 5 km, 4096 are 81 km.
    /// </summary>
    public static string Kilometres(int ogrids) => ogrids switch
    {
        <= 64 => "1.25 km",
        <= 128 => "2.5 km",
        <= 256 => "5 km",
        <= 512 => "10 km",
        <= 1024 => "20 km",
        <= 2048 => "40 km",
        _ => "81 km",
    };

    /// <summary>
    /// Shows a version of the document; only what changed is shown again.
    /// </summary>
    public void Refresh(MapDocument? document)
    {
        if (document is null)
        {
            _shown = null;
            _shownPreview = null;
            Settings.Refresh(null);
            Size = "";
            Players = "";
            Summary = "";
            Folder = "";
            Preview = null;
            return;
        }
        if (!ReferenceEquals(document.Scenario, _shown) || Folder != document.Folder.ToString())
        {
            _shown = document.Scenario;
            Settings.Refresh(document);
            Players = PlayersText(document.Scenario.PlayableArmies.Length, document.Scenario.ExtraArmies.Length);
            Folder = document.Folder.ToString();
            int players = document.Scenario.PlayableArmies.Length;
            string playing = string.Create(CultureInfo.InvariantCulture, $"{players} {(players == 1 ? "player" : "players")}");
            Summary = document.Version is int version ? string.Create(CultureInfo.InvariantCulture, $"v{version} · {playing}") : playing;
        }
        Size = document.Width == document.Height
            ? string.Create(CultureInfo.InvariantCulture, $"{Kilometres(document.Width)}, {document.Width} by {document.Height} ogrids")
            : string.Create(CultureInfo.InvariantCulture, $"{Kilometres(document.Width)} by {Kilometres(document.Height)}, {document.Width} by {document.Height} ogrids");
        if (!ReferenceEquals(document.Terrain.Preview, _shownPreview))
        {
            _shownPreview = document.Terrain.Preview;
            Preview = document.Terrain.Preview;
        }
    }
}
