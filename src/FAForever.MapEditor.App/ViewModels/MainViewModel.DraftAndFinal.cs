using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The core's side of the draft and final heightmaps (wave 9A): which one the views show and brushes
/// write, the document the views draw (<see cref="ViewDocument"/>), and the draft written for World
/// Machine and Gaea again whenever it changes.
/// </summary>
public sealed partial class MainViewModel
{
    private MapDocument? _viewOf;
    private HeightStackKind _viewHeights;
    private MapDocument? _view;
    private Terrain? _draftTerrainOf;
    private Heightmap? _draftHeightmapOf;
    private Terrain? _draftTerrain;
    private CancellationTokenSource? _draftWrite;
    private Task _draftWriting = Task.CompletedTask;
    private (TiledBuffer<ushort>? Heights, string Masks)? _draftWritten;

    /// <summary>
    /// The heightmap the views show and brushes write: the final (what the game gets) or the
    /// draft. Units, markers, props, decals and areas stay as they are; generators and the export
    /// always read the final.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsDraft))]
    public partial HeightStackKind ShownHeights { get; set; }

    public bool ShowsDraft => ShownHeights == HeightStackKind.Draft;

    /// <summary>
    /// The Terrain panel's draft and final heightmaps.
    /// </summary>
    public DraftFinalViewModel DraftFinal { get; private set; } = null!;

    /// <summary>
    /// How long after the last change the draft is written again for the procedural tools, while
    /// that is on (<see cref="ProceduralSettings.WriteDraftOnChange"/>); tests make it short.
    /// </summary>
    public TimeSpan DraftWriteDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How often the draft was written for the procedural tools since the editor started.
    /// </summary>
    public int DraftWrites { get; private set; }

    /// <summary>
    /// The heightmap shown (<see cref="ShownHeights"/>) of the shown document.
    /// </summary>
    public Heightmap? ShownHeightmap => ShownDocument is MapDocument document ? HeightStacks.HeightmapOf(document, ShownHeights) : null;

    /// <summary>
    /// The shown document as the views draw it: with the draft's heights as the terrain's while the
    /// draft is shown. Only for drawing and looking: tools and edits work on <see cref="ShownDocument"/>.
    /// The same object for the same version, so views comparing versions by reference stay cheap.
    /// </summary>
    public MapDocument? ViewDocument
    {
        get
        {
            MapDocument? document = ShownDocument;
            if (document is null || ShownHeights == HeightStackKind.Final || ReferenceEquals(document.DraftHeightmap, document.Terrain.Heightmap))
            {
                return document;
            }
            if (ReferenceEquals(document, _viewOf) && _viewHeights == ShownHeights && _view is not null)
            {
                return _view;
            }
            Heightmap draft = document.DraftHeightmap;
            if (!ReferenceEquals(document.Terrain, _draftTerrainOf) || _draftHeightmapOf is null || !ReferenceEquals(_draftHeightmapOf.Samples, draft.Samples))
            {
                _draftTerrainOf = document.Terrain;
                _draftHeightmapOf = draft;
                _draftTerrain = document.Terrain with { Heightmap = draft };
            }
            _viewOf = document;
            _viewHeights = ShownHeights;
            _view = document with { Terrain = _draftTerrain! };
            return _view;
        }
    }

    private void InitializeDraftAndFinal()
    {
        DraftFinal = new DraftFinalViewModel(RunWithAsync);
        DocumentChanged += (_, _) =>
        {
            DraftFinal.Refresh(ShownDocument, ShownHeights);
            if (_stroke is null)
            {
                FollowDraftWrite();
            }
        };
        MapChanged += (_, _) =>
        {
            if (Map is not null)
            {
                Map.Session.Heights = ShownHeights;
            }
            StopDraftWrite();
            _draftWritten = null;
            DraftFinal.Refresh(ShownDocument, ShownHeights);
        };
    }

    partial void OnShownHeightsChanged(HeightStackKind value)
    {
        if (Map is not null)
        {
            Map.Session.Heights = value;
        }
        HeightStack.Refresh(ShownDocument);
        DraftFinal.Refresh(ShownDocument, value);
        // the views draw the other heights: the top view colours the tiles that differ, the 3D view uploads them
        OverlaysChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Writes the draft heightmap and the ticked masks into the project's <c>procedural/temp/</c> on
    /// a pool thread (span <c>model.write-draft</c>).
    /// </summary>
    /// <returns>The files written, relative to the project folder.</returns>
    /// <exception cref="InvalidOperationException">No map, or a map not saved as a project yet.</exception>
    public async Task<IReadOnlyList<string>> WriteDraftAsync()
    {
        EditedMap map = Map ?? throw new InvalidOperationException("No map is open");
        string folder = map.ProjectFolder ?? throw new InvalidOperationException("Save the map as a project first: the draft is written into its procedural/temp folder");
        MapDocument document = map.Document;
        string[] masks = [.. document.Procedural.DraftMasks];
        IReadOnlyList<string> written = await Task.Run(() =>
        {
            using Performance.Span measure = Performance.Measure("model.write-draft");
            return ProceduralFiles.WriteDraft(document, folder, masks);
        });
        _draftWritten = (document.DraftHeightmap.Samples, MasksKey(document));
        DraftWrites++;
        Logger("Files").LogInformation("Wrote the draft heightmap and {Masks} masks into {Folder} for procedural tools", written.Count - 2, Path.Combine(folder, MapProjectLayout.ProceduralTemp));
        DraftFinal.Wrote(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"Wrote {string.Join(", ", written.Select(Path.GetFileName))} at {DateTime.Now:HH:mm:ss}."));
        return written;
    }

    /// <summary>
    /// Waits until a write of the draft that is waiting or running is done (tests).
    /// </summary>
    public Task WhenDraftWrittenAsync() => _draftWriting;

    /// <summary>
    /// The draft is written again a moment after it or a ticked mask changed, while that is on.
    /// </summary>
    private void FollowDraftWrite()
    {
        if (Map is not { ProjectFolder: not null } map || !map.Document.Procedural.WriteDraftOnChange)
        {
            StopDraftWrite();
            return;
        }
        MapDocument document = map.Document;
        (TiledBuffer<ushort>? Heights, string Masks) now = (document.DraftHeightmap.Samples, MasksKey(document));
        if (_draftWritten is { } written && ReferenceEquals(written.Heights, now.Heights) && written.Masks == now.Masks)
        {
            return;
        }
        StopDraftWrite();
        CancellationTokenSource cancel = new CancellationTokenSource();
        _draftWrite = cancel;
        _draftWriting = WriteDraftLaterAsync(cancel.Token);
    }

    private async Task WriteDraftLaterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(DraftWriteDelay, token);
            if (!token.IsCancellationRequested)
            {
                await WriteDraftAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // a newer change writes it
        }
        catch (Exception exception)
        {
            Logger("Files").LogError(exception, "Writing the draft for procedural tools failed: {Reason}", exception.Message);
            DraftFinal.Wrote($"Writing the draft failed: {exception.Message}");
        }
    }

    private void StopDraftWrite()
    {
        _draftWrite?.Cancel();
        _draftWrite = null;
    }

    /// <summary>
    /// The ticked masks and their grids, to tell whether they changed.
    /// </summary>
    private static string MasksKey(MapDocument document) =>
        string.Join("|", document.Procedural.DraftMasks.Select(name => name + ":" + (document.Masks.TryGetValue(name, out TiledBuffer<byte>? mask) ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(mask) : 0)));
}
