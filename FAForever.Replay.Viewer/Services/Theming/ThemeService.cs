using Microsoft.JSInterop;

namespace FAForever.Replay.Viewer.Services.Theming;

/// <summary>
/// Tracks the active faction theme. The theme itself is pure CSS: switching only swaps the
/// <c>data-theme</c> attribute on the root element (see wwwroot/js/app.js) and persists the
/// choice in local storage, which the pre-boot script in index.html reads to avoid a flash.
/// </summary>
public sealed class ThemeService(IJSRuntime js)
{
    public FactionTheme Current { get; private set; } = FactionTheme.Uef;

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        string? storedId = await js.InvokeAsync<string?>("fafReplay.getTheme");
        Current = FactionTheme.FromId(storedId);
        Changed?.Invoke();
    }

    public async Task SetAsync(FactionTheme theme)
    {
        if (theme == Current)
        {
            return;
        }

        Current = theme;
        await js.InvokeVoidAsync("fafReplay.setTheme", theme.Id);
        Changed?.Invoke();
    }
}
