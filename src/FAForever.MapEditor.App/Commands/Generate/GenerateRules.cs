using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Generation;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Makes every rules layer of the masks for tools and the strata from its rules (user story K-2),
/// on a thread of the pool, and puts them in the map: one step of undo. Only when asked (F-7): the
/// status bar and the rule editor say when they are out of date.
/// </summary>
/// <returns>How many masks and strata were made.</returns>
[EditorCommand("generate.masks", "Masks from rules", Story = "K-2", Needs = Needs.Map)]
public sealed record GenerateMasks : IRequest<int>;

public sealed class GenerateMasksHandler(MainViewModel model) : IRequestHandler<GenerateMasks, int>
{
    public async Task<int> Handle(GenerateMasks request, CancellationToken cancellationToken)
    {
        if (!model.Map!.Document.CoverageStacks.Values.Any(stack => stack.Layers.Any(layer => layer.Source is Project.Files.InMemory.RulesSource)))
        {
            model.Status = "No mask or stratum has rules: add a rules layer to a mask's stack in the Masks mode";
            return 0;
        }
        GeneratedResult result = await model.GenerateAsync(MaskRulesGenerator.GeneratorId);
        int count = ((IReadOnlyList<(string Target, string Layer, Project.Files.InMemory.TiledBuffer<byte> Grid)>)result.Value).Select(made => made.Target).Distinct().Count();
        model.Status = $"Generated {count} {(count == 1 ? "mask" : "masks")} from their rules";
        return count;
    }
}

/// <summary>
/// Places the props of every rule again (user story P-4), on a thread of the pool, in place of
/// the props the rules placed before: one step of undo. Props placed by hand stay.
/// </summary>
/// <returns>How many props the rules placed.</returns>
[EditorCommand("generate.props", "Props from rules", Story = "P-4", Needs = Needs.Map)]
public sealed record GenerateProps : IRequest<int>;

public sealed class GeneratePropsHandler(MainViewModel model) : IRequestHandler<GenerateProps, int>
{
    public async Task<int> Handle(GenerateProps request, CancellationToken cancellationToken)
    {
        if (model.Map!.Document.PropRules.IsEmpty && model.Map.Document.CountRuleProps() == 0)
        {
            model.Status = "The map has no prop rules: add one in the Props panel";
            return 0;
        }
        GeneratedResult result = await model.GenerateAsync(PropRulesGenerator.GeneratorId);
        int count = ((System.Collections.Immutable.ImmutableArray<Project.Files.InMemory.Prop>)result.Value).Length;
        model.Status = $"The rules placed {count.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture)} props; the props placed by hand stay";
        return count;
    }
}
