using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App;

public sealed class App : Application
{
    private AutomationServer? _automation;
    private Telemetry? _telemetry;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            UserSettings settings = UserSettings.Default;
            Telemetry telemetry = _telemetry = new Telemetry(Telemetry.DefaultFolder, settings.Telemetry);
            ILogger log = telemetry.CreateLogger("Editor");
            WatchCrashes(telemetry, log);
            Performance.Logger = telemetry.CreateLogger("Performance");
            Controls.AssetThumbnail.Cache.Logger = telemetry.CreateLogger("Assets");
            Performance.StartWatchdog();
            log.LogInformation("Started the editor {Version} on {System}", AssetConverter.EditorVersion, System.Runtime.InteropServices.RuntimeInformation.OSDescription);

            MainWindow window = new MainWindow();
            MainViewModel model = new MainViewModel(new WindowDialogs(window), telemetry);
            window.DataContext = model;
            desktop.MainWindow = window;

            StartOptions options = Program.Options;
            _ = model.StartAsync(settings, options.Folder);
            if (options.AutomationPort is int port)
            {
                _automation = new AutomationServer(window, model, port);
                try
                {
                    _automation.Start();
                }
                catch (System.Net.HttpListenerException exception)
                {
                    // another editor listens there: this one works without automation
                    _automation = null;
                    model.Status = $"Automation is off: port {port} is in use ({exception.Message})";
                    model.Logger("Automation").LogWarning("Automation is off: port {Port} is in use ({Reason})", port, exception.Message);
                }
            }
            desktop.Exit += (_, _) =>
            {
                _automation?.Dispose();
                log.LogInformation("Closed after {Minutes} minutes", Math.Round((DateTimeOffset.Now - telemetry.Started).TotalMinutes, 1));
                _telemetry?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Logs what stops the editor or a task that nobody waits for, and writes it to disk at once.
    /// </summary>
    private static void WatchCrashes(Telemetry telemetry, ILogger log)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Exception? exception = e.ExceptionObject as Exception;
            log.LogCritical(exception, "The editor stopped: {Reason}", exception?.Message);
            telemetry.Flush();
        };
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            log.LogCritical(e.Exception, "The window stopped: {Reason}", e.Exception.Message);
            telemetry.Flush();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
            log.LogError(e.Exception, "A task failed unnoticed: {Reason}", e.Exception.InnerException?.Message ?? e.Exception.Message);
    }
}
