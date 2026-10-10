using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("P-4", "Generate the props of every rule again; props placed by hand stay")]
[EditorCommand("generate.props", "Props from rules", Story = "P-4")]
public sealed record GenerateProps : IRequest;

[Incomplete("K-2", "Generate masks from slope, height, distance to water or other masks")]
[EditorCommand("generate.masks", "Masks from rules", Story = "K-2")]
public sealed record GenerateMasks : IRequest;
