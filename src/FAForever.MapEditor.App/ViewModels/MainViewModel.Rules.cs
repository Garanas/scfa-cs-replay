using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The core's side of masks and props from rules (user stories K-2, P-4): the rule editors' parts,
/// their generators (made only when asked, F-7) and the preview of a mask's rules in the top view.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The rules of the mask being edited in the Paint mode (user story K-2).
    /// </summary>
    public MaskRulesViewModel MaskRules { get; private set; } = null!;

    /// <summary>
    /// The rules that place props, in the Props mode (user story P-4).
    /// </summary>
    public PropRulesViewModel PropRules { get; private set; } = null!;

    private void InitializeRules()
    {
        Generation.Register(new MaskRulesGenerator(), "generate.masks");
        Generation.Register(new PropRulesGenerator(), "generate.props");
        MaskRules = new MaskRulesViewModel(RunWithAsync, () => StrataTool.Strata, Logger("Editor"), _ui);
        PropRules = new PropRulesViewModel(RunWithAsync, () => StrataTool.Strata);
        // a rule chosen takes the inspector from the selection (P-4)
        PropRules.ChosenInList += (_, _) => Selection.Clear();
        DocumentChanged += (_, _) => RefreshRules();
        MapChanged += (_, _) =>
        {
            MaskRules.Editing = null;
            RefreshRules();
        };
        MaskRules.PreviewChanged += (_, _) => OnPropertyChanged(nameof(ShownGrid));
        Generation.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GenerationViewModel.States) or nameof(GenerationViewModel.Rows))
            {
                RefreshRuleStatus();
            }
        };
    }

    /// <summary>
    /// A rule edit (a rule added, changed, moved or taken out, a rules layer added) with what the rules
    /// make: every rules layer made again from its rules, in the order they read each other (the masks
    /// from rules generated), in the same step. Editing rules is asking to see what they make, so a
    /// rules layer shows its result at once, in the stack, its thumbnail and the views; a change of
    /// what the rules read (the terrain, another mask) leaves them out of date until Generate, as every
    /// generated result (F-7). Rules that cannot be made (a layer the map has not, masks reading each
    /// other in a circle) leave the edit as it is and say why in the log.
    /// </summary>
    /// <param name="made">What was made, to keep as the generator's result (<see cref="KeepRulesMade"/>); null when nothing was.</param>
    internal MapDocument WithRulesMade(MapDocument document, out object? made)
    {
        made = null;
        if (Generation.Results.Find(MaskRulesGenerator.GeneratorId) is not IDocumentGenerator generator)
        {
            return document;
        }
        using Performance.Span measure = Performance.Measure("model.mask-rules-made");
        try
        {
            object value = generator.Generate(document);
            made = value;
            return generator.Apply(document, value);
        }
        catch (ArgumentException exception)
        {
            Logger("Editor").LogWarning("The rules were changed but cannot be made: {Reason}", exception.Message);
            return document;
        }
    }

    /// <summary>
    /// Keeps what <see cref="WithRulesMade"/> made as the masks' generated result of the map as it is
    /// now, so they read as current.
    /// </summary>
    internal void KeepRulesMade(object? made, MapDocument? document)
    {
        if (made is not null && document is not null && Generation.Results.Find(MaskRulesGenerator.GeneratorId) is IGenerator generator)
        {
            Generation.Results.Keep(GeneratedResult.Of(generator, document, made));
            Generation.Refresh(Map?.Document);
        }
    }

    /// <summary>
    /// Shows the rules of the shown document; each part makes its fields again only when what they
    /// show changed shape.
    /// </summary>
    private void RefreshRules()
    {
        using Performance.Span measure = Performance.Measure("model.rules");
        MaskRules.Refresh(ShownDocument);
        PropRules.Refresh(ShownDocument);
    }

    private void RefreshRuleStatus()
    {
        GeneratedState? Of(string id) => Generation.States.FirstOrDefault(state => state.Generator.Id == id);
        MaskRules.RefreshStatus(Of(MaskRulesGenerator.GeneratorId), Generation.IsRunning(MaskRulesGenerator.GeneratorId));
        PropRules.RefreshStatus(Of(PropRulesGenerator.GeneratorId), Generation.IsRunning(PropRulesGenerator.GeneratorId));
    }

    /// <summary>
    /// Opens the rule editor of a mask (the Masks mode's inspector, under its stack), chooses the
    /// mask and its lowest rules layer, and shows it over the terrain with the preview of its rules.
    /// </summary>
    /// <exception cref="ArgumentException">The map has no such mask.</exception>
    public void EditMaskRules(string name)
    {
        Masks.Choose(name);
        SelectMode("masks");
        Selection.Clear();
        MaskStack.Refresh(ShownDocument);
        if (ShownDocument is Project.Files.InMemory.MapDocument document
            && Project.Editing.RuleEdits.RulesLayerOf(Project.Editing.CoverageLayers.StackOf(document, Project.Files.InMemory.LayerTarget.Mask(Masks.Chosen!))) is { } rules)
        {
            MaskStack.Choose(rules.Name);
        }
        MaskRules.Editing = Masks.Chosen;
        ShowMask = true;
    }
}
