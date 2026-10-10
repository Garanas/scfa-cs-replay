using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Generation;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Makes every mask for tools that has rules from them (user story K-2), on a thread of the pool,
/// and puts them in the map: one step of undo. Only when asked (F-7): the status bar and the rule
/// editor say when they are out of date.
/// </summary>
/// <returns>How many masks were made.</returns>
[EditorCommand("generate.masks", "Masks from rules", Story = "K-2", Needs = Needs.Map)]
public sealed record GenerateMasks : IRequest<int>;

public sealed class GenerateMasksHandler(MainViewModel model) : IRequestHandler<GenerateMasks, int>
{
    public async Task<int> Handle(GenerateMasks request, CancellationToken cancellationToken)
    {
        if (model.Map!.Document.MaskRules.IsEmpty)
        {
            model.Status = "No mask has rules: open a mask's rules in the Paint panel and add some";
            return 0;
        }
        GeneratedResult result = await model.GenerateAsync(MaskRulesGenerator.GeneratorId);
        int count = ((System.Collections.Immutable.ImmutableSortedDictionary<string, Project.Files.InMemory.TiledBuffer<byte>>)result.Value).Count;
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
        model.Status = $"The rules placed {count:#,0} props; the props placed by hand stay";
        return count;
    }
}
