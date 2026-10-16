using System.ComponentModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// How the Procedural mode lists a generated result: its heading, what its inspector shows below the
/// common part, its picture, its facts and the tool it picks. A generator without one is listed with
/// the common part alone under <see cref="ProceduralListViewModel.OtherGroup"/>.
/// </summary>
/// <param name="GeneratorId">E.g. <c>walkability</c>; its entry is <c>procedural.walkability</c>.</param>
/// <param name="Group">The heading it is listed under, e.g. <c>Movement</c>.</param>
public sealed record ProceduralEntry(string GeneratorId, string Group)
{
    /// <summary>
    /// What the inspector shows below the common part (its template in
    /// <c>Views/ProceduralPanels/ProceduralPartView.axaml</c>): the path networks' view model, a
    /// map-wide decal's slot, <see cref="ProceduralWalkabilityDetails"/>, ...; null for nothing.
    /// </summary>
    public object? Details { get; init; }

    /// <summary>
    /// Its picture in the panel's entry and the inspector, from the map and its last result.
    /// </summary>
    public Func<MapDocument?, GeneratedResult?, PreviewPicture?>? Picture { get; init; }

    /// <summary>
    /// Short lines about it for the inspector, e.g. how many rules there are.
    /// </summary>
    public Func<MapDocument?, IReadOnlyList<string>>? Facts { get; init; }

    /// <summary>
    /// The entry's detail in place of how it stands, e.g. a decal taken from a file; null to say how it stands.
    /// </summary>
    public Func<string?>? Detail { get; init; }

    /// <summary>
    /// Whether it can be generated now (a map-wide decal taken from a file cannot); null: always.
    /// </summary>
    public Func<bool>? CanGenerate { get; init; }

    /// <summary>
    /// The tool choosing the entry picks, and that shows the entry when picked: e.g. <c>procedural.regions</c>.
    /// </summary>
    public string? Tool { get; init; }

    /// <summary>
    /// Whether <see cref="Details"/> has its own Generate, status and export check box (the path
    /// networks', which keep their settings first), so the common part leaves them out.
    /// </summary>
    public bool OwnGenerate { get; init; }

    /// <summary>
    /// What it is for: the entry's tool tip and the inspector's line under its title.
    /// </summary>
    public string? Help { get; init; }
}

/// <summary>
/// Walkability's choices in a Procedural inspector: the layer of units shown, what changed.
/// </summary>
public sealed record ProceduralWalkabilityDetails(MainViewModel Model);

/// <summary>
/// The navigation mesh's choices in a Procedural inspector: the layer shown, its connected regions.
/// </summary>
public sealed record ProceduralNavigationDetails(MainViewModel Model);

/// <summary>
/// The Procedural list's last entry: the export's steps and Export.
/// </summary>
public sealed record ProceduralExportPart(MainViewModel Model);

/// <summary>
/// What the inspector shows of a generated result chosen in the Procedural panel: how it stands with
/// Generate, its preview in the views, regenerating on its own, what the export does with it, what
/// it is made from, when it was made and how long that took, then its own details.
/// </summary>
public sealed partial class ProceduralPart : ObservableObject
{
    private readonly Func<string, JsonObject, Task> _run;

    public ProceduralPart(IGenerator generator, string? commandId, ProceduralEntry entry, Func<string, JsonObject, Task> run, Func<string, string?> titleOf)
    {
        Generator = generator;
        CommandId = commandId;
        Entry = entry;
        _run = run;
        string[] inputs = [.. generator.Inputs.Select(input => input.Name)];
        string[] results = [.. generator.Reads.Select(id => $"the last {(titleOf(id) ?? id).ToLowerInvariant()}")];
        ReadsText = inputs.Length + results.Length == 0 ? "Made from nothing of the map" : $"Made from {Words([.. inputs, .. results])}";
    }

    public IGenerator Generator { get; }

    public string GeneratorId => Generator.Id;

    public string PartId => ProceduralListViewModel.PartIdOf(Generator.Id);

    public string Title => Generator.Title;

    /// <summary>
    /// The command that generates it, e.g. <c>generate.walkability</c>.
    /// </summary>
    public string? CommandId { get; }

    /// <summary>
    /// How it is listed; another <see cref="ProceduralListViewModel.Describe"/> changes it.
    /// </summary>
    [ObservableProperty]
    public partial ProceduralEntry Entry { get; internal set; }

    partial void OnEntryChanged(ProceduralEntry value) => OnPropertyChanged(string.Empty);

    public object? Details => Entry.Details;

    public bool HasDetails => Entry.Details is not null;

