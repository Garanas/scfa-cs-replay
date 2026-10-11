using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// What the commands of mask rules share: the mask they work on and the names of kinds.
/// </summary>
internal static class MaskRuleArguments
{
    /// <summary>
    /// The mask named, else the one whose rules are edited, else the chosen one.
    /// </summary>
    /// <exception cref="ArgumentException">There is none.</exception>
    public static string Mask(MainViewModel model, string? mask) =>
        mask?.Trim() is { Length: > 0 } given ? given
        : model.MaskRules.Editing ?? model.Masks.Chosen ?? throw new ArgumentException("Name a mask: the map has no mask chosen");

    /// <summary>
    /// The rules layer named, else the one whose rules are edited when the mask is the edited one;
    /// null for the mask's lowest.
    /// </summary>
    public static string? Layer(MainViewModel model, string mask, string? layer) =>
        layer?.Trim() is { Length: > 0 } given ? given : model.MaskRules.Editing == mask ? model.MaskRules.Layer : null;

    public static T Parse<T>(string text, string what) where T : struct, Enum =>
        Enum.TryParse(text.Replace(" ", ""), ignoreCase: true, out T value) && Enum.IsDefined(value)
            ? value
            : text.Equals("distance to water", StringComparison.OrdinalIgnoreCase) && typeof(T) == typeof(MaskRuleKind)
                ? (T)(object)MaskRuleKind.WaterDistance
                : throw new ArgumentException($"'{text}' is no {what}: {string.Join(", ", Enum.GetNames<T>())}");
}

/// <summary>
/// Opens the rule editor of a mask for tools (user story K-2) in the Masks mode's inspector,
/// chooses the mask and its rules layer, and shows it over the terrain with a preview of what its
/// rules make.
/// </summary>
/// <param name="Name">The mask; left out, the chosen one.</param>
[EditorCommand("paint.maskRules", "Rules of the mask...", Story = "K-2", Needs = Needs.Map)]
public sealed record EditMaskRules(string? Name = null) : IRequest;

