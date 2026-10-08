using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App;

public sealed class App : Application
{
    private AutomationServer? _automation;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Diagnostics.Performance.StartWatchdog();
            MainWindow window = new MainWindow();
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            window.DataContext = model;
            desktop.MainWindow = window;

            StartOptions options = Program.Options;
            _ = model.StartAsync(UserSettings.Default, options.Folder);
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
                }
            }
            desktop.Exit += (_, _) => _automation?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
