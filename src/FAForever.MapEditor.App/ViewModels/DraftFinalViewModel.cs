using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A filter chip of the height layers' list (by use or by source): its text, whether it is the one
/// chosen, and the command that chooses it.
/// </summary>
public sealed partial class LayerFilterChip(string title, string label, string value, IAsyncRelayCommand choose) : ObservableObject
{
    public string Title => title;

    public string Label => label;

    /// <summary>
    /// What the command's argument says: <c>all</c>, <c>shared</c>, <c>sculpt</c>, ...
    /// </summary>
    public string Value => value;

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    public IAsyncRelayCommand Choose => choose;
}

/// <summary>
/// A mask for tools as the Terrain panel offers it for the procedural tools: ticked, it is written
/// beside the draft heightmap (<c>terrain.setDraftOut</c>).
/// </summary>
public sealed partial class DraftMaskItem(string name, bool ticked, Func<string, bool, Task> tick) : ObservableObject
{
    public string Name => name;

    public string Label => $"Write the mask {name} beside the draft";

    public bool IsTicked
    {
        get => ticked;
        set
        {
            if (value != ticked)
            {
                ticked = value;
                OnPropertyChanged();
                _ = tick(name, value);
            }
        }
    }
}

/// <summary>
/// The Terrain panel's draft and final heightmaps (wave 9A): which one the views show and brushes
/// write (<see cref="MainViewModel.ShownHeights"/>), both as previews, what the final's base is, and
/// the files for World Machine and Gaea (write the draft and ticked masks, use an output as the
/// final's base). Every change is a command (<c>terrain.switchHeights</c>, <c>terrain.writeDraft</c>,
/// <c>terrain.setDraftOut</c>, <c>terrain.useAsFinalBase</c>, <c>terrain.shareDraftBase</c>).
/// </summary>
public sealed partial class DraftFinalViewModel : ObservableObject
{
    private readonly Func<string, JsonObject, Task> _run;
    private string _maskNames = "";

    public DraftFinalViewModel(Func<string, JsonObject, Task> run)
    {
        _run = run;
    }

    /// <summary>
    /// Whether the views show the draft (else the final).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsFinal), nameof(ShownTitle))]
    public partial bool ShowsDraft { get; private set; }

    public bool ShowsFinal => !ShowsDraft;

    public string ShownTitle => ShowsDraft ? "Showing the draft" : "Showing the final";

    [ObservableProperty]
    public partial PreviewPicture? DraftPicture { get; private set; }

    [ObservableProperty]
    public partial PreviewPicture? FinalPicture { get; private set; }

    /// <summary>
    /// What the final's base is: the draft's, or a procedural tool's output read again when it changes.
    /// </summary>
    [ObservableProperty]
    public partial string BaseFacts { get; private set; } = "";

    /// <summary>
    /// The layers the shown heightmap lacks, so the list's reader knows they exist.
    /// </summary>
    [ObservableProperty]
    public partial string ElsewhereFacts { get; private set; } = "";

    [ObservableProperty]
    public partial bool HasOwnBase { get; private set; }

    [ObservableProperty]
    public partial bool HasLayers { get; private set; }

