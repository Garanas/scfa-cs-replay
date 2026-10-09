using System.Text.RegularExpressions;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The editor's look lives in its tokens and style classes (<c>Styles/</c>): the views and
    /// controls name what a thing is, never how it looks. These tests read the sources and name
    /// every loose value, so the look stays consistent and can change in one place.
    /// </summary>
    [TestClass]
    public sealed partial class StyleRulesTest
    {
        private static string App => Path.Combine(HeadlessApp.RepositoryFolder, "src", "FAForever.MapEditor.App");

        private static IEnumerable<(string File, int Line, string Text)> Lines(string pattern, params string[] folders) =>
            folders.SelectMany(folder => Directory.EnumerateFiles(Path.Combine(App, folder), pattern, SearchOption.AllDirectories))
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .SelectMany(file => File.ReadLines(file).Select((text, index) => (Path.GetRelativePath(App, file), index + 1, text)));

        [TestMethod]
        public void ViewsUseTokensAndClassesNotLooseValues()
        {
            string[] found = [.. Lines("*.axaml", "Views", "Controls")
                .Where(line => LooseXamlValue().IsMatch(line.Text))
                .Select(line => $"{line.File}:{line.Line}: {line.Text.Trim()}")];

            CollectionAssert.AreEqual(Array.Empty<string>(), found,
                "a colour, font size or weight, opacity or distance belongs in Styles/Tokens.axaml or a class of Styles/Classes.axaml");
        }

        [TestMethod]
        public void CodeDrawsWithTokensNotFixedColours()
        {
            string[] found = [.. Lines("*.cs", "Views", "Controls")
                .Where(line => FixedColour().IsMatch(line.Text))
                .Select(line => $"{line.File}:{line.Line}: {line.Text.Trim()}")];

            CollectionAssert.AreEqual(Array.Empty<string>(), found,
                "read colours from the tokens (TryFindResource); only colours that come from the map, such as a marker's, are built in code");
        }

        /// <summary>
        /// <c>#rrggbb</c>, <c>FontSize="12"</c>, <c>FontWeight="..."</c>, <c>Opacity="0.7"</c>, and a
        /// <c>Margin</c>, <c>Padding</c> or <c>Spacing</c> other than zero.
        /// </summary>
        [GeneratedRegex("\"#[0-9a-fA-F]{3,8}\"|FontSize=\"[0-9]|FontWeight=\"|Opacity=\"[0-9]|(Margin|Padding|Spacing)=\"[0-9,. ]*[1-9]")]
        private static partial Regex LooseXamlValue();

        /// <summary>
        /// A colour made of numbers in code, a named colour, or the 3D view's clear colour as numbers.
        /// </summary>
        [GeneratedRegex(@"Color\.FromRgb\(\s*[0-9]|Color\.FromArgb\(\s*[0-9]+\s*,\s*[0-9]|Color\.Parse\(|Brushes\.[A-Z]|Colors\.[A-Z]|ClearColor\(\s*[0-9]")]
        private static partial Regex FixedColour();
    }
}
