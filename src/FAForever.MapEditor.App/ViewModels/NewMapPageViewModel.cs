using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A size of map the page offers: <c>10 km (512 ogrids)</c>.
/// </summary>
public sealed record MapSizeChoice(int Ogrids)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{MapInformationViewModel.Kilometres(Ogrids)} ({Ogrids} ogrids)");
}

/// <summary>
/// The page of File, New map (user story M-2): the name, size, starting height, water and biome of
/// a map made from scratch. Create runs <c>file.new</c> with them, after asking about the unsaved
/// changes of the open map.
/// </summary>
/// <summary>
/// A choice of the New map page's template list: none, or a template of the workspace.
/// </summary>
public sealed record TemplateChoice(MapTemplate? Template)
{
    public static TemplateChoice None { get; } = new TemplateChoice((MapTemplate?)null);

    public string Title => Template?.Name ?? "None: from the fields below";

    public override string ToString() => Title;
}

public sealed partial class NewMapPageViewModel(Func<Task<bool>> confirmClose, Func<string, JsonObject, Task> run) : ObservableObject
{
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "New map";

    public IReadOnlyList<MapSizeChoice> Sizes { get; } = [.. NewMap.Sizes.Select(size => new MapSizeChoice(size))];

    [ObservableProperty]
    public partial MapSizeChoice Size { get; set; } = new MapSizeChoice(512);

    /// <summary>
    /// The terrain's height in game units, as text.
    /// </summary>
    [ObservableProperty]
    public partial string Height { get; set; } = "35";

    [ObservableProperty]
    public partial bool Water { get; set; } = true;

    [ObservableProperty]
    public partial string WaterElevation { get; set; } = "30";

    public IReadOnlyList<NewMapBiome> Biomes { get; } = NewMap.Biomes;

    [ObservableProperty]
    public partial NewMapBiome Biome { get; set; } = NewMap.Biomes[0];

    [ObservableProperty]
    public partial string Players { get; set; } = "2";

    /// <summary>
    /// What is wrong with the choices, or null.
    /// </summary>
    [ObservableProperty]
    public partial string? Error { get; private set; }

    /// <summary>
    /// Opens the page with the choices as they were last time.
    /// </summary>
    /// <summary>
    /// The map templates to start from (user story M-2): none (the fields below), the workspace's,
    /// the sandbox; read again each time the page opens.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<TemplateChoice> Templates { get; private set; } = [TemplateChoice.None];

    /// <summary>
    /// The template the new map starts from; <see cref="TemplateChoice.None"/> for the fields alone.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesFields), nameof(TemplateSummary))]
    public partial TemplateChoice Template { get; set; } = TemplateChoice.None;

    /// <summary>
    /// Whether the fields make the map: without a template, or with one of settings, which fills
    /// them; a template of a whole map takes only the name.
    /// </summary>
    public bool UsesFields => Template.Template?.Kind is null or MapTemplateKind.Settings;

    /// <summary>
    /// What the chosen template holds, in words.
    /// </summary>
    public string TemplateSummary => Template.Template is MapTemplate template
        ? template.Kind == MapTemplateKind.Settings ? $"Fills the fields below: {template.Summary}" : $"Copies {template.Summary}; the name below is the new map's"
        : "Save an open map as a template (File, Save as a template) to start from it here";

    public void Open() => Open(null);

    /// <param name="templatesFolder">The workspace's templates; null without a workspace (the sandbox only).</param>
    public void Open(string? templatesFolder)
    {
        Error = null;
        string? chosen = Template.Template?.Name;
        Templates = [TemplateChoice.None, .. MapTemplates.List(templatesFolder ?? "").Select(template => new TemplateChoice(template))];
        Template = Templates.FirstOrDefault(choice => choice.Template?.Name == chosen) ?? TemplateChoice.None;
        IsOpen = true;
    }


    partial void OnTemplateChanged(TemplateChoice? oldValue, TemplateChoice newValue)
    {
        // a combo box whose list is replaced lets go of its item
        if (newValue is null)
        {
            Template = oldValue ?? TemplateChoice.None;
            return;
        }
        StrataFromTemplate = false;
        if (newValue.Template is { Kind: MapTemplateKind.Settings, Path: string file })
        {
            try
            {
                Fill(MapTemplates.ReadSettings(file));
            }
            catch (MapProjectException exception)
            {
                Error = $"The template {newValue.Template.Name} cannot be read: {exception.Message}";
            }
        }
    }

    /// <summary>
    /// The fields from a template of settings: what it gives, the rest as they are.
    /// </summary>
    private void Fill(MapTemplateDto template)
    {
        _filling = true;
        try
        {
            FillFields(template);
        }
        finally
        {
            _filling = false;
        }
        // its own strata, until another biome is chosen
        StrataFromTemplate = template.Strata is { Count: > 0 };
    }

    private bool _filling;

    /// <summary>
    /// Whether the new map takes the template's own strata: a template of settings with strata was
    /// chosen and no biome since.
    /// </summary>
    [ObservableProperty]
    public partial bool StrataFromTemplate { get; private set; }

    partial void OnBiomeChanged(NewMapBiome value)
    {
        if (!_filling)
        {
            StrataFromTemplate = false;
        }
    }

    private void FillFields(MapTemplateDto template)
    {
        if (template.Size is int size && Sizes.FirstOrDefault(choice => choice.Ogrids == size) is MapSizeChoice choice)
        {
            Size = choice;
        }
        if (template.Height is float height)
        {
            Height = EditorCulture.Format(height);
        }
        if (template.Water is bool water)
        {
            Water = water;
        }
        if (template.WaterElevation is float elevation)
        {
            WaterElevation = EditorCulture.Format(elevation);
        }
        if (template.Biome is string biome && Biomes.FirstOrDefault(b => string.Equals(b.Id, biome, StringComparison.OrdinalIgnoreCase)) is NewMapBiome found)
        {
            Biome = found;
        }
        if (template.Players is int players)
        {
            Players = players.ToString(CultureInfo.InvariantCulture);
        }
    }

    [RelayCommand]
    private void Cancel() => IsOpen = false;

    /// <summary>
    /// The arguments of <c>file.new</c> for the choices; null (with <see cref="Error"/> set) when a
    /// number is not one.
    /// </summary>
    public JsonObject? Arguments()
    {
        if (!Setting.TryParse(Height, out double height) || !Setting.TryParse(WaterElevation, out double water)
            || !int.TryParse(Players.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int players))
        {
            Error = "The height, the water's elevation and the number of players are numbers";
            return null;
        }
        if (!UsesFields)
        {
            return new JsonObject { ["name"] = Name, ["template"] = Template.Template!.Name };
        }
        return new JsonObject
        {
            ["template"] = Template.Template?.Name,
            ["name"] = Name,
            ["size"] = Size.Ogrids,
            ["height"] = height,
            ["water"] = Water,
            ["waterElevation"] = water,
            ["biome"] = StrataFromTemplate ? null : Biome.Id,
            ["players"] = players,
        };
    }

    [RelayCommand]
    private async Task Create()
    {
        if (Arguments() is not JsonObject arguments || !await confirmClose())
        {
            return;
        }
        Error = null;
        IsOpen = false;
        await run("file.new", arguments);
    }
}
