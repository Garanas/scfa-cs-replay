using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using FAForever.MapEditor.Project.Workspaces;
using ImageMagick;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// One of the three map-wide decals as the panels list it and the inspector edits it (user story
/// D-1): its source (none, generated, a file), its file and the albedo's specular, the shadow's
/// settings, a preview, how its generated result stands, and Generate now.
/// </summary>
public sealed partial class MapWideSlotViewModel : ObservableObject
{
    private readonly Func<string, JsonObject, Task> _run;
    private MapWideSlot? _shownSlot;
    private string? _shownSpecular;

    public MapWideSlotViewModel(MapWideKind kind, Func<string, JsonObject, Task> run)
    {
        Kind = kind;
        _run = run;
        const string set = "decals.setWide";
        JsonObject Fixed() => new JsonObject { ["kind"] = KindName };
        List<Setting> settings =
        [
            new ChoiceSetting("Source", set, "source", kind == MapWideKind.Albedo ? ["None", "File"] : ["Generated", "File", "None"],
                d => d.WideDecals.SlotOf(kind).Source.ToString(), run, Fixed)
            {
                Help = kind == MapWideKind.Albedo
                    ? "A colour map of your own over the whole map (World Machine's or Gaea's), or none"
                    : "Generated from the final heightmap at export, a file of your own, or none",
            },
            new TextureSetting("File", set, "file", d => d.WideDecals.SlotOf(kind).File ?? "", run, Fixed,
                () => run("decals.chooseWideFile", new JsonObject { ["kind"] = KindName }))
            {
                Help = "The picture (PNG, DDS, ...), relative to the project when it lies in it; a DDS is copied, anything else converted. Choose opens a file",
            },
        ];
        if (kind == MapWideKind.Albedo)
        {
            settings.Add(new TextureSetting("Specular", set, "specular", d => d.WideDecals.Specular ?? "", run, Fixed,
                () => run("decals.chooseWideFile", new JsonObject { ["kind"] = KindName, ["target"] = "specular" }))
            {
                Help = "The albedo decal's second texture: how much each place shines (grey); empty for none",
            });
        }
        if (kind == MapWideKind.Shadow)
        {
            const string shadow = "decals.setShadow";
            settings.Add(new NumberSetting("Radius", shadow, "radius", d => d.WideDecals.ShadowSettings.Radius, run) { Minimum = 1, Maximum = 64, HasSlider = true, Help = "How far around each place the terrain darkens it, in ogrids" });
            settings.Add(new NumberSetting("Strength", shadow, "strength", d => d.WideDecals.ShadowSettings.Strength, run) { Minimum = 0, Maximum = 4, HasSlider = true, Help = "How dark the crevices get" });
            settings.Add(new NumberSetting("Darkest", shadow, "darkest", d => d.WideDecals.ShadowSettings.Darkest, run) { Minimum = 0, Maximum = 1, HasSlider = true, Help = "The darkest the decal gets (Kaali's: 0.45)" });
            settings.Add(new BoolSetting("Sun's shadows", shadow, "sunShadows", d => d.WideDecals.ShadowSettings.SunShadows, run) { Help = "Also darken what the terrain hides from the sun" });
        }
        Settings = new SettingGroup(Title, settings);
    }

    public MapWideKind Kind { get; }

    /// <summary>
    /// How commands name it: <c>albedo</c>, <c>normal</c>, <c>shadow</c>.
    /// </summary>
    public string KindName => Kind.ToString().ToLowerInvariant();

    public string Title => Kind switch
    {
        MapWideKind.Albedo => "Albedo",
        MapWideKind.Normal => "Normal decal",
        _ => "Shadow decal",
    };

    /// <summary>
    /// What it is for: the entry's tool tip and the inspector's line under its title.
    /// </summary>
    public string Help => Kind switch
    {
        MapWideKind.Albedo => "A colour map over the whole map with its specular: an albedo decal stretched over the map at export",
        MapWideKind.Normal => "Sharper normals over the whole map, made from the final heightmap at 16 pixels an ogrid; strongest on the cliffs",
        _ => "Darkens the crevices: black with the darkness in its alpha, from the final heightmap (ambient occlusion, the sun's shadows as an option)",
    };

    /// <summary>
    /// The generator of its result, for the normal and shadow decals; null for the albedo.
    /// </summary>
    public string? GeneratorId => Kind switch
    {
        MapWideKind.Normal => NormalDecalGenerator.GeneratorId,
        MapWideKind.Shadow => ShadowDecalGenerator.GeneratorId,
        _ => null,
    };

