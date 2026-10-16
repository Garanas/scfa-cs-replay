using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// The map-wide decal a command names: <c>albedo</c>, <c>normal</c> or <c>shadow</c>.
/// </summary>
internal static class MapWideArgument
{
    /// <exception cref="ArgumentException">Another word.</exception>
    public static MapWideKind Of(string kind) => kind.Trim().ToLowerInvariant() switch
    {
        "albedo" => MapWideKind.Albedo,
        "normal" or "normals" => MapWideKind.Normal,
        "shadow" or "lighting" => MapWideKind.Shadow,
        _ => throw new ArgumentException($"A map-wide decal is albedo, normal or shadow, not '{kind}'"),
    };

    /// <exception cref="ArgumentException">Another word.</exception>
    public static MapWideSource SourceOf(string source) =>
        Enum.TryParse(source.Trim(), ignoreCase: true, out MapWideSource parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ArgumentException($"A map-wide decal's source is none, generated or file, not '{source}'");
}

/// <summary>
/// Sets one of the map-wide decals (user story D-1), one step of undo: where its texture comes from
/// (<c>none</c>, <c>generated</c> for the normal and shadow decals, <c>file</c>), its file, and the
/// albedo's specular. A file given alone makes the source a file; an empty specular takes it away.
/// </summary>
/// <param name="Kind"><c>albedo</c>, <c>normal</c> or <c>shadow</c>.</param>
/// <param name="File">A picture (PNG, DDS, ...): kept relative to the project when it lies in it, a path into the
/// map's own folder as it is, else copied into the map's own files (<c>env/decals/map_albedo.png</c> and the like).</param>
/// <param name="Specular">The albedo's second texture, a file; empty for none.</param>
[UndoStep]
[EditorCommand("decals.setWide", "Set a map-wide decal", Story = "D-1", Needs = Needs.Map)]
public sealed record SetMapWideDecal(string Kind, string? Source = null, string? File = null, string? Specular = null) : IRequest;

public sealed class SetMapWideDecalHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<SetMapWideDecal>
{
    public Task Handle(SetMapWideDecal request, CancellationToken cancellationToken)
    {
        MapWideKind kind = MapWideArgument.Of(request.Kind);
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        MapWideDecals wide = document.WideDecals;
        MapWideSlot slot = wide.SlotOf(kind);
        List<(string Path, byte[] Bytes)> copies = [];
        string? file = request.File?.Trim() is { Length: > 0 } given ? MapWideFiles.Keep(model, document, given, MapWideFiles.NameOf(kind), copies) : null;
        MapWideSource source = request.Source is string named ? MapWideArgument.SourceOf(named) : file is not null ? MapWideSource.File : slot.Source;
        MapWideSlot changed = source switch
        {
            MapWideSource.File => MapWideSlot.Of(file ?? slot.File ?? throw new ArgumentException($"Give the file of the map-wide {kind.ToString().ToLowerInvariant()} decal (file), or choose one")),
            MapWideSource.Generated => MapWideSlot.Generated,
            _ => MapWideSlot.None,
        };
        wide = wide.With(kind, changed);
        if (request.Specular is string specular)
        {
            if (kind != MapWideKind.Albedo)
            {
                throw new ArgumentException("Only the map-wide albedo has a specular");
            }
            wide = wide with { Specular = specular.Trim().Length == 0 ? null : MapWideFiles.Keep(model, document, specular.Trim(), MapWideFiles.SpecularName, copies) };
        }
        MapWideDecals result = wide;
        edit.Transaction.Apply(d => MapWideFiles.With(d, copies) with { WideDecals = result });
        model.Status = $"The map-wide {model.WideDecals.SlotOf(kind).Title.ToLowerInvariant()}: {changed.Source switch { MapWideSource.File => changed.File, MapWideSource.Generated => "generated at export", _ => "none" }}";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Chooses the file of a map-wide decal (user story D-1) with a file dialog, one step of undo: its
/// picture, or with <c>target</c> <c>specular</c> the albedo's specular.
/// </summary>
/// <param name="Kind"><c>albedo</c>, <c>normal</c> or <c>shadow</c>.</param>
/// <param name="Target"><c>file</c> (left out) or <c>specular</c>.</param>
/// <param name="Path">The file without asking.</param>
/// <returns>The file chosen, as the map keeps it; null when none was.</returns>
[EditorCommand("decals.chooseWideFile", "Choose a map-wide decal's file...", Story = "D-1", Needs = Needs.Map | Needs.NoStroke)]
public sealed record ChooseMapWideFile(string Kind, string? Target = null, string? Path = null) : IRequest<string?>;

public sealed class ChooseMapWideFileHandler(MainViewModel model, IEditorDialogs dialogs) : IRequestHandler<ChooseMapWideFile, string?>
{
    /// <summary>
    /// The pictures a map-wide decal can be made from.
    /// </summary>
    public static IReadOnlyList<string> Extensions { get; } = ["png", "dds", "tga", "tif", "tiff", "jpg", "jpeg", "bmp"];

    public async Task<string?> Handle(ChooseMapWideFile request, CancellationToken cancellationToken)
    {
        MapWideKind kind = MapWideArgument.Of(request.Kind);
        bool specular = string.Equals(request.Target?.Trim(), "specular", StringComparison.OrdinalIgnoreCase);
        if (specular && kind != MapWideKind.Albedo)
        {
            throw new ArgumentException("Only the map-wide albedo has a specular");
        }
        string title = model.WideDecals.SlotOf(kind).Title.ToLowerInvariant();
        string? path = request.Path ?? await dialogs.PickFileAsync(specular ? "Choose the map-wide albedo's specular" : $"Choose the map-wide {title}'s picture", Extensions, model.Map?.ProjectFolder);
        if (path is null)
        {
            model.Status = "Chose no file";
            return null;
        }
        List<(string Path, byte[] Bytes)> copies = [];
        string file = MapWideFiles.Keep(model, model.ShownDocument!, path, specular ? MapWideFiles.SpecularName : MapWideFiles.NameOf(kind), copies);
        model.EditMap(specular ? "Choose the map-wide albedo's specular" : $"Choose the map-wide {title}'s file", document => MapWideFiles.With(document, copies) with
        {
            WideDecals = specular ? document.WideDecals with { Specular = file } : document.WideDecals.With(kind, MapWideSlot.Of(file)),
        });
        model.Status = $"The map-wide {(specular ? "albedo's specular" : title)}: {file}";
        return file;
    }
}

/// <summary>
/// Sets how the map-wide shadow decal is generated (user story D-1), one step of undo; what is left out stays.
/// </summary>
/// <param name="Radius">How far around each place the terrain darkens it, in ogrids.</param>
/// <param name="Strength">How dark the crevices get (1: the mean of the horizon's sines).</param>
/// <param name="Darkest">The darkest it gets, 0 to 1.</param>
/// <param name="SunShadows">Whether what the terrain hides from the sun is darkened too.</param>
[UndoStep]
[EditorCommand("decals.setShadow", "Set the map-wide shadow decal", Story = "D-1", Needs = Needs.Map)]
public sealed record SetShadowDecal(float? Radius = null, float? Strength = null, float? Darkest = null, bool? SunShadows = null) : IRequest;

public sealed class SetShadowDecalHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<SetShadowDecal>
{
    public Task Handle(SetShadowDecal request, CancellationToken cancellationToken)
    {
        if (request.Radius is <= 0 or > 256 || request.Strength is < 0 || request.Darkest is < 0 or > 1)
        {
            throw new ArgumentException("The shadow decal's radius is above 0 and at most 256 ogrids, its strength 0 or more, its darkest 0 to 1");
        }
        edit.Transaction.Apply(document =>
        {
            ShadowDecalSettings settings = document.WideDecals.ShadowSettings;
            return document with
            {
                WideDecals = document.WideDecals with
                {
                    ShadowSettings = settings with
                    {
                        Radius = request.Radius ?? settings.Radius,
                        Strength = request.Strength ?? settings.Strength,
                        Darkest = request.Darkest ?? settings.Darkest,
                        SunShadows = request.SunShadows ?? settings.SunShadows,
                    },
                },
            };
        });
        model.Status = "Set how the shadow decal is generated";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Generates the map-wide normal decal from the final heightmap (user story D-1) on a thread of the
/// pool, to preview it in the views; the export makes it again at full size.
/// </summary>
[EditorCommand("decals.generateNormal", "Normal decal", Story = "D-1", Needs = Needs.Map)]
public sealed record GenerateNormalDecal : IRequest;

public sealed class GenerateNormalDecalHandler(MainViewModel model) : IRequestHandler<GenerateNormalDecal>
{
    public async Task Handle(GenerateNormalDecal request, CancellationToken cancellationToken)
    {
        await model.GenerateAsync(NormalDecalGenerator.GeneratorId);
        model.WideDecals.Refresh();
        model.Status = "Generated the normal decal from the heightmap; the views show it, the export makes it at full size";
    }
}

/// <summary>
/// Generates the map-wide shadow decal from the final heightmap (user story D-1) on a thread of the
/// pool, to preview it in the views; the export makes it again at full size.
/// </summary>
[EditorCommand("decals.generateShadow", "Shadow decal", Story = "D-1", Needs = Needs.Map)]
public sealed record GenerateShadowDecal : IRequest;

public sealed class GenerateShadowDecalHandler(MainViewModel model) : IRequestHandler<GenerateShadowDecal>
{
    public async Task Handle(GenerateShadowDecal request, CancellationToken cancellationToken)
    {
        await model.GenerateAsync(ShadowDecalGenerator.GeneratorId);
        model.WideDecals.Refresh();
        model.Status = "Generated the shadow decal from the heightmap; the views show it, the export makes it at full size";
    }
}

/// <summary>
/// How the map keeps a map-wide decal's file.
/// </summary>
internal static class MapWideFiles
{
    /// <summary>
    /// The name a copied specular gets among the map's own files, as Kaali (kaali.v0004) names it.
    /// </summary>
    public const string SpecularName = "map_specular";

    /// <summary>
    /// The name a slot's copied file gets among the map's own files (with its own extension), as
    /// Kaali names the decals the export writes: <c>map_albedo</c>, <c>map_normals</c>, <c>map_lighting</c>.
    /// </summary>
    public static string NameOf(MapWideKind kind) => kind switch
    {
        MapWideKind.Albedo => "map_albedo",
        MapWideKind.Normal => "map_normals",
        _ => "map_lighting",
    };

    /// <summary>
    /// A file as the map keeps it (user story D-1): a path into the map's own folder as it is, a file
    /// in the project relative to it; a file anywhere else is copied into the map's own files
    /// (<c>env/decals/&lt;name&gt;</c>, kept in the project's <c>files/</c>), so the project keeps working
    /// when the file moves or on another computer, and the map names the copy. A file that is not
    /// there is kept by its full path (the slot then says it is missing).
    /// </summary>
    /// <param name="name">The copy's name without extension: <see cref="NameOf"/>, <see cref="SpecularName"/>.</param>
    /// <param name="copies">Gets the copy to add to the map's files (<see cref="With"/>).</param>
    public static string Keep(MainViewModel model, MapDocument document, string path, string name, List<(string Path, byte[] Bytes)> copies)
    {
        if (path.StartsWith("/maps/", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }
        string? project = model.Map?.ProjectFolder;
        string full = Path.GetFullPath(path, project ?? Environment.CurrentDirectory);
        string kept = AddHeightLayerHandler.SourceOf(project, full);
        if (!Path.IsPathRooted(kept) || !File.Exists(full))
        {
            return kept;
        }
        string inMap = $"{AddMapFilesHandler.DecalsFolder}/{name}{Path.GetExtension(full).ToLowerInvariant()}";
        copies.Add((inMap, File.ReadAllBytes(full)));
        model.Logger("Files").LogInformation("Copied {File} into the map's own files as {Copy}", full, inMap);
        return $"/maps/{document.Folder}/{inMap}";
    }

    /// <summary>
    /// The document with the copies among its own files, replacing a file of the same name in any case.
    /// </summary>
    public static MapDocument With(MapDocument document, IReadOnlyList<(string Path, byte[] Bytes)> copies)
    {
        if (copies.Count == 0)
        {
            return document;
        }
        var files = document.Files;
        foreach ((string path, byte[] bytes) in copies)
        {
            if (files.Keys.FirstOrDefault(file => string.Equals(file, path, StringComparison.OrdinalIgnoreCase)) is string old)
            {
                files = files.Remove(old);
            }
            files = files.SetItem(path, System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(bytes));
        }
        return document with { Files = files };
    }
}
