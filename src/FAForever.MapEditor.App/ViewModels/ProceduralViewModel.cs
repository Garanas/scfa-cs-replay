using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// What the export does with a generated result.
/// </summary>
public enum ExportUse
{
    /// <summary>Nothing: it stays beside the map (walkability, the navigation mesh).</summary>
    None,

    /// <summary>Made again when out of date, unless the map turns it off (the normal map).</summary>
    WhenOutOfDate,

    /// <summary>Made on every export (the preview).</summary>
    Always,

    /// <summary>Written into the files the export writes when the map says so (the path networks);
    /// the project never holds it.</summary>
    Written,

    /// <summary>Kept among the map's own files, which the export copies (the sky cube map).</summary>
    Copied,
}

/// <summary>
/// A line of the Procedural mode's list of generated results: how it stands with its Generate
/// button (<see cref="GeneratedRow"/>), the overlay that previews it, whether it regenerates on its
/// own, and what the export does with it.
/// </summary>
/// <param name="Overlay">The overlay that shows it in the views, e.g. <c>walkability</c>; null for none.</param>
/// <param name="CanAuto">Whether it may regenerate on its own: every generator may.</param>
/// <param name="AutoWhen">When the views show it, so that it regenerates on its own (<see cref="AutoGeneration.ShowsWhen"/>); null for always.</param>
public sealed record ProceduralRow(GeneratedRow Row, string? Overlay, bool PreviewOn, bool CanAuto, bool AutoOn, ExportUse Export, bool OnExport, string? AutoWhen = null)
{
    public string Id => Row.Id;

    public string Title => Row.Title;

    public bool HasOverlay => Overlay is not null;

    public string? OverlayCommand => Overlay is null ? null : $"view.overlay.{Overlay}";

    public string PreviewLabel => $"Preview the {Title.ToLowerInvariant()} in the views";

    public string AutoLabel => $"Regenerate the {Title.ToLowerInvariant()} on its own";

    /// <summary>
    /// When it regenerates on its own, e.g. <c>Regenerates on its own while the Walkability chip is on</c>.
    /// </summary>
    public string AutoWhenText => AutoWhen is null ? "Regenerates on its own a moment after a change" : $"Regenerates on its own {AutoWhen}";

    public bool HasExportToggle => Export is ExportUse.WhenOutOfDate or ExportUse.Written;

    /// <summary>
    /// The export check box's text.
    /// </summary>
    public string OnExportText => Export == ExportUse.Written ? "Write into the map on export" : "Generate on export when out of date";

    public string OnExportLabel => Export == ExportUse.Written
        ? $"Write the {Title.ToLowerInvariant()} into the map on export"
        : $"Generate the {Title.ToLowerInvariant()} on export when out of date";

    /// <summary>
    /// What the export does with it, in words; empty when it does nothing.
    /// </summary>
    public string ExportText => Export switch
    {
        ExportUse.Always => "On export: always made",
        ExportUse.WhenOutOfDate => OnExport ? "On export: made again when out of date" : "On export: kept as it is",
        ExportUse.Written => OnExport ? "On export: written into the markers" : "On export: not written",
        ExportUse.Copied => "On export: copied with the map's files",
        _ => "",
    };

    public bool HasExportText => Export is ExportUse.Always or ExportUse.Copied;
}

/// <summary>
/// The Procedural mode's list (the panel's "Generated results"): a line per registered generator
/// with a command, in the order they were registered.
/// </summary>
public sealed partial class ProceduralViewModel : ObservableObject
{
    private readonly GenerationViewModel _generation;
    private readonly AutoGeneration _auto;
    private readonly Func<MapDocument?> _document;
    private readonly Func<string, bool> _overlayOn;
    private readonly Dictionary<string, string> _overlays = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly Dictionary<string, ExportUse> _exports = new Dictionary<string, ExportUse>(StringComparer.Ordinal);
    private readonly Dictionary<string, (Func<ProceduralSettings, bool> IsOn, Func<ProceduralSettings, bool, ProceduralSettings> Set)> _exportSettings =
        new Dictionary<string, (Func<ProceduralSettings, bool>, Func<ProceduralSettings, bool, ProceduralSettings>)>(StringComparer.Ordinal);

    /// <param name="overlayOn">Whether an overlay (by id) shows.</param>
    public ProceduralViewModel(GenerationViewModel generation, AutoGeneration auto, Func<MapDocument?> document, Func<string, bool> overlayOn)
    {
        _generation = generation;
        _auto = auto;
        _document = document;
        _overlayOn = overlayOn;
        generation.PropertyChanged += OnGenerationChanged;
    }

    /// <summary>
    /// Names the overlay that previews a generator's result in the views.
    /// </summary>
    public void PreviewWith(string generatorId, string overlayId) => _overlays[generatorId] = overlayId;

    /// <summary>
    /// Says what the export does with a generator's result.
    /// </summary>
    public void OnExport(string generatorId, ExportUse use) => _exports[generatorId] = use;

    /// <summary>
    /// Says what the export does with a generator's result, with where the map keeps whether it does
    /// (in place of <see cref="ProceduralSettings.NotOnExport"/>).
    /// </summary>
    public void OnExport(string generatorId, ExportUse use, Func<ProceduralSettings, bool> isOn, Func<ProceduralSettings, bool, ProceduralSettings> set)
    {
        _exports[generatorId] = use;
        _exportSettings[generatorId] = (isOn, set);
    }

    /// <summary>
    /// Whether the export makes or writes a generator's result, as the map keeps it.
    /// </summary>
    public bool IsOnExport(ProceduralSettings settings, string generatorId) =>
        _exportSettings.TryGetValue(generatorId, out var own) ? own.IsOn(settings) : settings.GeneratesOnExport(generatorId);

    /// <summary>
    /// The settings with the export making or writing a generator's result on or off.
    /// </summary>
    public ProceduralSettings WithOnExport(ProceduralSettings settings, string generatorId, bool on) =>
        _exportSettings.TryGetValue(generatorId, out var own) ? own.Set(settings, on) : settings.WithOnExport(generatorId, on);

    public ExportUse ExportUseOf(string generatorId) => _exports.GetValueOrDefault(generatorId);

    public IReadOnlyList<ProceduralRow> Rows { get; private set; } = [];

    /// <summary>
    /// Makes the lines again: a result kept or out of date, a setting of the map, an overlay.
    /// </summary>
    public void Refresh()
    {
        ProceduralSettings settings = _document()?.Procedural ?? ProceduralSettings.Default;
        Rows = [.. _generation.Rows.Select(row => new ProceduralRow(
            row,
            _overlays.GetValueOrDefault(row.Id),
            _overlays.TryGetValue(row.Id, out string? overlay) && _overlayOn(overlay),
            true,
            settings.RegeneratesOnItsOwn(row.Id),
            _exports.GetValueOrDefault(row.Id),
            IsOnExport(settings, row.Id),
            _auto.ShownWhen(row.Id)))];
        OnPropertyChanged(nameof(Rows));
    }

    private void OnGenerationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GenerationViewModel.Rows) or nameof(GenerationViewModel.States))
        {
            Refresh();
        }
    }
}
