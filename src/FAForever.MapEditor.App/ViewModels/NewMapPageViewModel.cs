using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project;

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
    public void Open()
    {
        Error = null;
        IsOpen = true;
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
        return new JsonObject
        {
            ["name"] = Name,
            ["size"] = Size.Ogrids,
            ["height"] = height,
            ["water"] = Water,
            ["waterElevation"] = water,
            ["biome"] = Biome.Id,
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
