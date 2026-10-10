using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Gives the selected props another blueprint, chosen in the picker (user story P-1): pine06
/// becomes pine07 where they stand, turned and scaled as they were; one step of undo. Run without
/// a person, the key is the argument.
/// </summary>
/// <param name="Key">The prop to give them: a blueprint's game path.</param>
/// <returns>How many props changed.</returns>
[UndoStep]
[EditorCommand("props.replace", "Replace the props' blueprint...", Story = "P-1", Needs = Needs.Selection)]
public sealed record ReplaceProps(string? Key = null) : IRequest<int>;

public sealed class ReplacePropsHandler(MainViewModel model, SelectionViewModel selection, IEditorDialogs dialogs, CurrentEdit edit) : IRequestHandler<ReplaceProps, int>
{
    public async Task<int> Handle(ReplaceProps request, CancellationToken cancellationToken)
    {
        int[] ids = SelectedProps.Of(selection);
        string? current = edit.Transaction.Document.Find(ObjectRef.Prop(ids[0])) is Prop first
            ? AssetCatalog.KeyOf(first.Blueprint)
            : null;
        string? chosen = request.Key ?? await dialogs.PickAssetAsync(AssetKind.Prop, "Replace the props with", current);
        if (chosen is null)
        {
            model.Status = "Chose nothing";
            return 0;
        }
        AssetEntry entry = model.Assets.Catalog.Find(chosen) is { Kind: AssetKind.Prop } found
            ? found
            : throw new ArgumentException($"The workspace has no prop '{chosen}'");
        string blueprint = PropsViewModel.BlueprintOf(entry);
        int changed = 0;
        edit.Transaction.Apply(document =>
        {
            MapDocument after = document.ReplacePropBlueprints(ids, _ => blueprint);
            changed = ChangedProps.Between(document, after);
            return after;
        });
        model.Status = $"{changed} props are {entry.Name} now";
        return changed;
    }
}

/// <summary>
/// Gives each selected prop a variant of its family at random (user story P-1): pine06 may become
/// pine06_v1 or pine06_s2, of the same biome and folder; one step of undo.
/// </summary>
/// <returns>How many props changed.</returns>
[UndoStep]
[EditorCommand("props.randomVariant", "A random variant of each prop", Story = "P-1", Needs = Needs.Selection)]
public sealed record RandomPropVariants : IRequest<int>;

public sealed class RandomPropVariantsHandler(MainViewModel model, SelectionViewModel selection, CurrentEdit edit) : IRequestHandler<RandomPropVariants, int>
{
    public Task<int> Handle(RandomPropVariants request, CancellationToken cancellationToken)
    {
        int[] ids = SelectedProps.Of(selection);
        AssetCatalog catalog = model.Assets.Catalog;
        Dictionary<(string, string, string), AssetEntry[]> families = catalog.Entries
            .Where(entry => entry.Kind == AssetKind.Prop)
            .GroupBy(entry => (entry.Group, entry.Category, entry.Family))
            .ToDictionary(group => group.Key, group => group.ToArray());
        Random random = model.Props.Random;
        int unknown = 0;
        int changed = 0;
        edit.Transaction.Apply(document =>
        {
            MapDocument after = document.ReplacePropBlueprints(ids, prop =>
            {
                if (catalog.Find(prop.Blueprint) is not { Kind: AssetKind.Prop } entry)
                {
                    unknown++;
                    return prop.Blueprint;
                }
                AssetEntry[] family = families[(entry.Group, entry.Category, entry.Family)];
                return PropsViewModel.BlueprintOf(family[random.Next(family.Length)]);
            });
            changed = ChangedProps.Between(document, after);
            return after;
        });
        if (unknown > 0)
        {
            model.Logger("Editor").LogWarning("{Count} props have a blueprint the workspace has not; they keep it", unknown);
        }
        model.Status = unknown > 0 ? $"{changed} props changed; {unknown} have a blueprint the workspace has not" : $"{changed} props changed";
        return Task.FromResult(changed);
    }
}

/// <summary>
/// Counts the props that changed between two versions, for the status line.
/// </summary>
internal static class ChangedProps
{
    public static int Between(MapDocument before, MapDocument after)
    {
        if (after.Props == before.Props)
        {
            return 0;
        }
        int changed = 0;
        for (int i = 0; i < after.Props.Length && i < before.Props.Length; i++)
        {
            changed += ReferenceEquals(after.Props[i], before.Props[i]) ? 0 : 1;
        }
        return changed;
    }
}
