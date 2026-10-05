using System.Text;
using Microsoft.Extensions.FileProviders;

/// <summary>
/// What a link preview shows: the Open Graph card that Discord, X, Slack and other unfurlers read
/// from a page's HTML. <paramref name="Image"/> is an absolute URL; <paramref name="LargeImage"/>
/// asks for the wide card (a map preview) instead of a thumbnail beside the text (a unit icon).
/// </summary>
public sealed record LinkPreviewCard(string Title, string Description, string? Image = null, string? ImageAlt = null, bool LargeImage = false);

/// <summary>
/// Puts a <see cref="LinkPreviewCard"/> into the app's <c>index.html</c>: the block between
/// <c>&lt;!-- Link preview</c> and <c>&lt;!-- /Link preview --&gt;</c> holds the default tags,
/// and a page with a card of its own gets that block replaced. The app itself ignores the tags.
/// </summary>
public static class LinkPreviewHtml
{
    private const string BlockStart = "<!-- Link preview";
    private const string BlockEnd = "<!-- /Link preview -->";

    /// <summary>The app's entry page as it is, or null when the web root has none.</summary>
    public static async Task<string?> ReadIndexAsync(IWebHostEnvironment environment, CancellationToken cancellationToken)
    {
        IFileInfo index = environment.WebRootFileProvider.GetFileInfo("index.html");
        if (!index.Exists)
        {
            return null;
        }

        await using Stream stream = index.CreateReadStream();
        using StreamReader reader = new(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>The page with its default link preview replaced by <paramref name="card"/>.</summary>
    /// <param name="subject">What the card is about, for the comment in the source (e.g. "replay #123").</param>
    public static string Render(string html, LinkPreviewCard card, string pageUrl, string subject)
    {
        StringBuilder tags = new();
        tags.AppendLine($"{BlockStart} (Open Graph) for {subject}, filled in by FAForever.Vault.Server. -->");
        Meta(tags, "og:type", "website");
        Meta(tags, "og:site_name", "Vault of FAF");
        Meta(tags, "og:title", card.Title);
        Meta(tags, "og:description", card.Description);
        Meta(tags, "og:url", pageUrl);
        if (card.Image is { Length: > 0 } image)
        {
            Meta(tags, "og:image", image);
            if (card.ImageAlt is { Length: > 0 } alt)
            {
                Meta(tags, "og:image:alt", alt);
            }
        }
        Meta(tags, "twitter:card", card.Image is not null && card.LargeImage ? "summary_large_image" : "summary", name: true);
        tags.Append("    ").Append(BlockEnd);
        return ReplaceBlock(html, tags.ToString());
    }

    private static void Meta(StringBuilder tags, string property, string content, bool name = false)
        => tags.Append("    <meta ").Append(name ? "name" : "property").Append("=\"").Append(property)
            .Append("\" content=\"").Append(EncodeAttribute(content)).AppendLine("\" />");

    /// <summary>
    /// Escapes a double-quoted attribute value. Only what HTML requires, so names and the separator
    /// stay readable in the source (HtmlEncoder turns "·" and "+" into character references).
    /// </summary>
    private static string EncodeAttribute(string value) => value
        .Replace("&", "&amp;")
        .Replace("\"", "&quot;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\n", "&#10;");

    /// <summary>Replaces the default link preview block of index.html; the page as is when it has none.</summary>
    private static string ReplaceBlock(string html, string block)
    {
        int start = html.IndexOf(BlockStart, StringComparison.Ordinal);
        int end = start < 0 ? -1 : html.IndexOf(BlockEnd, start, StringComparison.Ordinal);
        return end < 0 ? html : string.Concat(html.AsSpan(0, start), block, html.AsSpan(end + BlockEnd.Length));
    }
}
