using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A layer of the terrain in the Paint panel's list (user story S-2).
/// </summary>
/// <param name="Index">0 is the lower layer, the last the upper one, the strata between.</param>
public sealed record LayerItem(int Index, string Title, string Albedo, string Normal, bool IsUsed, bool IsStratum, bool CanMoveUp, bool CanMoveDown)
{
    /// <summary>
    /// E.g. <c>grass000 · normal grass000_normals</c>, or that the stratum is unused.
    /// </summary>
    public string Textures => !IsUsed ? "unused" : Normal.Length == 0 ? Albedo : $"{Albedo} · normal {Normal}";
}

/// <summary>
/// The terrain's layers with their textures (user story S-2): the lower layer, the strata and the
/// upper layer, as many as the <c>.scmap</c> version holds (four strata in version 53, eight after).
/// The panel lists them and adds, removes and reorders strata (their masks move with them); the
/// inspector shows the chosen layer's albedo and normal texture, chosen in the picker or typed, and
/// their scales. Every change is a command (<c>paint.addStratum</c>, <c>paint.removeStratum</c>,
/// <c>paint.moveStratum</c>, <c>paint.chooseTexture</c>, <c>paint.setLayer</c>), one step of undo.
/// </summary>
public sealed partial class LayerTexturesViewModel : ObservableObject
{
    private readonly Func<string, JsonObject, Task> _run;
    private ImmutableArray<TerrainLayer> _shown;
    private MapDocument? _document;

    public LayerTexturesViewModel(Func<string, JsonObject, Task> run)
    {
        _run = run;
        const string set = "paint.setLayer";
        JsonObject Fixed() => new JsonObject { ["layer"] = SelectedIndex };
        TerrainLayer Of(MapDocument d) => SelectedIndex < d.Terrain.Layers.Length ? d.Terrain.Layers[SelectedIndex] : new TerrainLayer { AlbedoPath = "", NormalPath = "" };
        Layer = new SettingGroup("Layer",
        [
            new TextureSetting("Albedo", set, "albedo", d => Of(d).AlbedoPath, run, Fixed,
                () => run("paint.chooseTexture", new JsonObject { ["layer"] = SelectedIndex }))
            {
                Help = "The texture's game path; Choose opens the picker of the workspace's strata textures, and takes its normal map too",
            },
            new NumberSetting("Albedo scale", set, "albedoScale", d => Of(d).AlbedoScale, run, Fixed) { Help = "How many ogrids the texture repeats over" },
            new TextureSetting("Normal map", set, "normal", d => Of(d).NormalPath, run, Fixed,
                () => run("paint.chooseTexture", new JsonObject { ["layer"] = SelectedIndex, ["normal"] = true })),
            new NumberSetting("Normal scale", set, "normalScale", d => Of(d).NormalScale, run, Fixed),
        ]);
    }

    [ObservableProperty]
    public partial IReadOnlyList<LayerItem> Items { get; private set; } = [];

    /// <summary>
    /// The layer the inspector shows, and the buttons work on.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedIndex), nameof(SelectedTitle), nameof(CanRemove), nameof(CanMoveUp), nameof(CanMoveDown))]
    public partial LayerItem? Selected { get; set; }

    public int SelectedIndex => Selected?.Index ?? 0;

    public string SelectedTitle => Selected?.Title ?? "";

    public bool CanRemove => Selected is { IsStratum: true, IsUsed: true };

    public bool CanMoveUp => Selected?.CanMoveUp == true;

    public bool CanMoveDown => Selected?.CanMoveDown == true;

    /// <summary>
    /// E.g. <c>4 of 8 strata used (version 56)</c>.
    /// </summary>
    [ObservableProperty]
    public partial string Summary { get; private set; } = "";

    /// <summary>
    /// The chosen layer's textures and scales, for the inspector.
    /// </summary>
    public SettingGroup Layer { get; }

    partial void OnSelectedChanged(LayerItem? value) => Layer.Refresh(value is null ? null : _document);

    /// <summary>
    /// Chooses the layer the inspector shows.
    /// </summary>
    public void Choose(int layer) => Selected = Items.FirstOrDefault(item => item.Index == layer) ?? Selected;

    [RelayCommand]
    private Task Add() => _run("paint.addStratum", []);

    [RelayCommand]
    private Task Remove() => CanRemove ? _run("paint.removeStratum", new JsonObject { ["layer"] = SelectedIndex }) : Task.CompletedTask;

    [RelayCommand]
    private Task MoveUp() => CanMoveUp ? _run("paint.moveStratum", new JsonObject { ["layer"] = SelectedIndex, ["to"] = SelectedIndex + 1 }) : Task.CompletedTask;

    [RelayCommand]
    private Task MoveDown() => CanMoveDown ? _run("paint.moveStratum", new JsonObject { ["layer"] = SelectedIndex, ["to"] = SelectedIndex - 1 }) : Task.CompletedTask;

    /// <summary>
    /// The title of a layer: <c>Lower layer</c>, <c>Stratum 3</c>, <c>Upper layer</c>.
    /// </summary>
    public static string TitleOf(int layer, int count) =>
        layer == 0 ? "Lower layer" : layer == count - 1 ? "Upper layer" : string.Create(CultureInfo.InvariantCulture, $"Stratum {layer}");

    public void Refresh(MapDocument? document)
    {
        _document = document;
        ImmutableArray<TerrainLayer> layers = document?.Terrain.Layers ?? [];
        if (document is not null && layers == _shown)
        {
            return;
        }
        _shown = layers;
        int selected = Selected?.Index ?? 1;
        int count = layers.Length;
        // the upper layer first, as they lie on each other
        Items = [.. layers.Select((layer, index) => new LayerItem(index, TitleOf(index, count),
            Path.GetFileNameWithoutExtension(layer.AlbedoPath), Path.GetFileNameWithoutExtension(layer.NormalPath),
            layer.IsUsed() || index == 0 || index == count - 1, document!.Terrain.IsStratum(index),
            document.Terrain.IsStratum(index) && document.Terrain.IsStratum(index + 1),
            document.Terrain.IsStratum(index) && document.Terrain.IsStratum(index - 1))).Reverse()];
        Summary = document is null
            ? ""
            : string.Create(CultureInfo.InvariantCulture,
                $"{layers.Skip(1).Take(count - 2).Count(layer => layer.IsUsed())} of {count - 2} strata used (version {document.Terrain.ScmapVersion}); the upper layers cover the lower ones");
        Selected = Items.FirstOrDefault(item => item.Index == selected) ?? Items.LastOrDefault();
        Layer.Refresh(Selected is null ? null : document);
    }
}
