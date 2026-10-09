using System.Reflection;
using FAForever.MapEditor.App.ViewModels;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Where the commands are put together: MediatR with the handlers and the pipeline, and what the
/// handlers take. The one place that uses a container; nothing else asks it for anything.
/// </summary>
public static class CommandServices
{
    /// <summary>
    /// The registry of the commands in <paramref name="assemblies"/> (the editor's own, and a
    /// test's), sent to their handlers through MediatR.
    /// </summary>
    /// <param name="dialogs">The questions the handlers ask, refused while a command runs without a person.</param>
    public static CommandRegistry Create(MainViewModel model, DialogGuard dialogs, params Assembly[] assemblies)
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(model);
        // the parts are made after the registry (the palette needs it), so asked for when a handler is
        services.AddSingleton(_ => model.Diagnostics);
        services.AddSingleton(_ => model.Setup);
        services.AddSingleton(_ => model.VersionControl);
        services.AddSingleton(_ => model.Palette);
        services.AddSingleton<IEditorDialogs>(dialogs);
        services.AddSingleton<CurrentEdit>();
        // MediatR logs only that it runs without a licence key, which a desktop application needs
        // not set (its README); the editor's own logging is the Console and telemetry
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssemblies(assemblies);
            configuration.AddOpenBehavior(typeof(UndoStepBehavior<,>));
        });
        ServiceProvider provider = services.BuildServiceProvider();

        CommandRegistry registry = new CommandRegistry(provider.GetRequiredService<ISender>(), dialogs, model.Meets);
        foreach (Assembly assembly in assemblies)
        {
            registry.AddFrom(assembly);
        }
        return registry;
    }
}
