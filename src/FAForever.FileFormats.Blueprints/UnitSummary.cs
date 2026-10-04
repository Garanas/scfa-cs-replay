namespace FAForever.FileFormats.Blueprints
{
    /// <summary>
    /// The essentials of a unit for a unit card or a unit list: name, cost, health, intel and
    /// weapons. Built from a <see cref="BlueprintUnit"/> by <see cref="From"/> and stored per game
    /// version in a <see cref="UnitData"/> file, so the browser does not parse blueprints itself.
    /// Values are as written in the blueprint (see <see cref="BlueprintParser"/>); null means the
    /// blueprint does not set it.
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
            Categories = unit.Categories,
            MaxHealth = unit.Defense.MaxHealth,
            RegenRate = unit.Defense.RegenRate,
            ShieldMaxHealth = unit.Defense.Shield?.ShieldMaxHealth,
            BuildCostMass = unit.Economy.BuildCostMass,
            BuildCostEnergy = unit.Economy.BuildCostEnergy,
            BuildTime = unit.Economy.BuildTime,
            BuildRate = unit.Economy.BuildRate,
            VisionRadius = unit.Intel.VisionRadius,
            WaterVisionRadius = unit.Intel.WaterVisionRadius,
            RadarRadius = unit.Intel.RadarRadius,
            SonarRadius = unit.Intel.SonarRadius,
            OmniRadius = unit.Intel.OmniRadius,
            Weapons = unit.Weapons.Select(UnitSummaryWeapon.From).ToList(),
        };

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
        /// Damage per projectile or beam pulse.
        /// </summary>
        public double? Damage { get; init; }

        /// <summary>
        /// The radius of the damage area; 0 hits only what the projectile touches.
        /// </summary>
        public double? DamageRadius { get; init; }

        /// <summary>
        /// The weapon's range (<c>MaxRadius</c>).
        /// </summary>
        public double? MaxRadius { get; init; }

        /// <summary>
        /// Shots (salvos) per second; the game rounds the interval to whole ticks of 0.1 s.
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
        public static UnitSummaryWeapon From(BlueprintWeapon weapon) => new UnitSummaryWeapon
        {
            DisplayName = weapon.DisplayName,
            WeaponCategory = weapon.WeaponCategory,
            Damage = weapon.Damage,
            DamageRadius = weapon.DamageRadius,
            MaxRadius = weapon.MaxRadius,
            RateOfFire = weapon.RateOfFire,
            MuzzleSalvoSize = weapon.MuzzleSalvoSize,
            MuzzleSalvoDelay = weapon.MuzzleSalvoDelay,
            EnabledByEnhancement = weapon.EnabledByEnhancement,
        };
    }
}
