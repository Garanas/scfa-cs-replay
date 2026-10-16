using FAForever.Vault.Viewer;
using FAForever.Vault.Viewer.Services.Analytics;
using FAForever.Vault.Viewer.Services.Api;
using FAForever.Vault.Viewer.Services.Auth;
using FAForever.Vault.Viewer.Services.Maps;
using FAForever.Vault.Viewer.Services.Players;
using FAForever.Vault.Viewer.Services.Replays;
using FAForever.Vault.Viewer.Services.Theming;
using FAForever.Vault.Viewer.Services.Units;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<ReplayLoadingService>();
builder.Services.AddScoped<ReplaySessionState>();
builder.Services.AddScoped<LocalReplayLoader>();
builder.Services.AddScoped<LocalReplayFolder>();
builder.Services.AddScoped<ReplayMapLookup>();
builder.Services.AddScoped<UnitIconAtlas>();
builder.Services.AddScoped<UnitDatabase>();
builder.Services.AddScoped<UnitCardService>();
builder.Services.AddScoped<MapLoader>();
builder.Services.AddScoped<MapImages>();
builder.Services.AddScoped<AnalyticsService>();

builder.Services.AddSingleton(builder.Configuration.GetSection("OAuth").Get<OAuthOptions>() ?? new OAuthOptions());
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<FafApiClient>();

// The Players pages: the real API, or recorded documents when appsettings.Sample.json is loaded
// (ASPNETCORE_ENVIRONMENT=Sample on the server).
if (builder.Configuration["Players:SampleData"] == "true")
{
    builder.Services.AddScoped<IPlayersApi, SamplePlayersApi>();
}
else
{
    builder.Services.AddScoped<IPlayersApi, FafPlayersApi>();
}
builder.Services.AddScoped<AuthenticationStateProvider, FafAuthenticationStateProvider>();
builder.Services.AddAuthorizationCore();

WebAssemblyHost host = builder.Build();

// Sync the theme switcher with the theme that the pre-boot script in index.html applied.
await host.Services.GetRequiredService<ThemeService>().InitializeAsync();

// Restore a persisted login and complete the OAuth callback when we just returned from Hydra.
await host.Services.GetRequiredService<AuthService>().InitializeAsync();

// Count visits when analytics is configured (production only).
await host.Services.GetRequiredService<AnalyticsService>().InitializeAsync();

await host.RunAsync();