public sealed class EditMaskRulesHandler(MainViewModel model) : IRequestHandler<EditMaskRules>
{
    public Task Handle(EditMaskRules request, CancellationToken cancellationToken)
    {
        string name = request.Name?.Trim() is { Length: > 0 } given ? given : model.Masks.Chosen ?? throw new ArgumentException("The map has no mask: add one first");
        model.EditMaskRules(name);
        int count = model.Map!.Document.MaskRulesOf(model.MaskRules.Editing!).Length;
        model.Status = count == 0
            ? $"The mask {model.MaskRules.Editing} has no rules yet: add one in the inspector"
            : $"The mask {model.MaskRules.Editing} has {count} {(count == 1 ? "rule" : "rules")}, made again whenever they change; Generate after a change of what they read";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Adds a rule to a mask's rules (user story K-2), one step of undo, in which the rules layers are
/// made again from their rules (<see cref="MainViewModel.WithRulesMade"/>), so the mask shows what
/// its rules make at once; the rule edits below do the same.
/// </summary>
/// <param name="Kind">Slope, Height, WaterDistance, Layer, Noise, Blur, Grow, Shrink, Threshold or Invert.</param>
/// <param name="Mask">Left out, the mask being edited, else the chosen one; a stratum's rules layer
/// as <c>stratum:N</c>.</param>
/// <param name="Index">Where in the list; left out, at the end.</param>
/// <param name="Layer">The rules layer of the mask's stack; left out, the one being edited, else its lowest
/// (a mask without one gets one).</param>
/// <returns>The new rule's place in the list.</returns>
[UndoStep]
[EditorCommand("paint.addMaskRule", "Add a rule to a mask", Story = "K-2")]
public sealed record AddMaskRule(string Kind, string? Mask = null, int? Index = null, string? Combine = null, bool? Invert = null,
    float? Min = null, float? Max = null, float? Soft = null, string? Source = null, float? Scale = null, int? Seed = null, float? Radius = null,
    string? Layer = null) : IRequest<int>;

public sealed class AddMaskRuleHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<AddMaskRule, int>
{
    public Task<int> Handle(AddMaskRule r, CancellationToken cancellationToken)
    {
        string mask = MaskRuleArguments.Mask(model, r.Mask);
        string? layer = MaskRuleArguments.Layer(model, mask, r.Layer);
        MaskRuleKind kind = MaskRuleArguments.Parse<MaskRuleKind>(r.Kind, "kind of mask rule");
        MaskRule rule = SetMaskRuleHandler.Change(MaskRule.Of(kind), r.Combine, r.Invert, r.Min, r.Max, r.Soft, r.Source, r.Scale, r.Seed, r.Radius);
        if (kind == MaskRuleKind.Layer && rule.Source.Length == 0)
        {
            // a layer rule reads the first other mask, else the first stratum
            rule = rule with { Source = edit.Transaction.Document.Masks.Keys.FirstOrDefault(name => name != mask) ?? MaskRuleSources.Stratum(1) };
        }
        int index = r.Index ?? edit.Transaction.Document.MaskRulesOf(mask, layer).Length;
        object? made = null;
        edit.Transaction.Apply(document => model.WithRulesMade(document.AddMaskRule(mask, rule, index, layer), out made));
        model.KeepRulesMade(made, edit.Transaction.Document);
        if (model.MaskRules.Editing != mask && layer is null)
        {
            model.EditMaskRules(mask);
        }
        model.Status = $"Added a rule of {MaskRulesViewModel.TitleOf(kind).ToLowerInvariant()} to the mask {mask}";
        return Task.FromResult(index);
    }
}

/// <summary>
/// Changes a rule of a mask (user story K-2), one step of undo; the steps that change the same
/// field one after the other are one step (a slider dragged). What the rule editor's fields do.
/// </summary>
/// <param name="Index">The rule's place in the mask's list, from 0.</param>
/// <param name="Source">A layer: <c>stratum:N</c>, <c>Layer N...</c>, <c>Mask: name</c> or a mask's name.</param>
[EditorCommand("paint.setMaskRule", "Change a rule of a mask", Story = "K-2", Needs = Needs.Map | Needs.NoStroke)]
public sealed record SetMaskRule(int Index, string? Mask = null, string? Kind = null, string? Combine = null, bool? Invert = null,
    float? Min = null, float? Max = null, float? Soft = null, string? Source = null, float? Scale = null, int? Seed = null, float? Radius = null,
    string? Layer = null) : IRequest;

public sealed class SetMaskRuleHandler(MainViewModel model) : IRequestHandler<SetMaskRule>
{
    public Task Handle(SetMaskRule r, CancellationToken cancellationToken)
    {
        string mask = MaskRuleArguments.Mask(model, r.Mask);
        string? layer = MaskRuleArguments.Layer(model, mask, r.Layer);
        string given = WaterArguments.Given(("kind", r.Kind), ("combine", r.Combine), ("invert", r.Invert), ("min", r.Min), ("max", r.Max), ("soft", r.Soft),
            ("source", r.Source), ("scale", r.Scale), ("seed", r.Seed), ("radius", r.Radius));
        if (given.Length == 0)
        {
            throw new ArgumentException("Give what to change: kind, combine, invert, min, max, soft, source, scale, seed or radius");
        }
        MaskRuleKind? kind = r.Kind is null ? null : MaskRuleArguments.Parse<MaskRuleKind>(r.Kind, "kind of mask rule");
        object? made = null;
        model.EditMap($"Mask {mask}, rule {r.Index + 1}: {given.Replace(",", ", ")}", document =>
        {
            MapDocument changed = document.UpdateMaskRule(mask, r.Index, rule =>
                Change(kind is MaskRuleKind other && other != rule.Kind ? MaskRule.Of(other) with { Combine = rule.Combine, Invert = rule.Invert } : rule,
                    r.Combine, r.Invert, r.Min, r.Max, r.Soft, r.Source, r.Scale, r.Seed, r.Radius), layer);
            return ReferenceEquals(changed, document) ? document : model.WithRulesMade(changed, out made);
        }, $"paint.setMaskRule:{mask}:{layer}:{r.Index}:{given}");
        model.KeepRulesMade(made, model.Map?.Document);
        return Task.CompletedTask;
    }

    /// <exception cref="ArgumentException">A value is out of its range.</exception>
    internal static MaskRule Change(MaskRule rule, string? combine, bool? invert, float? min, float? max, float? soft, string? source, float? scale, int? seed, float? radius)
    {
        if (soft is < 0 || radius is < 0 || scale is <= 0)
        {
            throw new ArgumentException("A soft edge and a radius are 0 or more, a scale more than 0");
        }
        if (radius is > 256)
        {
            throw new ArgumentException("A radius is at most 256 ogrids");
        }
        return rule with
        {
            Combine = combine is null ? rule.Combine : MaskRuleArguments.Parse<MaskCombine>(combine, "way to combine"),
            Invert = invert ?? rule.Invert,
            Min = min ?? rule.Min,
            Max = max ?? rule.Max,
            Soft = soft ?? rule.Soft,
            Source = source is null ? rule.Source : RuleLayers.Parse(source),
            Scale = scale ?? rule.Scale,
            Seed = seed ?? rule.Seed,
            Radius = radius ?? rule.Radius,
        };
    }
}

/// <summary>
/// Takes a rule out of a mask's rules (user story K-2), one step of undo; a mask without rules is
/// one painted by hand again, and keeps what it holds.
/// </summary>
[UndoStep]
[EditorCommand("paint.removeMaskRule", "Take a rule out of a mask", Story = "K-2")]
public sealed record RemoveMaskRule(int Index, string? Mask = null, string? Layer = null) : IRequest;

public sealed class RemoveMaskRuleHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<RemoveMaskRule>
{
    public Task Handle(RemoveMaskRule request, CancellationToken cancellationToken)
    {
        string mask = MaskRuleArguments.Mask(model, request.Mask);
        string? layer = MaskRuleArguments.Layer(model, mask, request.Layer);
        object? made = null;
        edit.Transaction.Apply(document => model.WithRulesMade(document.RemoveMaskRule(mask, request.Index, layer), out made));
        model.KeepRulesMade(made, edit.Transaction.Document);
        model.Status = $"Took rule {request.Index + 1} out of the mask {mask}";
        return Task.CompletedTask;
    }
}

/// <summary>
/// Moves a rule of a mask to another place in its list (user story K-2): the rules run in order.
/// </summary>
[UndoStep]
[EditorCommand("paint.moveMaskRule", "Move a rule of a mask", Story = "K-2")]
public sealed record MoveMaskRule(int Index, int To, string? Mask = null, string? Layer = null) : IRequest;

public sealed class MoveMaskRuleHandler(MainViewModel model, CurrentEdit edit) : IRequestHandler<MoveMaskRule>
{
    public Task Handle(MoveMaskRule request, CancellationToken cancellationToken)
    {
        string mask = MaskRuleArguments.Mask(model, request.Mask);
        string? layer = MaskRuleArguments.Layer(model, mask, request.Layer);
        object? made = null;
        edit.Transaction.Apply(document => model.WithRulesMade(document.MoveMaskRule(mask, request.Index, request.To, layer), out made));
        model.KeepRulesMade(made, edit.Transaction.Document);
        model.Status = $"Moved rule {request.Index + 1} of the mask {mask} to {request.To + 1}";
        return Task.CompletedTask;
    }
}
