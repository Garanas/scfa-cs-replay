using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// One of the six symbols in the Symbol tool's row of choices (user story N-4): its icon, its name and
/// whether it is the one in hand.
/// </summary>
public sealed partial class SketchSymbolChoice(SketchSymbolKind kind, Func<SketchSymbolKind, Task> choose) : ObservableObject
{
    public SketchSymbolKind Kind => kind;

    public string Title => kind.ToString();

    /// <summary>
    /// The key of the icon in <c>Styles/Icons.axaml</c>.
    /// </summary>
    public string Icon => $"Icon.Symbol{kind}";

    [ObservableProperty]
    public partial bool IsOn { get; internal set; }

    public string Label => $"Symbol {Title.ToLowerInvariant()}";

    public string Help => kind == SketchSymbolKind.Arrow ? "Arrow: drag to point it" : Title;

    [RelayCommand]
    private Task Choose() => choose(kind);
}

/// <summary>
/// One of the three sizes of a symbol or a label in a row of choices (user story N-4).
/// </summary>
public sealed partial class SketchSizeChoice(SketchSize size, Func<SketchSize, Task> choose, string what) : ObservableObject
{
    public SketchSize Size => size;

    public string Title => size.ToString();

    [ObservableProperty]
    public partial bool IsOn { get; internal set; }

    public string Label => $"{Title} {what}";

    [RelayCommand]
    private Task Choose() => choose(size);
}

/// <summary>
/// The label being written over the top view (user story N-4): where it goes on the map and on screen,
/// and which label of the sheet it changes when it edits one.
/// </summary>
/// <param name="Place">Where the label's text is centred, in ogrids.</param>
/// <param name="Screen">Where the text box lies over the view, in its pixels.</param>
/// <param name="Index">The label of the chosen sheet it edits; null for a new one.</param>
public sealed record SketchLabelDraft(Vector2 Place, Vector2 Screen, SketchColour Colour, float Size, string Sheet, int? Index);

public sealed partial class SketchViewModel
{
    /// <summary>
    /// The symbols being stamped, which the views show over the sketch before they are part of it (the
    /// symbol under the mouse, with its images by the symmetry); empty for none.
    /// </summary>
    public IReadOnlyList<SketchSymbol> PendingSymbols { get; private set; } = [];

    public void SetPendingSymbols(IReadOnlyList<SketchSymbol> symbols)
    {
        PendingSymbols = symbols;
        PendingChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The label being written, or null; its text is <see cref="LabelText"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWriting), nameof(LabelMargin))]
    public partial SketchLabelDraft? Draft { get; private set; }

    /// <summary>
    /// What the text box over the view holds.
    /// </summary>
    [ObservableProperty]
    public partial string LabelText { get; set; } = "";

    public bool IsWriting => Draft is not null;

    /// <summary>
    /// How wide the text box is, in pixels.
    /// </summary>
    public const double LabelBoxWidth = 220;

    /// <summary>
    /// Where the text box lies in the view's overlay: centred on the label's place.
    /// </summary>
    public Thickness LabelMargin => Draft is { } draft ? new Thickness(draft.Screen.X - LabelBoxWidth / 2, draft.Screen.Y - LabelBoxHeight / 2, 0, 0) : default;

    private const double LabelBoxHeight = 32;

    /// <summary>
    /// Raised when a label is begun, so the text box takes the keyboard.
    /// </summary>
    public event EventHandler? WritingStarted;

    /// <summary>
    /// Begins a label: the text box opens over the view. A label being written is kept first.
    /// </summary>
    public async Task BeginLabel(SketchLabelDraft draft, string text)
    {
        if (Draft is not null)
        {
            await Keep();
        }
        LabelText = text;
        Draft = draft;
        WritingStarted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Enter: the label is kept (<c>sketch.label</c>, or <c>sketch.editLabel</c> for one that was there); a new
    /// label without text is dropped.
    /// </summary>
    [RelayCommand]
    public async Task Keep()
    {
        if (Draft is not { } draft)
        {
            return;
        }
        string text = LabelText.Trim();
        Draft = null;
        LabelText = "";
        if (text.Length == 0)
        {
            return;
        }
        if (draft.Index is int index)
        {
            await _run("sketch.editLabel", new JsonObject { ["sheet"] = draft.Sheet, ["label"] = index, ["text"] = text });
        }
        else
        {
            await _run("sketch.label", new JsonObject
            {
                ["text"] = text,
                ["x"] = Math.Round(draft.Place.X, 2),
                ["z"] = Math.Round(draft.Place.Y, 2),
                ["colour"] = draft.Colour.ToString(),
                ["size"] = Math.Round(draft.Size, 2),
                ["sheet"] = draft.Sheet.Length > 0 ? draft.Sheet : null,
            });
        }
    }

    /// <summary>
    /// Escape: the label is dropped and the sketch stays as it was.
    /// </summary>
    [RelayCommand]
    public void Drop()
    {
        Draft = null;
        LabelText = "";
    }
}
