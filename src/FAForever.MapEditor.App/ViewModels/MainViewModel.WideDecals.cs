using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The map-wide decals' side of the core (user story D-1): their three slots, the generators of the
/// normal and shadow decals, their entries in the Decals, Lighting and Procedural panels, their
/// previews in the views, and the export step that writes them.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The map-wide decals: albedo, normal and shadow.
    /// </summary>
    public MapWideDecalsViewModel WideDecals { get; private set; } = null!;

    private void InitializeWideDecals()
    {
        WideDecals = new MapWideDecalsViewModel(RunWithAsync, Generation, () => Map?.Document, () => Map?.ProjectFolder, () => Setup.Workspace);
        Generation.Register(new NormalDecalGenerator(), "decals.generateNormal");
        Generation.Register(new ShadowDecalGenerator(), "decals.generateShadow");
        static string? Unless(MapWideSlot slot) => slot.Source == MapWideSource.Generated ? null
            : slot.Source == MapWideSource.File ? "taken from a file" : "the slot is empty";
        Generation.LeaveWhenMissing(NormalDecalGenerator.GeneratorId, document => Unless(document.WideDecals.Normal));
        Generation.LeaveWhenMissing(ShadowDecalGenerator.GeneratorId, document => Unless(document.WideDecals.Shadow));
        Procedural.OnExport(NormalDecalGenerator.GeneratorId, ExportUse.Always);
        Procedural.OnExport(ShadowDecalGenerator.GeneratorId, ExportUse.Always);

        // the Lighting panel's shadow decal is the slot itself
        LightingSettings.Parts.Replace(LightingSettingsViewModel.ShadowDecalPart, WideDecals.Shadow);

        DocumentChanged += (_, _) => WideDecals.Refresh();
        MapChanged += (_, _) => WideDecals.Clear();
        WideDecals.Shadow.PropertyChanged += (_, e) =>
        {
            // the Lighting panel's entry says the shadow decal's source, as the Decals panel's does
            if (e.PropertyName == nameof(MapWideSlotViewModel.Detail) || e.PropertyName == nameof(MapWideSlotViewModel.Slot))
            {
                LightingSettings.Parts.Find(LightingSettingsViewModel.ShadowDecalPart)!.Detail = WideDecals.Shadow.Detail;
            }
        };
        Generation.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GenerationViewModel.States) or nameof(GenerationViewModel.Rows))
            {
                WideDecals.Refresh();
            }
        };
        WideDecals.PreviewChanged += (_, _) => OverlaysChanged?.Invoke(this, EventArgs.Empty);

        Export.Register(new ExportStep("wideDecals", "Write the map-wide decals", ExportStep.DecalsOrder)
        {
            Applies = run =>
            {
                MapWideDecals wide = run.Document.WideDecals;
                bool any = wide.Albedo.Source != MapWideSource.None || wide.Normal.Source != MapWideSource.None || wide.Shadow.Source != MapWideSource.None;
                run.Note = any ? null : "none chosen";
                return any;
            },
            Run = async run =>
            {
                MapDocument document = run.Document;
                (MapDocument exported, IReadOnlyList<MapWideWritten> written) = await Task.Run(() =>
                {
                    using Performance.Span measure = Performance.Measure("model.wide-decals");
                    MapDocument made = MapWideDecalsExport.Apply(document, WideDecals.ReadFile, out IReadOnlyList<MapWideWritten> list);
                    return (made, list);
                });
                run.Document = exported;
                run.Note = string.Join("; ", written.Select(item => $"{Path.GetFileName(item.Texture)} {item.How}"));
                Logger("Files").LogInformation("Wrote the map-wide decals: {Decals}", run.Note);
            },
        });
        WideDecals.Refresh();
    }

    /// <summary>
    /// The document the views draw: the map with the previews of its map-wide decals under its decals.
    /// </summary>
    public MapDocument? DrawnDocument => ShownDocument is MapDocument document ? WideDecals.Shown(document) : null;
}
