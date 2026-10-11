using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// An entry of a mode panel's list: the panel says what there is, and choosing an entry shows its
/// <see cref="Content"/> in the inspector, which edits it. The Water and Lighting modes list their
/// settings, maps and generators this way (<see cref="PanelParts"/>).
/// </summary>
/// <param name="id">How commands and automation name it, e.g. <c>water.foam</c> (<c>view.inspectPart</c>).</param>
/// <param name="group">The heading it is listed under in the panel, e.g. <c>Maps</c>.</param>
/// <param name="content">What the inspector shows of it: a <see cref="SettingsPart"/>, a
/// <see cref="PlannedPart"/>, or a part of its own with a template in <c>Views/MapPanels/PanelPartView.axaml</c>.</param>
public sealed partial class PanelPart(string id, string title, string group, object content, string? help = null) : ObservableObject
{
    public string Id => id;

    /// <summary>
    /// What the panel's entry says; <see cref="PanelParts.SyncGroup"/> retitles an entry that a mode
    /// keeps in step with the map (an army renamed).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label), nameof(PreviewLabel))]
    public partial string Title { get; internal set; } = title;

    public string Group => group;

    /// <summary>
    /// What the entry is for, its tool tip and the inspector's line under its title.
    /// </summary>
    public string? Help => help;

    /// <summary>
    /// What the inspector shows while it is chosen; <see cref="PanelParts.Replace"/> gives it another.
    /// </summary>
    [ObservableProperty]
    public partial object Content { get; internal set; } = content;

    /// <summary>
    /// A short line beside its title in the panel: a value of it, how many it holds.
    /// </summary>
    [ObservableProperty]
    public partial string Detail { get; set; } = "";

    [ObservableProperty]
    public partial bool IsChosen { get; internal set; }

    /// <summary>
    /// What a screen reader says of its entry: its title, and its detail when it has one.
    /// </summary>
    public string Label => Detail.Length == 0 ? Title : $"{Title}: {Detail}";

    partial void OnDetailChanged(string value) => OnPropertyChanged(nameof(Label));

    /// <summary>
    /// A small picture before its title in the panel (a water map's); null for none.
    /// </summary>
    [ObservableProperty]
    public partial Rendering.PreviewPicture? Preview { get; set; }

    /// <summary>
    /// What a screen reader says of its picture.
    /// </summary>
    public string PreviewLabel => $"Preview of {Title}";
}

/// <summary>
/// An entry of a list whose entries come and go with the map (an army, an area, a chain), as a mode
/// gives it to <see cref="PanelParts.SyncGroup"/>.
/// </summary>
/// <param name="Id">What names it whatever version of the map, e.g. <c>map.army.ARMY_1</c>.</param>
/// <param name="Content">Makes what the inspector shows of it, only when the entry is new.</param>
/// <param name="Detail">The short line beside its title; null leaves the entry's as it is.</param>
/// <param name="Help">Its tool tip and the inspector's line under its title, for a new entry.</param>
public sealed record PanelPartEntry(string Id, string Title, Func<object> Content, string? Detail = null, string? Help = null);

/// <summary>
/// The entries of a mode panel's list under one heading.
/// </summary>
public sealed record PanelPartGroup(string Title, IReadOnlyList<PanelPart> Parts);

/// <summary>
/// A mode panel's list of what there is to edit, one entry chosen at a time; the inspector shows the
/// chosen one (<c>Controls/PanelPartList</c> in the panel, <c>Views/MapPanels/PanelPartView</c> in the
/// inspector). One entry is always chosen, the first until another is.
/// </summary>
public sealed partial class PanelParts : ObservableObject
{
    public PanelParts(IReadOnlyList<PanelPart> parts)
    {
        if (parts.Count == 0)
        {
            throw new ArgumentException("A list of parts holds at least one", nameof(parts));
        }
        _items = [.. parts];
        Groups = GroupsOf(_items);
        Chosen = parts[0];
        parts[0].IsChosen = true;
    }