    /// <summary>
    /// Whether the draft and the ticked masks are written again whenever they change.
    /// </summary>
    [ObservableProperty]
    public partial bool WritesOnChange { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<DraftMaskItem> Masks { get; private set; } = [];

    /// <summary>
    /// What the last write of the draft did, or what it will do.
    /// </summary>
    [ObservableProperty]
    public partial string WriteFacts { get; private set; } = "Writes procedural/temp/draft-heightmap.png and .r16 (16-bit) for World Machine or Gaea.";

    public bool HasMasks => Masks.Count > 0;

    partial void OnMasksChanged(IReadOnlyList<DraftMaskItem> value) => OnPropertyChanged(nameof(HasMasks));

    /// <summary>
    /// Shows a document's heightmaps and settings.
    /// </summary>
    public void Refresh(MapDocument? document, HeightStackKind shown)
    {
        ShowsDraft = shown == HeightStackKind.Draft;
        if (document is null)
        {
            DraftPicture = null;
            FinalPicture = null;
            BaseFacts = "";
            ElsewhereFacts = "";
            HasOwnBase = false;
            HasLayers = false;
            Masks = [];
            _maskNames = "";
            return;
        }
        Heightmap final = document.Terrain.Heightmap;
        Heightmap draft = document.DraftHeightmap;
        if (FinalPicture is not HeightmapPicture { } held || !ReferenceEquals(held.Samples, final.Samples))
        {
            FinalPicture = new HeightmapPicture(final.Samples, final.Scale);
        }
        if (DraftPicture is not HeightmapPicture { } drawn || !ReferenceEquals(drawn.Samples, draft.Samples))
        {
            DraftPicture = new HeightmapPicture(draft.Samples, draft.Scale);
        }
        HasLayers = !document.HeightStack.IsEmpty;
        HasOwnBase = HeightStacks.FinalBase(document) is not null;
        BaseFacts = HeightStacks.FinalBase(document) switch
        {
            { Source: ImageSource image } own => $"The final's base is {own.Name}, read from {image.Path} again whenever the file changes. The draft keeps its own base.",
            Layer own => $"The final's base is {own.Name}; the draft keeps its own.",
            null when document.HeightStack.IsEmpty => "The draft and the final are the map's heights until a layer starts the stacks.",
            null => HeightStacks.MakeTheSame(document.HeightStack, document.DraftStack)
                ? "The final shares the draft's base and every layer: both are the same terrain."
                : "The final shares the draft's base; layers used in one only make them differ.",
        };
        LayerStack other = HeightStacks.Of(document, HeightStacks.Other(shown));
        LayerStack here = HeightStacks.Of(document, shown);
        string[] elsewhere = [.. other.Layers.Where(layer => here.Find(layer.Name) is null).Select(layer => layer.Name)];
        ElsewhereFacts = elsewhere.Length == 0 ? ""
            : string.Create(CultureInfo.InvariantCulture,
                $"Only in the {(shown == HeightStackKind.Draft ? "final" : "draft")} ({elsewhere.Length}): {string.Join(", ", elsewhere)}. Switch to see them.");
        WritesOnChange = document.Procedural.WriteDraftOnChange;
        string maskNames = string.Join("|", document.Masks.Keys) + "/" + string.Join("|", document.Procedural.DraftMasks);
        if (maskNames != _maskNames)
        {
            _maskNames = maskNames;
            Masks = [.. document.Masks.Keys.Select(name => new DraftMaskItem(name, document.Procedural.DraftMasks.Contains(name), TickAsync))];
        }
    }

    /// <summary>
    /// Says what the last write of the draft did.
    /// </summary>
    public void Wrote(string facts) => WriteFacts = facts;

    private Task TickAsync(string name, bool ticked)
    {
        JsonArray masks = [.. Masks.Where(item => item.IsTicked).Select(item => (JsonNode?)item.Name)];
        return _run("terrain.setDraftOut", new JsonObject { ["masks"] = masks });
    }

    [RelayCommand]
    private Task ShowDraft() => _run("terrain.switchHeights", new JsonObject { ["to"] = "draft" });

    [RelayCommand]
    private Task ShowFinal() => _run("terrain.switchHeights", new JsonObject { ["to"] = "final" });

    [RelayCommand]
    private Task WriteDraft() => _run("terrain.writeDraft", []);

    [RelayCommand]
    private Task ToggleWritesOnChange() => _run("terrain.setDraftOut", new JsonObject { ["auto"] = !WritesOnChange });

    [RelayCommand]
    private Task UseAsFinalBase() => _run("terrain.useAsFinalBase", []);

    [RelayCommand]
    private Task ShareDraftBase() => _run("terrain.shareDraftBase", []);
}
