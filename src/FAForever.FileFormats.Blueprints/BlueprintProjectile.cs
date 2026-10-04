namespace FAForever.FileFormats.Blueprints
{
    /// <summary>
    /// A projectile (<c>ProjectileBlueprint { ... }</c> in <c>projectiles/*/*_proj.bp</c>); its id is
    /// the lower case path, e.g. <c>/projectiles/tdfgauss01/tdfgauss01_proj.bp</c>. Damage is not
    /// part of the projectile: it comes from the weapon that fires it.
    /// </summary>
    public sealed record BlueprintProjectile : BlueprintEntity
    {
        public required BlueprintProjectileGeneral General { get; init; }

        public required BlueprintProjectilePhysics Physics { get; init; }

        public required BlueprintProjectileDisplay Display { get; init; }

        /// <summary>
        /// Set for projectiles that can be shot down (missiles).
        /// </summary>
        public BlueprintProjectileDefense? Defense { get; init; }

        /// <summary>
        /// Set for projectiles that are built (strategic and tactical missiles).
        /// </summary>
        public BlueprintProjectileEconomy? Economy { get; init; }

        /// <summary>
        /// Sounds by event, e.g. <c>Impact</c>, <c>ImpactWater</c>.
        /// </summary>
        public IReadOnlyDictionary<string, BlueprintSound> Audio { get; init; } = new Dictionary<string, BlueprintSound>();

        internal static BlueprintProjectile Read(BlueprintTableReader t, string blueprintId, string source) => new BlueprintProjectile
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
            General = t.SectionOrEmpty("General", BlueprintProjectileGeneral.Read),
            Physics = t.SectionOrEmpty("Physics", BlueprintProjectilePhysics.Read),
            Display = t.SectionOrEmpty("Display", BlueprintProjectileDisplay.Read),
            Defense = t.Section("Defense", BlueprintProjectileDefense.Read),
            Economy = t.Section("Economy", BlueprintProjectileEconomy.Read),
            Audio = t.Sounds("Audio"),
        };
    }

    /// <summary>
    /// Descriptive information; the game itself does not use it.
    /// </summary>
    public sealed record BlueprintProjectileGeneral : BlueprintTable
    {
        /// <summary>
        /// E.g. <c>Direct Fire</c>, <c>Anti Air</c>, <c>Missile</c>.
        /// </summary>
        public string? Category { get; init; }

        public string? EntityCategory { get; init; }

        public string? Faction { get; init; }

        public double? TechLevel { get; init; }

        /// <summary>
        /// The weapon that fires it, e.g. <c>Gauss Cannon</c>.
        /// </summary>
        public string? Weapon { get; init; }

        internal static BlueprintProjectileGeneral Read(BlueprintTableReader t) => new BlueprintProjectileGeneral
        {
            Raw = t.Table,
            Category = t.String("Category"),
            EntityCategory = t.String("EntityCategory"),
            Faction = t.String("Faction"),
            TechLevel = t.Number("TechLevel"),
            Weapon = t.String("Weapon"),
        };
    }

    public sealed record BlueprintProjectilePhysics : BlueprintTable
    {
        public double? InitialSpeed { get; init; }

        public double? InitialSpeedRange { get; init; }

        public double? MaxSpeed { get; init; }

        public double? Acceleration { get; init; }

        /// <summary>
        /// Seconds before the projectile is destroyed. A few files misspell it <c>LifeTime</c>,
        /// which the game ignores; so does this property.
        /// </summary>
        public double? Lifetime { get; init; }

        public double? OnLostTargetLifetime { get; init; }

        /// <summary>
        /// Degrees per second a tracking projectile can turn.
        /// </summary>
        public double? TurnRate { get; init; }

        public double? TurnRateRange { get; init; }

        public bool? TrackTarget { get; init; }

        public bool? TrackTargetGround { get; init; }

        public bool? LeadTarget { get; init; }

        public bool? UseGravity { get; init; }

        public bool? VelocityAlign { get; init; }

        public bool? StayUpright { get; init; }

        public bool? StayUnderwater { get; init; }

        public bool? DestroyOnWater { get; init; }

        public bool? CollideEntity { get; init; }

        public bool? CollideSurface { get; init; }

        public bool? RealisticOrdinance { get; init; }

        public double? DetonateAboveHeight { get; init; }

        public double? DetonateBelowHeight { get; init; }

        public double? RotationalVelocity { get; init; }

        public double? MaxZigZag { get; init; }

        public double? ZigZagFrequency { get; init; }

        /// <summary>
        /// The number of fragments it splits into (e.g. cluster bombs), of blueprint <see cref="FragmentId"/>.
        /// </summary>
        public double? Fragments { get; init; }

        public string? FragmentId { get; init; }

        public double? FragmentRadius { get; init; }

        internal static BlueprintProjectilePhysics Read(BlueprintTableReader t) => new BlueprintProjectilePhysics
        {
            Raw = t.Table,
            InitialSpeed = t.Number("InitialSpeed"),
            InitialSpeedRange = t.Number("InitialSpeedRange"),
            MaxSpeed = t.Number("MaxSpeed"),
            Acceleration = t.Number("Acceleration"),
            Lifetime = t.Number("Lifetime"),
            OnLostTargetLifetime = t.Number("OnLostTargetLifetime"),
            TurnRate = t.Number("TurnRate"),
            TurnRateRange = t.Number("TurnRateRange"),
            TrackTarget = t.Bool("TrackTarget"),
            TrackTargetGround = t.Bool("TrackTargetGround"),
            LeadTarget = t.Bool("LeadTarget"),
            UseGravity = t.Bool("UseGravity"),
            VelocityAlign = t.Bool("VelocityAlign"),
            StayUpright = t.Bool("StayUpright"),
            StayUnderwater = t.Bool("StayUnderwater"),
            DestroyOnWater = t.Bool("DestroyOnWater"),
            CollideEntity = t.Bool("CollideEntity"),
            CollideSurface = t.Bool("CollideSurface"),
            RealisticOrdinance = t.Bool("RealisticOrdinance"),
            DetonateAboveHeight = t.Number("DetonateAboveHeight"),
            DetonateBelowHeight = t.Number("DetonateBelowHeight"),
            RotationalVelocity = t.Number("RotationalVelocity"),
            MaxZigZag = t.Number("MaxZigZag"),
            ZigZagFrequency = t.Number("ZigZagFrequency"),
            Fragments = t.Number("Fragments"),
            FragmentId = t.String("FragmentId"),
            FragmentRadius = t.Number("FragmentRadius"),
        };
    }

    public sealed record BlueprintProjectileDisplay : BlueprintTable
    {
        public BlueprintDisplayMesh? Mesh { get; init; }

        public string? MeshBlueprint { get; init; }

        public double? UniformScale { get; init; }

        public double? StrategicIconSize { get; init; }

        public bool? CameraFollowsProjectile { get; init; }

        internal static BlueprintProjectileDisplay Read(BlueprintTableReader t) => new BlueprintProjectileDisplay
        {
            Raw = t.Table,
            Mesh = t.Section("Mesh", BlueprintDisplayMesh.Read),
            MeshBlueprint = t.String("MeshBlueprint"),
            UniformScale = t.Number("UniformScale"),
            StrategicIconSize = t.Number("StrategicIconSize"),
            CameraFollowsProjectile = t.Bool("CameraFollowsProjectile"),
        };
    }

    public sealed record BlueprintProjectileDefense : BlueprintTable
    {
        public double? Health { get; init; }

        public double? MaxHealth { get; init; }

        internal static BlueprintProjectileDefense Read(BlueprintTableReader t) => new BlueprintProjectileDefense
        {
            Raw = t.Table,
            Health = t.Number("Health"),
            MaxHealth = t.Number("MaxHealth"),
        };
    }

    public sealed record BlueprintProjectileEconomy : BlueprintTable
    {
        public double? BuildCostEnergy { get; init; }

        public double? BuildCostMass { get; init; }

        public double? BuildTime { get; init; }

        internal static BlueprintProjectileEconomy Read(BlueprintTableReader t) => new BlueprintProjectileEconomy
        {
            Raw = t.Table,
            BuildCostEnergy = t.Number("BuildCostEnergy"),
            BuildCostMass = t.Number("BuildCostMass"),
            BuildTime = t.Number("BuildTime"),
        };
    }
}
