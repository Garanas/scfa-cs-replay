using System.Numerics;

namespace FAForever.MapEditor.App.Rendering;

/// <summary>
/// A unit drawn as a ghost in the 3D view before it is placed (user story U-12): the place tool's
/// unit at every place a click or release would fill, or a preset's units around the mouse, each
/// standing on the terrain and turned as it will be placed.
/// </summary>
/// <param name="Blueprint">The unit's blueprint, whose model is drawn.</param>
/// <param name="Placement">Where it stands, as a unit's placement (<see cref="ObjectPlacement.Unit"/>).</param>
/// <param name="Blocked">Whether placing it there breaks a rule (it overlaps a structure, or the ground is ruled out): drawn in the danger colour.</param>
public readonly record struct UnitGhost(string Blueprint, Matrix4x4 Placement, bool Blocked);
