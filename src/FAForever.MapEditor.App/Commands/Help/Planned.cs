using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("F-17", "Describe a problem and bundle it with the recent sessions' logs and metrics into a file to attach to an issue, or send it")]
[EditorCommand("help.reportProblem", "Report a problem...", Story = "F-17")]
public sealed record ReportProblem : IRequest;
