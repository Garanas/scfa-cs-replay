using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Fluent's controls read their brushes through the aliases of <c>Styles/FluentBrushes.axaml</c>;
    /// each theme variant must find its own token there, or the control falls back to Fluent's
    /// colour (or the editor fails to start).
    /// </summary>
    [TestClass]
    public sealed class ThemeTest
    {
        [TestMethod]
        [DataRow("Dark")]
        [DataRow("Light")]
        public Task FluentsBrushesAreTheTokensOfTheVariant(string variant) => HeadlessApp.Run(() =>
        {
            ThemeVariant theme = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            Application application = Application.Current!;

            (string Fluent, string Token)[] aliases =
            [
                ("ButtonBackground", "ControlRaisedBrush"),
                ("TextControlBorderBrushFocused", "SelectedStrokeBrush"),
                ("SystemControlHighlightListAccentLowBrush", "SelectedBrush"),
                ("TabItemHeaderSelectedPipeFill", "SelectedStrokeBrush"),
                ("ToolTipForeground", "TextPrimaryBrush"),
            ];
            foreach ((string fluent, string token) in aliases)
            {
                Assert.IsTrue(application.TryGetResource(fluent, theme, out object? alias), $"{fluent} in {variant}");
                Assert.IsTrue(application.TryGetResource(token, theme, out object? value), $"{token} in {variant}");
                Assert.AreSame(value, alias, $"{fluent} should be {token} in {variant}");
            }
            return Task.CompletedTask;
        });
    }
}
