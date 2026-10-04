using Microsoft.AspNetCore.Components;
using FAForever.Vault.Viewer.Services;

namespace FAForever.Vault.Viewer.Features.Units;

/// <summary>
/// Writes the selection of the unit database to the URL: the selected unit (<c>unit</c>) and the
/// units being compared (<c>compare</c>, at most <see cref="MaxCompared"/>). Always with
/// <c>replace: true</c>, like every selection, so browsing units does not fill the history.
/// </summary>
public static class UnitLinks
{
    public const string UnitParameter = "unit";
    public const string CompareParameter = "compare";

    /// <summary>The comparison stays readable up to this many units side by side.</summary>
    public const int MaxCompared = 6;

    public static void Set(NavigationManager navigation, string parameter, string? value)
        => navigation.NavigateTo(navigation.GetUriWithQueryParameter(parameter, value), replace: true);

    /// <summary>Shows a unit in the detail panel, or closes the panel with null.</summary>
    public static void Select(NavigationManager navigation, string? blueprintId)
        => Set(navigation, UnitParameter, blueprintId);

    /// <summary>The compared unit ids according to the URL, in order, without duplicates.</summary>
    public static IReadOnlyList<string> Compared(NavigationManager navigation)
        => (UrlQuery.Get(navigation, CompareParameter) ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => id.ToLowerInvariant())
            .Distinct()
            .Take(MaxCompared)
            .ToList();

    /// <summary>Adds a unit to the comparison or takes it out.</summary>
    public static void ToggleCompare(NavigationManager navigation, string blueprintId)
    {
        List<string> compared = Compared(navigation).ToList();
        if (!compared.Remove(blueprintId) && compared.Count < MaxCompared)
        {
            compared.Add(blueprintId);
        }

        Set(navigation, CompareParameter, compared.Count > 0 ? string.Join(',', compared) : null);
    }
}
