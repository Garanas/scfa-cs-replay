using System.Net;
using System.Text;

namespace FAForever.MapEditor.Cli.Guide
{
    /// <summary>
    /// Writes the guide's pages (Markdown and HTML) from <c>guide.json</c>, without an editor: an
    /// index and one page per tour. Every picture has alt text from the step's title; the pictures
    /// carry no text, the text is on the page.
    /// </summary>
    public static class GuidePages
    {
        private const string Stylesheet = """
            :root { color-scheme: dark; --bg: #14171c; --panel: #1d2128; --line: #2e343d; --text: #e3e7ed; --muted: #9aa3b0; --link: #6cb8f6; }
            * { box-sizing: border-box; }
            body { margin: 0; background: var(--bg); color: var(--text); font: 16px/1.6 system-ui, sans-serif; }
            main { max-width: 56rem; margin: 0 auto; padding: 2rem 1rem 4rem; }
            h1 { font-size: 1.9rem; margin: 0 0 .5rem; }
            h2 { font-size: 1.3rem; margin: 2.5rem 0 .5rem; }
            a { color: var(--link); }
            img { display: block; max-width: 100%; height: auto; border: 1px solid var(--line); border-radius: 6px; margin: .75rem 0; }
            .step { border-top: 1px solid var(--line); padding-top: .5rem; }
            .more, .meta, .done { color: var(--muted); }
            .done { border-left: 3px solid var(--link); padding-left: .75rem; }
            nav { margin-bottom: 1.5rem; }
            ul.tours { list-style: none; padding: 0; }
            ul.tours li { background: var(--panel); border: 1px solid var(--line); border-radius: 6px; padding: .75rem 1rem; margin: .5rem 0; }
            """;

