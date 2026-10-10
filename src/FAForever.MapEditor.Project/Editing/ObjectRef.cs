using System.Globalization;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// The kinds of things that lie on a map and can be selected.
    /// </summary>
    public enum ObjectKind
    {
        Marker,
        Prop,
        Decal,
        Unit,
        Area,
    }

    /// <summary>
    /// A choice of kinds, for what a click may select. Path nodes are markers, chosen apart from the
    /// others: a map has thousands, and they are often hidden.
    /// </summary>
    [Flags]
    public enum ObjectKinds
    {
        None = 0,
        /// <summary>
        /// Markers other than path nodes: spawns, deposits, AI markers, weather.
        /// </summary>
        Markers = 1,
        PathNodes = 2,
        Props = 4,
        Decals = 8,
        Units = 16,
        Areas = 32,
        All = Markers | PathNodes | Props | Decals | Units | Areas,
    }

    /// <summary>
    /// Names one thing on the map: a marker, prop or decal by its id (in memory only), a unit or an
    /// area by its name (units are named per map, <c>UNIT_5</c>). It stays valid across versions of
    /// the document, as long as the thing exists.
    /// </summary>
    /// <remarks>
    /// Written as text it is the kind and the id or name: <c>marker:12</c>, <c>prop:5</c>,
    /// <c>decal:3</c>, <c>unit:UNIT_5</c>, <c>area:AREA_1</c> (<see cref="Parse"/>).
    /// </remarks>
    public readonly record struct ObjectRef
    {
        private ObjectRef(ObjectKind kind, int id, string? name)
        {
            Kind = kind;
            Id = id;
            Name = name;
        }

        public ObjectKind Kind { get; }

        /// <summary>
        /// The id of a marker, prop or decal; 0 for a unit or an area.
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// The name of a unit or an area; null for the others.
        /// </summary>
        public string? Name { get; }

        public static ObjectRef Marker(int id) => new ObjectRef(ObjectKind.Marker, id, null);

        public static ObjectRef Prop(int id) => new ObjectRef(ObjectKind.Prop, id, null);

        public static ObjectRef Decal(int id) => new ObjectRef(ObjectKind.Decal, id, null);

        public static ObjectRef Unit(string name) => new ObjectRef(ObjectKind.Unit, 0, name);

        public static ObjectRef Area(string name) => new ObjectRef(ObjectKind.Area, 0, name);

        /// <summary>
        /// Whether a choice of kinds holds this one; a marker counts as a path node when <paramref name="isPathNode"/>.
        /// </summary>
        public bool IsIn(ObjectKinds kinds, bool isPathNode = false) => (kinds & KindsOf(Kind, isPathNode)) != 0;

        /// <summary>
        /// The choice of kinds that holds only this kind.
        /// </summary>
        public static ObjectKinds KindsOf(ObjectKind kind, bool isPathNode = false) => kind switch
        {
            ObjectKind.Marker => isPathNode ? ObjectKinds.PathNodes : ObjectKinds.Markers,
            ObjectKind.Prop => ObjectKinds.Props,
            ObjectKind.Decal => ObjectKinds.Decals,
            ObjectKind.Unit => ObjectKinds.Units,
            _ => ObjectKinds.Areas,
        };

        public override string ToString() =>
            $"{Kind.ToString().ToLowerInvariant()}:{Name ?? Id.ToString(CultureInfo.InvariantCulture)}";

        /// <summary>
        /// Reads what <see cref="ToString"/> writes.
        /// </summary>
        /// <exception cref="FormatException">The text names no kind, or no id or name.</exception>
        public static ObjectRef Parse(string text)
        {
            int colon = text.IndexOf(':');
            if (colon <= 0 || colon == text.Length - 1 || !Enum.TryParse(text[..colon], ignoreCase: true, out ObjectKind kind) || !Enum.IsDefined(kind))
            {
                throw new FormatException($"'{text}' names nothing on the map: write marker:12, prop:5, decal:3, unit:UNIT_5 or area:AREA_1");
            }
            string key = text[(colon + 1)..];
            if (kind is ObjectKind.Unit or ObjectKind.Area)
            {
                return new ObjectRef(kind, 0, key);
            }
            return int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                ? new ObjectRef(kind, id, null)
                : throw new FormatException($"'{text}': a {kind.ToString().ToLowerInvariant()} is named by its id, a number");
        }
    }
}
