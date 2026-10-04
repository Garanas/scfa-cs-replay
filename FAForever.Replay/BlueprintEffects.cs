namespace FAForever.Replay
{
    /// <summary>
    /// A visual effect: particle emitter, trail or beam (in <c>effects/</c>). Its id is always the
    /// lower case path, even when the file sets a <c>BlueprintId</c>.
    /// </summary>
    public abstract record BlueprintEffect : Blueprint
    {
        /// <summary>
        /// Seconds the effect lives; -1 for as long as it is attached.
        /// </summary>
        public double? Lifetime { get; init; }

        /// <summary>
        /// The camera distance beyond which it is not drawn.
        /// </summary>
        public double? LODCutoff { get; init; }

        /// <summary>
        /// Whether it is shown at the low, medium and high fidelity graphics settings.
        /// </summary>
        public bool? LowFidelity { get; init; }

        public bool? MedFidelity { get; init; }

        public bool? HighFidelity { get; init; }
    }

    /// <summary>
    /// A particle emitter (<c>EmitterBlueprint { ... }</c>). Most behaviour is described by curves
    /// over the emitter's lifetime.
    /// </summary>
    public sealed record BlueprintEmitter : BlueprintEffect
    {
        /// <summary>
        /// The particle texture (the <c>Texture</c> key; the annotations call it <c>TextureName</c>).
        /// </summary>
        public string? Texture { get; init; }

        public string? RampTexture { get; init; }

        public double? Repeattime { get; init; }

        public double? Blendmode { get; init; }

        public double? SortOrder { get; init; }

        public double? TextureFramecount { get; init; }

        public double? TextureStripcount { get; init; }

        public bool? LocalVelocity { get; init; }

        public bool? LocalAcceleration { get; init; }

        public bool? Gravity { get; init; }

        public bool? AlignRotation { get; init; }

        public bool? AlignToBone { get; init; }

        public bool? Flat { get; init; }

        public bool? EmitIfVisible { get; init; }

        public bool? CatchupEmit { get; init; }

        public bool? CreateIfVisible { get; init; }

        public bool? SnapToWaterline { get; init; }

        public bool? OnlyEmitOnWater { get; init; }

        public bool? ParticleResistance { get; init; }

        public bool? InterpolateEmission { get; init; }

        public BlueprintCurve? EmitRateCurve { get; init; }

        public BlueprintCurve? LifetimeCurve { get; init; }

        public BlueprintCurve? SizeCurve { get; init; }

        public BlueprintCurve? StartSizeCurve { get; init; }

        public BlueprintCurve? EndSizeCurve { get; init; }

        public BlueprintCurve? VelocityCurve { get; init; }

        public BlueprintCurve? ResistanceCurve { get; init; }

        public BlueprintCurve? XDirectionCurve { get; init; }

        public BlueprintCurve? YDirectionCurve { get; init; }

        public BlueprintCurve? ZDirectionCurve { get; init; }

        public BlueprintCurve? XAccelCurve { get; init; }

        public BlueprintCurve? YAccelCurve { get; init; }

        public BlueprintCurve? ZAccelCurve { get; init; }

        public BlueprintCurve? XPosCurve { get; init; }

        public BlueprintCurve? YPosCurve { get; init; }

        public BlueprintCurve? ZPosCurve { get; init; }

        public BlueprintCurve? InitialRotationCurve { get; init; }

        public BlueprintCurve? RotationRateCurve { get; init; }

        public BlueprintCurve? FrameRateCurve { get; init; }

        public BlueprintCurve? TextureSelectionCurve { get; init; }

        public BlueprintCurve? RampSelectionCurve { get; init; }

        internal static BlueprintEmitter Read(BlueprintTableReader t, string blueprintId, string source) => new BlueprintEmitter
        {
            Raw = t.Table,
            BlueprintId = blueprintId,
            Source = source,
            Lifetime = t.Number("Lifetime"),
            LODCutoff = t.Number("LODCutoff"),
            LowFidelity = t.Bool("LowFidelity"),
            MedFidelity = t.Bool("MedFidelity"),
            HighFidelity = t.Bool("HighFidelity"),
            Texture = t.String("Texture"),
            RampTexture = t.String("RampTexture"),
            Repeattime = t.Number("Repeattime"),
            Blendmode = t.Number("Blendmode"),
            SortOrder = t.Number("SortOrder"),
            TextureFramecount = t.Number("TextureFramecount"),
            TextureStripcount = t.Number("TextureStripcount"),
            LocalVelocity = t.Bool("LocalVelocity"),
            LocalAcceleration = t.Bool("LocalAcceleration"),
            Gravity = t.Bool("Gravity"),
            AlignRotation = t.Bool("AlignRotation"),
            AlignToBone = t.Bool("AlignToBone"),
            Flat = t.Bool("Flat"),
            EmitIfVisible = t.Bool("EmitIfVisible"),
            CatchupEmit = t.Bool("CatchupEmit"),
            CreateIfVisible = t.Bool("CreateIfVisible"),
            SnapToWaterline = t.Bool("SnapToWaterline"),
            OnlyEmitOnWater = t.Bool("OnlyEmitOnWater"),
            ParticleResistance = t.Bool("ParticleResistance"),
            InterpolateEmission = t.Bool("InterpolateEmission"),
            EmitRateCurve = t.Section("EmitRateCurve", BlueprintCurve.Read),
            LifetimeCurve = t.Section("LifetimeCurve", BlueprintCurve.Read),
            SizeCurve = t.Section("SizeCurve", BlueprintCurve.Read),
            StartSizeCurve = t.Section("StartSizeCurve", BlueprintCurve.Read),
            EndSizeCurve = t.Section("EndSizeCurve", BlueprintCurve.Read),
            VelocityCurve = t.Section("VelocityCurve", BlueprintCurve.Read),
            ResistanceCurve = t.Section("ResistanceCurve", BlueprintCurve.Read),
            XDirectionCurve = t.Section("XDirectionCurve", BlueprintCurve.Read),
            YDirectionCurve = t.Section("YDirectionCurve", BlueprintCurve.Read),
            ZDirectionCurve = t.Section("ZDirectionCurve", BlueprintCurve.Read),
            XAccelCurve = t.Section("XAccelCurve", BlueprintCurve.Read),
            YAccelCurve = t.Section("YAccelCurve", BlueprintCurve.Read),
            ZAccelCurve = t.Section("ZAccelCurve", BlueprintCurve.Read),
            XPosCurve = t.Section("XPosCurve", BlueprintCurve.Read),
            YPosCurve = t.Section("YPosCurve", BlueprintCurve.Read),
            ZPosCurve = t.Section("ZPosCurve", BlueprintCurve.Read),
            InitialRotationCurve = t.Section("InitialRotationCurve", BlueprintCurve.Read),
            RotationRateCurve = t.Section("RotationRateCurve", BlueprintCurve.Read),
            FrameRateCurve = t.Section("FrameRateCurve", BlueprintCurve.Read),
            TextureSelectionCurve = t.Section("TextureSelectionCurve", BlueprintCurve.Read),
            RampSelectionCurve = t.Section("RampSelectionCurve", BlueprintCurve.Read),
        };
    }

    /// <summary>
    /// A ribbon that follows a moving bone (<c>TrailEmitterBlueprint { ... }</c>), e.g. a contrail.
    /// </summary>
    public sealed record BlueprintTrailEmitter : BlueprintEffect
    {
        public double? TrailLength { get; init; }

        public double? Size { get; init; }

        public double? SortOrder { get; init; }

        public double? BlendMode { get; init; }

        public string? RepeatTexture { get; init; }

        public string? RampTexture { get; init; }

        public double? TextureRepeatRate { get; init; }

        public double? UShift { get; init; }

        public double? VShift { get; init; }

        internal static BlueprintTrailEmitter Read(BlueprintTableReader t, string blueprintId, string source) => new BlueprintTrailEmitter
        {
            Raw = t.Table,
            BlueprintId = blueprintId,
            Source = source,
            Lifetime = t.Number("Lifetime"),
            LODCutoff = t.Number("LODCutoff"),
            LowFidelity = t.Bool("LowFidelity"),
            MedFidelity = t.Bool("MedFidelity"),
            HighFidelity = t.Bool("HighFidelity"),
            TrailLength = t.Number("TrailLength"),
            Size = t.Number("Size"),
            SortOrder = t.Number("SortOrder"),
            BlendMode = t.Number("BlendMode"),
            RepeatTexture = t.String("RepeatTexture"),
            RampTexture = t.String("RampTexture"),
            TextureRepeatRate = t.Number("TextureRepeatRate"),
            UShift = t.Number("UShift"),
            VShift = t.Number("VShift"),
        };
    }

    /// <summary>
    /// A textured beam between two points (<c>BeamBlueprint { ... }</c>).
    /// </summary>
    public sealed record BlueprintBeam : BlueprintEffect
    {
        public double? Length { get; init; }

        public double? Thickness { get; init; }

        public string? TextureName { get; init; }

        public BlueprintColor? StartColor { get; init; }

        public BlueprintColor? EndColor { get; init; }

        public double? UShift { get; init; }

        public double? VShift { get; init; }

        public double? RepeatRate { get; init; }

        public double? Blendmode { get; init; }

        internal static BlueprintBeam Read(BlueprintTableReader t, string blueprintId, string source) => new BlueprintBeam
        {
            Raw = t.Table,
            BlueprintId = blueprintId,
            Source = source,
            Lifetime = t.Number("Lifetime"),
            LODCutoff = t.Number("LODCutoff"),
            LowFidelity = t.Bool("LowFidelity"),
            MedFidelity = t.Bool("MedFidelity"),
            HighFidelity = t.Bool("HighFidelity"),
            Length = t.Number("Length"),
            Thickness = t.Number("Thickness"),
            TextureName = t.String("TextureName"),
            StartColor = t.Section("StartColor", BlueprintColor.Read),
            EndColor = t.Section("EndColor", BlueprintColor.Read),
            UShift = t.Number("UShift"),
            VShift = t.Number("VShift"),
            RepeatRate = t.Number("RepeatRate"),
            Blendmode = t.Number("Blendmode"),
        };
    }

    /// <summary>
    /// A curve over an emitter's lifetime: <c>{ XRange = 1, Keys = { { x = 0.5, y = 0, z = 0 }, ... } }</c>.
    /// Each key is a point (x = time, y = value) with z the random spread around the value.
    /// </summary>
    public sealed record BlueprintCurve : BlueprintTable
    {
        public double? XRange { get; init; }

        public IReadOnlyList<BlueprintCurveKey> Keys { get; init; } = [];

        internal static BlueprintCurve Read(BlueprintTableReader t) => new BlueprintCurve
        {
            Raw = t.Table,
            XRange = t.Number("XRange"),
            Keys = t.List("Keys", key => new BlueprintCurveKey(key.Number("x") ?? 0, key.Number("y") ?? 0, key.Number("z") ?? 0)),
        };
    }

    public readonly record struct BlueprintCurveKey(double X, double Y, double Z);

    /// <summary>
    /// A colour written as <c>{ x = r, y = g, z = b, w = a }</c>, each 0 to 1.
    /// </summary>
    public sealed record BlueprintColor : BlueprintTable
    {
        public double? X { get; init; }

        public double? Y { get; init; }

        public double? Z { get; init; }

        public double? W { get; init; }

        internal static BlueprintColor Read(BlueprintTableReader t) => new BlueprintColor
        {
            Raw = t.Table,
            X = t.Number("x"),
            Y = t.Number("y"),
            Z = t.Number("z"),
            W = t.Number("w"),
        };
    }
}
