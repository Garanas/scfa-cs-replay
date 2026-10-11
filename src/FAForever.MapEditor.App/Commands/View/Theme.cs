using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Styling;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Switches the window between the dark and the light theme at run time, to look at the light
/// variant of the tokens (the editor starts dark; every token has a value in both variants, which
/// <c>ThemeTest</c> checks). Nothing is saved.
/// </summary>
/// <param name="Variant"><c>dark</c>, <c>light</c> or <c>system</c> (what the operating system uses).</param>
/// <returns>The variant asked for and the one in effect.</returns>
[EditorCommand("view.theme", "Switch between the dark and the light theme", Story = "F-1")]
public sealed record SetTheme(string Variant) : IRequest<JsonObject>;

public sealed class SetThemeHandler : IRequestHandler<SetTheme, JsonObject>
{
    public Task<JsonObject> Handle(SetTheme request, CancellationToken cancellationToken)
    {
        Application application = Application.Current ?? throw new InvalidOperationException("The editor has no application to theme");
        application.RequestedThemeVariant = request.Variant.Trim().ToLowerInvariant() switch
        {
            "dark" => ThemeVariant.Dark,
            "light" => ThemeVariant.Light,
            "system" or "default" => ThemeVariant.Default,
            _ => throw new ArgumentException($"There is no theme variant '{request.Variant}': dark, light or system"),
        };
        return Task.FromResult(new JsonObject
        {
            ["variant"] = request.Variant,
            ["actual"] = application.ActualThemeVariant.ToString(),
        });
    }
}
