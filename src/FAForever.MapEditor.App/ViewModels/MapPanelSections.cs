namespace FAForever.MapEditor.App.ViewModels;

// The parts of the mode panels and the inspector for the map and its atmosphere (user stories M-1,
// M-5, M-6, W-1, L-1, S-2), each with its template in Views/ModePanel.axaml or Views/Inspector.axaml
// and its view model a part of MainViewModel (MainViewModel.MapPanels.cs).

/// <summary>
/// The Map mode's panel: what players see of the map (user story M-1), <see cref="MainViewModel.MapInformation"/>.
/// </summary>
public sealed record MapInformationSection() : PanelSection("Information");

/// <summary>
/// The Map mode's inspector: the map's size, players and preview (user story M-1).
/// </summary>
public sealed record MapFactsSection() : PanelSection("Map");

/// <summary>
/// The Map mode's areas by name (user story M-5), <see cref="MainViewModel.AreaList"/>.
/// </summary>
public sealed record AreasSection() : PanelSection("Areas");

/// <summary>
/// The map's script and the other files of its folder (user story M-6), <see cref="MainViewModel.ScriptFiles"/>.
/// </summary>
public sealed record ScriptSection() : PanelSection("Script and files");

/// <summary>
/// The Water mode's panel: on or off, the elevations and colours (user story W-1), <see cref="MainViewModel.WaterSettings"/>.
/// </summary>
public sealed record WaterSection() : PanelSection("Water");

/// <summary>
/// The Water mode's inspector: the sun on the water, reflection, textures and wave generators (user story W-1).
/// </summary>
public sealed record WaterDetailsSection() : PanelSection("Surface and waves");

/// <summary>
/// The Lighting mode's panel: the sun, colours, bloom and fog (user story L-1), <see cref="MainViewModel.LightingSettings"/>.
/// </summary>
public sealed record LightingSection() : PanelSection("Lighting");

/// <summary>
/// The Paint mode's layers with their textures: add, remove, reorder (user story S-2), <see cref="MainViewModel.LayerTextures"/>.
/// </summary>
public sealed record LayerTexturesSection() : PanelSection("Layer textures");

/// <summary>
/// The Paint mode's inspector: the chosen layer's textures and scales (user story S-2).
/// </summary>
public sealed record LayerSection() : PanelSection("Layer");
