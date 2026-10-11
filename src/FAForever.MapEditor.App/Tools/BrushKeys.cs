using System.Globalization;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// The brushes' radius and strength by keys and the wheel (user story T-1), for every tool with a
/// brush: <c>[</c> and <c>]</c> make the radius smaller and larger by a fifth, shift with them the
/// strength by a twentieth of its range (the props brush: its density); control and the wheel
/// over a view the radius, control, shift and the wheel the strength. Q and E turn the picture of
/// a brush with one (<see cref="IShapedBrushTool"/>, user story T-1) 15 degrees anticlockwise or
/// clockwise, shift with them 90, alt and the wheel over a view 5 degrees a notch; a round brush
/// ignores them, and the select and place tools keep Q and E for what they turn. The options row's
/// sliders and its Rotation show the new values, also behind More.
/// </summary>
public static class BrushKeys
{
    /// <summary>
    /// The least and greatest radius, as the sliders of the options row.
    /// </summary>
    public const double MinimumRadius = 1, MaximumRadius = 128;

    /// <summary>
    /// The degrees Q and E turn a brush's picture, with shift, and a notch of the wheel with alt.
    /// </summary>
    public const double TurnStep = 15, TurnStepLarge = 90, WheelTurnStep = 5;

    /// <summary>
    /// A key of a view or the window for the active tool: <c>OemOpenBrackets</c> or
    /// <c>OemCloseBrackets</c> without control (control with them orders the decals).
    /// </summary>
    /// <param name="status">What changed, for the status line; null when the key is not one of these.</param>
    /// <returns>Whether the key changed the brush.</returns>
    public static bool Key(IMapTool? tool, string key, ToolModifiers modifiers, out string? status)
    {
        if (TurnKey(tool, key, modifiers, out status))
        {
            return true;
        }
        status = null;
        // by the key's value: a key's name may be its other name (Oem4 for OemOpenBrackets)
        int steps = !Enum.TryParse(key, out Avalonia.Input.Key pressed) ? 0 : pressed switch
        {
            Avalonia.Input.Key.OemOpenBrackets => -1,
            Avalonia.Input.Key.OemCloseBrackets => 1,
            _ => 0,
        };
        if (steps == 0 || modifiers.HasFlag(ToolModifiers.Control) || modifiers.HasFlag(ToolModifiers.Alt))
        {
            return false;
        }
        return Step(tool, steps, strength: modifiers.HasFlag(ToolModifiers.Shift), out status);
    }

    /// <summary>
    /// Q or E (shift with them) for a tool with a brush's picture: turns it. The stamp tool turns its
    /// picture with its own keys (<see cref="StampTool.Key"/>), so they are left to it.
    /// </summary>
    /// <returns>Whether the key turned the brush.</returns>
    public static bool TurnKey(IMapTool? tool, string key, ToolModifiers modifiers, out string? status)
    {
        status = null;
        if (key is not ("Q" or "E") || modifiers.HasFlag(ToolModifiers.Control) || modifiers.HasFlag(ToolModifiers.Alt) || tool is StampTool)
        {
            return false;
        }
        double step = modifiers.HasFlag(ToolModifiers.Shift) ? TurnStepLarge : TurnStep;
        return Turn(tool, key == "E" ? step : -step, out status);
    }

    /// <summary>
    /// Notches of the wheel with alt held over a view: turns the brush's picture 5 degrees a notch,
    /// clockwise for a notch away from the person.
    /// </summary>
    public static bool TurnWheel(IMapTool? tool, int notches, out string? status) => Turn(tool, notches * WheelTurnStep, out status);

    /// <summary>
    /// Turns the picture of the tool's brush by some degrees, clockwise in the top view, within
    /// -180 to 180. With random rotation on it is the angle the dabs start from.
    /// </summary>
    /// <returns>Whether the tool has a brush with a picture; a round one is not turned.</returns>
    public static bool Turn(IMapTool? tool, double degrees, out string? status)
    {
        status = null;
        if (degrees == 0 || tool is not IShapedBrushTool { Shape: { IsShaped: true } shape })
        {
            return false;
        }
        double rotation = Math.Round(shape.Rotation + degrees, 3) % 360;
        shape.Rotation = rotation > 180 ? rotation - 360 : rotation <= -180 ? rotation + 360 : rotation;
        status = Say("Rotation", shape.Rotation, "0.#", "°") + (shape.RandomRotation ? ", where each dab's random turn starts" : "");
        return true;
    }

