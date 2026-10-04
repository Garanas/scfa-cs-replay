using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace FAForever.Vault.Viewer.Services.Auth;

/// <summary>
/// Exposes the <see cref="AuthService"/> session to Blazor's standard authorization
/// primitives (AuthorizeView, CascadingAuthenticationState).
/// </summary>
public sealed class FafAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly AuthService auth;

    public FafAuthenticationStateProvider(AuthService auth)
    {
        this.auth = auth;
        this.auth.Changed += OnAuthChanged;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        ClaimsPrincipal principal = auth.Session is { } session
            ? new ClaimsPrincipal(new ClaimsIdentity(BuildClaims(session), authenticationType: "faforever"))
            : new ClaimsPrincipal(new ClaimsIdentity());

        return Task.FromResult(new AuthenticationState(principal));
    }

    private static IEnumerable<Claim> BuildClaims(AuthSession session)
    {
        if (session.UserId is { } userId)
        {
            yield return new Claim(ClaimTypes.NameIdentifier, userId);
        }

        yield return new Claim(ClaimTypes.Name, session.UserName ?? "FAForever user");
    }

    private void OnAuthChanged() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    public void Dispose() => auth.Changed -= OnAuthChanged;
}