        /// <summary>
        /// Writes <c>index.md</c>, <c>index.html</c> and <c>&lt;tour&gt;.md</c> and <c>.html</c> per
        /// tour into the folder.
        /// </summary>
        public static void Write(GuideManifest manifest, string folder)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "index.md"), IndexMarkdown(manifest));
            File.WriteAllText(Path.Combine(folder, "index.html"), IndexHtml(manifest));
            foreach (GuideTour tour in manifest.Tours)
            {
                File.WriteAllText(Path.Combine(folder, tour.Id + ".md"), TourMarkdown(manifest, tour));
                File.WriteAllText(Path.Combine(folder, tour.Id + ".html"), TourHtml(manifest, tour));
            }
        }

        public static string IndexMarkdown(GuideManifest manifest)
        {
            StringBuilder text = new StringBuilder();
            text.Append("# The map editor: a guide\n\n");
            text.Append("A guide to the FAF map editor's window, made from the editor's own tours. ");
            text.Append("Help, Tours in the editor plays each one over the window.\n\n");
            foreach (GuideTour tour in Ordered(manifest))
            {
                text.Append($"- [{tour.Title}]({tour.Id}.md): {tour.Summary}\n");
            }
            text.Append('\n').Append(Footer(manifest)).Append('\n');
            return text.ToString();
        }

        public static string IndexHtml(GuideManifest manifest)
        {
            StringBuilder body = new StringBuilder();
            body.Append("<h1>The map editor: a guide</h1>\n");
            body.Append("<p>A guide to the FAF map editor's window, made from the editor's own tours. Help, Tours in the editor plays each one over the window.</p>\n");
            body.Append("<ul class=\"tours\">\n");
            foreach (GuideTour tour in Ordered(manifest))
            {
                body.Append($"<li><a href=\"{Attribute(tour.Id)}.html\">{Html(tour.Title)}</a><br>{Html(tour.Summary)}</li>\n");
            }
            body.Append("</ul>\n");
            body.Append($"<p class=\"meta\">{Html(Footer(manifest))}</p>\n");
            return Document("The map editor: a guide", body.ToString());
        }

        public static string TourMarkdown(GuideManifest manifest, GuideTour tour)
        {
            StringBuilder text = new StringBuilder();
            text.Append($"# {tour.Title}\n\n[All tours](index.md)\n\n{tour.Summary}\n\n");
            if (tour.Overview is not null)
            {
                text.Append($"![{Alt(tour.Title + ": an overview of the window, its parts numbered by the steps")}]({tour.Overview})\n\n");
            }
            foreach (GuideStep step in tour.Steps)
            {
                text.Append($"## {step.Number}. {step.Title}\n\n");
                if (step.Image is not null)
                {
                    text.Append($"![{Alt(step.Title)}]({step.Image})\n\n");
                }
                text.Append(step.Text).Append("\n\n");
                if (!string.IsNullOrWhiteSpace(step.More))
                {
                    text.Append(step.More).Append("\n\n");
                }
                if (!string.IsNullOrWhiteSpace(step.Done))
                {
                    text.Append($"> {step.Done}\n\n");
                }
                if (step.After is not null)
                {
                    text.Append($"![{Alt(step.Title + ", after the step")}]({step.After})\n\n");
                }
            }
            text.Append(Footer(manifest)).Append('\n');
            return text.ToString();
        }

        public static string TourHtml(GuideManifest manifest, GuideTour tour)
        {
            StringBuilder body = new StringBuilder();
            body.Append("<nav><a href=\"index.html\">All tours</a></nav>\n");
            body.Append($"<h1>{Html(tour.Title)}</h1>\n<p>{Html(tour.Summary)}</p>\n");
            if (tour.Overview is not null)
            {
                body.Append($"<img src=\"{Attribute(tour.Overview)}\" alt=\"{Attribute(Alt(tour.Title + ": an overview of the window, its parts numbered by the steps"))}\">\n");
            }
            foreach (GuideStep step in tour.Steps)
            {
                body.Append($"<section class=\"step\" id=\"{Attribute(step.Id)}\">\n<h2>{step.Number}. {Html(step.Title)}</h2>\n");
                if (step.Image is not null)
                {
                    body.Append($"<img src=\"{Attribute(step.Image)}\" alt=\"{Attribute(Alt(step.Title))}\">\n");
                }
                body.Append($"<p>{Html(step.Text)}</p>\n");
                if (!string.IsNullOrWhiteSpace(step.More))
                {
                    body.Append($"<p class=\"more\">{Html(step.More)}</p>\n");
                }
                if (!string.IsNullOrWhiteSpace(step.Done))
                {
                    body.Append($"<p class=\"done\">{Html(step.Done)}</p>\n");
                }
                if (step.After is not null)
                {
                    body.Append($"<img src=\"{Attribute(step.After)}\" alt=\"{Attribute(Alt(step.Title + ", after the step"))}\">\n");
                }
                body.Append("</section>\n");
            }
            body.Append($"<p class=\"meta\">{Html(Footer(manifest))}</p>\n");
            return Document(tour.Title, body.ToString());
        }

        private static IEnumerable<GuideTour> Ordered(GuideManifest manifest) =>
            manifest.Tours.OrderBy(tour => tour.Order).ThenBy(tour => tour.Id, StringComparer.Ordinal);

        private static string Footer(GuideManifest manifest) =>
            $"Made with the map editor {manifest.Editor.Version ?? "(unknown version)"} on {manifest.Editor.Generated}, window {manifest.Window[0]} by {manifest.Window[1]}, dark theme.";

        /// <summary>
        /// The alt text of a picture: the step's title, one line, without characters that end a
        /// Markdown image's text.
        /// </summary>
        public static string Alt(string text) => text.Replace('[', '(').Replace(']', ')').Replace('\n', ' ').Trim();

        private static string Html(string text) => WebUtility.HtmlEncode(text);

        private static string Attribute(string text) => WebUtility.HtmlEncode(text);

        private static string Document(string title, string body) =>
            "<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n"
            + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n"
            + $"<title>{Html(title)}</title>\n<style>\n{Stylesheet}\n</style>\n</head>\n<body>\n<main>\n{body}</main>\n</body>\n</html>\n";
    }
}