    /// <summary>
    /// Notches of the wheel with control held over a view (shift too: the strength).
    /// </summary>
    public static bool Wheel(IMapTool? tool, int notches, bool strength, out string? status) => Step(tool, notches, strength, out status);

    /// <summary>
    /// Makes the radius (or strength) of the tool's brush larger or smaller by a number of steps.
    /// </summary>
    /// <returns>Whether the tool has such a brush.</returns>
    public static bool Step(IMapTool? tool, int steps, bool strength, out string? status)
    {
        status = null;
        if (steps == 0)
        {
            return false;
        }
        switch (tool)
        {
            case SculptTool sculpt when strength:
                sculpt.Strength = StrengthStep(sculpt.Strength, steps, 0.01, sculpt.StrengthMaximum);
                status = Say("Strength", sculpt.Strength, "0.00");
                return true;
            case SculptTool sculpt:
                sculpt.Radius = RadiusStep(sculpt.Radius, steps);
                status = Say("Radius", sculpt.Radius, "0", " ogrids");
                return true;
            // a plugin's tool whose radius is one of its fields (the field's own limits hold)
            case PluginTool plugin when plugin.HasRadiusField && !strength:
                status = Say("Radius", plugin.SetRadius(RadiusStep(plugin.CursorRadius, steps)), "0.#", " ogrids");
                return true;
            case GridPaintTool grid when strength:
                grid.Strength = StrengthStep(grid.Strength, steps, 0.01, 1);
                status = Say("Strength", grid.Strength, "0.00");
                return true;
            case GridPaintTool grid:
                grid.Radius = RadiusStep(grid.Radius, steps);
                status = Say("Radius", grid.Radius, "0", " ogrids");
                return true;
            case PropPaintTool paint when strength:
                paint.Density = StrengthStep(paint.Density, steps, 0.1, 50);
                status = Say("Density", paint.Density, "0.#", " props per 100 square ogrids");
                return true;
            case PropPaintTool paint:
                paint.Radius = RadiusStep(paint.Radius, steps);
                status = Say("Radius", paint.Radius, "0", " ogrids");
                return true;
            case StampTool stamp when strength:
                stamp.Height = Math.Clamp(Math.Round(stamp.Height + steps * Math.Max(1, stamp.Height / 10)), 0, 1024);
                status = Say("Height", stamp.Height, "0", " game units");
                return true;
            case StampTool stamp:
                stamp.Radius = RadiusStep(stamp.Radius, steps, StampTool.MaximumRadius);
                status = Say("Radius", stamp.Radius, "0", " ogrids");
                return true;
            case PropEraseTool erase when !strength:
                erase.Radius = RadiusStep(erase.Radius, steps);
                status = Say("Radius", erase.Radius, "0", " ogrids");
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// A wheel's delta as whole notches, at least one in its direction.
    /// </summary>
    public static int Notches(double delta) => Math.Sign(delta) * Math.Max(1, (int)Math.Round(Math.Abs(delta)));

    /// <summary>
    /// A fifth larger or smaller per step, at least one ogrid a step, within the sliders' range.
    /// </summary>
    public static double RadiusStep(double radius, int steps) => RadiusStep(radius, steps, MaximumRadius);

    /// <summary>
    /// A radius a fifth larger or smaller per step, at least one ogrid a step, from 1 to <paramref name="maximum"/>.
    /// </summary>
    public static double RadiusStep(double radius, int steps, double maximum)
    {
        double next = radius;
        for (int i = 0; i < Math.Abs(steps); i++)
        {
            double scaled = steps > 0 ? next * 1.2 : next / 1.2;
            next = steps > 0 ? Math.Max(Math.Round(scaled), next + 1) : Math.Min(Math.Round(scaled), next - 1);
        }
        return Math.Clamp(next, MinimumRadius, maximum);
    }

    /// <summary>
    /// A twentieth of the range per step, within it.
    /// </summary>
    public static double StrengthStep(double strength, int steps, double minimum, double maximum) =>
        Math.Clamp(Math.Round(strength + steps * (maximum - minimum) / 20, 3), minimum, maximum);

    private static string Say(string what, double value, string format, string unit = "") =>
        $"Brush {what.ToLowerInvariant()}: {value.ToString(format, CultureInfo.InvariantCulture)}{unit}";
}
