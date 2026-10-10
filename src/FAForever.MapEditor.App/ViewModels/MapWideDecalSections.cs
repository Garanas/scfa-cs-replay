namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The Decals panel's three map-wide slots (albedo, normal, shadow) above the groups: a preview, the
/// source and Generate now each; choosing one shows it in the inspector.
/// </summary>
public sealed record MapWideDecalsSection() : PanelSection("Map-wide decals");

/// <summary>
/// The Decals inspector's part of the chosen map-wide slot, while nothing is selected.
/// </summary>
public sealed record MapWideDecalDetailsSection() : PanelSection("Map-wide decal");
