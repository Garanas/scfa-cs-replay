namespace FAForever.FileFormats.Blueprints
{
    /// <summary>
    /// A unit (<c>UnitBlueprint { ... }</c> in <c>units/*/*_unit.bp</c>); its id is the short one,
    /// e.g. <c>uel0101</c>.
    /// </summary>
    public sealed record BlueprintUnit : BlueprintEntity
    {
        public double? BuildIconSortPriority { get; init; }

        /// <summary>
        /// 1 to 3 for the categories <c>TECH1</c> to <c>TECH3</c>, 4 for <c>EXPERIMENTAL</c>, null otherwise
        /// (e.g. commanders, which have <c>COMMAND</c>).
        /// </summary>
        public int? TechLevel =>
            HasCategory("EXPERIMENTAL") ? 4 : HasCategory("TECH3") ? 3 : HasCategory("TECH2") ? 2 : HasCategory("TECH1") ? 1 : null;

        public required BlueprintUnitGeneral General { get; init; }

        public required BlueprintUnitEconomy Economy { get; init; }

        public required BlueprintUnitDefense Defense { get; init; }

        public required BlueprintUnitPhysics Physics { get; init; }

        public required BlueprintUnitIntel Intel { get; init; }

        public required BlueprintUnitDisplay Display { get; init; }

        /// <summary>
        /// Flight characteristics; only aircraft (and a few hovering units) have them.
        /// </summary>
        public BlueprintUnitAir? Air { get; init; }

        public BlueprintUnitTransport? Transport { get; init; }

        public BlueprintFootprint? Footprint { get; init; }

        /// <summary>
        /// The kills needed per veterancy level, in the old kill-based system (the <c>Veteran</c> table).
        /// </summary>
        public BlueprintUnitVeterancy? Veteran { get; init; }

        /// <summary>
        /// The mass to kill per veterancy level, relative to the previous level; takes precedence
        /// over <see cref="VeteranMassMult"/>.
        /// </summary>
        public IReadOnlyList<double> VeteranMass { get; init; } = [];

        /// <summary>
        /// The multiple of the unit's own mass cost needed per veterancy level.
        /// </summary>
        public double? VeteranMassMult { get; init; }

        public BlueprintUnitWreckage? Wreckage { get; init; }

        /// <summary>
        /// The weapons in file order (the <c>Weapon</c> table).
        /// </summary>
        public IReadOnlyList<BlueprintWeapon> Weapons { get; init; } = [];

        /// <summary>
        /// Enhancements by name, e.g. <c>AdvancedEngineering</c>, including the "...Remove" entries
        /// that undo one. The <c>Slots</c> entry of the table is not an enhancement and left out.
        /// </summary>
        public IReadOnlyDictionary<string, BlueprintUnitEnhancement> Enhancements { get; init; } = new Dictionary<string, BlueprintUnitEnhancement>();

        /// <summary>
        /// Sounds by event, e.g. <c>Destroyed</c>, <c>UISelection</c>.
        /// </summary>
        public IReadOnlyDictionary<string, BlueprintSound> Audio { get; init; } = new Dictionary<string, BlueprintSound>();

        internal static BlueprintUnit Read(BlueprintTableReader t, string blueprintId, string source) => new BlueprintUnit
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
            BuildIconSortPriority = t.Number("BuildIconSortPriority"),
            General = t.SectionOrEmpty("General", BlueprintUnitGeneral.Read),
            Economy = t.SectionOrEmpty("Economy", BlueprintUnitEconomy.Read),
            Defense = t.SectionOrEmpty("Defense", BlueprintUnitDefense.Read),
            Physics = t.SectionOrEmpty("Physics", BlueprintUnitPhysics.Read),
            Intel = t.SectionOrEmpty("Intel", BlueprintUnitIntel.Read),
            Display = t.SectionOrEmpty("Display", BlueprintUnitDisplay.Read),
            Air = t.Section("Air", BlueprintUnitAir.Read),
            Transport = t.Section("Transport", BlueprintUnitTransport.Read),
            Footprint = t.Section("Footprint", BlueprintFootprint.Read),
            Veteran = t.Section("Veteran", BlueprintUnitVeterancy.Read),
            VeteranMass = t.Numbers("VeteranMass"),
            VeteranMassMult = t.Number("VeteranMassMult"),
            Wreckage = t.Section("Wreckage", BlueprintUnitWreckage.Read),
            Weapons = t.List("Weapon", BlueprintWeapon.Read),
            Enhancements = t.Dictionary("Enhancements", BlueprintUnitEnhancement.Read, name => name != "Slots"),
            Audio = t.Sounds("Audio"),
        };
    }

    public sealed record BlueprintUnitGeneral : BlueprintTable
    {
        /// <summary>
        /// The unit's name, e.g. <c>&lt;LOC uel0001_name&gt;Black Ops ACU</c> (with its localisation tag).
        /// </summary>
        public string? UnitName { get; init; }

        /// <summary>
        /// E.g. <c>Command</c>, <c>Direct Fire</c>, <c>Construction</c>.
        /// </summary>
        public string? Category { get; init; }

        /// <summary>
        /// E.g. <c>RULEUC_Commander</c>.
        /// </summary>
        public string? Classification { get; init; }

        /// <summary>
        /// <c>RULEUTL_Basic</c>, <c>RULEUTL_Advanced</c>, <c>RULEUTL_Secret</c> or <c>RULEUTL_Experimental</c>.
        /// Most units leave it out: use <see cref="BlueprintUnit.TechLevel"/>, from the categories.
        /// </summary>
        public string? TechLevel { get; init; }

        /// <summary>
        /// <c>UEF</c>, <c>Aeon</c>, <c>Cybran</c> or <c>Seraphim</c>.
        /// </summary>
        public string? FactionName { get; init; }

        /// <summary>
        /// The background of the build icon: <c>land</c>, <c>air</c>, <c>sea</c> or <c>amph</c>.
        /// </summary>
        public string? Icon { get; init; }

        public double? CapCost { get; init; }

        public string? UpgradesFrom { get; init; }

        public string? UpgradesTo { get; init; }

        public string? UpgradesFromBase { get; init; }

        /// <summary>
        /// The orders the unit accepts, e.g. <c>RULEUCC_Move</c>, <c>RULEUCC_Attack</c>.
        /// </summary>
        public IReadOnlySet<string> CommandCaps { get; init; } = new HashSet<string>();

        /// <summary>
        /// The toggles the unit has, e.g. <c>RULEUTC_ShieldToggle</c>.
        /// </summary>
        public IReadOnlySet<string> ToggleCaps { get; init; } = new HashSet<string>();

        internal static BlueprintUnitGeneral Read(BlueprintTableReader t) => new BlueprintUnitGeneral
        {
            Raw = t.Table,
            UnitName = t.String("UnitName"),
            Category = t.String("Category"),
            Classification = t.String("Classification"),
            TechLevel = t.String("TechLevel"),
            FactionName = t.String("FactionName"),
            Icon = t.String("Icon"),
            CapCost = t.Number("CapCost"),
            UpgradesFrom = t.String("UpgradesFrom"),
            UpgradesTo = t.String("UpgradesTo"),
            UpgradesFromBase = t.String("UpgradesFromBase"),
            CommandCaps = t.Flags("CommandCaps"),
            ToggleCaps = t.Flags("ToggleCaps"),
        };
    }

    public sealed record BlueprintUnitEconomy : BlueprintTable
    {
        public double? BuildCostEnergy { get; init; }

        public double? BuildCostMass { get; init; }

        /// <summary>
        /// Build time in build-power seconds: a builder with build rate 10 takes BuildTime / 10 seconds.
        /// </summary>
        public double? BuildTime { get; init; }

        /// <summary>
        /// The build power of the unit itself.
        /// </summary>
        public double? BuildRate { get; init; }

        /// <summary>
        /// Category expressions of what it can build, e.g. <c>BUILTBYTIER1ENGINEER UEF</c>.
        /// </summary>
        public IReadOnlyList<string> BuildableCategory { get; init; } = [];

        public double? MaxBuildDistance { get; init; }

        public double? ProductionPerSecondEnergy { get; init; }

        public double? ProductionPerSecondMass { get; init; }

        public double? MaintenanceConsumptionPerSecondEnergy { get; init; }

        public double? MaintenanceConsumptionPerSecondMass { get; init; }

        public double? StorageEnergy { get; init; }

        public double? StorageMass { get; init; }

        internal static BlueprintUnitEconomy Read(BlueprintTableReader t) => new BlueprintUnitEconomy
        {
            Raw = t.Table,
            BuildCostEnergy = t.Number("BuildCostEnergy"),
            BuildCostMass = t.Number("BuildCostMass"),
            BuildTime = t.Number("BuildTime"),
            BuildRate = t.Number("BuildRate"),
            BuildableCategory = t.Strings("BuildableCategory"),
            MaxBuildDistance = t.Number("MaxBuildDistance"),
            ProductionPerSecondEnergy = t.Number("ProductionPerSecondEnergy"),
            ProductionPerSecondMass = t.Number("ProductionPerSecondMass"),
            MaintenanceConsumptionPerSecondEnergy = t.Number("MaintenanceConsumptionPerSecondEnergy"),
            MaintenanceConsumptionPerSecondMass = t.Number("MaintenanceConsumptionPerSecondMass"),
            StorageEnergy = t.Number("StorageEnergy"),
            StorageMass = t.Number("StorageMass"),
        };
    }

    public sealed record BlueprintUnitDefense : BlueprintTable
    {
        /// <summary>
        /// E.g. <c>Normal</c>, <c>Commander</c>, <c>Structure</c>, <c>Experimental</c>.
        /// </summary>
        public string? ArmorType { get; init; }

        public double? Health { get; init; }

        public double? MaxHealth { get; init; }

        /// <summary>
        /// Health regenerated per second.
        /// </summary>
        public double? RegenRate { get; init; }

        public BlueprintUnitShield? Shield { get; init; }

        public double? AirThreatLevel { get; init; }

        public double? EconomyThreatLevel { get; init; }

        public double? SubThreatLevel { get; init; }

        public double? SurfaceThreatLevel { get; init; }

        internal static BlueprintUnitDefense Read(BlueprintTableReader t) => new BlueprintUnitDefense
        {
            Raw = t.Table,
            ArmorType = t.String("ArmorType"),
            Health = t.Number("Health"),
            MaxHealth = t.Number("MaxHealth"),
            RegenRate = t.Number("RegenRate"),
            Shield = t.Section("Shield", BlueprintUnitShield.Read),
            AirThreatLevel = t.Number("AirThreatLevel"),
            EconomyThreatLevel = t.Number("EconomyThreatLevel"),
            SubThreatLevel = t.Number("SubThreatLevel"),
            SurfaceThreatLevel = t.Number("SurfaceThreatLevel"),
        };
    }

    public sealed record BlueprintUnitShield : BlueprintTable
    {
        public double? ShieldMaxHealth { get; init; }

        public double? ShieldRegenRate { get; init; }

        /// <summary>
        /// Seconds after taking damage before the shield regenerates.
        /// </summary>
        public double? ShieldRegenStartTime { get; init; }

        /// <summary>
        /// Seconds to come back up after collapsing.
        /// </summary>
        public double? ShieldRechargeTime { get; init; }

        /// <summary>
        /// The diameter of a bubble shield.
        /// </summary>
        public double? ShieldSize { get; init; }

        public double? ShieldVerticalOffset { get; init; }

        public double? ShieldEnergyDrainRechargeTime { get; init; }

        public bool? PersonalShield { get; init; }

        public bool? PersonalBubble { get; init; }

        public bool? AntiArtilleryShield { get; init; }

        public bool? TransportShield { get; init; }

        internal static BlueprintUnitShield Read(BlueprintTableReader t) => new BlueprintUnitShield
        {
            Raw = t.Table,
            ShieldMaxHealth = t.Number("ShieldMaxHealth"),
            ShieldRegenRate = t.Number("ShieldRegenRate"),
            ShieldRegenStartTime = t.Number("ShieldRegenStartTime"),
            ShieldRechargeTime = t.Number("ShieldRechargeTime"),
            ShieldSize = t.Number("ShieldSize"),
            ShieldVerticalOffset = t.Number("ShieldVerticalOffset"),
            ShieldEnergyDrainRechargeTime = t.Number("ShieldEnergyDrainRechargeTime"),
            PersonalShield = t.Bool("PersonalShield"),
            PersonalBubble = t.Bool("PersonalBubble"),
            AntiArtilleryShield = t.Bool("AntiArtilleryShield"),
            TransportShield = t.Bool("TransportShield"),
        };
    }

    public sealed record BlueprintUnitPhysics : BlueprintTable
    {
        /// <summary>
        /// E.g. <c>RULEUMT_Land</c>, <c>RULEUMT_Air</c>, <c>RULEUMT_Amphibious</c>, <c>RULEUMT_None</c> (structures).
        /// </summary>
        public string? MotionType { get; init; }

        public string? AltMotionType { get; init; }

        /// <summary>
        /// The layers it can be built on, e.g. <c>LAYER_Land</c>.
        /// </summary>
        public IReadOnlySet<string> BuildOnLayerCaps { get; init; } = new HashSet<string>();

        public double? MaxSpeed { get; init; }

        public double? MaxSpeedReverse { get; init; }

        public double? MaxAcceleration { get; init; }

        public double? MaxBrake { get; init; }

        public double? TurnRate { get; init; }

        public double? TurnRadius { get; init; }

        public double? Elevation { get; init; }

        public double? FuelUseTime { get; init; }

        public double? FuelRechargeRate { get; init; }

        public double? LandSpeedMultiplier { get; init; }

        public double? WaterSpeedMultiplier { get; init; }

        public double? SkirtSizeX { get; init; }

        public double? SkirtSizeZ { get; init; }

        internal static BlueprintUnitPhysics Read(BlueprintTableReader t) => new BlueprintUnitPhysics
        {
            Raw = t.Table,
            MotionType = t.String("MotionType"),
            AltMotionType = t.String("AltMotionType"),
            BuildOnLayerCaps = t.Flags("BuildOnLayerCaps"),
            MaxSpeed = t.Number("MaxSpeed"),
            MaxSpeedReverse = t.Number("MaxSpeedReverse"),
            MaxAcceleration = t.Number("MaxAcceleration"),
            MaxBrake = t.Number("MaxBrake"),
            TurnRate = t.Number("TurnRate"),
            TurnRadius = t.Number("TurnRadius"),
            Elevation = t.Number("Elevation"),
            FuelUseTime = t.Number("FuelUseTime"),
            FuelRechargeRate = t.Number("FuelRechargeRate"),
            LandSpeedMultiplier = t.Number("LandSpeedMultiplier"),
            WaterSpeedMultiplier = t.Number("WaterSpeedMultiplier"),
            SkirtSizeX = t.Number("SkirtSizeX"),
            SkirtSizeZ = t.Number("SkirtSizeZ"),
        };
    }

    public sealed record BlueprintUnitIntel : BlueprintTable
    {
        public double? VisionRadius { get; init; }

        public double? WaterVisionRadius { get; init; }

        public double? RadarRadius { get; init; }

        public double? SonarRadius { get; init; }

        public double? OmniRadius { get; init; }

        public double? RemoteViewingRadius { get; init; }

        public bool? RadarStealth { get; init; }

        public bool? SonarStealth { get; init; }

        public bool? Cloak { get; init; }

        public double? RadarStealthFieldRadius { get; init; }

        public double? SonarStealthFieldRadius { get; init; }

        public double? CloakFieldRadius { get; init; }

        public double? JammerBlips { get; init; }

        /// <summary>
        /// The <c>JamRadius = { Min = ..., Max = ... }</c> range of a jammer.
        /// </summary>
        public double? JamRadiusMin { get; init; }

        public double? JamRadiusMax { get; init; }

        internal static BlueprintUnitIntel Read(BlueprintTableReader t)
        {
            BlueprintTableReader jam = t.SectionOrEmpty("JamRadius", jam => jam);
            return new BlueprintUnitIntel
            {
                Raw = t.Table,
                VisionRadius = t.Number("VisionRadius"),
                WaterVisionRadius = t.Number("WaterVisionRadius"),
                RadarRadius = t.Number("RadarRadius"),
                SonarRadius = t.Number("SonarRadius"),
                OmniRadius = t.Number("OmniRadius"),
                RemoteViewingRadius = t.Number("RemoteViewingRadius"),
                RadarStealth = t.Bool("RadarStealth"),
                SonarStealth = t.Bool("SonarStealth"),
                Cloak = t.Bool("Cloak"),
                RadarStealthFieldRadius = t.Number("RadarStealthFieldRadius"),
                SonarStealthFieldRadius = t.Number("SonarStealthFieldRadius"),
                CloakFieldRadius = t.Number("CloakFieldRadius"),
                JammerBlips = t.Number("JammerBlips"),
                JamRadiusMin = jam.Number("Min"),
                JamRadiusMax = jam.Number("Max"),
            };
        }
    }

    public sealed record BlueprintUnitDisplay : BlueprintTable
    {
        /// <summary>
        /// The abilities listed in the unit view, e.g. <c>&lt;LOC ability_radar&gt;Radar</c>.
        /// </summary>
        public IReadOnlyList<string> Abilities { get; init; } = [];

        public BlueprintDisplayMesh? Mesh { get; init; }

        public string? MeshBlueprint { get; init; }

        public double? UniformScale { get; init; }

        internal static BlueprintUnitDisplay Read(BlueprintTableReader t) => new BlueprintUnitDisplay
        {
            Raw = t.Table,
            Abilities = t.Strings("Abilities"),
            Mesh = t.Section("Mesh", BlueprintDisplayMesh.Read),
            MeshBlueprint = t.String("MeshBlueprint"),
            UniformScale = t.Number("UniformScale"),
        };
    }

    public sealed record BlueprintUnitAir : BlueprintTable
    {
        public bool? CanFly { get; init; }

        public bool? Winged { get; init; }

        public double? MaxAirspeed { get; init; }

        public double? MinAirspeed { get; init; }

        public double? TurnSpeed { get; init; }

        public double? CombatTurnSpeed { get; init; }

        public double? EngageDistance { get; init; }

        public double? BreakOffDistance { get; init; }

        public double? StartTurnDistance { get; init; }

        public double? TransportHoverHeight { get; init; }

        internal static BlueprintUnitAir Read(BlueprintTableReader t) => new BlueprintUnitAir
        {
            Raw = t.Table,
            CanFly = t.Bool("CanFly"),
            Winged = t.Bool("Winged"),
            MaxAirspeed = t.Number("MaxAirspeed"),
            MinAirspeed = t.Number("MinAirspeed"),
            TurnSpeed = t.Number("TurnSpeed"),
            CombatTurnSpeed = t.Number("CombatTurnSpeed"),
            EngageDistance = t.Number("EngageDistance"),
            BreakOffDistance = t.Number("BreakOffDistance"),
            StartTurnDistance = t.Number("StartTurnDistance"),
            TransportHoverHeight = t.Number("TransportHoverHeight"),
        };
    }

    public sealed record BlueprintUnitTransport : BlueprintTable
    {
        /// <summary>
        /// How many slots the unit takes in a transport: 1 (small) to 4.
        /// </summary>
        public int? TransportClass { get; init; }

        public int? Class1Capacity { get; init; }

        public int? Class2AttachSize { get; init; }

        public int? Class3AttachSize { get; init; }

        public int? StorageSlots { get; init; }

        public double? DockingSlots { get; init; }

        public bool? AirClass { get; init; }

        public bool? CanFireFromTransport { get; init; }

        public double? RepairRate { get; init; }

        internal static BlueprintUnitTransport Read(BlueprintTableReader t) => new BlueprintUnitTransport
        {
            Raw = t.Table,
            TransportClass = t.Integer("TransportClass"),
            Class1Capacity = t.Integer("Class1Capacity"),
            Class2AttachSize = t.Integer("Class2AttachSize"),
            Class3AttachSize = t.Integer("Class3AttachSize"),
            StorageSlots = t.Integer("StorageSlots"),
            DockingSlots = t.Number("DockingSlots"),
            AirClass = t.Bool("AirClass"),
            CanFireFromTransport = t.Bool("CanFireFromTransport"),
            RepairRate = t.Number("RepairRate"),
        };
    }

    public sealed record BlueprintUnitVeterancy : BlueprintTable
    {
        public int? Level1 { get; init; }

        public int? Level2 { get; init; }

        public int? Level3 { get; init; }

        public int? Level4 { get; init; }

        public int? Level5 { get; init; }

        internal static BlueprintUnitVeterancy Read(BlueprintTableReader t) => new BlueprintUnitVeterancy
        {
            Raw = t.Table,
            Level1 = t.Integer("Level1"),
            Level2 = t.Integer("Level2"),
            Level3 = t.Integer("Level3"),
            Level4 = t.Integer("Level4"),
            Level5 = t.Integer("Level5"),
        };
    }

    public sealed record BlueprintUnitWreckage : BlueprintTable
    {
        /// <summary>
        /// The prop blueprint of the wreck, e.g. <c>/props/DefaultWreckage/DefaultWreckage_prop.bp</c>.
        /// </summary>
        public string? Blueprint { get; init; }

        /// <summary>
        /// The share of the unit's mass cost left in the wreck.
        /// </summary>
        public double? MassMult { get; init; }

        public double? EnergyMult { get; init; }

        public double? HealthMult { get; init; }

        public double? ReclaimTimeMultiplier { get; init; }

        /// <summary>
        /// The layers on which a wreck is left, e.g. <c>Land</c>, <c>Seabed</c>.
        /// </summary>
        public IReadOnlySet<string> WreckageLayers { get; init; } = new HashSet<string>();

        internal static BlueprintUnitWreckage Read(BlueprintTableReader t) => new BlueprintUnitWreckage
        {
            Raw = t.Table,
            Blueprint = t.String("Blueprint"),
            MassMult = t.Number("MassMult"),
            EnergyMult = t.Number("EnergyMult"),
            HealthMult = t.Number("HealthMult"),
            ReclaimTimeMultiplier = t.Number("ReclaimTimeMultiplier"),
            WreckageLayers = t.Flags("WreckageLayers"),
        };
    }

    public sealed record BlueprintUnitEnhancement : BlueprintTable
    {
        /// <summary>
        /// The display name, e.g. <c>&lt;LOC enhancements_0037&gt;Tech 2 Engineering Suite</c>.
        /// </summary>
        public string? Name { get; init; }

        public string? Icon { get; init; }

        /// <summary>
        /// <c>Back</c>, <c>LCH</c> (left arm) or <c>RCH</c> (right arm).
        /// </summary>
        public string? Slot { get; init; }

        /// <summary>
        /// The enhancement that has to be installed first.
        /// </summary>
        public string? Prerequisite { get; init; }

        /// <summary>
        /// The enhancements this one removes; set on the "...Remove" entries.
        /// </summary>
        public IReadOnlyList<string> RemoveEnhancements { get; init; } = [];

        public double? BuildCostEnergy { get; init; }

        public double? BuildCostMass { get; init; }

        public double? BuildTime { get; init; }

        /// <summary>
        /// Categories added to what the unit can build, e.g. <c>BUILTBYTIER2COMMANDER UEF</c>.
        /// </summary>
        public string? BuildableCategoryAdds { get; init; }

        public double? NewHealth { get; init; }

        public double? NewRegenRate { get; init; }

        public double? NewBuildRate { get; init; }

        public double? NewMaxRadius { get; init; }

        public double? NewRateOfFire { get; init; }

        public double? NewDamageRadius { get; init; }

        public double? NewVisionRadius { get; init; }

        public double? NewOmniRadius { get; init; }

        public double? ProductionPerSecondEnergy { get; init; }

        public double? ProductionPerSecondMass { get; init; }

        public double? MaintenanceConsumptionPerSecondEnergy { get; init; }

        public double? ShieldMaxHealth { get; init; }

        public double? ShieldSize { get; init; }

        public double? ShieldRegenRate { get; init; }

        public double? ShieldRechargeTime { get; init; }

        internal static BlueprintUnitEnhancement Read(BlueprintTableReader t) => new BlueprintUnitEnhancement
        {
            Raw = t.Table,
            Name = t.String("Name"),
            Icon = t.String("Icon"),
            Slot = t.String("Slot"),
            Prerequisite = t.String("Prerequisite"),
            RemoveEnhancements = t.Strings("RemoveEnhancements"),
            BuildCostEnergy = t.Number("BuildCostEnergy"),
            BuildCostMass = t.Number("BuildCostMass"),
            BuildTime = t.Number("BuildTime"),
            BuildableCategoryAdds = t.String("BuildableCategoryAdds"),
            NewHealth = t.Number("NewHealth"),
            NewRegenRate = t.Number("NewRegenRate"),
            NewBuildRate = t.Number("NewBuildRate"),
            NewMaxRadius = t.Number("NewMaxRadius"),
            NewRateOfFire = t.Number("NewRateOfFire"),
            NewDamageRadius = t.Number("NewDamageRadius"),
            NewVisionRadius = t.Number("NewVisionRadius"),
            NewOmniRadius = t.Number("NewOmniRadius"),
            ProductionPerSecondEnergy = t.Number("ProductionPerSecondEnergy"),
            ProductionPerSecondMass = t.Number("ProductionPerSecondMass"),
            MaintenanceConsumptionPerSecondEnergy = t.Number("MaintenanceConsumptionPerSecondEnergy"),
            ShieldMaxHealth = t.Number("ShieldMaxHealth"),
            ShieldSize = t.Number("ShieldSize"),
            ShieldRegenRate = t.Number("ShieldRegenRate"),
            ShieldRechargeTime = t.Number("ShieldRechargeTime"),
        };
    }
}
