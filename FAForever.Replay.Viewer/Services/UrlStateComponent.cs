using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace FAForever.Replay.Viewer.Services;

/// <summary>
/// Base class for components that derive state from the URL (see AGENTS.md § Shareable
/// view state). A query-only navigation does not re-render a page whose parameters are
/// unchanged value types - Blazor skips the whole subtree - so filter changes written by
/// one component would never reach its siblings. This base subscribes to LocationChanged
/// and re-runs <see cref="ComponentBase.OnParametersSet"/> (the derivation hook these
/// components already use) followed by a re-render.
/// </summary>
public abstract class UrlStateComponent : ComponentBase, IDisposable
{
    [Inject] protected NavigationManager Navigation { get; set; } = default!;

    protected override void OnInitialized()
        => Navigation.LocationChanged += OnLocationChanged;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
        => _ = InvokeAsync(() =>
        {
            OnParametersSet();
            StateHasChanged();
        });

    public void Dispose()
        => Navigation.LocationChanged -= OnLocationChanged;
}
