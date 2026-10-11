using Avalonia;

namespace FAForever.MapEditor.App;

/// <summary>
/// What the editor is started with: <c>faf-map-editor [folder] [--automation port]</c>. The folder
/// is a project or a map folder of the game; with <c>--automation</c> the editor takes commands
/// over HTTP on <c>http://localhost:port/</c> (see <see cref="Automation.AutomationServer"/>).
/// </summary>
public sealed record StartOptions(string? Folder, int? AutomationPort)
{
    public static StartOptions Parse(string[] args)
    {
        string? folder = null;
        int? port = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--automation" && i + 1 < args.Length && int.TryParse(args[i + 1], out int value))
            {
                port = value;
                i++;
            }
            else if (!string.IsNullOrWhiteSpace(args[i]) && !args[i].StartsWith("--", StringComparison.Ordinal))
            {
                folder = args[i];
            }
        }
        return new StartOptions(folder, port);
    }
}

public static class Program
{
    public static StartOptions Options { get; private set; } = new StartOptions(null, null);

    [STAThread]
    public static void Main(string[] args)
    {
        // numbers show and are read alike on every computer
        ViewModels.EditorCulture.Use();
        Options = StartOptions.Parse(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// An environment variable that, set to 1, lets the window draw with Mesa's software OpenGL
    /// (llvmpipe) on Linux, which Avalonia turns down by default: for the 3D view on a computer or
    /// container without a graphics driver (a virtual display, CI). Slow, but the 3D view works.
    /// </summary>
    public const string SoftwareOpenGlVariable = "FAF_MAP_EDITOR_SOFTWARE_GL";

    public static AppBuilder BuildAvaloniaApp()
    {
        AppBuilder builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithEditorFonts()
            .LogToTrace();
        return Environment.GetEnvironmentVariable(SoftwareOpenGlVariable) == "1"
            ? builder.With(new X11PlatformOptions { GlxRendererBlacklist = [] })
            : builder;
    }
}
