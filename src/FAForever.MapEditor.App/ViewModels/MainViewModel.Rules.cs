using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Generation;

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
    /// Opens the rule editor of a mask (the Paint mode's inspector), chooses the mask and shows it
    /// over the terrain, with the preview of its rules.
    /// </summary>
    /// <exception cref="ArgumentException">The map has no such mask.</exception>
    public void EditMaskRules(string name)
    {
        Masks.Choose(name);
        SelectMode("paint");
        if (ActiveTool?.Tool is FAForever.MapEditor.App.Tools.StrataTool)
        {
            // the strata tool shows the layer it paints; the masks tool lets the preview show
            SelectTool("paint.masks");
        }
        Selection.Clear();
        MaskRules.Editing = Masks.Chosen;
        ShowMask = true;
    }
}
