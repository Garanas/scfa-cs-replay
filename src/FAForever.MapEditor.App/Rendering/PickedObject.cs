namespace FAForever.MapEditor.App.Rendering;

/// <summary>
/// The kinds of objects a map holds that the views can point at.
/// </summary>
public enum PickedKind
{
    Marker,
    Prop,
    Decal,
    Unit,
}

/// <summary>
/// An object of the map, as picking finds it under a point of the 3D view and as the 3D view
/// highlights it: markers, props and decals by their <c>Id</c> (which the editor gives them while
/// the map is open), units by their <c>Name</c>.
/// </summary>
/// <param name="Id">The marker's, prop's or decal's id; 0 for a unit.</param>
/// <param name="Name">The unit's name, e.g. <c>UNIT_5</c>; null for the other kinds.</param>
public sealed record PickedObject(PickedKind Kind, int Id, string? Name = null)
{
    public static PickedObject Marker(int id) => new PickedObject(PickedKind.Marker, id);

    public static PickedObject Prop(int id) => new PickedObject(PickedKind.Prop, id);

    public static PickedObject Decal(int id) => new PickedObject(PickedKind.Decal, id);

    public static PickedObject Unit(string name) => new PickedObject(PickedKind.Unit, 0, name);

    public override string ToString() => Kind == PickedKind.Unit ? $"unit {Name}" : $"{Kind.ToString().ToLowerInvariant()} {Id}";
}
