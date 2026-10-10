using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// What the commands of prop rules share: the rule they work on.
/// </summary>
internal static class PropRuleArguments
{
    /// <summary>
    /// The rule named, else the one chosen in the panel.
    /// </summary>
    /// <exception cref="ArgumentException">There is none.</exception>
    public static string Rule(MainViewModel model, string? rule) =>
        rule?.Trim() is { Length: > 0 } given ? given
        : model.PropRules.Chosen ?? throw new ArgumentException("Name a rule: the map has no rule chosen");
}

/// <summary>
/// Adds a rule that places props from the layers (user story P-4), one step of undo, and chooses
/// it: its palette is the Props panel's (or empty), anywhere on the map until it gets conditions.
/// Its props come when generated (<c>generate.props</c>).
/// </summary>
/// <param name="Name">Left out, the panel's field, else a free one (<c>Rule</c>, <c>Rule 2</c>).</param>
/// <param name="Layer">A layer it grows on (<c>stratum:N</c>, <c>Layer N...</c>, <c>Mask: name</c>).</param>
/// <returns>The new rule's name.</returns>
[UndoStep]
[EditorCommand("props.addRule", "Add a rule", Story = "P-4")]
public sealed record AddPropRule(string? Name = null, string? Layer = null, float? Density = null, float? Spacing = null) : IRequest<string>;

public sealed class AddPropRuleHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<AddPropRule, string>
{
    public Task<string> Handle(AddPropRule request, CancellationToken cancellationToken)
    {
        MapDocument document = edit.Transaction.Document;
        string name = request.Name?.Trim() is { Length: > 0 } given
            ? given
            : model.PropRules.NewName.Trim() is { Length: > 0 } typed ? typed : RuleEdits.FreePropRuleName(document);
        PropsViewModel props = model.Props;
        PropRule rule = new PropRule
        {
            Name = name,
            Palette = [.. props.PaletteEntries],
            Conditions = request.Layer is null ? [] : [new PropRuleCondition(RuleLayers.Parse(request.Layer))],
            Density = request.Density ?? 2,
            Spacing = request.Spacing ?? 2,
            Placement = props.Placement,
        };
        edit.Transaction.Apply(d => d.AddPropRule(rule));
        model.PropRules.NewName = "";
        model.PropRules.Chosen = name;
        model.SelectMode("props");
        // the inspector shows the new rule, in place of the selection
        model.Selection.Clear();
        model.Status = rule.Palette.IsEmpty
            ? $"Added the rule {name}: put props in the palette and take it into the rule, then generate"
            : $"Added the rule {name} with {rule.Palette.Length} props of the palette: Generate places them";
        return Task.FromResult(name);
    }
}

