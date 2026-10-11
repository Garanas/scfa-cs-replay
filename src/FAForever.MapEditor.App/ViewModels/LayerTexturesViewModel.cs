using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A layer of the terrain in the Paint panel's list (user story S-2).
/// </summary>
/// <param name="Index">0 is the lower layer, the last the upper one, the strata between.</param>
/// <param name="index">0 is the lower layer, the last the upper one, the strata between.</param>
/// <param name="texture">Its albedo texture as a small picture (the converted PNG); null without one.</param>
/// <param name="mask">A stratum's final mask (what its stack of layers makes, what the map is drawn and exported with); null for the lower and upper layers.</param>
public sealed partial class LayerItem(int index, string title, string albedo, string normal, bool isUsed, bool isStratum, bool canMoveUp, bool canMoveDown,
    PreviewPicture? texture = null, PreviewPicture? mask = null) : ObservableObject
{
    public int Index => index;

    public string Title => title;

    public string Albedo => albedo;

    public string Normal => normal;

    public bool IsUsed => isUsed;

    public bool IsStratum => isStratum;

    public bool CanMoveUp => canMoveUp;

    public bool CanMoveDown => canMoveDown;

    public PreviewPicture? Texture => texture;

    /// <summary>
    /// A stratum's final mask as a small picture, larger while the mouse rests on it; it follows a
    /// stroke. Null for the lower layer (under everything) and the upper one (by its alpha).
    /// </summary>
    [ObservableProperty]
    public partial PreviewPicture? Mask { get; internal set; } = mask;

    /// <summary>
    /// What a screen reader says of its picture.
    /// </summary>
    public string TextureLabel => $"Texture of {Title}";

    /// <summary>
    /// What a screen reader says of its mask's picture.
    /// </summary>
    public string MaskLabel => IsStratum ? $"Final mask of {Title}" : $"{Title}: no mask";

    /// <summary>
    /// E.g. <c>grass000 · normal grass000_normals</c>, or that the stratum is unused.
    /// </summary>
    public string Textures => !IsUsed ? "unused" : Normal.Length == 0 ? Albedo : $"{Albedo} · normal {Normal}";

    public override string ToString() => Title;
}

/// <summary>
/// The terrain's layers with their textures (user story S-2): the lower layer, the strata and the
/// upper layer, as many as the <c>.scmap</c> version holds (four strata in version 53, eight after).
/// The panel lists them and adds, removes and reorders strata (their masks move with them); the
/// inspector shows the chosen layer's albedo and normal texture, chosen in the picker or typed, and
/// their scales. Every change is a command (<c>paint.addStratum</c>, <c>paint.removeStratum</c>,
/// <c>paint.moveStratum</c>, <c>paint.chooseTexture</c>, <c>paint.setLayer</c>), one step of undo.
/// The terrain shader that blends them is chosen here too (user story S-4, <c>paint.shader</c>).
/// </summary>
public sealed partial class LayerTexturesViewModel : ObservableObject
{
    private readonly Func<string, JsonObject, Task> _run;
    private ImmutableArray<TerrainLayer> _shown;
    private StratumMasks? _shownMasks;
    private MapDocument? _document;

    private readonly Func<string, string?> _textureFile;

