using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// What the ruler measured between two places of the map (user story V-5).
/// </summary>
/// <param name="Distance">Across the map, in ogrids (game units).</param>
/// <param name="Rise">The height of the end above the start, in game units.</param>
/// <param name="Slope">The rise against the distance, in degrees.</param>
public readonly record struct Measurement(float X0, float Z0, float X1, float Z1, double Distance, double Rise, double Slope)
{
    /// <summary>
    /// A distance in kilometres, as the lobby counts a map's size: 256 ogrids are 5 km.
    /// </summary>
    public double Kilometres => Distance * 5 / 256;

    /// <summary>
    /// Measures between two places of the terrain.
    /// </summary>
    public static Measurement Between(Heightmap heights, float x0, float z0, float x1, float z1)
    {
        double distance = Math.Sqrt((x1 - x0) * (double)(x1 - x0) + (z1 - z0) * (double)(z1 - z0));
        double rise = heights.HeightAt(x1, z1) - heights.HeightAt(x0, z0);
        double slope = distance > 0 ? Math.Atan2(Math.Abs(rise), distance) * 180 / Math.PI : 0;
        return new Measurement(x0, z0, x1, z1, distance, rise, slope);
    }

    /// <summary>
    /// E.g. <c>128.0 ogrids (2.50 km), rise +12.3, slope 5.5°</c>.
    /// </summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Distance:0.0} ogrids ({Kilometres:0.00} km), rise {Rise:+0.0;-0.0;0.0}, slope {Slope:0.0}°");
}

/// <summary>
/// Measures distances and heights (user story V-5): dragging from one place to another shows the
/// distance across the map (in ogrids, the game's units, and kilometres), how much higher the end
/// lies than the start and the slope between them, as a line in the top view and in the status
/// line. The measurement stays until the next drag; it changes nothing on the map.
/// </summary>
public sealed partial class RulerTool(MainViewModel model) : ObservableObject, IMapTool
{
    private bool _pressed;

    public double CursorRadius => 0;

    /// <summary>
    /// The last measurement, or null before the first.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    public partial Measurement? Measured { get; private set; }

    /// <summary>
    /// What the options bar shows: the measurement, or how to make one.
    /// </summary>
    public string Text => Measured?.ToString() ?? "Drag from one place to another to measure the distance, the rise and the slope between them";

    public ToolMarks? Marks => Measured is Measurement m
        ? new ToolMarks([(m.X0, m.Z0), (m.X1, m.Z1)], (m.X0, m.Z0, m.X1, m.Z1), m.ToString())
        : null;

    public void Press(float x, float z)
    {
        _pressed = true;
        Measure(x, z, x, z);
    }

    public void Drag(float x, float z)
    {
        if (_pressed && Measured is Measurement m)
        {
            Measure(m.X0, m.Z0, x, z);
        }
    }

    public void Release()
    {
        _pressed = false;
        if (Measured is Measurement m)
        {
            model.Status = $"Measured {m}";
        }
    }

    public bool Key(string key, ToolModifiers modifiers)
    {
        if (key == "Escape" && Measured is not null)
        {
            Measured = null;
            return true;
        }
        return false;
    }

    private void Measure(float x0, float z0, float x1, float z1)
    {
        if (model.ShownDocument is not MapDocument document)
        {
            return;
        }
        Measured = Measurement.Between(document.Terrain.Heightmap, x0, z0, x1, z1);
        model.Status = Measured.Value.ToString();
    }
}