    /// <summary>
    /// The command of its Generate now.
    /// </summary>
    public string? GenerateCommand => Kind switch
    {
        MapWideKind.Normal => "decals.generateNormal",
        MapWideKind.Shadow => "decals.generateShadow",
        _ => null,
    };

    /// <summary>
    /// Its source, the file and the shadow's settings.
    /// </summary>
    public SettingGroup Settings { get; }

    /// <summary>
    /// The slot as the map has it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate), nameof(Detail))]
    public partial MapWideSlot Slot { get; private set; } = MapWideSlot.None;

    /// <summary>
    /// Its picture: the generated result or the file, small; null for none.
    /// </summary>
    [ObservableProperty]
    public partial PreviewPicture? Preview { get; set; }

    /// <summary>
    /// How it stands: <c>Generated, up to date</c>, <c>Not generated yet: Generate now shows it</c>, <c>From albedo.png</c>.
    /// </summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GenerateText))]
    public partial bool IsGenerating { get; set; }

    /// <summary>
    /// Whether Generate now applies: a generated normal or shadow decal.
    /// </summary>
    public bool CanGenerate => GeneratorId is not null && Slot.Source == MapWideSource.Generated;

    public string GenerateText => IsGenerating ? "Generating..." : "Generate now";

    public string GenerateLabel => $"Generate the {Title.ToLowerInvariant()} now";

    /// <summary>
    /// The short line beside its entry: its source.
    /// </summary>
    public string Detail => Slot.Source switch
    {
        MapWideSource.Generated => "generated",
        MapWideSource.File => Path.GetFileName(Slot.File ?? ""),
        _ => "none",
    };

    [RelayCommand]
    private Task GenerateNow() => GenerateCommand is string command ? _run(command, []) : Task.CompletedTask;

    /// <summary>
    /// Shows the slot as a version of the map has it; true when the slot or its specular changed.
    /// </summary>
    internal bool Show(MapDocument? document)
    {
        Settings.Refresh(document);
        MapWideSlot slot = document?.WideDecals.SlotOf(Kind) ?? MapWideSlot.None;
        string? specular = Kind == MapWideKind.Albedo ? document?.WideDecals.Specular : null;
        bool changed = slot != _shownSlot || specular != _shownSpecular;
        _shownSlot = slot;
        _shownSpecular = specular;
        Slot = slot;
        return changed;
    }
}

/// <summary>
/// An entry of a list of map-wide decals in a panel: its part (chosen, its detail), the slot, and
/// the list it is chosen in.
/// </summary>
public sealed record MapWideRow(PanelPart Part, MapWideSlotViewModel Slot, PanelParts Parts)
{
    public string PreviewLabel => $"Preview of the map-wide {Slot.Title.ToLowerInvariant()}";
}

/// <summary>
/// A list of map-wide decals in a panel (<c>Views/DecalPanels/MapWideDecalsList</c>).
/// </summary>
public sealed record MapWideList(IReadOnlyList<MapWideRow> Rows, PanelParts Parts);

/// <summary>
/// The map-wide decals (user story D-1): three slots (albedo, normal, shadow), settings of the map
/// apart from its decals, turned into decals over the whole map at export
/// (<see cref="MapWideDecalsExport"/>). The Decals panel lists all three, the Lighting panel the
/// shadow and the Procedural mode the normal and shadow; the inspector edits the chosen one. The
/// generated ones are previewed in the views from their last result (at most 2048 pixels on a side;
/// the export makes them at full size), the files as they are.
/// </summary>
public sealed partial class MapWideDecalsViewModel : ObservableObject
{
    /// <summary>
    /// The prefix of the texture paths the views draw the previews by.
    /// </summary>
    public const string PreviewPath = "editor:wide/";

    // the ids of the entries (view.inspectPart); the Procedural mode's are its list's (ProceduralListViewModel)
    public const string AlbedoPart = "decals.wideAlbedo";
    public const string NormalPart = "decals.wideNormal";
    public const string ShadowPart = "decals.wideShadow";
    public const string ProceduralNormalPart = "procedural.normalDecal";
    public const string ProceduralShadowPart = "procedural.shadowDecal";

