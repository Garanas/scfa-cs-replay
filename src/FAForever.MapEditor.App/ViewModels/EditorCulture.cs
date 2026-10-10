using System.Globalization;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// How the editor writes and reads numbers: the same on every computer, whatever its language. A
/// number shows with a point as the decimal separator ("12.5"), and a number typed is read with a
/// point or, when it has no point, a single comma as the separator ("12.5" and "12,5" are both
/// twelve and a half); digits are never grouped. The editor's threads run in the invariant culture
/// (<see cref="Use"/>), so the bindings of the views and the labels of the tools' options format
/// alike; text fields bound to a number read it through <see cref="Controls.NumberTextConverter"/>.
/// </summary>
public static class EditorCulture
{
    /// <summary>
    /// Makes the invariant culture the culture of every thread of the editor, before the window
    /// starts (and in the tests' assembly initialisation). The language of the interface stays.
    /// </summary>
    public static void Use()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    /// <summary>
    /// A number as a field shows it: up to four decimals, a point as the separator.
    /// </summary>
    public static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// A number typed into a field: with a point as the decimal separator, or a single comma when it
    /// has no point; finite only.
    /// </summary>
    public static bool TryParse(string? text, out double value)
    {
        string trimmed = (text ?? "").Trim();
        if (!trimmed.Contains('.') && trimmed.Count(c => c == ',') == 1)
        {
            trimmed = trimmed.Replace(',', '.');
        }
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    /// <inheritdoc cref="TryParse(string?, out double)"/>
    public static bool TryParse(string? text, out float value)
    {
        bool parsed = TryParse(text, out double number) && float.IsFinite((float)number);
        value = parsed ? (float)number : 0;
        return parsed;
    }
}
