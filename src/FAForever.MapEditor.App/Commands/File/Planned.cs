using MediatR;

namespace FAForever.MapEditor.App.Commands;


[Incomplete("M-8", "Upload the exported map to the FAF vault")]
[EditorCommand("file.upload", "Upload to the vault...", Story = "M-8")]
public sealed record Upload : IRequest;
