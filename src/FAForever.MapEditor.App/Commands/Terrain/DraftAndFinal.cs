using System.Collections.Immutable;
using System.Globalization;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Switches the heightmap the views show and brushes write between the draft and the final
/// (wave 9A). Units, markers, props, decals and areas stay as they are; generators and the export
/// always read the final. Not a step of undo: it changes the view, not the map.
/// </summary>
/// <param name="To"><c>draft</c> or <c>final</c>; left out, the other one.</param>
/// <returns>The heightmap shown now: <c>draft</c> or <c>final</c>.</returns>
[EditorCommand("terrain.switchHeights", "Switch between the draft and final heightmap", Gesture = "Ctrl+Shift+D", Story = "T-9", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SwitchHeights(string? To = null) : IRequest<string>;

public sealed class SwitchHeightsHandler(MainViewModel model) : IRequestHandler<SwitchHeights, string>
{
    public Task<string> Handle(SwitchHeights request, CancellationToken cancellationToken)
    {
        HeightStackKind to = request.To?.Trim().ToLowerInvariant() switch
        {
            null => model.ShownHeights == HeightStackKind.Draft ? HeightStackKind.Final : HeightStackKind.Draft,
            "draft" => HeightStackKind.Draft,
            "final" => HeightStackKind.Final,
            _ => throw new ArgumentException($"The heightmap shown is the draft or the final, not '{request.To}'"),
        };
        model.ShownHeights = to;
        string name = to == HeightStackKind.Draft ? "draft" : "final";
        model.Status = to == HeightStackKind.Draft
            ? "Showing the draft heightmap: brushes write the draft; the export takes the final"
            : "Showing the final heightmap: what the game gets";
        return Task.FromResult(name);
    }
}

/// <summary>
/// Filters the Terrain panel's height layers by use and by source (wave 9A), as its chips do.
/// </summary>
/// <param name="Use"><c>all</c>, <c>shared</c>, <c>draft</c> (only in the draft) or <c>final</c>.</param>
/// <param name="Source"><c>all</c>, <c>sculpt</c>, <c>image</c>, <c>curve</c> or <c>mask</c>.</param>
[EditorCommand("terrain.filterLayers", "Filter the height layers", Story = "T-9", Needs = Needs.Map)]
public sealed record FilterHeightLayers(string? Use = null, string? Source = null) : IRequest;

public sealed class FilterHeightLayersHandler(MainViewModel model) : IRequestHandler<FilterHeightLayers>
{
    public Task Handle(FilterHeightLayers request, CancellationToken cancellationToken)
    {
        model.HeightStack.SetFilter(request.Use, request.Source);
        int shown = model.HeightStack.Items.Count;
        model.Status = string.Create(CultureInfo.InvariantCulture,
            $"Height layers: used in {model.HeightStack.UseFilter}, source {model.HeightStack.SourceFilter} ({shown} shown)");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Writes the draft heightmap (16-bit PNG and RAW) and the ticked masks for tools into the project's
/// <c>procedural/temp/</c> for World Machine or Gaea (wave 9A, <see cref="ProceduralFiles"/>).
/// </summary>
/// <returns>The files written, relative to the project folder.</returns>
[EditorCommand("terrain.writeDraft", "Write the draft for procedural tools", Story = "T-9", Needs = Needs.Map | Needs.NoStroke | Needs.Idle)]
public sealed record WriteDraft : IRequest<IReadOnlyList<string>>;

public sealed class WriteDraftHandler(MainViewModel model) : IRequestHandler<WriteDraft, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> Handle(WriteDraft request, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> written = await model.WriteDraftAsync();
        model.Status = string.Create(CultureInfo.InvariantCulture,
            $"Wrote the draft for procedural tools: {string.Join(", ", written)}");
        return written;
    }
}

/// <summary>
/// Chooses the masks for tools written beside the draft, and whether the draft is written again
/// whenever it changes (wave 9A); one step of undo, kept in the project.
/// </summary>
/// <param name="Masks">The masks' names (replacing the ticked ones).</param>
/// <param name="Auto">Whether the draft and those masks are written again a moment after they change.</param>
[EditorCommand("terrain.setDraftOut", "Choose what is written for procedural tools", Story = "T-9", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetDraftOut(string[]? Masks = null, bool? Auto = null) : IRequest;

public sealed class SetDraftOutHandler(MainViewModel model) : IRequestHandler<SetDraftOut>
{
    public Task Handle(SetDraftOut request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        if (request.Masks?.FirstOrDefault(name => !document.Masks.ContainsKey(name)) is string missing)
        {
            throw new ArgumentException($"The map has no mask '{missing}': {string.Join(", ", document.Masks.Keys)}");
        }
        string fields = string.Join(", ", new[] { request.Masks is null ? null : "masks", request.Auto is null ? null : "writing on change" }.OfType<string>());
        if (fields.Length == 0)
        {
            return Task.CompletedTask;
        }
        model.EditMap($"Procedural tools: {fields}", d => d with
        {
            Procedural = d.Procedural with
            {
                DraftMasks = request.Masks is string[] masks ? ImmutableSortedSet.CreateRange(StringComparer.Ordinal, masks) : d.Procedural.DraftMasks,
                WriteDraftOnChange = request.Auto ?? d.Procedural.WriteDraftOnChange,
            },
        });
        MapDocument now = model.ShownDocument!;
        model.Status = string.Create(CultureInfo.InvariantCulture,
            $"Procedural tools get the draft{(now.Procedural.DraftMasks.IsEmpty ? "" : " and " + string.Join(", ", now.Procedural.DraftMasks))}{(now.Procedural.WriteDraftOnChange ? ", written again whenever it changes" : "")}");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Makes a procedural tool's output the final's base (wave 9A): an image layer marked base, read
/// again whenever the file changes; the base the final shared with the draft stays the draft's.
/// One step of undo. The output usually lies in the project's <c>procedural/temp/</c> beside the
/// draft the tool read.
/// </summary>
/// <param name="Path">The output (PNG, <c>.r16</c>, <c>.raw</c>, TIFF); left out, the editor asks,
/// starting in <c>procedural/temp/</c>.</param>
/// <returns>The final base's name, or null when no file was chosen.</returns>
[EditorCommand("terrain.useAsFinalBase", "Use an output as the final's base...", Story = "T-9", Needs = Needs.Map | Needs.NoStroke | Needs.Idle)]
public sealed record UseAsFinalBase(string? Path = null) : IRequest<string?>;

public sealed class UseAsFinalBaseHandler(MainViewModel model, IEditorDialogs dialogs) : IRequestHandler<UseAsFinalBase, string?>
{
    public async Task<string?> Handle(UseAsFinalBase request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        string? folder = model.Map?.ProjectFolder;
        string? startIn = folder is null ? null : System.IO.Path.Combine(folder, MapProjectLayout.ProceduralTemp);
        string? path = request.Path ?? await dialogs.PickFileAsync("Use an output as the final's base", ProceduralFiles.OutputExtensions,
            startIn is not null && Directory.Exists(startIn) ? startIn : folder);
        if (path is null)
        {
            model.Status = "Chose no file";
            return null;
        }
        path = System.IO.Path.GetFullPath(folder is not null && !System.IO.Path.IsPathRooted(path) ? System.IO.Path.Combine(folder, path) : path);
        ushort[] samples = await Task.Run(() =>
        {
            using Performance.Span measure = Performance.Measure("model.import-heightmap");
            return HeightmapFiles.ReadResampled(document, path, out _);
        }, cancellationToken);
        string source = AddHeightLayerHandler.SourceOf(folder, path);
        using Performance.Span span = Performance.Measure("model.height-layers");
        model.EditMap($"Use {System.IO.Path.GetFileName(path)} as the final's base", d => HeightStacks.UseAsFinalBase(d, samples, source));
        string name = HeightStacks.FinalBase(model.ShownDocument!)?.Name ?? HeightStacks.FinalBaseName;
        if (System.IO.Path.IsPathRooted(source))
        {
            model.Logger("Files").LogWarning("The final's base {File} lies outside the project folder: it is read again when the project opens, not while it is open", path);
        }
        model.Logger("Files").LogInformation("The final's base {Layer} is read from {File}", name, path);
        model.Status = $"The final's base is {source}, read again whenever it changes";
        return name;
    }
}

/// <summary>
/// Makes the final share the draft's base again (wave 9A): the final's own base (a procedural
/// tool's output) is gone. One step of undo.
/// </summary>
[EditorCommand("terrain.shareDraftBase", "Share the draft's base with the final", Story = "T-9", Needs = Needs.Map | Needs.NoStroke)]
public sealed record ShareDraftBase : IRequest;

public sealed class ShareDraftBaseHandler(MainViewModel model) : IRequestHandler<ShareDraftBase>
{
    public Task Handle(ShareDraftBase request, CancellationToken cancellationToken)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        if (HeightStacks.FinalBase(document) is not Layer own)
        {
            model.Status = "The final shares the draft's base already";
            return Task.CompletedTask;
        }
        using Performance.Span span = Performance.Measure("model.height-layers");
        model.EditMap("Share the draft's base with the final", HeightStacks.ShareDraftBase);
        model.Status = $"The final shares the draft's base again; {own.Name} is gone";
        return Task.CompletedTask;
    }
}