    /// <summary>
    /// The line of the generated results with its settings; null while no map is open.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(StatusText), nameof(IsStale), nameof(CanGenerate), nameof(GenerateText),
        nameof(HasOverlay), nameof(PreviewOn), nameof(CanAuto), nameof(AutoOn), nameof(AutoWhenText), nameof(HasExportToggle), nameof(OnExport),
        nameof(OnExportText), nameof(ExportText), nameof(HasExportText), nameof(ShowsExportToggle))]
    public partial ProceduralRow? Row { get; internal set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPicture))]
    public partial PreviewPicture? Picture { get; internal set; }

    public bool HasPicture => Picture is not null;

    /// <summary>
    /// When it was made in this session, how long it took and how.
    /// </summary>
    [ObservableProperty]
    public partial string MadeText { get; internal set; } = "Not made in this session";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFacts))]
    public partial IReadOnlyList<string> Facts { get; internal set; } = [];

    public bool HasFacts => Facts.Count > 0;

    /// <summary>
    /// E.g. <c>Made from heightmap, terrain types and the last walkability</c>.
    /// </summary>
    public string ReadsText { get; }

    /// <summary>
    /// How it stands in one or two words: <c>Missing</c>, <c>Out of date</c>, <c>Current</c>, <c>Generating</c>.
    /// </summary>
    public string StateText => Row?.Row switch
    {
        null => "No map",
        { IsRunning: true } => "Generating",
        { State: GeneratedStatus.Missing } => "Missing",
        { State: GeneratedStatus.Stale } => "Out of date",
        _ => "Current",
    };

    /// <summary>
    /// How it stands in full, e.g. <c>Out of date: heightmap changed</c>.
    /// </summary>
    public string StatusText => Row?.Row.Status ?? "Open a map to generate from it";

    public bool IsStale => Row?.Row.IsStale == true;

    public bool CanGenerate => Row is not null && !Row.Row.IsRunning && (Entry.CanGenerate?.Invoke() ?? true);

    public string GenerateText => Row?.Row.ButtonText ?? "Generate";

    /// <summary>
    /// The panel entry's Generate.
    /// </summary>
    public string GenerateLabel => $"Generate the {Title.ToLowerInvariant()}";

    /// <summary>
    /// The inspector's Generate.
    /// </summary>
    public string InspectorGenerateLabel => $"Generate the {Title.ToLowerInvariant()} from the inspector";

    public string PictureLabel => $"Picture of the {Title.ToLowerInvariant()}";

    public bool ShowsGenerate => !Entry.OwnGenerate;

    public bool HasOverlay => Row?.HasOverlay == true;

    public bool PreviewOn => Row?.PreviewOn == true;

    public string PreviewLabel => $"Preview the {Title.ToLowerInvariant()} in the views";

    public bool CanAuto => Row?.CanAuto == true;

    public bool AutoOn => Row?.AutoOn == true;

    public string AutoLabel => $"Regenerate the {Title.ToLowerInvariant()} on its own";

    /// <summary>
    /// When it regenerates on its own: only while the views show it.
    /// </summary>
    public string AutoWhenText => Row?.AutoWhenText ?? "";

    public string AutoWhenLabel => $"When the {Title.ToLowerInvariant()} regenerates on its own";

    public bool HasExportToggle => Row?.HasExportToggle == true;

    public bool ShowsExportToggle => HasExportToggle && !Entry.OwnGenerate;

    public bool OnExport => Row?.OnExport == true;

    public string OnExportText => Row?.OnExportText ?? "";

    public string OnExportLabel => Row?.OnExportLabel ?? "";

    public string ExportText => Row?.ExportText is { Length: > 0 } text ? text : "On export: nothing; it stays beside the map";

    public bool HasExportText => Row is not null && !HasExportToggle;

    public string WhatItReadsLabel => $"What the {Title.ToLowerInvariant()} is made from";

    public string WhenMadeLabel => $"When the {Title.ToLowerInvariant()} was made";

    public string StatusLabel => $"How the {Title.ToLowerInvariant()} stands";

    [RelayCommand]
    private Task Generate() => CommandId is null ? Task.CompletedTask : _run(CommandId, new JsonObject());

    [RelayCommand]
    private Task TogglePreview() => Row?.OverlayCommand is string command ? _run(command, new JsonObject()) : Task.CompletedTask;

    [RelayCommand]
    private Task ToggleAuto() => _run("generate.auto", new JsonObject { ["id"] = GeneratorId });

    [RelayCommand]
    private Task ToggleOnExport() => _run("generate.onExport", new JsonObject { ["id"] = GeneratorId });

    /// <summary>
    /// What new maps of the workspace do with this result, e.g. <c>New maps of this workspace
    /// regenerate the walkability on their own</c>; empty without a workspace.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkspaceDefault))]
    public partial string WorkspaceDefaultText { get; internal set; } = "";

    public bool HasWorkspaceDefault => WorkspaceDefaultText.Length > 0;

    /// <summary>
    /// Whether the open map makes the workspace's choices for this result: Use as workspace default
    /// would change nothing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseAsWorkspaceDefault))]
    public partial bool IsWorkspaceDefault { get; internal set; }

    public bool CanUseAsWorkspaceDefault => HasWorkspaceDefault && !IsWorkspaceDefault;

    public string UseAsWorkspaceDefaultLabel => $"Use the {Title.ToLowerInvariant()} choices of this map as the workspace default";

    public string UseAsWorkspaceDefaultHelp => $"New and imported maps of the workspace then regenerate the {Title.ToLowerInvariant()} on their own, or not, as this map does"
        + (HasExportToggle ? ", and make it on export, or not" : "") + "; the other results keep their defaults and projects keep their own";

    public string WorkspaceDefaultLabel => $"The workspace's default for the {Title.ToLowerInvariant()}";

    [RelayCommand]
    private Task UseAsWorkspaceDefault() => _run("generate.useAsWorkspaceDefault", new JsonObject { ["id"] = GeneratorId });

    private static string Words(IReadOnlyList<string> words) => words.Count == 1 ? words[0] : $"{string.Join(", ", words.Take(words.Count - 1))} and {words[^1]}";
}

