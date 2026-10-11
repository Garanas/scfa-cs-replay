using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The commands a stack control runs for one target, and how it names a layer in its labels. Each
/// command takes the layer by name in <see cref="LayerArgument"/>: <see cref="Set"/> the arguments
/// <c>name</c>, <c>combine</c>, <c>strength</c>, the clip (<see cref="ClipArgument"/>),
/// <c>visible</c> and <c>locked</c>; <see cref="Move"/> <c>by</c>; <see cref="Remove"/> nothing more.
/// </summary>
/// <param name="Word">What a layer is called in labels: <c>height layer</c>.</param>
/// <param name="ClipTitle">The clip field's label: <c>Mask</c>.</param>
public sealed record LayerStackCommands(string Set, string Move, string Remove, string Word = "layer", string ClipArgument = "mask",
    string ClipTitle = "Mask", string LayerArgument = "layer");

/// <summary>
/// One way to add a layer, as a button of the stack control: its text, label for screen readers
/// and agents, help, and the command it runs with its arguments.
/// </summary>
public sealed record LayerAddChoice(string Title, string Label, string Help, string Command, JsonObject? Arguments = null)
{
    /// <summary>
    /// Runs the choice's command (set by the stack that offers it).
    /// </summary>
    public IAsyncRelayCommand Run { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

/// <summary>
/// A layer as a stack control lists it, the highest first: its name, a preview, a summary, whether
/// it shows and whether it is locked.
/// </summary>
public sealed partial class StackLayerItem(Layer layer, int index, int count, PreviewPicture? preview, string summary, LayerStackViewModel stack) : ObservableObject
{
    private Layer _layer = layer;

    public Layer Layer => _layer;

    public string Name => _layer.Name;

    /// <summary>
    /// Its place in the stack: 0 is the lowest.
    /// </summary>
    public int Index => index;

    public bool IsBase => _layer.IsBase;

    public bool CanMoveUp => !IsBase && index < count - 1;

    public bool CanMoveDown => !IsBase && index > (stack.HasBase ? 1 : 0);

    /// <summary>
    /// The layer's values as a small picture, larger while the mouse rests on it; it follows the
    /// layer's values (a brush stroke) without the list being made again.
    /// </summary>
    [ObservableProperty]
    public partial PreviewPicture? Preview { get; private set; } = preview;

    /// <summary>
    /// Shows a new version of the same layer whose settings are as they were (its values changed).
    /// </summary>
    internal void Follow(Layer changed, PreviewPicture? picture)
    {
        _layer = changed;
        Preview = picture;
    }

    /// <summary>
    /// E.g. <c>Sculpt · add · 50 % · mask rivers</c>.
    /// </summary>
    public string Summary => summary;

    public string ShowLabel => $"Show the {stack.Commands.Word} {_layer.Name}";

    public string LockLabel => $"Lock the {stack.Commands.Word} {_layer.Name}";

    public string PreviewLabel => $"Preview of the {stack.Commands.Word} {_layer.Name}";

    /// <summary>
    /// Whether it counts; unticking hides it.
    /// </summary>
    public bool IsShown
    {
        get => _layer.Visible;
        set
        {
            if (value != _layer.Visible)
            {
                _ = stack.SetAsync(_layer.Name, new JsonObject { ["visible"] = value });
            }
        }
    }

    /// <summary>
    /// Whether edits of its values leave it alone; ticking locks it.
    /// </summary>
    public bool IsLocked
    {
        get => _layer.Locked;
        set
        {
            if (value != _layer.Locked)
            {
                _ = stack.SetAsync(_layer.Name, new JsonObject { ["locked"] = value });
            }
        }
    }

    public override string ToString() => _layer.Name;
}

/// <summary>
/// A stack of layers of the open map as the reusable stack control shows it
/// (<c>Controls/LayerStackView</c>): the layers, the highest first, each with a preview; the chosen
/// one, whose settings (name, combine, strength, clip, shown, locked) are in <see cref="Layer"/>;
/// adding by source, moving, removing. Every change is a command of the target
/// (<see cref="LayerStackCommands"/>), one step of undo; the layers themselves are the document's.
/// </summary>
/// <remarks>
/// A target gets its stack control by making one of these with its commands, add choices and the
/// stack it reads (<c>document =&gt; document.HeightStack</c>); <see cref="HeightLayersViewModel"/>
/// is the heightmap's and adds what only it has (curves, bake, reading a file again).
/// </remarks>
public partial class LayerStackViewModel : ObservableObject
{
    private static readonly Layer NoLayer = new Layer { Name = "", Source = LayerSource.Painted };

    private readonly Func<string, JsonObject, Task> _run;
    private readonly Func<MapDocument, LayerStack?> _read;
    private LayerStack? _shown;
    private string _maskNames = "";
    private string _signature = "";
    private MapDocument? _document;

    /// <summary>
    /// What the clip choice says for none.
    /// </summary>
    public const string Everywhere = "Everywhere";

    public static IReadOnlyList<string> Combines { get; } = [.. Enum.GetNames<LayerCombine>()];

    public LayerStackViewModel(Func<string, JsonObject, Task> run, Func<MapDocument, LayerStack?> read, LayerStackCommands commands, IReadOnlyList<LayerAddChoice> adds)
    {
        _run = run;
        _read = read;
        Commands = commands;
        AddChoices = [.. adds.Select(choice => choice with { Run = new AsyncRelayCommand(() => run(choice.Command, choice.Arguments?.DeepClone().AsObject() ?? [])) })];
        Layer = MakeLayerSettings([Everywhere]);
    }

    public LayerStackCommands Commands { get; }

    /// <summary>
    /// The ways to add a layer, as buttons.
    /// </summary>
    public IReadOnlyList<LayerAddChoice> AddChoices { get; }

    /// <summary>
    /// The list's label: <c>Height layers</c>.
    /// </summary>
    public string ListTitle => char.ToUpperInvariant(Commands.Word[0]) + Commands.Word[1..] + "s";

    public string MoveUpLabel => $"Move the {Commands.Word} up";

    public string MoveDownLabel => $"Move the {Commands.Word} down";

    public string RemoveLabel => $"Remove the {Commands.Word}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(HasLayers))]
    public partial IReadOnlyList<StackLayerItem> Items { get; private set; } = [];

    public bool IsEmpty => Items.Count == 0;

    public bool HasLayers => Items.Count > 0;

    /// <summary>
    /// Whether the stack has a base, which stays the lowest.
    /// </summary>
    public bool HasBase => _shown?.Layers.Any(layer => layer.IsBase) == true;

    /// <summary>
    /// The chosen layer's name; null without layers.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChosenItem), nameof(ChosenLayer), nameof(CanRemove), nameof(CanMoveUp), nameof(CanMoveDown))]
    public partial string? Chosen { get; set; }

    public StackLayerItem? ChosenItem
    {
        get => Items.FirstOrDefault(item => item.Name == Chosen);
        set
        {
            if (value is not null)
            {
                Chosen = value.Name;
            }
        }
    }

    public Layer? ChosenLayer => ChosenItem?.Layer ?? (Chosen is null ? null : _shown?.Find(Chosen));

    public bool CanRemove => ChosenItem is { IsBase: false } && (!KeepsLastLayer || Items.Count > 1);

    /// <summary>
    /// Whether the last layer stays (a grid of coverage is made by at least one).
    /// </summary>
    protected virtual bool KeepsLastLayer => false;

    public bool CanMoveUp => ChosenItem?.CanMoveUp == true;

    public bool CanMoveDown => ChosenItem?.CanMoveDown == true;

    /// <summary>
    /// The chosen layer's name, way of combining, strength, clip, visibility and lock.
    /// </summary>
    [ObservableProperty]
    public partial SettingGroup Layer { get; private set; }

    /// <summary>
    /// What the stack control says about the stack: how many layers.
    /// </summary>
    [ObservableProperty]
    public partial string Summary { get; private set; } = "";

    /// <summary>
    /// Raised when another layer is chosen.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// The document last shown, for subclasses' settings.
    /// </summary>
    protected MapDocument? Document => _document;

    partial void OnChosenChanged(string? value)
    {
        RefreshSettings();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Shows the layers of a document; the chosen one stays while the document has it, else
    /// <see cref="Preferred"/>. The list is made again only when what it shows changes (a layer's
    /// settings or values), not when nothing of the stack changed.
    /// </summary>
    public void Refresh(MapDocument? document)
    {
        _document = document;
        LayerStack? stack = document is null ? null : _read(document);
        ImmutableArray<Layer> layers = stack?.Layers ?? [];
        string maskNames = document is null ? "" : string.Join("|", ClipChoices(document));
        bool newSettings = maskNames != _maskNames;
        if (newSettings)
        {
            _maskNames = maskNames;
            Layer = MakeLayerSettings(document is null ? [Everywhere] : ClipChoices(document));
        }
        // a dab of a brush changes a layer's values, not what the list shows of it: only the previews follow
        string signature = FilterKey + "#" + string.Join("|", layers.Select(layer => layer.Name + ":" + SummaryOf(layer) + ":" + layer.Path));
        if (document is not null && stack is not null && signature == _signature)
        {
            if (!ReferenceEquals(stack, _shown))
            {
                FollowPreviews(document, stack);
            }
            _shown = stack;
            if (newSettings)
            {
                // the fields made again for other clip choices show the chosen layer at once
                RefreshSettings();
            }
            return;
        }
        _shown = stack;
        _signature = signature;
        int count = layers.Length;
        Items = [.. layers.Select((layer, index) => (Layer: layer, Index: index))
            .Where(entry => document is null || Shows(document, entry.Layer))
            .Select(entry => new StackLayerItem(entry.Layer, entry.Index, count, document is null || stack is null ? null : PreviewPicture.OfLayer(document, stack, entry.Layer),
                SummaryOf(entry.Layer), this)).Reverse()];
        Summary = SummaryOfStack(count);
        string? chosen = Chosen is string name && layers.Any(layer => layer.Name == name)
            ? name
            : Preferred(document, layers)?.Name;
        if (chosen == Chosen)
        {
            OnPropertyChanged(nameof(ChosenItem));
            OnPropertyChanged(nameof(ChosenLayer));
            OnPropertyChanged(nameof(CanRemove));
            OnPropertyChanged(nameof(CanMoveUp));
            OnPropertyChanged(nameof(CanMoveDown));
            OnChosenRefreshed();
            RefreshSettings();
        }
        Chosen = chosen;
    }

    /// <summary>
    /// Forgets what the list shows, so the next <see cref="Refresh"/> makes it again and chooses
    /// anew (another target shown whose layers look the same).
    /// </summary>
    protected void ForgetShown()
    {
        _shown = null;
        _signature = "";
    }

    /// <summary>
    /// Shows the new values of layers whose settings stayed: the items stay, their previews follow.
    /// </summary>
    private void FollowPreviews(MapDocument document, LayerStack stack)
    {
        foreach (StackLayerItem item in Items)
        {
            if (item.Index < stack.Layers.Length && stack.Layers[item.Index] is Layer layer && !ReferenceEquals(layer, item.Layer))
            {
                bool sameGrids = ReferenceEquals(layer.Values, item.Layer.Values) && ReferenceEquals(layer.Coverage, item.Layer.Coverage);
                item.Follow(layer, sameGrids ? item.Preview : PreviewPicture.OfLayer(document, stack, layer));
            }
        }
    }

    /// <summary>
    /// Whether the list shows a layer of the stack (a subclass's filters); every layer by default.
    /// </summary>
    protected virtual bool Shows(MapDocument document, Layer layer) => true;

    /// <summary>
    /// What the filters choose, so the list is made again when they change.
    /// </summary>
    protected virtual string FilterKey => "";

    /// <summary>
    /// The layer chosen when the chosen one is gone: the highest that can be painted, else the highest.
    /// </summary>
    protected virtual Layer? Preferred(MapDocument? document, ImmutableArray<Layer> layers) =>
        layers.LastOrDefault(layer => LayerSources.CanPaint(layer.Source) && !layer.Locked && layer.Visible) ?? layers.LastOrDefault();

    /// <summary>
    /// What the summary says for a stack of that many layers.
    /// </summary>
    protected virtual string SummaryOfStack(int count) =>
        count == 0 ? "" : string.Create(CultureInfo.InvariantCulture, $"{count} layers, the highest last");

    /// <summary>
    /// Called when the list is made again while the same layer stays chosen.
    /// </summary>
    protected virtual void OnChosenRefreshed()
    {
    }

    /// <summary>
    /// Chooses a layer by name (exactly, else without regard to case).
    /// </summary>
    /// <exception cref="ArgumentException">The stack has no such layer.</exception>
    public void Choose(string name) =>
        Chosen = Items.FirstOrDefault(item => item.Name == name)?.Name
            ?? Items.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Name
            ?? _shown?.Find(name)?.Name
            ?? throw new ArgumentException(Items.Count == 0
                ? $"The map has no {Commands.Word}s"
                : $"The map has no {Commands.Word} '{name}': {string.Join(", ", Items.Select(item => item.Name))}");

    /// <summary>
    /// Runs the target's set command for a layer with the given arguments.
    /// </summary>
    public Task SetAsync(string layer, JsonObject arguments)
    {
        arguments[Commands.LayerArgument] = layer;
        return _run(Commands.Set, arguments);
    }

    /// <summary>
    /// The clip choices of a document: everywhere, its masks for tools, the strata.
    /// </summary>
    public static IReadOnlyList<string> ClipChoices(MapDocument document) =>
        [Everywhere,
            .. document.Masks.Keys.Select(name => LayerStacks.MaskTitle(LayerStacks.MaskKey(name))),
            .. Enumerable.Range(1, document.Terrain.StratumMasks.Layers.Length).Select(layer => LayerStacks.MaskTitle(LayerStacks.StratumKey(layer)))];

    /// <summary>
    /// The mask key a choice names (a key, a title such as <c>mask rivers</c>, or a mask's name),
    /// or null for everywhere.
    /// </summary>
    /// <exception cref="ArgumentException">The document has no such mask.</exception>
    public static string? MaskKeyOf(MapDocument document, string choice)
    {
        choice = choice.Trim();
        if (choice.Length == 0 || choice.Equals(Everywhere, StringComparison.OrdinalIgnoreCase) || choice.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        // the one way to name a mask: any of its names (a key, a title, the rules' Layer 3: and Mask: forest) gives its key
        IEnumerable<string> keys = document.Masks.Keys.Select(LayerStacks.MaskKey)
            .Concat(Enumerable.Range(1, document.Terrain.StratumMasks.Layers.Length).Select(LayerStacks.StratumKey));
        return LayerStacks.KeyOf(document, choice)
            ?? keys.FirstOrDefault(key => LayerStacks.MaskTitle(key).Equals(choice, StringComparison.OrdinalIgnoreCase))
            ?? LayerStacks.KeyOf(document, RuleLayers.Parse(choice))
            ?? throw new ArgumentException($"The map has no mask '{choice}': {string.Join(", ", ClipChoices(document))}");
    }

    /// <summary>
    /// E.g. <c>Sculpt · add · 50 % · mask rivers</c>.
    /// </summary>
    public virtual string SummaryOf(Layer layer)
    {
        List<string> parts = [KindOf(layer)];
        if (!layer.IsBase)
        {
            parts.Add(layer.Combine.ToString().ToLowerInvariant());
        }
        if (layer.Strength < 1)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{layer.Strength * 100:0} %"));
        }
        if (layer.Clip is string clip)
        {
            parts.Add(LayerStacks.MaskTitle(clip));
        }
        if (layer.Source is MaskRefSource reference)
        {
            parts.Add("reads " + LayerStacks.MaskTitle(reference.Key));
        }
        if (layer.Source is DecalMasksSource decals)
        {
            parts.Add(DecalMasksSummary(decals));
        }
        if (layer.Curve is HeightCurve curve)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{curve.Profile.ToString().ToLowerInvariant()}, {curve.Points.Length} points"));
        }
        if (!layer.Visible)
        {
            parts.Add("hidden");
        }
        if (layer.Locked)
        {
            parts.Add("locked");
        }
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// The kind of a layer for people: <c>Base</c>, <c>Painted</c>, <c>Image</c>, <c>Mask</c>, <c>Rules</c>, <c>Curve</c>, <c>Decal masks</c>.
    /// </summary>
    public virtual string KindOf(Layer layer) => layer.IsBase ? "Base" : layer.Source switch
    {
        PaintedSource => "Painted",
        ImageSource => "Image",
        MaskRefSource => "Mask",
        RulesSource => "Rules",
        CurveSource => "Curve",
        DecalMasksSource => "Decal masks",
        _ => LayerSources.NameOf(layer.Source),
    };

    /// <summary>
    /// What a decal-masks layer draws, in words: <c>mask moss of every set</c>, <c>mask main of rock01, rock02</c>.
    /// </summary>
    public static string DecalMasksSummary(DecalMasksSource source) =>
        $"mask {source.Mask} of " + (source.Sets.IsEmpty ? "every set" : string.Join(", ", source.Sets.Select(set => Path.GetFileName(set))));

    /// <summary>
    /// The add choice of a decal-masks layer (the masks of the placed decal sets at their place,
    /// turn and size), for any stack: its command adds the layer with the arguments given, and the
    /// layer's mask name and sets are then set in its settings.
    /// </summary>
    public static LayerAddChoice DecalMasksChoice(string command, JsonObject arguments) =>
        new LayerAddChoice("Decal masks", "Add a decal-masks layer",
            "The greyscale _mask of every placed decal set at its place, turn and size; it follows the decals when they move", command, arguments);

    [RelayCommand]
    private Task Remove() => Chosen is string name ? _run(Commands.Remove, new JsonObject { [Commands.LayerArgument] = name }) : Task.CompletedTask;

    [RelayCommand]
    private Task MoveUp() => Chosen is string name ? _run(Commands.Move, new JsonObject { [Commands.LayerArgument] = name, ["by"] = 1 }) : Task.CompletedTask;

    [RelayCommand]
    private Task MoveDown() => Chosen is string name ? _run(Commands.Move, new JsonObject { [Commands.LayerArgument] = name, ["by"] = -1 }) : Task.CompletedTask;

    /// <summary>
    /// The help of the combine field.
    /// </summary>
    protected virtual string CombineHelp => "Add its values, subtract them, keep the higher (maximum) or lower (minimum) of it and the layers below, replace them, or multiply";

    /// <summary>
    /// The help of the name field.
    /// </summary>
    protected virtual string NameHelp => "Letters, digits, spaces, dashes and underscores; also the name of its file";

    private SettingGroup MakeLayerSettings(IReadOnlyList<string> masks)
    {
        JsonObject Fixed() => new JsonObject { [Commands.LayerArgument] = Chosen };
        Layer Of(MapDocument d) => _read(d)?.Find(Chosen) ?? NoLayer;
        string set = Commands.Set;
        return new SettingGroup("Layer",
        [
            new TextSetting("Name", set, "name", d => Of(d).Name, _run, Fixed) { Help = NameHelp },
            new ChoiceSetting("Combine", set, "combine", Combines, d => Of(d).Combine.ToString(), _run, Fixed) { Help = CombineHelp },
            new NumberSetting("Strength", set, "strength", d => Of(d).Strength, _run, Fixed) { Minimum = 0, Maximum = 1, HasSlider = true, Help = "From 0 (not at all) to 1" },
            new ChoiceSetting(Commands.ClipTitle, set, Commands.ClipArgument, masks, d => Of(d).Clip is string key ? LayerStacks.MaskTitle(key) : Everywhere, _run, Fixed)
            {
                Help = "Where it works: everywhere, a mask for tools (Paint mode) or a stratum's mask",
            },
            new BoolSetting("Shown", set, "visible", d => Of(d).Visible, _run, Fixed) { Help = "A hidden layer counts for nothing" },
            new BoolSetting("Locked", set, "locked", d => Of(d).Locked, _run, Fixed) { Help = "Brushes and other edits leave a locked layer alone" },
        ]);
    }

    /// <summary>
    /// Shows the chosen layer's settings; subclasses add their own groups.
    /// </summary>
    protected virtual void RefreshSettings()
    {
        Layer.Refresh(Chosen is null ? null : _document);
    }
}
