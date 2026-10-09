using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("L-2", "Apply the lighting of a biome")]
[EditorCommand("lighting.preset", "Apply a lighting preset...", Story = "L-2")]
public sealed record ApplyLightingPreset : IRequest;
