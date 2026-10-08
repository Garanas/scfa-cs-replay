using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;

namespace FAForever.MapEditor.App;

public sealed class App : Application
{
    private AutomationServer? _automation;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow window = new MainWindow();
            MainViewModel model = new MainViewModel(new WindowDialogs(window));
            window.DataContext = model;
            desktop.MainWindow = window;

            StartOptions options = Program.Options;
            if (options.Folder is string folder)
            {
                model.Open(folder);
            }
            if (options.AutomationPort is int port)
            {
                _automation = new AutomationServer(window, model, port);
                _automation.Start();
            }
            desktop.Exit += (_, _) => _automation?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
