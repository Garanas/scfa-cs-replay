using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// Turns the sun with the mouse (user story L-1), in either view: the sun stands where the mouse
/// points, seen from the middle of the map. The direction from the middle to the mouse is the
/// azimuth; the distance is the elevation, straight up at the middle and low over the horizon at
/// half the map's size from it. A drag is one step of undo, and the 3D view is lit by it while
/// the mouse moves. The top view draws the line from the middle to the sun.
/// </summary>
public sealed partial class SunTool(MainViewModel model) : ObservableObject, IMapTool
{
    /// <summary>
    /// The lowest the tool puts the sun, in degrees: lower, the terrain would be all shadow.
    /// </summary>
    public const double LowestElevation = 5;

    private EditTransaction? _turn;
    private MapDocument? _before;

    public double CursorRadius => 0;

    public string Help => "Drag in a view: the sun stands where the mouse points, seen from the middle of the map; at the middle straight up, towards the edge low";

    /// <summary>
    /// The sun as the tool last put it, e.g. <c>Azimuth 135°, elevation 40°</c>.
    /// </summary>
    [ObservableProperty]
    public partial string Angles { get; private set; } = "";

    public ToolMarks? Marks
    {
        get
        {
            if (model.ShownDocument is not MapDocument document)
            {
                return null;
            }
            (double azimuth, double elevation) = SunAngles.Of(document.Lighting.SunDirection);
            (float x, float z) = PlaceOf(document, azimuth, elevation);
            return new ToolMarks([(x, z)], (document.Width / 2f, document.Height / 2f, x, z), Describe(azimuth, elevation));
        }
    }

    /// <summary>
    /// The sun's angles for a place of the map, in degrees.
    /// </summary>
    public static (double Azimuth, double Elevation) AnglesAt(MapDocument document, float x, float z)
    {
        double dx = x - document.Width / 2.0;
        double dz = z - document.Height / 2.0;
        double reach = Math.Min(document.Width, document.Height) / 2.0;
        double azimuth = Math.Atan2(dx, -dz) * 180 / Math.PI;
        double elevation = Math.Clamp(90 * (1 - Math.Sqrt(dx * dx + dz * dz) / reach), LowestElevation, 90);
        return (azimuth < 0 ? azimuth + 360 : azimuth, elevation);
    }

    /// <summary>
    /// The place of the map that stands for the sun's angles: the reverse of <see cref="AnglesAt"/>.
    /// </summary>
    public static (float X, float Z) PlaceOf(MapDocument document, double azimuth, double elevation)
    {
        double reach = Math.Min(document.Width, document.Height) / 2.0 * (1 - Math.Clamp(elevation, 0, 90) / 90);
        double a = azimuth * Math.PI / 180;
        return ((float)(document.Width / 2.0 + reach * Math.Sin(a)), (float)(document.Height / 2.0 - reach * Math.Cos(a)));
    }

    public static string Describe(double azimuth, double elevation) =>
        string.Create(CultureInfo.InvariantCulture, $"Sun: azimuth {azimuth:0}°, elevation {elevation:0}°");

    public void Press(float x, float z)
    {
        if (model.ShownDocument is null || model.StrokeInProgress is not null)
        {
            return;
        }
        _turn = model.StartStroke("Turn the sun");
        _before = _turn.Document;
        Turn(x, z);
    }

    public void Drag(float x, float z)
    {
        if (_turn is not null)
        {
            Turn(x, z);
        }
    }

    public void Release()
    {
        if (_turn is not null)
        {
            model.EndStroke(_turn);
            model.Status = Angles;
        }
        _turn = null;
        _before = null;
    }

    private void Turn(float x, float z)
    {
        if (model.StrokeInProgress != _turn || _before is not MapDocument before)
        {
            return;
        }
        (double azimuth, double elevation) = AnglesAt(before, x, z);
        System.Numerics.Vector3 direction = SunAngles.Direction(azimuth, elevation);
        _turn!.Apply(_ => before with { Lighting = before.Lighting with { SunDirection = direction } });
        Angles = Describe(azimuth, elevation);
        model.Status = Angles;
        model.ShowStroke();
    }
}