/// <summary>
/// Changes a prop rule (user story P-4), one step of undo; the steps that change the same field one
/// after the other are one step. What the inspector's fields do. A new name goes to its props too.
/// </summary>
/// <param name="Clustering">None, Noise or Clusters.</param>
/// <param name="Alignment">ByCategory, Upright or Terrain.</param>
[EditorCommand("props.setRule", "Change a prop rule", Story = "P-4", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetPropRule(string? Rule = null, string? Name = null, float? Density = null, float? Spacing = null, bool? Fade = null,
    string? Clustering = null, float? ClusterSize = null, float? ClusterCover = null, float? MinSlope = null, float? MaxSlope = null,
    bool? AboveWater = null, int? Seed = null, string? Alignment = null, bool? RandomTurn = null, float? RandomTilt = null,
    float? ScaleMin = null, float? ScaleMax = null, float? Sink = null) : IRequest;

public sealed class SetPropRuleHandler(MainViewModel model) : IRequestHandler<SetPropRule>
{
    public Task Handle(SetPropRule r, CancellationToken cancellationToken)
    {
        string name = PropRuleArguments.Rule(model, r.Rule);
        string given = WaterArguments.Given(("name", r.Name), ("density", r.Density), ("spacing", r.Spacing), ("fade", r.Fade), ("clustering", r.Clustering),
            ("clusterSize", r.ClusterSize), ("clusterCover", r.ClusterCover), ("minSlope", r.MinSlope), ("maxSlope", r.MaxSlope), ("aboveWater", r.AboveWater),
            ("seed", r.Seed), ("alignment", r.Alignment), ("randomTurn", r.RandomTurn), ("randomTilt", r.RandomTilt), ("scaleMin", r.ScaleMin),
            ("scaleMax", r.ScaleMax), ("sink", r.Sink));
        if (given.Length == 0)
        {
            throw new ArgumentException("Give what to change: name, density, spacing, fade, clustering, clusterSize, clusterCover, minSlope, maxSlope, aboveWater, seed, alignment, randomTurn, randomTilt, scaleMin, scaleMax or sink");
        }
        if (r.Density is < 0 or > 100 || r.Spacing is < 0 or > 64)
        {
            throw new ArgumentException("A density is 0 to 100 props per 100 square ogrids, a spacing 0 to 64 ogrids");
        }
        if (r.ClusterSize is <= 0 || r.ClusterCover is < 0 or > 1 || r.ScaleMin is <= 0 || r.ScaleMax is <= 0)
        {
            throw new ArgumentException("A cluster size and a scale are more than 0, a cover 0 to 1");
        }
        PropClustering? clustering = r.Clustering is null ? null : MaskRuleArguments.Parse<PropClustering>(r.Clustering, "clustering");
        PropAlignment? alignment = r.Alignment is null ? null : MaskRuleArguments.Parse<PropAlignment>(r.Alignment, "alignment");
        string? newName = r.Name?.Trim();
        model.EditMap($"Rule {name}: {given.Replace(",", ", ")}", document => document.UpdatePropRule(name, rule => rule with
        {
            Name = newName is { Length: > 0 } ? newName : rule.Name,
            Density = r.Density ?? rule.Density,
            Spacing = r.Spacing ?? rule.Spacing,
            Fade = r.Fade ?? rule.Fade,
            Clustering = clustering ?? rule.Clustering,
            ClusterSize = r.ClusterSize ?? rule.ClusterSize,
            ClusterCover = r.ClusterCover ?? rule.ClusterCover,
            MinSlope = r.MinSlope ?? rule.MinSlope,
            MaxSlope = r.MaxSlope ?? rule.MaxSlope,
            AboveWater = r.AboveWater ?? rule.AboveWater,
            Seed = r.Seed ?? rule.Seed,
            Placement = rule.Placement with
            {
                Alignment = alignment ?? rule.Placement.Alignment,
                RandomTurn = r.RandomTurn ?? rule.Placement.RandomTurn,
                RandomTilt = r.RandomTilt ?? rule.Placement.RandomTilt,
                ScaleMin = r.ScaleMin ?? rule.Placement.ScaleMin,
                ScaleMax = r.ScaleMax ?? rule.Placement.ScaleMax,
                Sink = r.Sink ?? rule.Placement.Sink,
            },
        }), $"props.setRule:{name}:{given}");
        if (newName is { Length: > 0 } && model.PropRules.Chosen == name)
        {
            model.PropRules.Chosen = newName;
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Chooses a prop rule (user story P-4) as the Props panel's list does: the inspector edits it.
/// </summary>
[EditorCommand("props.chooseRule", "Choose a prop rule", Story = "P-4", Needs = Needs.Map)]
public sealed record ChoosePropRule(string Rule) : IRequest;

public sealed class ChoosePropRuleHandler(MainViewModel model) : IRequestHandler<ChoosePropRule>
{
    public Task Handle(ChoosePropRule request, CancellationToken cancellationToken)
    {
        model.PropRules.Chosen = model.Map!.Document.FindPropRule(request.Rule)?.Name ?? throw new ArgumentException($"The map has no rule '{request.Rule}'");
        model.SelectMode("props");
        model.Selection.Clear();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Gives a prop rule the Props panel's palette (user story P-4), or the props given, one step of undo.
/// </summary>
/// <param name="Props">Blueprint paths, each with a weight of 1; left out, the panel's palette with its weights.</param>
[UndoStep]
[EditorCommand("props.setRulePalette", "Take the palette into a rule", Story = "P-4")]
public sealed record SetPropRulePalette(string? Rule = null, string[]? Props = null) : IRequest;

public sealed class SetPropRulePaletteHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<SetPropRulePalette>
{
    public Task Handle(SetPropRulePalette request, CancellationToken cancellationToken)
    {
        string name = PropRuleArguments.Rule(model, request.Rule);
        PaletteEntry[] palette = request.Props is null
            ? [.. model.Props.PaletteEntries]
            : [.. request.Props.Select(key => new PaletteEntry(key.StartsWith('/') ? key : "/" + key, 1))];
        if (palette.Length == 0)
        {
            throw new ArgumentException("The palette is empty: add props to the Props panel's palette first");
        }
        edit.Transaction.Apply(document => document.UpdatePropRule(name, rule => rule with { Palette = [.. palette] }));
        model.Status = $"The rule {name} places {palette.Length} {(palette.Length == 1 ? "prop" : "props")}";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Adds a layer that allows (or forbids) a prop rule's props (user story P-4), one step of undo.
/// </summary>
/// <param name="Layer">Left out, the first stratum. <c>stratum:N</c>, <c>Layer N...</c>, <c>Mask: name</c> or a mask's name.</param>
/// <param name="Threshold">From 0 to 1: the layer covers where it is at least this; 0.5 when left out.</param>
[UndoStep]
[EditorCommand("props.addRuleCondition", "Add a layer to a rule", Story = "P-4")]
public sealed record AddPropRuleCondition(string? Rule = null, string? Layer = null, float? Threshold = null, bool? Forbid = null) : IRequest;

public sealed class AddPropRuleConditionHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<AddPropRuleCondition>
{
    public Task Handle(AddPropRuleCondition request, CancellationToken cancellationToken)
    {
        string name = PropRuleArguments.Rule(model, request.Rule);
        PropRuleCondition condition = new PropRuleCondition(RuleLayers.Parse(request.Layer ?? MaskRuleSources.Stratum(1)), Math.Clamp(request.Threshold ?? 0.5f, 0, 1), request.Forbid ?? false);
        edit.Transaction.Apply(document => document.UpdatePropRule(name, rule => rule with { Conditions = rule.Conditions.Add(condition) }));
        model.Status = $"The rule {name} {(condition.Forbid ? "stays off" : "grows on")} {condition.Layer}";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Changes a layer condition of a prop rule (user story P-4); the inspector's fields.
/// </summary>
/// <param name="Index">The condition's place, from 0.</param>
/// <param name="Kind">Allows or Forbids (the same as <paramref name="Forbid"/>).</param>
[EditorCommand("props.setRuleCondition", "Change a layer of a rule", Story = "P-4", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetPropRuleCondition(int Index, string? Rule = null, string? Layer = null, float? Threshold = null, bool? Forbid = null, string? Kind = null) : IRequest;

public sealed class SetPropRuleConditionHandler(MainViewModel model) : IRequestHandler<SetPropRuleCondition>
{
    public Task Handle(SetPropRuleCondition r, CancellationToken cancellationToken)
    {
        string name = PropRuleArguments.Rule(model, r.Rule);
        bool? forbid = r.Forbid ?? r.Kind?.Trim().ToLowerInvariant() switch
        {
            null => null,
            "allows" or "allow" => false,
            "forbids" or "forbid" => true,
            _ => throw new ArgumentException($"'{r.Kind}' is no kind of condition: Allows or Forbids"),
        };
        string given = WaterArguments.Given(("layer", r.Layer), ("threshold", r.Threshold), ("forbid", forbid));
        model.EditMap($"Rule {name}, layer {r.Index + 1}: {given.Replace(",", ", ")}", document => document.UpdatePropRule(name, rule =>
        {
            if (r.Index < 0 || r.Index >= rule.Conditions.Length)
            {
                throw new ArgumentException($"The rule {rule.Name} has {rule.Conditions.Length} layers, not one at {r.Index}");
            }
            PropRuleCondition condition = rule.Conditions[r.Index];
            return rule with
            {
                Conditions = rule.Conditions.SetItem(r.Index, condition with
                {
                    Layer = r.Layer is null ? condition.Layer : RuleLayers.Parse(r.Layer),
                    Threshold = r.Threshold is float threshold ? Math.Clamp(threshold, 0, 1) : condition.Threshold,
                    Forbid = forbid ?? condition.Forbid,
                }),
            };
        }), $"props.setRuleCondition:{name}:{r.Index}:{given}");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Takes a layer condition out of a prop rule (user story P-4), one step of undo.
/// </summary>
[UndoStep]
[EditorCommand("props.removeRuleCondition", "Take a layer out of a rule", Story = "P-4")]
public sealed record RemovePropRuleCondition(int Index, string? Rule = null) : IRequest;

public sealed class RemovePropRuleConditionHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<RemovePropRuleCondition>
{
    public Task Handle(RemovePropRuleCondition request, CancellationToken cancellationToken)
    {
        string name = PropRuleArguments.Rule(model, request.Rule);
        edit.Transaction.Apply(document => document.UpdatePropRule(name, rule => request.Index >= 0 && request.Index < rule.Conditions.Length
            ? rule with { Conditions = rule.Conditions.RemoveAt(request.Index) }
            : throw new ArgumentException($"The rule {rule.Name} has {rule.Conditions.Length} layers, not one at {request.Index}")));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Deletes a prop rule and the props it placed (user story P-4), one step of undo; with
/// <paramref name="KeepProps"/> its props stay as props placed by hand.
/// </summary>
[UndoStep]
[EditorCommand("props.removeRule", "Delete a prop rule", Story = "P-4")]
public sealed record RemovePropRule(string? Rule = null, bool KeepProps = false) : IRequest;

public sealed class RemovePropRuleHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<RemovePropRule>
{
    public Task Handle(RemovePropRule request, CancellationToken cancellationToken)
    {
        string name = PropRuleArguments.Rule(model, request.Rule);
        int count = edit.Transaction.Document.CountRuleProps(edit.Transaction.Document.FindPropRule(name)?.Name);
        edit.Transaction.Apply(document => document.RemovePropRule(name, request.KeepProps));
        model.Status = request.KeepProps
            ? $"Deleted the rule {name}; its {count.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture)} props stay as placed by hand"
            : $"Deleted the rule {name} and its {count.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture)} props";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Makes the selected props that rules placed props placed by hand (user story P-4): generating
/// again leaves them where they are. One step of undo.
/// </summary>
[UndoStep]
[EditorCommand("props.keepFromRules", "Keep the props from the rules", Story = "P-4", Needs = Needs.Selection)]
public sealed record KeepPropsFromRules : IRequest<int>;

public sealed class KeepPropsFromRulesHandler(MainViewModel model, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<KeepPropsFromRules, int>
{
    public Task<int> Handle(KeepPropsFromRules request, CancellationToken cancellationToken)
    {
        HashSet<int> ids = [.. SelectedProps.Of(selection)];
        int count = edit.Transaction.Document.Props.Count(prop => prop.Rule is not null && ids.Contains(prop.Id));
        edit.Transaction.Apply(document => document.KeepRuleProps(ids));
        model.Status = count == 0 ? "None of the selected props was placed by a rule" : $"Kept {count} props: generating the rules again leaves them";
        return Task.FromResult(count);
    }
}
