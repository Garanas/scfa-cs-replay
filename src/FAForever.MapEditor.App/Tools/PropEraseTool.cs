using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// Takes props away with a round brush (user story P-2): every prop whose centre the brush passes
/// over, or only those of the palette. A stroke is one step of undo.
/// </summary>
public sealed partial class PropEraseTool(MainViewModel model) : ObservableObject, IMapTool
{
    private BrushStroke? _stroke;
    private PropGrid? _grid;

    /// <summary>
    /// The radius in ogrids.
    /// </summary>
    [ObservableProperty]
    public partial double Radius { get; set; } = 8;

    /// <summary>
    /// Whether it takes away only props of the palette, leaving the others.
    /// </summary>
    [ObservableProperty]
    public partial bool OnlyPalette { get; set; }

    public double CursorRadius => Radius;

    public void Press(float x, float z)
    {
        MapDocument? document = model.ShownDocument;
        if (document is null)
        {
            return;
        }
        HashSet<string>? palette = OnlyPalette
            ? new HashSet<string>(model.Props.Palette.Select(item => item.Key), StringComparer.Ordinal)
            : null;
        using (Performance.Measure("tool.props-erase-start"))
        {
            _grid = new PropGrid(document.Props);
        }
        float radius = (float)Radius;
        _stroke = new BrushStroke(model, "Erase props", Radius, x, z, (edit, at, to) =>
        {
            PropGrid grid = _grid!;
            List<(int Id, float X, float Z, string Blueprint)> under = [.. grid.Near(at, to, radius)
                .Where(prop => palette is null || palette.Contains(Project.Workspaces.AssetCatalog.KeyOf(prop.Blueprint)))];
            if (under.Count == 0)
            {
                return;
            }
            foreach ((int id, float px, float pz, _) in under)
            {
                grid.Remove(id, px, pz);
            }
            HashSet<int> ids = [.. under.Select(prop => prop.Id)];
            edit.Apply(current => current.RemoveProps(ids));
        });
    }

    public void Drag(float x, float z) => _stroke?.MoveTo(x, z);

    public void Release()
    {
        _stroke?.End();
        _stroke = null;
        _grid = null;
    }
}
