using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// How a click or a box changes the selection.
/// </summary>
public enum SelectionChange
{
    /// <summary>
    /// What it picks becomes the selection.
    /// </summary>
    Replace,

    /// <summary>
    /// What it picks is added (shift).
    /// </summary>
    Add,

    /// <summary>
    /// What it picks is taken out when selected, else added (control).
    /// </summary>
    Toggle,
}

/// <summary>
/// The things selected on the map (user story F-11): markers, props, decals, units and areas, by
/// <see cref="ObjectRef"/>, in the order they were selected; what was copied; and the inspector's
/// view of them (<see cref="Inspector"/>).
/// </summary>
/// <remarks>
/// It holds references, never the things: a thing that an undo or a reload takes away drops out
/// (<see cref="Prune"/>), and one that moves stays selected. The editor's core
/// (<c>MainViewModel.Selection.cs</c>) picks with the mouse and edits the selection through commands.
/// </remarks>
public sealed partial class SelectionViewModel : ObservableObject
{
    private ImmutableArray<ObjectRef> _items = [];
    private HashSet<ObjectRef> _set = [];

    /// <param name="run">Runs a command with its arguments, for the inspector's fields.</param>
    public SelectionViewModel(Func<string, System.Text.Json.Nodes.JsonObject, Task> run)
    {
        Inspector = new SelectionInspector(this, run);
    }

    /// <summary>
    /// What is selected, in the order it was selected.
    /// </summary>
    public ImmutableArray<ObjectRef> Items => _items;

    public int Count => _items.Length;

    public bool IsEmpty => _items.IsEmpty;

    public bool HasAny => !_items.IsEmpty;

    /// <summary>
    /// The inspector's part for the selection: its shared fields and a section per kind.
    /// </summary>
    public SelectionInspector Inspector { get; }

    /// <summary>
    /// What <c>edit.copy</c> copied last, for <c>edit.paste</c>; null before anything was copied.
    /// </summary>
    public ObjectClipboard? Clipboard { get; set; }

    /// <summary>
    /// Raised after the selection changed.
    /// </summary>
    public event EventHandler? Changed;

    public bool Contains(ObjectRef thing) => _set.Contains(thing);

    /// <summary>
    /// Selects what is given, in one of the ways a click does.
    /// </summary>
    public void Apply(IEnumerable<ObjectRef> things, SelectionChange change)
    {
        ObjectRef[] given = [.. things.Distinct()];
        switch (change)
        {
            case SelectionChange.Replace:
                Set(given);
                break;
            case SelectionChange.Add:
                Set([.. _items, .. given.Where(thing => !_set.Contains(thing))]);
                break;
            default:
                HashSet<ObjectRef> toggled = [.. given];
                Set([.. _items.Where(thing => !toggled.Contains(thing)), .. given.Where(thing => !_set.Contains(thing))]);
                break;
        }
    }

    public void Set(IEnumerable<ObjectRef> things)
    {
        ImmutableArray<ObjectRef> items = [.. things.Distinct()];
        if (items.SequenceEqual(_items))
        {
            return;
        }
        _items = items;
        _set = [.. items];
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasAny));
        OnPropertyChanged(nameof(Summary));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear() => Set([]);

    /// <summary>
    /// Drops what the document no longer has (an undo took it away), keeping the rest.
    /// </summary>
    public void Prune(MapDocument? document)
    {
        if (_items.IsEmpty)
        {
            return;
        }
        if (document is null)
        {
            Clear();
            return;
        }
        if (_items.Any(thing => !document.Exists(thing)))
        {
            Set(_items.Where(document.Exists));
        }
    }

    /// <summary>
    /// How many of each kind are selected, e.g. <c>2 markers, 1 prop</c>; empty for none.
    /// </summary>
    public string Summary => string.Join(", ", _items
        .GroupBy(thing => thing.Kind)
        .OrderBy(group => group.Key)
        .Select(group => Plural(group.Count(), group.Key)));

    /// <summary>
    /// <c>1 marker</c>, <c>3 props</c>.
    /// </summary>
    public static string Plural(int count, ObjectKind kind) => $"{count} {kind.ToString().ToLowerInvariant()}{(count == 1 ? "" : "s")}";
}
