using MediatR;

namespace FAForever.MapEditor.App.Commands;

[Incomplete("T-7", "Generate walkability from the height layers and compare it with the last time")]
[EditorCommand("generate.walkability", "Walkability", Story = "T-7")]
public sealed record GenerateWalkability : IRequest;

[Incomplete("P-4", "Generate the props of every rule again; props placed by hand stay")]
[EditorCommand("generate.props", "Props from rules", Story = "P-4")]
public sealed record GenerateProps : IRequest;

[Incomplete("K-2", "Generate masks from slope, height, distance to water or other masks")]
[EditorCommand("generate.masks", "Masks from rules", Story = "K-2")]
public sealed record GenerateMasks : IRequest;

[Incomplete("S-4", "Generate the normal map and the preview from the terrain")]
[EditorCommand("generate.normalMap", "Normal map and preview", Story = "S-4")]
public sealed record GenerateNormalMap : IRequest;

[Incomplete("F-7", "Generate everything whose inputs changed")]
[EditorCommand("generate.stale", "Everything out of date", Story = "F-7")]
public sealed record GenerateStale : IRequest;
