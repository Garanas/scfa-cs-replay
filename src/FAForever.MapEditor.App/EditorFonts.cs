using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace FAForever.MapEditor.App;

/// <summary>
/// The editor's fonts, IBM Plex Sans and IBM Plex Mono (<c>Assets/Fonts</c>), as the font
/// collection <c>fonts:Editor</c>: the tokens <c>SansFontFamily</c> and <c>MonoFontFamily</c>
/// name them from there. They come with the editor, so it looks the same on every computer.
/// </summary>
public static class EditorFonts
{
    public static readonly Uri Key = new Uri("fonts:Editor");

    public static AppBuilder WithEditorFonts(this AppBuilder builder) => builder.ConfigureFonts(fonts =>
        fonts.AddFontCollection(new EmbeddedFontCollection(Key, new Uri("avares://faf-map-editor/Assets/Fonts"))));
}
