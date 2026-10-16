namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// Decals along a height curve (user story T-4, D2): the settings the curve's inspector part shows.
/// </summary>
public sealed partial class MainViewModel
{
    private DecalsAlongCurveViewModel? _decalsAlong;

    /// <summary>
    /// The decal, spacing, width, offset and sides of placing decals along a curve
    /// (<c>decals.alongCurve</c>).
    /// </summary>
    public DecalsAlongCurveViewModel DecalsAlong => _decalsAlong ??= new DecalsAlongCurveViewModel(this);
}
