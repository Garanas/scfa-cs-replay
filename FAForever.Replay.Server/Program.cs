using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string tokenEndpoint = builder.Configuration["OAuth:TokenEndpoint"] ?? "https://hydra.faforever.com/oauth2/token";

builder.Services.AddHttpClient("Hydra", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("FAForever-Replay-Viewer");
});

WebApplication app = builder.Build();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

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
}).WithMetadata(new RequestSizeLimitAttribute(8 * 1024));

app.MapFallbackToFile("index.html");

app.Run();