    private readonly Func<MapDocument?> _document;
    private readonly Func<string?> _projectFolder;
    private readonly Func<Workspace?> _workspace;
    private readonly GenerationViewModel _generation;
    private readonly Dictionary<string, (object Version, DecodedTexture? Picture, string? Problem)> _files = new Dictionary<string, (object, DecodedTexture?, string?)>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private (ImmutableArray<Decal> Decals, ImmutableArray<Decal> Wide, ImmutableArray<Decal> Combined)? _shown;
    private ImmutableArray<Decal> _wide = [];
    private readonly Dictionary<string, TextureSource> _textures = new Dictionary<string, TextureSource>(StringComparer.Ordinal);

    public MapWideDecalsViewModel(Func<string, JsonObject, Task> run, GenerationViewModel generation, Func<MapDocument?> document, Func<string?> projectFolder, Func<Workspace?> workspace)
    {
        _generation = generation;
        _document = document;
        _projectFolder = projectFolder;
        _workspace = workspace;
        Albedo = new MapWideSlotViewModel(MapWideKind.Albedo, run);
        Normal = new MapWideSlotViewModel(MapWideKind.Normal, run);
        Shadow = new MapWideSlotViewModel(MapWideKind.Shadow, run);
        Parts = new PanelParts(
        [
            new PanelPart(AlbedoPart, Albedo.Title, "Map-wide decals", Albedo, Albedo.Help),
            new PanelPart(NormalPart, Normal.Title, "Map-wide decals", Normal, Normal.Help),
            new PanelPart(ShadowPart, Shadow.Title, "Map-wide decals", Shadow, Shadow.Help),
        ]);
        DecalList = new MapWideList([.. Parts.Items.Select(part => new MapWideRow(part, (MapWideSlotViewModel)part.Content, Parts))], Parts);
    }

    public MapWideSlotViewModel Albedo { get; }

    public MapWideSlotViewModel Normal { get; }

    public MapWideSlotViewModel Shadow { get; }

    public IEnumerable<MapWideSlotViewModel> Slots => [Albedo, Normal, Shadow];

    /// <summary>
    /// The Decals panel's list: the three slots.
    /// </summary>
    public PanelParts Parts { get; }

    /// <summary>
    /// The Decals panel's rows.
    /// </summary>
    public MapWideList DecalList { get; }

    /// <summary>
    /// Raised when what the views draw of the map-wide decals changed.
    /// </summary>
    public event EventHandler? PreviewChanged;

    public MapWideSlotViewModel SlotOf(MapWideKind kind) => kind switch
    {
        MapWideKind.Albedo => Albedo,
        MapWideKind.Normal => Normal,
        _ => Shadow,
    };

