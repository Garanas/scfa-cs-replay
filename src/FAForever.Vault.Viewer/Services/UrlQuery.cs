using Microsoft.AspNetCore.Components;

namespace FAForever.Vault.Viewer.Services;

/// <summary>
/// Reads query parameters of the current URL. Panels keep their shareable state (active
/// tab, filters) in the query string: reading happens here, writing goes through the
/// framework's NavigationManager.GetUriWithQueryParameter.
/// </summary>
public static class UrlQuery
{
    public static string? Get(NavigationManager navigation, string name) => Get(new Uri(navigation.Uri), name);

    public static string? Get(Uri uri, string name)
    {
        string query = uri.Query;
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            string key = Uri.UnescapeDataString(separator < 0 ? pair : pair[..separator]);
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return separator < 0 ? string.Empty : Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
            }
        }

        return null;
    }
}
