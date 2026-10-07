using FAForever.FileFormats.Lua;

namespace FAForever.FileFormats.Blueprints
{
    /// <summary>
    /// A weapon of a unit (an entry of its <c>Weapon</c> table). Units without a real weapon often
    /// still have a <c>DeathWeapon</c> (the explosion when they die).
    /// </summary>
    public sealed record BlueprintWeapon : BlueprintTable
    {
        /// <summary>
        /// The name the unit's script refers to the weapon by, e.g. <c>MainGun</c>. Absent on some
        /// dummy weapons that only aim a turret.
        /// </summary>
        public string? Label { get; init; }

        public string? DisplayName { get; init; }

        /// <summary>
        /// E.g. <c>Direct Fire</c>, <c>Artillery</c>, <c>Anti Air</c>, <c>Missile</c>, <c>Death</c>.
        /// </summary>
        public string? WeaponCategory { get; init; }

        /// <summary>
        /// E.g. <c>UWRC_DirectFire</c>, <c>UWRC_IndirectFire</c>, <c>UWRC_AntiAir</c>.
        /// </summary>
        public string? RangeCategory { get; init; }

        /// <summary>
        /// Damage per projectile (or per beam pulse).
        /// </summary>
        public double? Damage { get; init; }

        public double? DamageRadius { get; init; }

        /// <summary>
        /// E.g. <c>Normal</c>, <c>Overcharge</c>, <c>Deathnuke</c>.
        /// </summary>
        public string? DamageType { get; init; }

        public bool? DamageFriendly { get; init; }

        public bool? CollideFriendly { get; init; }

        public double? InitialDamage { get; init; }

        /// <summary>
        /// The number of damage-over-time pulses, spread over <see cref="DoTTime"/> seconds.
        /// </summary>
        public double? DoTPulses { get; init; }

        public double? DoTTime { get; init; }

        public double? MinRadius { get; init; }

        public double? MaxRadius { get; init; }

        /// <summary>
        /// Shots per second (the game rounds the interval to whole ticks).
        /// </summary>
        public double? RateOfFire { get; init; }

        public double? MuzzleVelocity { get; init; }

        public int? MuzzleSalvoSize { get; init; }

        public double? MuzzleSalvoDelay { get; init; }

        public int? RackSalvoSize { get; init; }

        public int? ProjectilesPerOnFire { get; init; }

        /// <summary>
        /// The projectile blueprint, e.g. <c>/projectiles/TDFGauss01/TDFGauss01_proj.bp</c>. Compare
        /// case-insensitively with <see cref="Blueprint.BlueprintId"/>, which is lower case.
        /// </summary>
        public string? ProjectileId { get; init; }

        public double? ProjectileLifetime { get; init; }

        /// <summary>
        /// E.g. <c>RULEUBA_None</c>, <c>RULEUBA_LowArc</c>, <c>RULEUBA_HighArc</c>.
        /// </summary>
        public string? BallisticArc { get; init; }

        public double? FiringTolerance { get; init; }

        public double? FiringRandomness { get; init; }

        public bool? Turreted { get; init; }

        public double? TurretYawRange { get; init; }

        public double? TurretYawSpeed { get; init; }

        public double? TurretPitchRange { get; init; }

        public double? TurretPitchSpeed { get; init; }

        public double? EnergyRequired { get; init; }

        public double? EnergyDrainPerSecond { get; init; }

        public bool? ContinuousBeam { get; init; }

        public double? BeamLifetime { get; init; }

        public double? BeamCollisionDelay { get; init; }

        public bool? ManualFire { get; init; }

        public bool? NukeWeapon { get; init; }

        public double? NukeInnerRingDamage { get; init; }

        public double? NukeInnerRingRadius { get; init; }

        public double? NukeOuterRingDamage { get; init; }

        public double? NukeOuterRingRadius { get; init; }

        public bool? CountedProjectile { get; init; }

        public double? MaxProjectileStorage { get; init; }

        public double? InitialProjectileStorage { get; init; }

        /// <summary>
        /// The enhancement that enables the weapon, e.g. <c>TacticalMissile</c>.
        /// </summary>
        public string? EnabledByEnhancement { get; init; }

        /// <summary>
        /// Category expressions in order of preference, e.g. <c>COMMAND</c>, <c>TECH3 MOBILE</c>.
        /// </summary>
        public IReadOnlyList<string> TargetPriorities { get; init; } = [];

        public string? TargetRestrictDisallow { get; init; }

        public string? TargetRestrictOnlyAllow { get; init; }

        /// <summary>
        /// Per layer of the unit, the layers it can fire at, e.g. <c>Land → "Land|Water|Seabed"</c>.
        /// </summary>
        public IReadOnlyDictionary<string, string> FireTargetLayerCapsTable { get; init; } = new Dictionary<string, string>();

        public bool? AboveWaterTargetsOnly { get; init; }

        public bool? AboveWaterFireOnly { get; init; }

        public bool? BelowWaterFireOnly { get; init; }

        public bool? WeaponUnpacks { get; init; }