    /// <summary>
    /// Shows the slots as the open map has them, with their results' states and previews: after
    /// every step and every generation.
    /// </summary>
    public void Refresh()
    {
        MapDocument? document = _document();
        bool changed = false;
        foreach (MapWideSlotViewModel slot in Slots)
        {
            changed |= slot.Show(document);
            slot.IsGenerating = slot.GeneratorId is string id && _generation.IsRunning(id);
            (slot.Status, slot.Preview) = StateOf(slot, document);
        }
        foreach (PanelPart part in Parts.Items)
        {
            part.Detail = ((MapWideSlotViewModel)part.Content).Detail;
        }
        ImmutableArray<Decal> wide = document is null ? [] : PreviewDecals(document);
        if (changed || !wide.SequenceEqual(_wide))
        {
            _wide = wide;
            PreviewChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Forgets the pictures of files: another map is open.
    /// </summary>
    public void Clear()
    {
        _files.Clear();
        _textures.Clear();
        _shown = null;
        Refresh();
    }

    /// <summary>
    /// How a slot stands and its picture.
    /// </summary>
    private (string Status, PreviewPicture? Preview) StateOf(MapWideSlotViewModel slot, MapDocument? document)
    {
        if (document is null)
        {
            return ("", null);
        }
        switch (slot.Slot.Source)
        {
            case MapWideSource.Generated:
            {
                GeneratedState? state = _generation.States.FirstOrDefault(s => s.Generator.Id == slot.GeneratorId);
                DecodedTexture? result = _generation.Results.ResultOf(slot.GeneratorId!)?.Value as DecodedTexture;
                string status = slot.IsGenerating ? "Generating..."
                    : state?.Status switch
                    {
                        GeneratedStatus.Current => "Generated from the final heightmap, up to date; the export makes it again at full size",
                        GeneratedStatus.Stale => $"Out of date: {string.Join(", ", state.ChangedInputs)} changed. The export makes it as the map is",
                        _ => "Not generated yet: Generate now previews it in the views; the export makes it in any case",
                    };
                return (status, result is null ? null : new TexturePicture(result, slot.Kind == MapWideKind.Normal ? TexturePicture.Normals : TexturePicture.Darkness));
            }
            case MapWideSource.File:
            {
                DecodedTexture? picture = FilePicture(document, slot.Slot.File!, out string? problem);
                return (problem ?? $"From {slot.Slot.File}", picture is null ? null : new TexturePicture(picture, slot.Kind == MapWideKind.Shadow ? TexturePicture.Darkness : slot.Kind == MapWideKind.Normal ? TexturePicture.Normals : TexturePicture.Colour));
            }
            default:
                return (slot.Kind == MapWideKind.Albedo ? "None: choose a file to lay a colour map over the whole map" : "None: the export writes no such decal", null);
        }
    }

    /// <summary>
    /// A slot's file, read small (at most 1024 on a side) once per time it was written; null when it cannot be read.
    /// </summary>
    private DecodedTexture? FilePicture(MapDocument document, string file, out string? problem)
    {
        problem = null;
        string key;
        object version;
        Func<byte[]> read;
        if (MapWideDecalsExport.OwnFile(document, file) is string own)
        {
            if (!document.Files.TryGetValue(own, out ImmutableArray<byte> bytes))
            {
                problem = $"The map's own files lack {file}";
                return null;
            }
            byte[] content = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(bytes)!;
            (key, version, read) = ("map:" + own, content, () => content);
        }
        else
        {
            string path = FullPath(file);
            if (!File.Exists(path))
            {
                problem = $"{file} is missing: choose another file";
                return null;
            }
            (key, version, read) = (path, File.GetLastWriteTimeUtc(path), () => File.ReadAllBytes(path));
        }
        if (_files.TryGetValue(key, out (object Version, DecodedTexture? Picture, string? Problem) held) && Equals(held.Version, version))
        {
            problem = held.Problem;
            return held.Picture;
        }
        (DecodedTexture? Picture, string? Problem) Decode()
        {
            try
            {
                return (Small(read(), 1024), null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or MagickException)
            {
                return (null, $"{file} cannot be read: {exception.Message}");
            }
        }
        if (_ui is null)
        {
            (DecodedTexture? picture, problem) = Decode();
            _files[key] = (version, picture, problem);
            return picture;
        }
        // a large file (an albedo of 8192) is read on the pool; the slot shows it when it is there
        if (_loading.Add(key))
        {
            _ = Task.Run(() =>
            {
                (DecodedTexture? picture, string? failed) = Decode();
                _ui.Post(_ =>
                {
                    _loading.Remove(key);
                    _files[key] = (version, picture, failed);
                    Refresh();
                }, null);
            });
        }
        problem = $"Reading {Path.GetFileName(file)}...";
        return null;
    }

    /// <summary>
    /// A slot's file as a full path: relative ones lie in the project folder.
    /// </summary>
    public string FullPath(string file) =>
        Path.IsPathRooted(file) || _projectFolder() is not string folder ? file : FilePaths.InFolder(folder, file);

    /// <summary>
    /// Reads a slot's file for the export; null when it cannot be read.
    /// </summary>
    public byte[]? ReadFile(string file)
    {
        string path = FullPath(file);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    private static DecodedTexture Small(byte[] bytes, int largest)
    {
        MagickReadSettings settings = new MagickReadSettings();
        if (bytes.Length >= 4 && bytes[0] == 'D' && bytes[1] == 'D' && bytes[2] == 'S')
        {
            settings.Format = MagickFormat.Dds;
            settings.SetDefine(MagickFormat.Dds, "skip-mipmaps", "true");
        }
        using MagickImage image = new MagickImage(bytes, settings);
        if (Math.Max(image.Width, image.Height) > largest)
        {
            image.Resize(new MagickGeometry((uint)largest, (uint)largest));
        }
        image.Alpha(AlphaOption.Set);
        using IPixelCollection<ushort> pixels = image.GetPixels();
        return new DecodedTexture((int)image.Width, (int)image.Height, pixels.ToByteArray(PixelMapping.RGBA)!);
    }

    /// <summary>
    /// The map-wide decals as automation reads them: each slot's source, file, status and preview,
    /// the shadow's settings, the decals the views draw for them, and the entries chosen.
    /// </summary>
    public System.Text.Json.Nodes.JsonObject Describe()
    {
        MapDocument? document = _document();
        return new System.Text.Json.Nodes.JsonObject
        {
            ["slots"] = new System.Text.Json.Nodes.JsonArray([.. Slots.Select(slot => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
            {
                ["kind"] = slot.KindName,
                ["source"] = slot.Slot.Source.ToString(),
                ["file"] = slot.Slot.File,
                ["status"] = slot.Status,
                ["canGenerate"] = slot.CanGenerate,
                ["generating"] = slot.IsGenerating,
                ["preview"] = slot.Preview is TexturePicture picture ? $"{picture.Texture.Width}x{picture.Texture.Height}" : null,
            })]),
            ["specular"] = document?.WideDecals.Specular,
            ["shadow"] = document is null ? null : new System.Text.Json.Nodes.JsonObject
            {
                ["radius"] = document.WideDecals.ShadowSettings.Radius,
                ["strength"] = document.WideDecals.ShadowSettings.Strength,
                ["darkest"] = document.WideDecals.ShadowSettings.Darkest,
                ["sunShadows"] = document.WideDecals.ShadowSettings.SunShadows,
            },
            ["drawn"] = new System.Text.Json.Nodes.JsonArray([.. _wide.Select(decal => (System.Text.Json.Nodes.JsonNode)System.Text.Json.Nodes.JsonValue.Create($"{decal.Type}: {decal.Textures[0]}"))]),
            ["chosen"] = Parts.Chosen.Id,
        };
    }

    #region The views

    /// <summary>
    /// The decals the views draw for the map-wide slots, as the export writes them (albedo, shadow,
    /// normals, under all others): the generated ones from their last result, files as they are.
    /// They have ids below 0, so no selection or group meets them.
    /// </summary>
    private ImmutableArray<Decal> PreviewDecals(MapDocument document)
    {
        _textures.Clear();
        List<Decal> decals = [];
        Vector3 corner = new Vector3(0, document.Terrain.Heightmap.HeightAt(0, 0), 0);
        Vector3 scale = new Vector3(document.Width, document.Width, document.Height);
        void Add(MapWideSlotViewModel slot, DecalType type)
        {
            string? texture = TextureOf(document, slot);
            if (texture is not null)
            {
                decals.Add(new Decal
                {
                    Id = -1 - (int)slot.Kind,
                    Type = type,
                    Textures = [texture, ""],
                    Scale = scale,
                    Position = corner,
                    CutOffLod = MapWideDecalsExport.CutOffLod,
                    OwnerArmy = -1,
                });
            }
        }
        Add(Albedo, DecalType.Albedo);
        Add(Shadow, DecalType.Albedo);
        Add(Normal, DecalType.Normals);
        return [.. decals];
    }

    /// <summary>
    /// The texture path a slot's preview decal names: the map's own file as it is, else one of the
    /// editor's (<see cref="PreviewPath"/>) whose source <see cref="TextureOf(string)"/> gives.
    /// </summary>
    private string? TextureOf(MapDocument document, MapWideSlotViewModel slot)
    {
        string path = PreviewPath + slot.KindName;
        switch (slot.Slot.Source)
        {
            case MapWideSource.Generated when _generation.Results.ResultOf(slot.GeneratorId!)?.Value is DecodedTexture result:
                _textures[path] = new TextureSource(path, result, () => result);
                return path;
            case MapWideSource.File when slot.Slot.File is string file:
                if (MapWideDecalsExport.OwnFile(document, file) is not null)
                {
                    return "/" + file.Replace('\\', '/').TrimStart('/');
                }
                string full = FullPath(file);
                if (!File.Exists(full))
                {
                    return null;
                }
                DateTime written = File.GetLastWriteTimeUtc(full);
                _textures[path] = new TextureSource(path + "|" + full.ToLowerInvariant(), written, () => Small(File.ReadAllBytes(full), 4096));
                return path;
            default:
                return null;
        }
    }

    /// <summary>
    /// The texture of a preview decal's path; null for a path that is no preview.
    /// </summary>
    public TextureSource? TextureOf(string path) =>
        path.StartsWith(PreviewPath, StringComparison.Ordinal) && _textures.TryGetValue(path, out TextureSource? source) ? source : null;

    /// <summary>
    /// The document the views draw: with the map-wide decals' previews under its decals; the same
    /// object for the same decals, so the views do not rebuild their decals every frame.
    /// </summary>
    public MapDocument Shown(MapDocument document)
    {
        if (_wide.IsEmpty)
        {
            return document;
        }
        if (_shown is not { } shown || shown.Decals != document.Decals || shown.Wide != _wide)
        {
            shown = (document.Decals, _wide, [.. _wide, .. document.Decals]);
            _shown = shown;
        }
        return document with { Decals = shown.Combined };
    }

    #endregion
}
