using System.Net.Http.Headers;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string tokenEndpoint = builder.Configuration["OAuth:TokenEndpoint"] ?? "https://hydra.faforever.com/oauth2/token";

builder.Services.AddHttpClient("Hydra", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("FAForever-Replay-Viewer");
});

// Link previews (ReplayLinkPreview.cs): the metadata line of a replay, read from the vault.
builder.Services.AddHttpClient(ReplayLinkPreview.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("FAForever-Replay-Viewer");
});
builder.Services.AddMemoryCache(options => options.SizeLimit = 10_000);
builder.Services.AddSingleton<ReplayLinkPreview>();
// ... and of the unit and About pages (PageLinkPreview.cs), from the app's own unit data.
builder.Services.AddSingleton<PageLinkPreview>();

// The token proxy is public once deployed: limit it per client address so it cannot be used
// to hammer Hydra. A sign-in takes one request and a refresh one more per hour, so this is
// generous for people. Behind a reverse proxy the address comes from X-Forwarded-For (set
// ASPNETCORE_FORWARDEDHEADERS_ENABLED=true, as the container does).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("token", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
});

WebApplication app = builder.Build();

app.UseRateLimiter();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        // Without a Cache-Control header browsers cache heuristically, which serves stale
        // CSS/JS during development. no-cache forces revalidation (cheap 304s via ETag).
        if (app.Environment.IsDevelopment() || MustRevalidate(context.File.Name))
        {
            context.Context.Response.Headers.CacheControl = "no-cache";
        }
    },
});

// Hydra's token endpoint does not send CORS headers, so a browser app cannot call it
// directly. This endpoint forwards the (public, PKCE-based) token request verbatim and
// returns Hydra's response verbatim. No secrets are involved and nothing is stored.
app.MapPost("/api/oauth/token", async (HttpContext context, IHttpClientFactory httpClientFactory, ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out MediaTypeHeaderValue? contentType)
        || contentType.MediaType != "application/x-www-form-urlencoded")
    {
        context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
        return;
    }

    using HttpRequestMessage upstreamRequest = new(HttpMethod.Post, tokenEndpoint)
    {
        Content = new StreamContent(context.Request.Body),
    };
    upstreamRequest.Content.Headers.ContentType = contentType;

    HttpClient client = httpClientFactory.CreateClient("Hydra");
    try
    {
        using HttpResponseMessage upstreamResponse = await client.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        context.Response.StatusCode = (int)upstreamResponse.StatusCode;
        context.Response.ContentType = upstreamResponse.Content.Headers.ContentType?.ToString() ?? "application/json";
        await upstreamResponse.Content.CopyToAsync(context.Response.Body, cancellationToken);
    }
    catch (HttpRequestException exception)
    {
        logger.LogError(exception, "Token exchange with {TokenEndpoint} failed", tokenEndpoint);
        context.Response.StatusCode = StatusCodes.Status502BadGateway;
        await context.Response.WriteAsJsonAsync(new { error = "upstream_unreachable" }, cancellationToken);
    }
}).WithMetadata(new RequestSizeLimitAttribute(8 * 1024)).RequireRateLimiting("token");

// A replay page is the app's index.html with that replay's link preview (Open Graph tags): link
// unfurlers (Discord, X, Slack, ...) do not run the app. The app itself ignores the tags.
app.MapGet("/replays/{replayId:int}", async (int replayId, HttpContext context, ReplayLinkPreview preview, CancellationToken cancellationToken) =>
{
    if (await preview.RenderAsync(replayId, context, cancellationToken) is not string html)
    {
        return Results.NotFound();
    }

    context.Response.Headers.CacheControl = "no-cache";
    return Results.Content(html, "text/html; charset=utf-8");
});

// Replay pages used to live below /replay/ (a replay, the local file): old links are sent on, query
// included, so the unfurlers find the card too. The app reroutes them as well (OldReplayAddress.razor),
// for the installed app, whose service worker answers navigations without asking the server.
app.MapGet("/replay/{**rest}", (string? rest, HttpContext context) =>
    Results.Redirect($"{context.Request.PathBase}/replays/{rest}{context.Request.QueryString}", permanent: true));

// A map page is index.html with that map's card. The route is needed for the page itself too: a
// folder like theta_passage.v0001 looks like a file name, which the fallback below leaves out. The
// fixed map pages (/maps/featured, ...) are literal routes and win over this one.
app.MapGet("/maps/{folder}", async (string folder, HttpContext context, IWebHostEnvironment environment, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (await LinkPreviewHtml.ReadIndexAsync(environment, cancellationToken) is not string html)
    {
        return Results.NotFound();
    }

    string previewFormat = configuration["LinkPreview:MapPreviewUrl"] ?? "https://content.faforever.com/maps/previews/large/{0}.png";
    if (MapLinkPreview.Card(folder, previewFormat) is { } card)
    {
        html = LinkPreviewHtml.Render(html, card, context.Request.GetEncodedUrl(), $"maps/{folder}");
    }

    context.Response.Headers.CacheControl = "no-cache";
    return Results.Content(html, "text/html; charset=utf-8");
});

// The unit pages get a card about the units in their address, the About pages a fixed one.
foreach (string path in PageLinkPreview.Paths)
{
    app.MapGet(path, async (HttpContext context, PageLinkPreview preview, CancellationToken cancellationToken) =>
    {
        if (await preview.RenderAsync(context, cancellationToken) is not string html)
        {
            return Results.NotFound();
        }

        context.Response.Headers.CacheControl = "no-cache";
        return Results.Content(html, "text/html; charset=utf-8");
    });
}

app.MapFallbackToFile("index.html");

app.Run();

// The files that decide which version of the app a browser runs: the service worker, its list of
// assets and the entry page. Never cached heuristically, or a deploy would go unnoticed.
static bool MustRevalidate(string fileName) =>
    fileName is "index.html" or "service-worker.js" or "service-worker-assets.js" or "manifest.webmanifest";