/// <summary>
/// An entry of the Procedural panel: its part of the list (chosen, its detail) and, for a generated
/// result, what it shows (picture, Generate).
/// </summary>
public sealed record ProceduralEntryRow(PanelPart Part, ProceduralPart? Result, PanelParts Parts)
{
    public bool IsResult => Result is not null;
}

/// <summary>
/// The entries of the Procedural panel under one heading.
/// </summary>
public sealed record ProceduralEntryGroup(string Title, IReadOnlyList<ProceduralEntryRow> Rows);

/// <summary>
/// The Procedural mode's list (the panel lists, the inspector edits): an entry per generated result,
/// under headings, and the export's steps last. Each entry says how its result stands, shows a
/// picture where there is one and has Generate; the inspector shows the chosen one
/// (<see cref="ProceduralPart"/>). A generator registered with a command
/// (<see cref="GenerationViewModel.Register"/>) is listed by itself, also after the list was made;
/// <see cref="Describe"/> gives it a heading, details, a picture and a tool.
/// </summary>
public sealed partial class ProceduralListViewModel : ObservableObject
{
    public const string Prefix = "procedural.";
    public const string ExportPart = "procedural.export";

    /// <summary>
    /// The heading of a result that was not described.
    /// </summary>
    public const string OtherGroup = "Other";

    private readonly GenerationViewModel _generation;
    private readonly ProceduralViewModel _procedural;
    private readonly Func<MapDocument?> _document;
    private readonly Func<string, JsonObject, Task> _run;
    private readonly List<ProceduralEntry> _described = [];
    private readonly ExportViewModel _export;

