using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("P-4", "Add a rule that places props from layers")]
[EditorCommand("props.addRule", "Add a rule", Story = "P-4")]
public sealed record AddPropRule : IRequest;