    private readonly List<PanelPart> _items;

    public IReadOnlyList<PanelPart> Items => _items;

    /// <summary>
    /// The entries by heading, in the order of their first entry.
    /// </summary>
    public IReadOnlyList<PanelPartGroup> Groups { get; private set; }

    private static IReadOnlyList<PanelPartGroup> GroupsOf(IEnumerable<PanelPart> parts) =>
        [.. parts.GroupBy(part => part.Group).Select(group => new PanelPartGroup(group.Key, [.. group]))];

    /// <summary>
    /// Adds an entry at a place of the list (at its end when <paramref name="index"/> is out of it), e.g.
    /// a generator registered after the list was made; the panel shows it at once.
    /// </summary>
    /// <exception cref="ArgumentException">The list has an entry of that id.</exception>
    public void Insert(int index, PanelPart part)
    {
        if (Find(part.Id) is not null)
        {
            throw new ArgumentException($"The list has a part '{part.Id}' already");
        }
        _items.Insert(index < 0 || index > _items.Count ? _items.Count : index, part);
        Groups = GroupsOf(_items);
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(Groups));
    }

    /// <summary>
    /// The entry the inspector shows.
    /// </summary>
    [ObservableProperty]
    public partial PanelPart Chosen { get; private set; }

    public PanelPart? Find(string id) => Items.FirstOrDefault(part => part.Id == id);

    /// <summary>
    /// Shows an entry in the inspector.
    /// </summary>
    /// <exception cref="ArgumentException">There is no such entry.</exception>
    public void Choose(string id)
    {
        PanelPart part = Find(id) ?? throw new ArgumentException($"There is no part '{id}' here: {string.Join(", ", Items.Select(item => item.Id))}");
        foreach (PanelPart item in Items)
        {
            item.IsChosen = item == part;
        }
        Chosen = part;
    }

    /// <summary>
    /// Takes an entry out of the list; when it was chosen, the first entry is chosen.
    /// </summary>
    /// <exception cref="ArgumentException">There is no such entry, or it is the only one.</exception>
    public void Remove(string id)
    {
        PanelPart part = Find(id) ?? throw new ArgumentException($"There is no part '{id}' here");
        if (_items.Count == 1)
        {
            throw new ArgumentException("A list of parts holds at least one");
        }
        _items.Remove(part);
        Groups = GroupsOf(_items);
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(Groups));
        if (part == Chosen)
        {
            Choose(_items[0].Id);
        }
    }

    /// <summary>
    /// Keeps the entries under one heading in step with what a mode has now (the map's armies, areas
    /// or chains, on every change of the map): entries whose id is new are added, in the order
    /// given, those whose id is gone are taken out, and those that stay keep their content and are
    /// retitled (and given the new detail, when one is given). The chosen entry stays chosen when it
    /// stays; when it went, the heading's first entry is chosen, else the list's first. Cheap when
    /// nothing changed: no entry is made and nothing is raised.
    /// </summary>
    /// <param name="group">The heading, e.g. <c>Armies</c>.</param>
    /// <param name="entries">Every entry the heading holds now; none takes the heading away.</param>
    /// <param name="afterGroup">Where a heading that has no entry yet goes: after the entries of
    /// this heading (the list's end when null or absent). A heading with entries stays where it is.</param>
    /// <returns>Whether anything changed.</returns>
    /// <exception cref="ArgumentException">Two entries have one id, an id is another heading's, or
    /// the list would be empty.</exception>
    public bool SyncGroup(string group, IReadOnlyList<PanelPartEntry> entries, string? afterGroup = null)
    {
        int first = _items.FindIndex(part => part.Group == group);
        int held = first < 0 ? 0 : _items.Count(part => part.Group == group);
        bool same = held == entries.Count;
        for (int i = 0, j = Math.Max(first, 0); same && i < entries.Count; j++)
        {
            if (_items[j].Group != group)
            {
                continue;
            }
            same = _items[j].Id == entries[i].Id;
            i++;
        }
        if (same)
        {
            bool retitled = false;
            for (int i = 0, j = Math.Max(first, 0); i < entries.Count; j++)
            {
                if (_items[j].Group == group)
                {
                    retitled |= Update(_items[j], entries[i]);
                    i++;
                }
            }
            return retitled;
        }

        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (PanelPartEntry entry in entries)
        {
            if (!ids.Add(entry.Id))
            {
                throw new ArgumentException($"Two entries of {group} have the id '{entry.Id}'");
            }
            if (Find(entry.Id) is { } other && other.Group != group)
            {
                throw new ArgumentException($"The id '{entry.Id}' is an entry of {other.Group}");
            }
        }
        if (entries.Count == 0 && held == _items.Count)
        {
            throw new ArgumentException("A list of parts holds at least one");
        }

        Dictionary<string, PanelPart> kept = _items.Where(part => part.Group == group).ToDictionary(part => part.Id, StringComparer.Ordinal);
        bool chosenHere = Chosen.Group == group;
        int at = first >= 0
            ? first - _items.Take(first).Count(part => part.Group == group)
            : afterGroup is not null && _items.FindLastIndex(part => part.Group == afterGroup) is int last and >= 0 ? last + 1 : _items.Count;
        _items.RemoveAll(part => part.Group == group);
        PanelPart[] synced = [.. entries.Select(entry =>
        {
            if (kept.TryGetValue(entry.Id, out PanelPart? part))
            {
                Update(part, entry);
                return part;
            }
            return new PanelPart(entry.Id, entry.Title, group, entry.Content(), entry.Help) { Detail = entry.Detail ?? "" };
        })];
        _items.InsertRange(Math.Min(at, _items.Count), synced);
        Groups = GroupsOf(_items);
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(Groups));
        if (chosenHere && !_items.Contains(Chosen))
        {
            Choose(synced.Length > 0 ? synced[0].Id : _items[0].Id);
        }
        return true;
    }

    private static bool Update(PanelPart part, PanelPartEntry entry)
    {
        bool changed = false;
        if (part.Title != entry.Title)
        {
            part.Title = entry.Title;
            changed = true;
        }
        if (entry.Detail is string detail && part.Detail != detail)
        {
            part.Detail = detail;
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Gives an entry another content for the inspector, e.g. a water map's layer stack in place of
    /// its brush; the inspector shows it at once when the entry is chosen.
    /// </summary>
    /// <exception cref="ArgumentException">There is no such entry.</exception>
    public void Replace(string id, object content)
    {
        PanelPart part = Find(id) ?? throw new ArgumentException($"There is no part '{id}' here");
        part.Content = content;
        if (part == Chosen)
        {
            OnPropertyChanged(nameof(Chosen));
        }
    }

    /// <summary>
    /// Raised when a person chose an entry (its button in the panel), also the one chosen already:
    /// the inspector then shows it (<see cref="MainViewModel.InspectTheMode"/>).
    /// </summary>
    public event EventHandler? Picked;

    /// <summary>
    /// An entry's button in the panel.
    /// </summary>
    [RelayCommand]
    private void ChooseItem(PanelPart part)
    {
        Choose(part.Id);
        Picked?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// An inspector part of settings fields: one or more groups, each drawn by <c>Controls/SettingList</c>.
/// </summary>
/// <param name="Caption">A line under the fields, e.g. what a tool does with them; null for none.</param>
public sealed record SettingsPart(IReadOnlyList<SettingGroup> Groups, string? Caption = null)
{
    /// <summary>
    /// Whether each group shows its title: when there are several (one alone has the part's).
    /// </summary>
    public bool ShowsTitles => Groups.Count > 1;
}

/// <summary>
/// An inspector part that is not built yet: what it will hold, and its user story (as a
/// <see cref="PlannedSection"/> says it in a panel).
/// </summary>
public sealed record PlannedPart(string Story, string Missing);
