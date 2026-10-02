namespace FAForever.Replay.Viewer.Services.Theming;

/// <summary>
/// One of the four faction-flavoured colour themes. The <see cref="Id"/> matches the
/// <c>data-theme</c> attribute on the root element and the CSS blocks in Styles/app.css.
/// </summary>
public sealed record FactionTheme(string Id, string DisplayName)
{
    public static readonly FactionTheme Uef = new("uef", "UEF");
    public static readonly FactionTheme Cybran = new("cybran", "Cybran");
    public static readonly FactionTheme Aeon = new("aeon", "Aeon");
    public static readonly FactionTheme Seraphim = new("seraphim", "Seraphim");

    public static readonly IReadOnlyList<FactionTheme> All = [Uef, Cybran, Aeon, Seraphim];

    public static FactionTheme FromId(string? id)
        => All.FirstOrDefault(theme => theme.Id == id) ?? Uef;
}
