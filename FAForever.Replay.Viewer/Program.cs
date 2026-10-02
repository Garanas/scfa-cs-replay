using FAForever.Replay.Viewer;
using FAForever.Replay.Viewer.Services.Api;
using FAForever.Replay.Viewer.Services.Auth;
using FAForever.Replay.Viewer.Services.Replays;
using FAForever.Replay.Viewer.Services.Theming;
using FAForever.Replay.Viewer.Services.Units;
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
builder.Services.AddScoped<UnitIconAtlas>();

builder.Services.AddSingleton(builder.Configuration.GetSection("OAuth").Get<OAuthOptions>() ?? new OAuthOptions());
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<FafApiClient>();
builder.Services.AddScoped<AuthenticationStateProvider, FafAuthenticationStateProvider>();
builder.Services.AddAuthorizationCore();

WebAssemblyHost host = builder.Build();

// Sync the theme switcher with the theme that the pre-boot script in index.html applied.
await host.Services.GetRequiredService<ThemeService>().InitializeAsync();

// Restore a persisted login and complete the OAuth callback when we just returned from Hydra.
await host.Services.GetRequiredService<AuthService>().InitializeAsync();

await host.RunAsync();
