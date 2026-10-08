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
            else if (!args[i].StartsWith("--", StringComparison.Ordinal))
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
        Options = StartOptions.Parse(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
