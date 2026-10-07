using FAForever.FileFormats.Lua;

namespace FAForever.FileFormats.Blueprints
{
    /// <summary>
    /// A prop: trees, rocks, wrecks (<c>PropBlueprint { ... }</c>, mostly in <c>env/</c>); its id is
    /// the lower case path.
    /// </summary>
    public sealed record BlueprintProp : BlueprintEntity
    {
        public required BlueprintPropDefense Defense { get; init; }

        public required BlueprintPropEconomy Economy { get; init; }

        public required BlueprintPropDisplay Display { get; init; }

        public BlueprintFootprint? Footprint { get; init; }

        /// <summary>
        /// The Lua class that implements it, e.g. <c>Tree</c>, <c>TreeGroup</c>.
        /// </summary>
        public string? ScriptClass { get; init; }

        public string? ScriptModule { get; init; }

        internal static BlueprintProp Read(LuaTableReader t, string blueprintId, string source) => new BlueprintProp
        {
            Raw = t.Table,
            BlueprintId = blueprintId,
            Source = source,
            Description = t.String("Description"),
            Categories = t.Strings("Categories"),
            StrategicIconName = t.String("StrategicIconName"),
            SizeX = t.Number("SizeX"),
            SizeY = t.Number("SizeY"),
            SizeZ = t.Number("SizeZ"),
            Defense = t.SectionOrEmpty("Defense", BlueprintPropDefense.Read),
            Economy = t.SectionOrEmpty("Economy", BlueprintPropEconomy.Read),
            Display = t.SectionOrEmpty("Display", BlueprintPropDisplay.Read),
            Footprint = t.Section("Footprint", BlueprintFootprint.Read),
            ScriptClass = t.String("ScriptClass"),
            ScriptModule = t.String("ScriptModule"),
        };
    }

    public sealed record BlueprintPropDefense : BlueprintTable
    {
        public double? Health { get; init; }

        public double? MaxHealth { get; init; }

        internal static BlueprintPropDefense Read(LuaTableReader t) => new BlueprintPropDefense
        {
            Raw = t.Table,
            Health = t.Number("Health"),
            MaxHealth = t.Number("MaxHealth"),
        };
    }

    public sealed record BlueprintPropEconomy : BlueprintTable
    {
        public double? ReclaimMassMax { get; init; }

        public double? ReclaimEnergyMax { get; init; }

        public double? ReclaimTime { get; init; }

        internal static BlueprintPropEconomy Read(LuaTableReader t) => new BlueprintPropEconomy
        {
            Raw = t.Table,
            ReclaimMassMax = t.Number("ReclaimMassMax"),
            ReclaimEnergyMax = t.Number("ReclaimEnergyMax"),
            ReclaimTime = t.Number("ReclaimTime"),
        };
    }

    public sealed record BlueprintPropDisplay : BlueprintTable
    {
        public BlueprintDisplayMesh? Mesh { get; init; }

        public string? MeshBlueprint { get; init; }

        public double? UniformScale { get; init; }

        internal static BlueprintPropDisplay Read(LuaTableReader t) => new BlueprintPropDisplay
        {
            Raw = t.Table,
            Mesh = t.Section("Mesh", BlueprintDisplayMesh.Read),
            MeshBlueprint = t.String("MeshBlueprint"),
            UniformScale = t.Number("UniformScale"),
        };
    }
}