    public ProceduralListViewModel(GenerationViewModel generation, ProceduralViewModel procedural, ExportViewModel export, Func<MapDocument?> document,
        Func<string, JsonObject, Task> run, ProceduralExportPart exportPart)
    {
        _generation = generation;
        _procedural = procedural;
        _export = export;
        _document = document;
        _run = run;
        Parts = new PanelParts([new PanelPart(ExportPart, "Export", "Export", exportPart,
            "The steps the export runs, in order, and how each stands; Export writes the map folder into the workspace's vault")]);
        Parts.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PanelParts.Groups))
            {
                MakeGroups();
            }
        };
        generation.Registered += generator => Add(generator, null);
        procedural.PropertyChanged += OnProceduralChanged;
        export.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ExportViewModel.Lines) or nameof(ExportViewModel.IsRunning) or nameof(ExportViewModel.LastFolder))
            {
                RefreshExport();
            }
        };
        foreach (IGenerator generator in generation.Listed)
        {
            Add(generator, null);
        }
        MakeGroups();
        RefreshExport();
    }

    public static string PartIdOf(string generatorId) => Prefix + generatorId;

    /// <summary>
    /// The entries; <c>view.inspectPart</c> chooses one by id (<c>procedural.walkability</c>).
    /// </summary>
    public PanelParts Parts { get; }

    /// <summary>
    /// What new maps of the workspace do with a result and whether the open map does the same (each
    /// entry's line above Use as workspace default); null without a workspace.
    /// </summary>
    public Func<string, (string Text, bool Same)?>? WorkspaceDefault { get; set; }

    /// <summary>
    /// The panel's entries under their headings.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<ProceduralEntryGroup> Groups { get; private set; } = [];

    /// <summary>
    /// The generated results as listed.
    /// </summary>
    public IEnumerable<ProceduralPart> Results => Parts.Items.Select(part => part.Content).OfType<ProceduralPart>();

    public ProceduralPart? PartOf(string generatorId) => Results.FirstOrDefault(part => part.GeneratorId == generatorId);

    /// <summary>
    /// Says how a generated result is listed: its heading, details, picture, tool. The entries are
    /// listed in the order they were described, those not described after them, the export last.
    /// </summary>
    public void Describe(ProceduralEntry entry)
    {
        _described.RemoveAll(described => described.GeneratorId == entry.GeneratorId);
        _described.Add(entry);
        if (PartOf(entry.GeneratorId) is ProceduralPart existing)
        {
            if (existing.Entry.Group == entry.Group)
            {
                existing.Entry = entry;
                Refresh();
                return;
            }
            // under another heading: listed again in its place
            Parts.Remove(existing.PartId);
        }
        if (_generation.Results.Find(entry.GeneratorId) is IGenerator generator && _generation.CommandOf(entry.GeneratorId) is not null)
        {
            Add(generator, entry);
        }
    }

    private void Add(IGenerator generator, ProceduralEntry? entry)
    {
        if (PartOf(generator.Id) is not null)
        {
            return;
        }
        entry ??= _described.FirstOrDefault(described => described.GeneratorId == generator.Id) ?? new ProceduralEntry(generator.Id, OtherGroup);
        ProceduralPart result = new ProceduralPart(generator, _generation.CommandOf(generator.Id), entry, _run, id => _generation.Results.Find(id)?.Title);
        PanelPart part = new PanelPart(result.PartId, generator.Title, entry.Group, result, entry.Help ?? $"How the {generator.Title.ToLowerInvariant()} stands, Generate, and what it is made from");
        Parts.Insert(PlaceOf(entry), part);
        Refresh(result);
    }

    /// <summary>
    /// Where a new entry goes: after the described ones before it, the undescribed after the
    /// described, the export always last.
    /// </summary>
    private int PlaceOf(ProceduralEntry entry)
    {
        int order = _described.FindIndex(described => described.GeneratorId == entry.GeneratorId);
        int index = 0;
        foreach (PanelPart part in Parts.Items)
        {
            if (part.Content is not ProceduralPart listed)
            {
                break;
            }
            int listedOrder = _described.FindIndex(described => described.GeneratorId == listed.GeneratorId);
            bool before = order < 0 ? true : listedOrder >= 0 && listedOrder < order;
            if (!before)
            {
                break;
            }
            index++;
        }
        return index;
    }

    private void MakeGroups() =>
        Groups = [.. Parts.Groups.Select(group => new ProceduralEntryGroup(group.Title, [.. group.Parts.Select(part => new ProceduralEntryRow(part, part.Content as ProceduralPart, Parts))]))];

    private void OnProceduralChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProceduralViewModel.Rows))
        {
            Refresh();
        }
    }

    /// <summary>
    /// Shows every entry as the map and the results stand: after every step and generation.
    /// </summary>
    public void Refresh()
    {
        using Diagnostics.Performance.Span measure = Diagnostics.Performance.Measure("model.procedural-list");
        foreach (ProceduralPart result in Results)
        {
            Refresh(result);
        }
    }

    private void Refresh(ProceduralPart result)
    {
        MapDocument? document = _document();
        result.Row = _procedural.Rows.FirstOrDefault(row => row.Id == result.GeneratorId);
        result.Picture = result.Entry.Picture?.Invoke(document, document is null ? null : _generation.Results.ResultOf(result.GeneratorId));
        result.Facts = result.Entry.Facts?.Invoke(document) ?? [];
        (string Text, bool Same)? workspaceDefault = WorkspaceDefault?.Invoke(result.GeneratorId);
        result.WorkspaceDefaultText = workspaceDefault?.Text ?? "";
        result.IsWorkspaceDefault = workspaceDefault?.Same ?? false;
        result.MadeText = _generation.TimingOf(result.GeneratorId)?.Text
            ?? (result.Row?.Row.State == GeneratedStatus.Missing || result.Row is null ? "Not made in this session" : "Taken from the map as it was opened");
        if (Parts.Find(result.PartId) is PanelPart part)
        {
            part.Detail = result.Entry.Detail?.Invoke() ?? result.StateText;
        }
    }

    private void RefreshExport()
    {
        if (Parts.Find(ExportPart) is not PanelPart part)
        {
            return;
        }
        part.Detail = _export.IsRunning ? "Running"
            : _export.Lines.Any(line => line.IsFailed) ? "Failed"
            : _export.LastFolder is not null ? "Exported"
            : _export.Steps.Count == 1 ? "1 step" : $"{_export.Steps.Count} steps";
    }
}
