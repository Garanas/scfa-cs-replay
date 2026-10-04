using Microsoft.JSInterop;

namespace FAForever.Vault.Viewer.Services.Theming;

/// <summary>
/// Tracks the two appearance preferences: the faction, which colours the accents (buttons,
/// links, navigation, borders), and the mode, which picks the light or dark page. Both are pure
/// CSS: changing one only swaps an attribute on the root element (see wwwroot/js/app.js) and
/// persists the choice in local storage, which the pre-boot script in index.html reads to avoid
/// a flash.
/// </summary>
public sealed class ThemeService(IJSRuntime js)
{
    public FactionTheme Current { get; private set; } = FactionTheme.Cybran;

    public ThemeMode Mode { get; private set; } = ThemeMode.Auto;

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        string? storedId = await js.InvokeAsync<string?>("fafReplay.getFaction");
        Current = FactionTheme.FromId(storedId);
        Mode = ParseMode(await js.InvokeAsync<string?>("fafReplay.getMode"));
        Changed?.Invoke();
    }

    public async Task SetAsync(FactionTheme theme)
    {
        if (theme == Current)
        {
            return;
        }

        Current = theme;
        await js.InvokeVoidAsync("fafReplay.setFaction", theme.Id);
        Changed?.Invoke();
    }

    /// <summary>Moves to the next mode: auto, light, dark and back to auto.</summary>
    public async Task CycleModeAsync()
    {
        Mode = Next(Mode);
        await js.InvokeVoidAsync("fafReplay.setMode", Mode.ToString().ToLowerInvariant());
        Changed?.Invoke();
    }

    public static ThemeMode Next(ThemeMode mode) => mode switch
    {
        ThemeMode.Auto => ThemeMode.Light,
        ThemeMode.Light => ThemeMode.Dark,
        _ => ThemeMode.Auto,
    };

    private static ThemeMode ParseMode(string? value) => value switch
    {
        "light" => ThemeMode.Light,
        "dark" => ThemeMode.Dark,
        _ => ThemeMode.Auto,
    };
}

/// <summary>Light or dark page; <see cref="Auto"/> follows the system setting.</summary>
public enum ThemeMode
{
    Auto,
    Light,
    Dark,
}
