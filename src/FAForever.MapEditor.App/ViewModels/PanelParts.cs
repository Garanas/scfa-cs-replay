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

    public string Title => title;

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
}

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
        Items = parts;
        Groups = [.. parts.GroupBy(part => part.Group).Select(group => new PanelPartGroup(group.Key, [.. group]))];
        Chosen = parts[0];
        parts[0].IsChosen = true;
    }

    public IReadOnlyList<PanelPart> Items { get; }

    /// <summary>
    /// The entries by heading, in the order of their first entry.
    /// </summary>
    public IReadOnlyList<PanelPartGroup> Groups { get; }

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
    /// An entry's button in the panel.
    /// </summary>
    [RelayCommand]
    private void ChooseItem(PanelPart part) => Choose(part.Id);
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
