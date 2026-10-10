using System.Collections.Immutable;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The core's side of the stacks of coverage (wave 9B; user stories K-1, K-2, S-1, W-2): the Masks
/// mode's stack of the chosen mask, the Paint mode's stack of the chosen stratum and the Water mode's
/// stacks of its three maps. The chosen painted layer of each is the one its brush paints
/// (<see cref="Project.Editing.EditSession.PaintLayers"/>).
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The stack of the mask for tools chosen in the Masks mode.
    /// </summary>
    public CoverageStackViewModel MaskStack { get; private set; } = null!;

    /// <summary>
    /// The stack of the stratum chosen in the Paint mode.
    /// </summary>
    public CoverageStackViewModel StratumStack { get; private set; } = null!;

    /// <summary>
    /// The stacks of the water's three maps, shown in the Water mode's inspector.
    /// </summary>
    public IReadOnlyDictionary<WaterMask, CoverageStackViewModel> WaterStacks { get; private set; } = ImmutableDictionary<WaterMask, CoverageStackViewModel>.Empty;

    /// <summary>
    /// The grid of coverage the mode shows: the Masks mode's chosen mask, the Paint mode's stratum,
    /// the Water mode's map; null in other modes or without one.
    /// </summary>
    public LayerTarget? ShownCoverageTarget => SelectedMode?.Definition.Id switch
    {
        "masks" => MaskTarget,
        "paint" => StratumTarget,
        "water" => LayerTarget.Water(Tool<WaterPaintTool>().Painted.Mask),
        _ => null,
    };

    private LayerTarget? MaskTarget => Masks.Chosen is string name ? LayerTarget.Mask(name) : null;

    private LayerTarget? StratumTarget => StrataTool.Painted is Stratum stratum ? LayerTarget.Stratum(stratum.Layer) : null;

    /// <summary>
    /// The stack control that shows a grid of coverage, when one does.
    /// </summary>
    public CoverageStackViewModel? CoverageStackOf(LayerTarget target)
    {
        if (target.Key.StartsWith("water-", StringComparison.Ordinal))
        {
            return WaterStacks.Values.FirstOrDefault(stack => stack.Target == target);
        }
        if (target.Key.StartsWith("stratum-", StringComparison.Ordinal))
        {
            return StratumStack.Target == target ? StratumStack : null;
        }
        return MaskStack.Target == target ? MaskStack : null;
    }

    /// <summary>
    /// Shows a grid of coverage in its mode: the mask chosen in the Masks mode, the stratum in the
    /// Paint mode, the map in the Water mode.
    /// </summary>
    /// <exception cref="ArgumentException">The map has no such grid.</exception>
    public void ShowCoverageTarget(LayerTarget target)
    {
        if (target.Key.StartsWith("water-", StringComparison.Ordinal))
        {
            WaterMask mask = WaterStacks.First(entry => entry.Value.Target == target).Key;
            SelectMode("water");
            WaterSettings.Parts.Choose(WaterMaps.Of(mask).PartId);
        }
        else if (target.Key.StartsWith("stratum-", StringComparison.Ordinal))
        {
            SelectMode("paint");
            LayerTextures.Choose(int.Parse(target.Key["stratum-".Length..], System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            SelectMode("masks");
            Masks.Choose(target.Key["mask-".Length..]);
        }
    }

    private void InitializeCoverageStacks()
    {
        MaskStack = new CoverageStackViewModel(RunWithAsync, () => MaskTarget, "mask layer");
        StratumStack = new CoverageStackViewModel(RunWithAsync, () => StratumTarget, "stratum layer");
        WaterPaintTool water = Tool<WaterPaintTool>();
        WaterStacks = Enum.GetValues<WaterMask>().ToDictionary(mask => mask,
            mask => new CoverageStackViewModel(RunWithAsync, () => LayerTarget.Water(mask), "water layer"));
        foreach (WaterMap map in WaterMaps.All)
        {
            WaterSettings.Parts.Replace(map.PartId, new WaterMapPart(map, water, WaterStacks[map.Mask]));
        }

        IEnumerable<CoverageStackViewModel> all = [MaskStack, StratumStack, .. WaterStacks.Values];
        foreach (CoverageStackViewModel stack in all)
        {
            stack.Changed += (_, _) => FollowChosenPaintLayer(stack);
        }
        void RefreshAll()
        {
            foreach (CoverageStackViewModel stack in all)
            {
                stack.Refresh(ShownDocument);
            }
        }
        DocumentChanged += (_, _) =>
        {
            RefreshAll();
            ShowEditedRulesLayer();
        };
        MaskRules.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MaskRulesViewModel.Editing))
            {
                ShowEditedRulesLayer();
            }
        };
        MapChanged += (_, _) =>
        {
            foreach (CoverageStackViewModel stack in all)
            {
                stack.Retarget(ShownDocument);
                FollowChosenPaintLayer(stack);
            }
        };
        Masks.Changed += (_, _) => MaskStack.Retarget(ShownDocument);
        StrataTool.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StrataTool.Painted))
            {
                StratumStack.Retarget(ShownDocument);
            }
        };
        MaskStack.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LayerStackViewModel.Chosen))
            {
                FollowChosenRulesLayer(MaskStack);
            }
        };
        StratumStack.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LayerStackViewModel.Chosen))
            {
                FollowChosenRulesLayer(StratumStack);
            }
        };
        // the rule editor shows the rules layer of the mode's stack
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SelectedMode))
            {
                switch (SelectedMode?.Definition.Id)
                {
                    case "masks":
                        FollowChosenRulesLayer(MaskStack);
                        break;
                    case "paint":
                        FollowChosenRulesLayer(StratumStack);
                        break;
                }
            }
        };
        RefreshAll();
    }

    /// <summary>
    /// While the rule editor edits a rules layer of the mode's mask or stratum (a rule added to a
    /// mask makes its rules layer), that layer is the one chosen in the stack.
    /// </summary>
    private void ShowEditedRulesLayer()
    {
        CoverageStackViewModel? stack = SelectedMode?.Definition.Id switch
        {
            "masks" => MaskStack,
            "paint" => StratumStack,
            _ => null,
        };
        if (stack?.Target is not LayerTarget target || MaskRules.Editing != RulesOwner(target) || stack.IsRules || ShownDocument is not MapDocument document)
        {
            return;
        }
        LayerStack layers = CoverageLayers.StackOf(document, target);
        if ((MaskRules.Layer is string named ? layers.Find(named) : RuleEdits.RulesLayerOf(layers)) is { Source: RulesSource } rules)
        {
            stack.Choose(rules.Name);
        }
    }

    /// <summary>
    /// The chosen layer of a stack is the one its brush paints.
    /// </summary>
    private void FollowChosenPaintLayer(CoverageStackViewModel stack)
    {
        if (Map is null || stack.Target is not LayerTarget target)
        {
            return;
        }
        if (stack.Chosen is string chosen)
        {
            Map.Session.PaintLayers[target.Key] = chosen;
        }
        else
        {
            Map.Session.PaintLayers.Remove(target.Key);
        }
    }

    /// <summary>
    /// A rules layer chosen in a stack shows its rules in the rule editor (and their preview).
    /// </summary>
    private void FollowChosenRulesLayer(CoverageStackViewModel stack)
    {
        if (stack.ChosenLayer is { Source: RulesSource } layer && stack.Target is LayerTarget target)
        {
            MaskRules.EditLayer(RulesOwner(target), layer.Name);
        }
        else if (MaskRules.Editing is string editing && stack.Target is LayerTarget shown && RulesOwner(shown) == editing)
        {
            MaskRules.Editing = null;
        }
        OnPropertyChanged(nameof(ShownGrid));
    }

    /// <summary>
    /// How the rule editor names the grid of a rules layer: a mask by its name, a stratum as <c>stratum:N</c>.
    /// </summary>
    internal static string RulesOwner(LayerTarget target) =>
        target.Key.StartsWith("stratum-", StringComparison.Ordinal) ? MaskRuleSources.Stratum(int.Parse(target.Key["stratum-".Length..], System.Globalization.CultureInfo.InvariantCulture))
        : target.Key.StartsWith("mask-", StringComparison.Ordinal) ? target.Key["mask-".Length..]
        : target.Key;

    /// <summary>
    /// The stacks' part of automation's state: per stack shown, its target, layers and chosen one.
    /// </summary>
    internal JsonObject DescribeCoverageStacksState()
    {
        MapDocument? document = ShownDocument;
        JsonObject Of(CoverageStackViewModel stack) => new JsonObject
        {
            ["target"] = stack.Target?.Key,
            ["chosen"] = stack.Chosen,
            ["kept"] = stack.Target is LayerTarget target && document?.CoverageStacks.ContainsKey(target.Key) == true,
            ["layers"] = new JsonArray([.. stack.Items.Select(item => (JsonNode)new JsonObject
            {
                ["name"] = item.Name,
                ["source"] = LayerSources.NameOf(item.Layer.Source),
                ["combine"] = item.Layer.Combine.ToString(),
                ["strength"] = item.Layer.Strength,
                ["clip"] = item.Layer.Clip,
                ["visible"] = item.Layer.Visible,
                ["locked"] = item.Layer.Locked,
                ["reads"] = (item.Layer.Source as MaskRefSource)?.Key,
                ["summary"] = item.Summary,
            })]),
        };
        return new JsonObject
        {
            ["shown"] = ShownCoverageTarget?.Key,
            ["mask"] = Of(MaskStack),
            ["stratum"] = Of(StratumStack),
            ["water"] = new JsonObject([.. WaterStacks.Select(entry => new KeyValuePair<string, JsonNode?>(entry.Key.ToString(), Of(entry.Value)))]),
            ["stacks"] = new JsonArray([.. (document?.CoverageStacks.Keys ?? []).Select(key => (JsonNode)key)]),
        };
    }
}
