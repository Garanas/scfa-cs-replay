using System.Collections.Immutable;
using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// How <see cref="DecalEdits.AlongCurve"/> places decals along a curve (user story T-4, D2). Sizes
    /// and distances are in ogrids.
    /// </summary>
    public sealed record DecalsAlongCurve
    {
        /// <summary>
        /// The distance between two decals' middles along the curve; null for the decal's length, so
        /// they lie end to end. The curve is cut into equal pieces as close to this as fits, so the
        /// last decal ends where the curve does.
        /// </summary>
        public float? Spacing { get; init; }

        /// <summary>
        /// Each decal's width (across the curve, unless turned); null for the pattern's.
        /// </summary>
        public float? Width { get; init; }

        /// <summary>
        /// Each decal's length (along the curve); null for the pattern's.
        /// </summary>
        public float? Length { get; init; }

        /// <summary>
        /// How far beside the curve the decals' middles lie: positive on its left (walking from the
        /// first point to the last, as seen in the top view), negative on its right.
        /// </summary>
        public float Offset { get; init; }

        /// <summary>
        /// Every other decal on the other side (the offset turned round).
        /// </summary>
        public bool Alternate { get; init; }

        /// <summary>
        /// Up to how many degrees each decal is turned either way, at random.
        /// </summary>
        public float RandomTurn { get; init; }

        /// <summary>
        /// Up to how far each decal is moved along and across the curve, at random.
        /// </summary>
        public float Jitter { get; init; }

        /// <summary>
        /// The seed of the random turn and jitter: the same seed places the same decals.
        /// </summary>
        public int Seed { get; init; }

        /// <summary>
        /// The distance beyond which the game does not draw them; null for the pattern's.
        /// </summary>
        public float? CutOff { get; init; }
    }

    /// <summary>
    /// One place <see cref="DecalEdits.PlacesAlong"/> finds on a curve: a decal's middle and its turn
    /// (radians, as <see cref="Decal.Rotation"/>).
    /// </summary>
    public readonly record struct DecalPlace(float X, float Z, float Heading);

    public static partial class DecalEdits
    {
        /// <summary>
        /// Places decals along a height curve (user story T-4, D2), once, as ordinary decals on top of
        /// the others: each centred on its place and turned with the curve, its length along the
        /// direction of travel (then turned by the random turn). A decal whose middle would lie beside
        /// the map is left out. A curve drawn by a symmetry (<see cref="HeightCurve.Symmetry"/>) gets
        /// the images of its decals in the other parts of the map too, as mirrored placing makes them
        /// (<see cref="SymmetryEdits.MirrorObjects"/>): each the exact image of a decal along the curve.
        /// </summary>
        /// <param name="pattern">The decal to place (<see cref="NewDecal"/> or <see cref="NewDecalSet"/>,
        /// anywhere): its textures, type, set and distances; its size where the settings leave it out.</param>
        /// <param name="added">The decals along the curve, then their images.</param>
        public static MapDocument AlongCurve(this MapDocument document, HeightCurve curve, Decal pattern, DecalsAlongCurve settings,
            out ImmutableArray<ObjectRef> added)
        {
            float width = MathF.Max(settings.Width ?? pattern.Scale.X, SmallestSide);
            float length = MathF.Max(settings.Length ?? pattern.Scale.Z, SmallestSide);
            Heightmap heights = document.Terrain.Heightmap;
            ImmutableArray<ObjectRef>.Builder placed = ImmutableArray.CreateBuilder<ObjectRef>();
            foreach (DecalPlace place in PlacesAlong(HeightCurves.Spline(curve), settings with { Width = width, Length = length }))
            {
                if (place.X < 0 || place.Z < 0 || place.X > document.Width || place.Z > document.Height)
                {
                    continue;
                }
                Decal decal = NewDecal(heights, pattern.Textures.FirstOrDefault() ?? "", place.X, place.Z, width, length, place.Heading,
                    settings.CutOff ?? pattern.CutOffLod, pattern.NearCutOffLod) with
                {
                    Type = pattern.Type,
                    Textures = pattern.Textures,
                    Set = pattern.Set,
                    OwnerArmy = pattern.OwnerArmy,
                };
                document = document.PlaceDecal(decal, out ObjectRef one);
                placed.Add(one);
            }
            if (curve.Symmetry is SymmetrySetting symmetry && placed.Count > 0)
            {
                IReadOnlyList<MapSymmetryTransform> transforms = MapSymmetry.Transforms(symmetry.Kind, symmetry.Count, document.Width, document.Height);
                document = document.MirrorObjects(placed, transforms, out ImmutableArray<ObjectRef> images);
                placed.AddRange(images);
            }
            added = placed.ToImmutable();
            return document;
        }

        /// <summary>
        /// The places of decals along a line (a curve's spline, <see cref="HeightCurves.Spline"/>), in
        /// its order: the line cut into equal pieces about <see cref="DecalsAlongCurve.Spacing"/> long
        /// (at least one), a decal in the middle of each, moved by the offset, alternation and jitter,
        /// turned so its length runs along the line, and by the random turn. The settings' width and
        /// length are those of the decals (1 when left out).
        /// </summary>
        public static IReadOnlyList<DecalPlace> PlacesAlong(IReadOnlyList<CurvePoint> line, DecalsAlongCurve settings)
        {
            // the distance along the line at each sample; pieces of no length are skipped
            List<(Vector2 Place, float Distance)> samples = [];
            foreach (CurvePoint point in line)
            {
                Vector2 place = new Vector2(point.X, point.Z);
                if (samples.Count == 0)
                {
                    samples.Add((place, 0));
                }
                else if (Vector2.Distance(samples[^1].Place, place) is float step && step > 1e-4f)
                {
                    samples.Add((place, samples[^1].Distance + step));
                }
            }
            if (samples.Count < 2)
            {
                return [];
            }
            float total = samples[^1].Distance;
            float spacing = MathF.Max(settings.Spacing ?? settings.Length ?? 1, SmallestSide);
            int count = Math.Max(1, (int)MathF.Round(total / spacing));
            float piece = total / count;
            float turn = MathF.Max(0, settings.RandomTurn) * MathF.PI / 180;
            float jitter = MathF.Max(0, settings.Jitter);
            Random random = new Random(settings.Seed);
            List<DecalPlace> places = new List<DecalPlace>(count);
            int segment = 0;
            for (int k = 0; k < count; k++)
            {
                float at = (k + 0.5f) * piece;
                while (segment < samples.Count - 2 && samples[segment + 1].Distance < at)
                {
                    segment++;
                }
                (Vector2 a, float from) = samples[segment];
                (Vector2 b, float to) = samples[segment + 1];
                Vector2 along = Vector2.Normalize(b - a);
                // left of the direction of travel in the top view (z down): the side a cliff raises
                Vector2 left = new Vector2(along.Y, -along.X);
                Vector2 middle = Vector2.Lerp(a, b, Math.Clamp((at - from) / (to - from), 0, 1));
                // the same draws for every decal whatever is used, so a setting changed keeps the others' places
                float alongJitter = (float)(random.NextDouble() * 2 - 1) * jitter;
                float acrossJitter = (float)(random.NextDouble() * 2 - 1) * jitter;
                float turned = (float)(random.NextDouble() * 2 - 1) * turn;
                float offset = settings.Alternate && k % 2 == 1 ? -settings.Offset : settings.Offset;
                Vector2 place = middle + along * alongJitter + left * (offset + acrossJitter);
                // the decal's own z axis (its length) is (-sin h, cos h): along the line at h = atan2(-x, z)
                float heading = MathF.Atan2(-along.X, along.Y) + turned;
                places.Add(new DecalPlace(place.X, place.Y, heading));
            }
            return places;
        }
    }
}
