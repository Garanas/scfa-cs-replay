using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace FAForever.Replay.Viewer.Services.Analytics;

/// <summary>
/// Counts visits with a self-hosted GoatCounter (compose.yaml in Garanas/jipwijnia-vps), when
/// <c>Analytics:GoatCounter</c> is configured (only in appsettings.Production.json, so nothing
/// is counted during development). GoatCounter sets no cookies and stores no personal data.
///
/// GoatCounter only counts the page load by itself; in this single-page app the navigations are
/// counted here: a page view when the path changes (<c>/replay/123</c>, without the query, so
/// filters do not split a page into many), and an event when the replay tab changes
/// (<c>tab/buildorder</c>), which is how we learn which tabs are used.
/// </summary>
public sealed class AnalyticsService(IJSRuntime js, NavigationManager navigation, IConfiguration configuration) : IDisposable
{
    private string? countedPath;
    private string? countedTab;
    private bool enabled;

    public async Task InitializeAsync()
    {
        string? endpoint = configuration["Analytics:GoatCounter"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return;
        }

        try
        {
            await js.InvokeVoidAsync("fafReplay.analyticsInit", endpoint);
            await CountAsync(navigation.Uri);
        }
        catch (JSException)
        {
            return;
        }

        enabled = true;
        navigation.LocationChanged += OnLocationChanged;
    }

    private async void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        try
        {
            await CountAsync(args.Location);
        }
        catch (JSException)
        {
            // Analytics must never break the app (e.g. the script is blocked).
        }
    }

    private async Task CountAsync(string location)
    {
        Uri uri = new(location);
        string path = "/" + navigation.ToBaseRelativePath(uri.GetLeftPart(UriPartial.Path));
        string? tab = path.StartsWith("/replay/", StringComparison.Ordinal) ? UrlQuery.Get(uri, "tab") ?? "overview" : null;

        if (path != countedPath)
        {
            countedPath = path;
            countedTab = null;
            await js.InvokeVoidAsync("fafReplay.analyticsCount", path, null, false);
        }

        if (tab is not null && tab != countedTab)
        {
            countedTab = tab;
            await js.InvokeVoidAsync("fafReplay.analyticsCount", $"tab/{tab.ToLowerInvariant()}", $"Tab: {tab}", true);
        }
    }

    public void Dispose()
    {
        if (enabled)
        {
            navigation.LocationChanged -= OnLocationChanged;
        }
    }
}
