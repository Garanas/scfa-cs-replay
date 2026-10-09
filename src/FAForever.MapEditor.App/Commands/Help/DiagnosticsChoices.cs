using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Takes and remembers the choices about diagnostics, as the Diagnostics page does (user story
/// F-17); what is left out stays.
/// </summary>
/// <param name="Record">Whether the session's logs and metrics are written to disk.</param>
/// <param name="Send">Whether what is recorded is sent to the endpoint.</param>
/// <param name="Endpoint">The OpenTelemetry collector to send to, e.g. <c>http://localhost:4318</c>; empty for none.</param>
[EditorCommand("diagnostics.set", "Choose what diagnostics keep and send", Story = "F-17")]
public sealed record SetDiagnostics(bool? Record = null, bool? Send = null, string? Endpoint = null) : IRequest;

public sealed class SetDiagnosticsHandler(MainViewModel model) : IRequestHandler<SetDiagnostics>
{
    public Task Handle(SetDiagnostics request, CancellationToken cancellationToken)
    {
        model.RecordDiagnostics = request.Record ?? model.RecordDiagnostics;
        model.SendDiagnostics = request.Send ?? model.SendDiagnostics;
        model.DiagnosticsEndpoint = request.Endpoint ?? model.DiagnosticsEndpoint;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Writes what the session logged and measured to its files now (user story F-17).
/// </summary>
[EditorCommand("diagnostics.flush", "Write the diagnostics now", Story = "F-17")]
public sealed record FlushDiagnostics : IRequest;

public sealed class FlushDiagnosticsHandler(MainViewModel model) : IRequestHandler<FlushDiagnostics>
{
    public Task Handle(FlushDiagnostics request, CancellationToken cancellationToken)
    {
        model.Telemetry.Flush();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Sends what is recorded and not sent yet to the endpoint now (user story F-17).
/// </summary>
/// <returns>The lines sent.</returns>
[EditorCommand("diagnostics.upload", "Send the diagnostics now", Story = "F-17")]
public sealed record UploadDiagnostics : IRequest<int>;

public sealed class UploadDiagnosticsHandler(MainViewModel model) : IRequestHandler<UploadDiagnostics, int>
{
    public Task<int> Handle(UploadDiagnostics request, CancellationToken cancellationToken) => model.Telemetry.UploadAsync(cancellationToken);
}