        public BlueprintWeaponOvercharge? Overcharge { get; init; }

        /// <summary>
        /// Sounds by event, e.g. <c>Fire</c>.
        /// </summary>
        public IReadOnlyDictionary<string, BlueprintSound> Audio { get; init; } = new Dictionary<string, BlueprintSound>();

        internal static BlueprintWeapon Read(LuaTableReader t) => new BlueprintWeapon
        {
            Raw = t.Table,
            Label = t.String("Label"),
            DisplayName = t.String("DisplayName"),
            WeaponCategory = t.String("WeaponCategory"),
            RangeCategory = t.String("RangeCategory"),
            Damage = t.Number("Damage"),
            DamageRadius = t.Number("DamageRadius"),
            DamageType = t.String("DamageType"),
            DamageFriendly = t.Bool("DamageFriendly"),
            CollideFriendly = t.Bool("CollideFriendly"),
            InitialDamage = t.Number("InitialDamage"),
            DoTPulses = t.Number("DoTPulses"),
            DoTTime = t.Number("DoTTime"),
            MinRadius = t.Number("MinRadius"),
            MaxRadius = t.Number("MaxRadius"),
            RateOfFire = t.Number("RateOfFire"),
            MuzzleVelocity = t.Number("MuzzleVelocity"),
            MuzzleSalvoSize = t.Integer("MuzzleSalvoSize"),
            MuzzleSalvoDelay = t.Number("MuzzleSalvoDelay"),
            RackSalvoSize = t.Integer("RackSalvoSize"),
            ProjectilesPerOnFire = t.Integer("ProjectilesPerOnFire"),
            ProjectileId = t.String("ProjectileId"),
            ProjectileLifetime = t.Number("ProjectileLifetime"),
            BallisticArc = t.String("BallisticArc"),
            FiringTolerance = t.Number("FiringTolerance"),
            FiringRandomness = t.Number("FiringRandomness"),
            Turreted = t.Bool("Turreted"),
            TurretYawRange = t.Number("TurretYawRange"),
            TurretYawSpeed = t.Number("TurretYawSpeed"),
            TurretPitchRange = t.Number("TurretPitchRange"),
            TurretPitchSpeed = t.Number("TurretPitchSpeed"),
            EnergyRequired = t.Number("EnergyRequired"),
            EnergyDrainPerSecond = t.Number("EnergyDrainPerSecond"),
            ContinuousBeam = t.Bool("ContinuousBeam"),
            BeamLifetime = t.Number("BeamLifetime"),
            BeamCollisionDelay = t.Number("BeamCollisionDelay"),
            ManualFire = t.Bool("ManualFire"),
            NukeWeapon = t.Bool("NukeWeapon"),
            NukeInnerRingDamage = t.Number("NukeInnerRingDamage"),
            NukeInnerRingRadius = t.Number("NukeInnerRingRadius"),
            NukeOuterRingDamage = t.Number("NukeOuterRingDamage"),
            NukeOuterRingRadius = t.Number("NukeOuterRingRadius"),
            CountedProjectile = t.Bool("CountedProjectile"),
            MaxProjectileStorage = t.Number("MaxProjectileStorage"),
            InitialProjectileStorage = t.Number("InitialProjectileStorage"),
            EnabledByEnhancement = t.String("EnabledByEnhancement"),
            TargetPriorities = t.Strings("TargetPriorities"),
            TargetRestrictDisallow = t.String("TargetRestrictDisallow"),
            TargetRestrictOnlyAllow = t.String("TargetRestrictOnlyAllow"),
            FireTargetLayerCapsTable = t.StringDictionary("FireTargetLayerCapsTable"),
            AboveWaterTargetsOnly = t.Bool("AboveWaterTargetsOnly"),
            AboveWaterFireOnly = t.Bool("AboveWaterFireOnly"),
            BelowWaterFireOnly = t.Bool("BelowWaterFireOnly"),
            WeaponUnpacks = t.Bool("WeaponUnpacks"),
            Overcharge = t.Section("Overcharge", BlueprintWeaponOvercharge.Read),
            Audio = t.Dictionary("Audio", BlueprintSound.Read),
        };
    }

    /// <summary>
    /// The overcharge settings of an ACU's overcharge weapon. The keys are lower camel case in the file.
    /// </summary>
    public sealed record BlueprintWeaponOvercharge : BlueprintTable
    {
        public double? EnergyMult { get; init; }

        public double? CommandDamage { get; init; }

        public double? StructureDamage { get; init; }

        public double? MinDamage { get; init; }

        public double? MaxDamage { get; init; }

        internal static BlueprintWeaponOvercharge Read(LuaTableReader t) => new BlueprintWeaponOvercharge
        {
            Raw = t.Table,
            EnergyMult = t.Number("energyMult"),
            CommandDamage = t.Number("commandDamage"),
            StructureDamage = t.Number("structureDamage"),
            MinDamage = t.Number("minDamage"),
            MaxDamage = t.Number("maxDamage"),
        };
    }
}