    /// <param name="textureFile">The converted PNG of a texture's game path, for the list's pictures; null for none.</param>
    public LayerTexturesViewModel(Func<string, JsonObject, Task> run, Func<string, string?>? textureFile = null)
    {
        _run = run;
        _textureFile = textureFile ?? (_ => null);
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

    private string? _shownShader;
    private bool _showingShader;

    /// <summary>
    /// The terrain shaders to choose from (user story S-4), and the map's own when the editor does
    /// not know it.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<TerrainShaderInfo> Shaders { get; private set; } = TerrainShaderCatalog.All;

    /// <summary>
    /// The map's terrain shader; choosing another runs <c>paint.shader</c>, one step of undo.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShaderUses), nameof(ShaderSummary))]
    public partial TerrainShaderInfo? Shader { get; set; }

    /// <summary>
    /// What the chosen shader reads, a line each.
    /// </summary>
    public IReadOnlyList<string> ShaderUses => Shader?.Uses ?? [];

    /// <summary>
    /// What the chosen shader is, and where the game has it.
    /// </summary>
    public string ShaderSummary => Shader is not { } shader ? ""
        : TerrainShaderCatalog.Find(shader.Name) is null ? "Not a shader the editor knows (a mod's?): the views draw it as TTerrainXP"
        : $"{char.ToUpperInvariant(shader.Summary[0])}{shader.Summary[1..]}; {(shader.IsFaf ? "FAF's game only" : "the game's own")}";

    partial void OnShaderChanged(TerrainShaderInfo? value)
    {
        if (!_showingShader && value is not null && value.Name != _shownShader)
        {
            _ = _run("paint.shader", new JsonObject { ["name"] = value.Name });
        }
    }

    private void ShowShader(MapDocument? document)
    {
        string? name = document?.Terrain.Shader;
        if (name == _shownShader && document is not null)
        {
            return;
        }
        _shownShader = name;
        _showingShader = true;
        try
        {
            TerrainShaderInfo? known = TerrainShaderCatalog.Find(name);
            Shaders = known is null && !string.IsNullOrWhiteSpace(name)
                ? [.. TerrainShaderCatalog.All, new TerrainShaderInfo(name, "not known to the editor", 8, false, false, true, false, ["Drawn by the views as TTerrainXP"])]
                : TerrainShaderCatalog.All;
            Shader = name is null ? null : Shaders.FirstOrDefault(shader => string.Equals(shader.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _showingShader = false;
        }
    }

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

    /// <summary>
    /// Shows the textures' pictures again: the converted assets were read anew.
    /// </summary>
    public void RefreshTextures()
    {
        _shown = default;
        Refresh(_document);
    }

    public void Refresh(MapDocument? document)
    {
        _document = document;
        ShowShader(document);
        ImmutableArray<TerrainLayer> layers = document?.Terrain.Layers ?? [];
        StratumMasks? masks = document?.Terrain.StratumMasks;
        PreviewPicture? MaskOf(int index) => masks is not null && document!.Terrain.IsStratum(index) && index - 1 < masks.Layers.Length
            ? new CoveragePicture(masks.Layers[index - 1])
            : null;
        if (document is not null && layers == _shown)
        {
            if (!ReferenceEquals(masks, _shownMasks))
            {
                // a stroke or a stack's layer changes a mask: its picture follows, the list stays
                StratumMasks? before = _shownMasks;
                _shownMasks = masks;
                foreach (LayerItem item in Items)
                {
                    int at = item.Index - 1;
                    if (item.IsStratum && (before is null || at >= before.Layers.Length || at >= masks!.Layers.Length
                        || !ReferenceEquals(before.Layers[at], masks.Layers[at])))
                    {
                        item.Mask = MaskOf(item.Index);
                    }
                }
            }
            return;
        }
        _shown = layers;
        _shownMasks = masks;
        int selected = Selected?.Index ?? 1;
        int count = layers.Length;
        // the upper layer first, as they lie on each other
        Items = [.. layers.Select((layer, index) => new LayerItem(index, TitleOf(index, count),
            Path.GetFileNameWithoutExtension(layer.AlbedoPath), Path.GetFileNameWithoutExtension(layer.NormalPath),
            layer.IsUsed() || index == 0 || index == count - 1, document!.Terrain.IsStratum(index),
            document.Terrain.IsStratum(index) && document.Terrain.IsStratum(index + 1),
            document.Terrain.IsStratum(index) && document.Terrain.IsStratum(index - 1),
            layer.AlbedoPath.Length > 0 && _textureFile(layer.AlbedoPath) is string file ? new FilePicture(file) : null,
            MaskOf(index))).Reverse()];
        Summary = document is null
            ? ""
            : string.Create(CultureInfo.InvariantCulture,
                $"{layers.Skip(1).Take(count - 2).Count(layer => layer.IsUsed())} of {count - 2} strata used (version {document.Terrain.ScmapVersion}); the upper layers cover the lower ones");
        Selected = Items.FirstOrDefault(item => item.Index == selected) ?? Items.LastOrDefault();
        Layer.Refresh(Selected is null ? null : document);
    }
}
