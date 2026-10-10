using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The layers rules read (user stories K-2, P-4), as the rule editors offer them: the strata as the
/// paint tool names them (<c>Layer 3: SandLight</c>) and the masks for tools (<c>Mask: forest</c>).
/// A rule keeps a layer as the library names it (<c>stratum:3</c>, <c>forest</c>, <see cref="MaskRuleSources"/>).
/// </summary>
public static class RuleLayers
{
    public const string MaskPrefix = "Mask: ";

    /// <summary>
    /// What the choices say for the map's strata and masks.
    /// </summary>
    public static IReadOnlyList<string> Choices(IEnumerable<Stratum> strata, MapDocument? document, string? except = null) =>
        [.. strata.Select(stratum => stratum.Name), .. (document?.Masks.Keys ?? []).Where(name => name != except).Select(name => MaskPrefix + name)];

    /// <summary>
    /// What a choice says for a layer a rule names; the name itself for a layer the map has not.
    /// </summary>
    public static string Label(string source, IEnumerable<Stratum> strata)
    {
        if (MaskRuleSources.IsStratum(source, out int layer))
        {
            return strata.FirstOrDefault(stratum => stratum.Layer == layer)?.Name ?? $"Layer {layer}";
        }
        return source.Length == 0 ? "" : MaskPrefix + source;
    }

    /// <summary>
    /// The layer a choice or an argument names, as a rule keeps it: <c>Layer 3...</c>,
    /// <c>stratum:3</c>, <c>Mask: forest</c> or the mask's name.
    /// </summary>
    public static string Parse(string text)
    {
        text = text.Trim();
        if (text.StartsWith(MaskPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return text[MaskPrefix.Length..].Trim();
        }
        if (MaskRuleSources.IsStratum(text, out int stratum))
        {
            return MaskRuleSources.Stratum(stratum);
        }
        if (text.StartsWith("Layer ", StringComparison.OrdinalIgnoreCase))
        {
            string number = new([.. text["Layer ".Length..].TakeWhile(char.IsAsciiDigit)]);
            if (int.TryParse(number, out int layer))
            {
                return MaskRuleSources.Stratum(layer);
            }
        }
        return text;
    }
}
