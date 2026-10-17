using System.Net;
using System.Text;
using FAForever.Vault.Viewer.Services.Auth;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;

namespace FAForever.Vault.Viewer.Tests.Account;

/// <summary>
/// The login round trip: a login started on a page returns to that page, query included.
/// The callback is completed in Program.cs, before the router listens: a navigation there changes
/// the address bar but leaves the home page on screen. So the callback must not navigate; it hands
/// the page to App (TakePendingReturnUrl), which navigates once the app runs.
/// </summary>
[TestClass]
public class AuthServiceTest
{
    private const string AppBase = "http://127.0.0.1:5080/";

    [TestMethod]
    public async Task LoginReturnsToThePageItStartedFrom()
    {
        FakeSessionStorage storage = new();

        RecordingNavigationManager page = new(AppBase + "replays/23225104?tab=chat&players=MarcusM");
        await CreateService(storage, page).BeginLoginAsync();
        string authorize = page.Navigations.Single();
        string state = QueryValue(authorize, "state");

        RecordingNavigationManager callback = new(AppBase + "?code=abc&state=" + state);
        AuthService auth = CreateService(storage, callback);
        await auth.InitializeAsync();

        Assert.IsTrue(auth.IsAuthenticated, auth.LastError);
        Assert.AreEqual(0, callback.Navigations.Count, "The callback runs before the router listens: it must leave the navigation to App.");
        Assert.AreEqual("replays/23225104?tab=chat&players=MarcusM", auth.TakePendingReturnUrl());
        Assert.IsNull(auth.TakePendingReturnUrl(), "The page is handed out once.");
    }

    [TestMethod]
    public async Task FailedLoginReturnsToThePageToo()
    {
        FakeSessionStorage storage = new();

        RecordingNavigationManager page = new(AppBase + "maps/search?q=theta");
        await CreateService(storage, page).BeginLoginAsync();
        string state = QueryValue(page.Navigations.Single(), "state");

        RecordingNavigationManager callback = new(AppBase + "?error=access_denied&state=" + state);
        AuthService auth = CreateService(storage, callback);
        await auth.InitializeAsync();

        Assert.IsFalse(auth.IsAuthenticated);
        Assert.AreEqual("access_denied", auth.LastError);
        Assert.AreEqual(0, callback.Navigations.Count);
        Assert.AreEqual("maps/search?q=theta", auth.TakePendingReturnUrl());
    }

    [TestMethod]
    public async Task StartWithoutCallbackHasNoPageToReturnTo()
    {
        RecordingNavigationManager navigation = new(AppBase + "about");
        AuthService auth = CreateService(new FakeSessionStorage(), navigation);
        await auth.InitializeAsync();

        Assert.IsNull(auth.TakePendingReturnUrl());
        Assert.AreEqual(0, navigation.Navigations.Count);
    }

    private static AuthService CreateService(FakeSessionStorage storage, NavigationManager navigation)
    {
        HttpClient http = new(new TokenProxy()) { BaseAddress = new Uri(AppBase) };
        OAuthOptions options = new() { ClientId = "client", RedirectUri = AppBase };
        return new AuthService(http, storage, navigation, options, new ConfigurationBuilder().Build());
    }

    private static string QueryValue(string url, string name)
    {
        string query = new Uri(url).Query.TrimStart('?');
        string pair = query.Split('&').Single(p => p.StartsWith(name + "=", StringComparison.Ordinal));
        return Uri.UnescapeDataString(pair[(name.Length + 1)..]);
    }

    /// <summary>The <c>fafReplay.session*</c> functions of js/app.js, over a dictionary.</summary>
    private sealed class FakeSessionStorage : IJSRuntime
    {
        private readonly Dictionary<string, string> values = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            string key = (string)args![0]!;
            switch (identifier)
            {
                case "fafReplay.sessionGet":
                    return ValueTask.FromResult((TValue)(object?)values.GetValueOrDefault(key)!);
                case "fafReplay.sessionSet":
                    values[key] = (string)args[1]!;
                    return ValueTask.FromResult(default(TValue)!);
                case "fafReplay.sessionRemove":
                    values.Remove(key);
                    return ValueTask.FromResult(default(TValue)!);
                default:
                    throw new NotSupportedException(identifier);
            }
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }

    /// <summary>A navigation manager that stays on its page and records where it was sent.</summary>
    private sealed class RecordingNavigationManager : NavigationManager
    {
        public RecordingNavigationManager(string uri) => Initialize(AppBase, uri);

        public List<string> Navigations { get; } = [];

        protected override void NavigateToCore(string uri, NavigationOptions options) => Navigations.Add(uri);
    }

    /// <summary>The server's token proxy, answering every exchange with a token.</summary>
    private sealed class TokenProxy : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"token","expires_in":3600}""", Encoding.UTF8, "application/json"),
            });
    }
}
