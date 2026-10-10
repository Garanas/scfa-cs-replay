using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// A mask for tools as the Masks panel lists it: its name, size and a preview that follows its pixels.
/// </summary>
/// <param name="size">Its pixels across and down, e.g. <c>256 × 256</c>.</param>
public sealed partial class MaskItem(string name, string size, PreviewPicture? preview) : ObservableObject
{
    public string Name => name;

    public string Size => size;

    /// <summary>
    /// The mask as a small picture, larger while the mouse rests on it.
    /// </summary>
    [ObservableProperty]
    public partial PreviewPicture? Preview { get; internal set; } = preview;

    /// <summary>
    /// What a screen reader says of its preview.
    /// </summary>
    public string PreviewLabel => $"Preview of the mask {name}";

    /// <summary>
    /// How many layers its stack has, e.g. <c>3 layers</c>; empty for its own painted layer only.
    /// </summary>
    [ObservableProperty]
    public partial string Layers { get; internal set; } = "";

    public override string ToString() => name;
}

/// <summary>
/// The masks for tools of the open map (user story K-1) as the Paint panel's "Masks for tools"
/// shows them, and the one chosen there: the masks tool paints it, and the overlay <c>mask</c>
/// shows it. The masks themselves are the document's (<see cref="MapDocument.Masks"/>); adding,
/// renaming and removing them are commands (<c>paint.addMask</c>, <c>paint.renameMask</c>,
/// <c>paint.deleteMask</c>), each one step of undo. Later features (props painted where a mask
/// is, P-3; rules, K-2) read them from the document by name.
/// </summary>
public sealed partial class MasksViewModel : ObservableObject
{
    private ImmutableSortedDictionary<string, TiledBuffer<byte>>? _shown;
    private ImmutableSortedDictionary<string, LayerStack>? _shownStacks;

    /// <summary>
    /// The masks, by name.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<MaskItem> Items { get; private set; } = [];

    /// <summary>
    /// The chosen mask's name; null when the map has none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChosenItem), nameof(HasChosen))]
    public partial string? Chosen { get; set; }

    /// <summary>
    /// The chosen mask as the list shows it, for the list's selection.
    /// </summary>
    public MaskItem? ChosenItem
    {
        get => Items.FirstOrDefault(item => item.Name == Chosen);
        set
        {
            if (value is not null)
            {
                Chosen = value.Name;
            }
        }
    }

    public bool HasChosen => Chosen is not null;

    public bool IsEmpty => Items.Count == 0;

    /// <summary>
    /// The name the panel's field holds, for a new mask or a new name of the chosen one.
    /// </summary>
    [ObservableProperty]
    public partial string NewName { get; set; } = "";

    /// <summary>
    /// Whether a new mask has half the map's size, as the stratum masks mostly have.
    /// </summary>
    [ObservableProperty]
    public partial bool Half { get; set; }

    /// <summary>
    /// Raised when another mask is chosen.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Shows the masks of a document; the chosen one stays while the document has it, else the
    /// first is chosen.
    /// </summary>
    public void Refresh(MapDocument? document)
    {
        ImmutableSortedDictionary<string, TiledBuffer<byte>>? masks = document?.Masks;
        if (ReferenceEquals(masks, _shown) && ReferenceEquals(document?.CoverageStacks, _shownStacks))
        {
            return;
        }
        bool sameNames = masks is not null && _shown is not null && masks.Keys.SequenceEqual(_shown.Keys);
        ImmutableSortedDictionary<string, TiledBuffer<byte>>? before = _shown;
        _shown = masks;
        _shownStacks = document?.CoverageStacks;
        string LayersOf(string name) => document?.CoverageStacks.TryGetValue(LayerTarget.Mask(name).Key, out LayerStack? stack) == true
            ? (stack.Count == 1 ? "1 layer" : $"{stack.Count} layers")
            : "";
        if (sameNames && masks is not null)
        {
            // a stroke changes a mask's pixels: its preview follows, the list stays
            foreach (MaskItem item in Items)
            {
                if (masks.TryGetValue(item.Name, out TiledBuffer<byte>? mask) && !ReferenceEquals(before?.GetValueOrDefault(item.Name), mask))
                {
                    item.Preview = new CoveragePicture(mask);
                }
                item.Layers = LayersOf(item.Name);
            }
        }
        if (!sameNames)
        {
            Items = masks is null ? [] : [.. masks.Select(entry => new MaskItem(entry.Key, $"{entry.Value.Width} × {entry.Value.Height}", new CoveragePicture(entry.Value)) { Layers = LayersOf(entry.Key) })];
            OnPropertyChanged(nameof(IsEmpty));
            string? chosen = Chosen is string name && masks?.ContainsKey(name) == true ? name : Items.FirstOrDefault()?.Name;
            if (chosen == Chosen)
            {
                OnPropertyChanged(nameof(ChosenItem));
            }
            Chosen = chosen;
        }
    }

    /// <summary>
    /// Chooses a mask by name.
    /// </summary>
    /// <exception cref="ArgumentException">The map has no such mask.</exception>
    public void Choose(string name) =>
        Chosen = Items.FirstOrDefault(item => item.Name == name)?.Name
            ?? Items.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Name
            ?? throw new ArgumentException($"The map has no mask '{name}'{(Items.Count == 0 ? "" : $": {string.Join(", ", Items.Select(item => item.Name))}")}");

    partial void OnChosenChanged(string? value) => Changed?.Invoke(this, EventArgs.Empty);
}
