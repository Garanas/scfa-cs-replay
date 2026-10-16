using FAForever.Vault.Viewer.Services;
using Microsoft.AspNetCore.Components;

namespace FAForever.Vault.Viewer.Features.Players;

/// <summary>
/// The base of the Players components that keep view state in the query string: reads one
/// parameter, writes one or several (a default, or an empty value, removes the parameter) with
/// <c>replace: true</c>, as AGENTS.md prescribes. Every write also drops <c>page</c>, since a
/// changed filter starts on the first page.
/// </summary>
public abstract class PlayerUrlComponent : UrlStateComponent
{
    /// <summary>The value of a query parameter, or null when it is absent or empty.</summary>
    protected string? Read(string name) => UrlQuery.Get(Navigation, name) is { Length: > 0 } value ? value : null;

    /// <summary>Sets one parameter; a null value, empty or the default removes it from the address.</summary>
    protected void Write(string name, string? value, string? defaultValue = null, bool resetPage = true)
        => Write(new Dictionary<string, string?> { [name] = value }, defaultValue is null ? null : new Dictionary<string, string?> { [name] = defaultValue }, resetPage);

    /// <summary>Sets several parameters at once, so one navigation carries them all.</summary>
    protected void Write(IDictionary<string, string?> values, IDictionary<string, string?>? defaults = null, bool resetPage = true)
    {
        var next = new Dictionary<string, object?>();
        foreach ((string name, string? value) in values)
        {
            bool isDefault = string.IsNullOrEmpty(value) || (defaults is not null && defaults.TryGetValue(name, out string? d) && d == value);
            next[name] = isDefault ? null : value;
        }

        if (resetPage && !next.ContainsKey("page"))
        {
            next["page"] = null;
        }

        Navigation.NavigateTo(Navigation.GetUriWithQueryParameters(next), replace: true);
    }

    /// <summary>The CSS of a chip: a choice among a few, pressed when chosen. 44 px high for fingers.</summary>
    protected static string ChipClass(bool chosen) => chosen
        ? "inline-flex min-h-11 cursor-pointer items-center rounded-full border border-primary bg-primary/15 px-3.5 text-sm font-semibold text-primary transition"
        : "inline-flex min-h-11 cursor-pointer items-center rounded-full border border-edge bg-raised px-3.5 text-sm text-ink-muted transition hover:border-primary hover:text-ink";
}
