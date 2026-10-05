namespace FAForever.FileFormats.Blueprints
{
    /// <summary>
    /// The essentials of a unit for a unit card or a unit list: name, cost, health, intel and
    /// weapons. Built from a <see cref="BlueprintUnit"/> by <see cref="From"/> and stored per game
    /// version in a <see cref="UnitData"/> file, so the browser does not parse blueprints itself.
    /// Values are those of the blueprint (see <see cref="BlueprintParser"/>), evened out so that two
    /// versions only differ where the meaning does: numbers rounded to 4 decimals, 0 read as not set
    /// where it means "none" (regeneration, shield, build power, production, speed, intel, damage
    /// area, salvo delay, damage over time), categories sorted. Null means not set.
    /// </summary>
    public sealed record UnitSummary
    {
        /// <summary>
        /// The lower case blueprint id, e.g. <c>uel0201</c>.
        /// </summary>
        public required string BlueprintId { get; init; }

        /// <summary>
        /// The unit's name without its localisation tag, e.g. "MA12 Striker" (<c>General.UnitName</c>).
        /// Many structures and some units have none.
        /// </summary>
        public string? Name { get; init; }

        /// <summary>
        /// What the unit is, e.g. "Medium Tank" (<c>Description</c>, without its localisation tag).
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// <c>UEF</c>, <c>Aeon</c>, <c>Cybran</c> or <c>Seraphim</c> (<c>General.FactionName</c>).
        /// </summary>
        public string? Faction { get; init; }

        /// <summary>
        /// 1 to 3, or 4 for experimentals, from the categories (<see cref="BlueprintUnit.TechLevel"/>).
        /// </summary>
        public int? TechLevel { get; init; }

        /// <summary>
        /// How the unit moves (<c>Physics.MotionType</c>): <c>RULEUMT_Land</c>, <c>RULEUMT_Air</c>,
        /// <c>RULEUMT_Amphibious</c>, <c>RULEUMT_AmphibiousFloating</c>, <c>RULEUMT_Hover</c>,
        /// <c>RULEUMT_Water</c>, <c>RULEUMT_SurfacingSub</c> or <c>RULEUMT_None</c> (structures).
        /// </summary>
        public string? MotionType { get; init; }

        /// <summary>
        /// All categories, upper case, e.g. <c>BOMBER</c>, <c>GROUNDATTACK</c> (gunships), <c>ENGINEER</c>.
        /// </summary>
        public IReadOnlyList<string> Categories { get; init; } = [];

        public double? MaxHealth { get; init; }

        /// <summary>
        /// Health regenerated per second.
        /// </summary>
        public double? RegenRate { get; init; }

        /// <summary>
        /// The health of the unit's shield, when it has one.
        /// </summary>
        public double? ShieldMaxHealth { get; init; }

        public double? BuildCostMass { get; init; }

        public double? BuildCostEnergy { get; init; }

        /// <summary>
        /// Build time in build-power seconds: a builder with build power 10 takes BuildTime / 10 seconds.
        /// </summary>
        public double? BuildTime { get; init; }

        /// <summary>
        /// The unit's own build power (<c>Economy.BuildRate</c>), for engineers, factories and commanders.
        /// </summary>
        public double? BuildRate { get; init; }

        /// <summary>
        /// Mass and energy the unit produces per second (extractors, generators, commanders).
        /// </summary>
        public double? ProductionPerSecondMass { get; init; }

        public double? ProductionPerSecondEnergy { get; init; }

        /// <summary>
        /// Top speed: <c>Air.MaxAirspeed</c> for aircraft, otherwise <c>Physics.MaxSpeed</c>. Null for structures.
        /// </summary>
        public double? MaxSpeed { get; init; }

        /// <summary>
        /// Whether a player can get the unit in a game: a commander, or reachable from one through
        /// builds and upgrades (<see cref="UnitBuildTree.Buildable"/>). Campaign, civilian and helper
        /// units are not. Set by <see cref="UnitData.From"/>; false from <see cref="From"/> alone.
        /// </summary>
        public bool Buildable { get; init; }

        /// <summary>
        /// The ids of the units it can build (<see cref="UnitBuildTree.Builds"/>), including what
        /// enhancements add. Set by <see cref="UnitData.From"/>; empty from <see cref="From"/> alone.
        /// </summary>
        public IReadOnlyList<string> Builds { get; init; } = [];

        /// <summary>
        /// The unit it upgrades from or to in place, e.g. a tech 1 mass extractor to tech 2
        /// (<c>General.UpgradesFrom</c> / <c>General.UpgradesTo</c>), lower case.
        /// </summary>
        public string? UpgradesFrom { get; init; }

        public string? UpgradesTo { get; init; }

        public double? VisionRadius { get; init; }

        public double? WaterVisionRadius { get; init; }

        public double? RadarRadius { get; init; }

        public double? SonarRadius { get; init; }

        public double? OmniRadius { get; init; }

        /// <summary>
        /// The weapons in blueprint order, including those that are not really weapons (a death
        /// explosion, a teleport effect, aim-only dummies); see <see cref="UnitSummaryWeapon.WeaponCategory"/>.
        /// </summary>
        public IReadOnlyList<UnitSummaryWeapon> Weapons { get; init; } = [];

        public bool HasCategory(string category) => Categories.Contains(category, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The summary of a unit blueprint.
        /// </summary>
        public static UnitSummary From(BlueprintUnit unit) => new UnitSummary
        {
            BlueprintId = unit.BlueprintId.ToLowerInvariant(),
            Name = WithoutLocalization(unit.General.UnitName),
            Description = WithoutLocalization(unit.Description),
            Faction = unit.General.FactionName,
            TechLevel = unit.TechLevel,
            MotionType = unit.Physics.MotionType,
            Categories = unit.Categories.Order(StringComparer.Ordinal).ToList(),
            MaxHealth = Round(unit.Defense.MaxHealth),
            RegenRate = Positive(unit.Defense.RegenRate),
            ShieldMaxHealth = Positive(unit.Defense.Shield?.ShieldMaxHealth),
            BuildCostMass = Round(unit.Economy.BuildCostMass),
            BuildCostEnergy = Round(unit.Economy.BuildCostEnergy),
            BuildTime = Round(unit.Economy.BuildTime),
            BuildRate = Positive(unit.Economy.BuildRate),
            ProductionPerSecondMass = Positive(unit.Economy.ProductionPerSecondMass),
            ProductionPerSecondEnergy = Positive(unit.Economy.ProductionPerSecondEnergy),
            MaxSpeed = unit.Physics.MotionType == "RULEUMT_None" ? null : Positive(unit.Air?.MaxAirspeed ?? unit.Physics.MaxSpeed),
            UpgradesFrom = UnitId(unit.General.UpgradesFrom),
            UpgradesTo = UnitId(unit.General.UpgradesTo),
            VisionRadius = Positive(unit.Intel.VisionRadius),
            WaterVisionRadius = Positive(unit.Intel.WaterVisionRadius),
            RadarRadius = Positive(unit.Intel.RadarRadius),
            SonarRadius = Positive(unit.Intel.SonarRadius),
            OmniRadius = Positive(unit.Intel.OmniRadius),
            Weapons = unit.Weapons.Select(UnitSummaryWeapon.From).ToList(),
        };

        // Blueprints get rewritten without a change in meaning (a value of 0 left out, 10/60 written as
        // 0.1667, categories reordered). The summary evens that out, so that a difference between two
        // versions is a real change: numbers are rounded to 4 decimals, and where 0 means "none" it
        // reads as not set.
        internal static double? Round(double? value) => value is { } number ? Math.Round(number, 4) : null;

        internal static double? Positive(double? value) => value is > 0 ? Round(value) : null;

        // An empty value and "none" (used by a few blueprints) mean no unit.
        private static string? UnitId(string? id) =>
            id is { Length: > 0 } && !id.Equals("none", StringComparison.OrdinalIgnoreCase) ? id.ToLowerInvariant() : null;

        /// <summary>
        /// Drops the localisation tag the game looks the text up by: <c>&lt;LOC uel0201_name&gt;MA12 Striker</c>
        /// becomes "MA12 Striker", the English text the game falls back to. Empty text becomes null.
        /// </summary>
        public static string? WithoutLocalization(string? text)
        {
            if (text is null)
            {
                return null;
            }

            if (text.StartsWith("<LOC", StringComparison.OrdinalIgnoreCase) && text.IndexOf('>') is int end and > 0)
            {
                text = text[(end + 1)..];
            }

            text = text.Trim();
            return text.Length > 0 ? text : null;
        }
    }

    /// <summary>
    /// The essentials of a weapon: what it is, its damage and range and how it fires.
    /// </summary>
    public sealed record UnitSummaryWeapon
    {
        /// <summary>
        /// The name shown in game, e.g. "Gauss Cannon".
        /// </summary>
        public string? DisplayName { get; init; }

        /// <summary>
        /// E.g. <c>Direct Fire</c>, <c>Artillery</c>, <c>Anti Air</c>, <c>Bomb</c>, <c>Missile</c>;
        /// <c>Death</c> for the explosion when the unit dies and <c>Teleport</c> for teleport effects.
        /// </summary>
        public string? WeaponCategory { get; init; }

        /// <summary>
        /// What the weapon is for, which the game uses to show its range: <c>UWRC_DirectFire</c>,
        /// <c>UWRC_IndirectFire</c> (artillery, missiles), <c>UWRC_AntiAir</c>, <c>UWRC_AntiNavy</c>
        /// (torpedoes) or <c>UWRC_Countermeasure</c> (missile and torpedo defence).
        /// </summary>
        public string? RangeCategory { get; init; }

        /// <summary>
        /// Damage per projectile or beam pulse.
        /// </summary>
        public double? Damage { get; init; }

        /// <summary>
        /// Damage over time: the damage lands in this many pulses (<c>DoTPulses</c>), spread over
        /// <see cref="DoTTime"/> seconds, e.g. a napalm bomb. Not set for a weapon that hits once.
        /// </summary>
        public double? DoTPulses { get; init; }

        /// <summary>
        /// The seconds over which the pulses of <see cref="DoTPulses"/> land (<c>DoTTime</c>).
        /// </summary>
        public double? DoTTime { get; init; }

        /// <summary>
        /// The radius of the damage area; 0 hits only what the projectile touches.
        /// </summary>
        public double? DamageRadius { get; init; }

        /// <summary>
        /// The weapon's range (<c>MaxRadius</c>).
        /// </summary>
        public double? MaxRadius { get; init; }

        /// <summary>
        /// Shots (salvos) per second, as the game fires them: it rounds the interval to whole ticks of
        /// 0.1 s, so this is 10 / ticks (see <see cref="TickRate"/>), e.g. 0.1493 for a written 0.15.
        /// </summary>
        public double? RateOfFire { get; init; }

        /// <summary>
        /// Projectiles fired per salvo from each muzzle (<c>MuzzleSalvoSize</c>).
        /// </summary>
        public int? MuzzleSalvoSize { get; init; }

        /// <summary>
        /// Seconds between the projectiles of a salvo (<c>MuzzleSalvoDelay</c>).
        /// </summary>
        public double? MuzzleSalvoDelay { get; init; }

        /// <summary>
        /// The commander or SCU upgrade that adds the weapon, e.g. <c>TacticalMissile</c>.
        /// </summary>
        public string? EnabledByEnhancement { get; init; }

        /// <summary>
        /// The summary of a weapon blueprint.
        /// </summary>
        /// <summary>
        /// The rate of fire the game uses: "Game logic rounds the timings to the nearest tick", the
        /// interval being <c>floor(max(0.1, 1 / RateOfFire) * 10 + 0.5) / 10</c> seconds
        /// (<c>lua/system/blueprints-units.lua</c>, <c>DetermineWeaponDPS</c>). Blueprints write the
        /// same rate in different ways (0.15, or <c>10/67</c> since 3810), which all give the same ticks.
        /// </summary>
        public static double? TickRate(double? rateOfFire)
        {
            if (rateOfFire is not { } rate || rate <= 0)
            {
                return null;
            }

            double ticks = Math.Floor(Math.Max(0.1, 1 / rate) * 10 + 0.5);
            return UnitSummary.Round(10 / ticks);
        }

        public static UnitSummaryWeapon From(BlueprintWeapon weapon) => new UnitSummaryWeapon
        {
            DisplayName = weapon.DisplayName,
            WeaponCategory = weapon.WeaponCategory,
            RangeCategory = weapon.RangeCategory,
            Damage = UnitSummary.Round(weapon.Damage),
            DoTPulses = UnitSummary.Positive(weapon.DoTPulses),
            DoTTime = UnitSummary.Positive(weapon.DoTTime),
            DamageRadius = UnitSummary.Positive(weapon.DamageRadius),
            MaxRadius = UnitSummary.Round(weapon.MaxRadius),
            RateOfFire = TickRate(weapon.RateOfFire),
            MuzzleSalvoSize = weapon.MuzzleSalvoSize,
            MuzzleSalvoDelay = UnitSummary.Positive(weapon.MuzzleSalvoDelay),
            EnabledByEnhancement = weapon.EnabledByEnhancement,
        };
    }
}
