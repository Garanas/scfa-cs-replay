using System.Collections.Immutable;
using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Checks;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class SymmetryEditsTest
    {
        private static readonly IReadOnlyList<MapSymmetryTransform> Four = MapSymmetry.Transforms(MapSymmetryKind.Rotational, 4, 256, 256);
        private static readonly IReadOnlyList<MapSymmetryTransform> Diagonal = MapSymmetry.Transforms(MapSymmetryKind.MirroredDiagonal, 2, 256, 256);

        /// <summary>
        /// A flat map of 256 ogrids without things but its spawns, which <see cref="NewMap"/> places
        /// symmetric for two players.
        /// </summary>
        private static MapDocument Blank() => NewMap.Create(new NewMapSettings { Name = "Mirror", Size = 256, Height = 30, Players = 2 });

        private static Prop Rock(float x, float z, float heading = 0) => new Prop
        {
            Id = 0,
            Blueprint = "/env/evergreen/props/rocks/rock01_prop.bp",
            Position = new Vector3(x, 30, z),
            RotationX = new Vector3(MathF.Cos(heading), 0, MathF.Sin(heading)),
            RotationY = Vector3.UnitY,
            RotationZ = new Vector3(-MathF.Sin(heading), 0, MathF.Cos(heading)),
            Scale = Vector3.One,
        };

        // a shape that tells left from right and top from bottom: bright in its top left quarter
        private static BrushShape Corner()
        {
            float[] values = new float[16 * 16];
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    values[y * 16 + x] = x < 8 && y < 8 ? 1 : x < 8 ? 0.4f : 0.1f;
                }
            }
            return new BrushShape(16, 16, values);
        }

        [TestMethod]
        public void ADabIsPaintedInEveryPartOnceWhereImagesCoincide()
        {
            IReadOnlyList<SymmetricDab> four = SymmetricDabs.Of(Four, 100, 60, 30, round: false);
            Assert.AreEqual(4, four.Count);
            Assert.AreEqual(new Vector2(100, 60), new Vector2(four[0].X, four[0].Z));
            Assert.AreEqual(196, four[1].X, 1e-3f, "a quarter turn clockwise about the middle");
            Assert.AreEqual(100, four[1].Z, 1e-3f);
            Assert.AreEqual(120, four[1].Rotation, 1e-3f, "the shape turns with its part");
            Assert.IsFalse(four.Any(dab => dab.Mirrored));

            IReadOnlyList<SymmetricDab> mirrored = SymmetricDabs.Of(Diagonal, 100, 60, 30, round: false);
            Assert.AreEqual(new Vector2(60, 100), new Vector2(mirrored[1].X, mirrored[1].Z));
            Assert.IsTrue(mirrored[1].Mirrored, "a mirror flips the shape");

            Assert.AreEqual(1, SymmetricDabs.Of(Four, 128, 128, 0, round: true).Count, "a round dab in the middle once");
            Assert.AreEqual(4, SymmetricDabs.Of(Four, 128, 128, 0, round: false).Count, "a shape there turns in each part");
            Assert.AreEqual(1, SymmetricDabs.Of(Diagonal, 80, 80, 0, round: true).Count, "a round dab on the axis once");
            Assert.AreEqual(1, SymmetricDabs.Of([], 80, 80, 0, round: true).Count, "no symmetry: the dab alone");
        }

        [TestMethod]
        public void TheImageOfAShapedDabPaintsTheImageOfItsFootprint()
        {
            foreach (IReadOnlyList<MapSymmetryTransform> transforms in new[] { Four, Diagonal, MapSymmetry.Transforms(MapSymmetryKind.MirroredLeftRight, 2, 256, 256) })
            {
                TiledBuffer<byte>.Builder mask = TiledBuffer<byte>.Create(256, 256).ToBuilder();
                MaskBrush brush = new MaskBrush { Radius = 10, Strength = 1, Shape = Corner() };
                foreach (SymmetricDab dab in SymmetricDabs.Of(transforms, 70, 40, 25, round: false))
                {
                    (brush with { Rotation = dab.Rotation, Mirrored = dab.Mirrored }).Apply(mask, 256, 256, dab.X, dab.Z);
                }
                for (int pz = 25; pz < 56; pz++)
                {
                    for (int px = 55; px < 86; px++)
                    {
                        foreach (MapSymmetryTransform transform in transforms)
                        {
                            Vector2 image = transform.Apply(new Vector2(px + 0.5f, pz + 0.5f));
                            Assert.AreEqual(mask[px, pz], mask[(int)image.X, (int)image.Y], 1, $"{transform} at {px}, {pz}");
                        }
                    }
                }
                Assert.IsTrue(mask[64, 34] > 0, "it painted");
            }
        }

        [TestMethod]
        public void NoiseIsTheSameInEveryPart()
        {
            MapDocument document = Blank();
            TiledBuffer<ushort>.Builder heights = document.Terrain.Heightmap.Samples.ToBuilder();
            HeightBrush brush = new HeightBrush { Mode = HeightBrushMode.Noise, Radius = 12, Strength = 8, Seed = 5 };
            foreach (SymmetricDab dab in SymmetricDabs.Of(Diagonal, 90, 40, 0, round: true))
            {
                (brush with { NoisePlace = dab.Image.Inverse() }).Apply(heights, document.Terrain.Heightmap.Scale, dab.X, dab.Z);
            }
            Assert.AreEqual(heights[95, 38], heights[38, 95]);
            Assert.AreEqual(heights[86, 45], heights[45, 86]);
            Assert.AreNotEqual(heights[95, 38], heights[86, 45], "it is noise");
        }

        [TestMethod]
        public void MirroringAPropMirrorsItsPlaceAndRotation()
        {
            MapDocument document = Blank().AddProps([Rock(100, 60, 0.3f)], out ImmutableArray<int> ids);
            document = document.MirrorObjects([ObjectRef.Prop(ids[0])], Four, out ImmutableArray<ObjectRef> added);

            Assert.AreEqual(3, added.Length);
            Prop turned = (Prop)document.Find(added[0])!;
            Assert.AreEqual(196, turned.Position.X, 1e-3f);
            Assert.AreEqual(100, turned.Position.Z, 1e-3f);
            // a prop's heading is a yaw: a quarter turn clockwise takes a quarter from it
            Assert.AreEqual(MapObjects.PropHeading(Rock(0, 0, 0.3f)) - MathF.PI / 2, MapObjects.PropHeading(turned), 1e-4f, "turned a quarter with its part");

            MapDocument diagonal = Blank().AddProps([Rock(100, 60, 0.3f)], out ids).MirrorObjects([ObjectRef.Prop(ids[0])], Diagonal, out added);
            Prop mirrored = (Prop)diagonal.Find(added[0])!;
            Matrix4x4 rotation = new Matrix4x4(
                mirrored.RotationX.X, mirrored.RotationX.Y, mirrored.RotationX.Z, 0,
                mirrored.RotationY.X, mirrored.RotationY.Y, mirrored.RotationY.Z, 0,
                mirrored.RotationZ.X, mirrored.RotationZ.Y, mirrored.RotationZ.Z, 0,
                0, 0, 0, 1);
            Assert.AreEqual(1, rotation.GetDeterminant(), 1e-4f, "still a rotation");
            Assert.AreEqual(new Vector2(60, 100), new Vector2(mirrored.Position.X, mirrored.Position.Z));

            // mirrored again, nothing is added: each has its partner
            diagonal = diagonal.MirrorObjects([ObjectRef.Prop(ids[0]), added[0]], Diagonal, out ImmutableArray<ObjectRef> again);
            Assert.AreEqual(0, again.Length);
        }

        [TestMethod]
        public void FindsMirrorPartnersOfTheSameKindNearTheImage()
        {
            MapDocument document = Blank().AddProps([Rock(100, 60), Rock(60.6f, 100.3f), Rock(10, 10)], out ImmutableArray<int> ids);
            document = document.AddProps([Rock(10, 10) with { Blueprint = "/env/other.bp" }], out ImmutableArray<int> other);
            ObjectRef a = ObjectRef.Prop(ids[0]);

            Assert.AreEqual(ObjectRef.Prop(ids[1]), document.PartnerOf(a, Diagonal[1]), "within an ogrid of the image");
            Assert.IsNull(document.PartnerOf(ObjectRef.Prop(ids[2]), Diagonal[1]), "on the axis it is its own image");
            Assert.IsNull(document.PartnerOf(a, Four[1]));

            ImmutableArray<SymmetryPartner> partners = document.Partners([a, ObjectRef.Prop(ids[1])], Diagonal);
            Assert.AreEqual(0, partners.Length, "a partner that is selected moves as itself");
            partners = document.Partners([a], Diagonal);
            Assert.AreEqual(new SymmetryPartner(a, ObjectRef.Prop(ids[1]), Diagonal[1]), partners.Single());
        }

        [TestMethod]
        public void PartnersMoveTurnAndArePlacedAsImages()
        {
            MapDocument document = Blank().AddProps([Rock(100, 60), Rock(60, 100)], out ImmutableArray<int> ids);
            ObjectRef a = ObjectRef.Prop(ids[0]);
            ObjectRef b = ObjectRef.Prop(ids[1]);
            ImmutableArray<SymmetryPartner> partners = document.Partners([a], Diagonal);

            MapDocument moved = document.MoveObjects([a], partners, 5, -2);
            Assert.AreEqual(new Vector2(105, 58), Flat(moved.PlaceOf(a)!.Value));
            Assert.AreEqual(new Vector2(58, 105), Flat(moved.PlaceOf(b)!.Value), "by the image of the offset");

            MapDocument turned = document.RotateObjects([a], partners, 0.5f);
            Assert.AreEqual(0.5f, turned.HeadingOf(a)!.Value, 1e-4f);
            Assert.AreEqual(-0.5f, turned.HeadingOf(b)!.Value, 1e-4f, "the other way in a mirrored part");

            MapDocument placed = document.PlaceObjects([a], partners, 120, null, 0.25f);
            Assert.AreEqual(new Vector2(60, 120), Flat(placed.PlaceOf(b)!.Value));
            Assert.AreEqual(-0.25f, placed.HeadingOf(b)!.Value, 1e-4f);
        }

        [TestMethod]
        public void MirroredMarkersAreNewSpawnsDepositsAndLinkedPathNodes()
        {
            MapDocument document = Blank();
            int spawns = document.Markers.Spawns.Length;
            document = document.AddMarker(new ResourceMarker { Id = 0, Name = "Mass 01", Kind = ResourceKind.Mass, Position = new Vector3(100.5f, 30, 60.5f) }, out Marker mass);
            document = document.AddMarker(new Spawn { Id = 0, Name = "", Position = new Vector3(40, 30, 200) }, out Marker spawn);
            document = document.AddMarker(new PathNode { Id = 0, Name = "LandPN1", Layer = PathLayer.Land, Position = new Vector3(90, 30, 30) }, out Marker first);
            document = document.AddMarker(new PathNode { Id = 0, Name = "LandPN2", Layer = PathLayer.Land, Position = new Vector3(110, 30, 30), AdjacentTo = [first.Id] }, out Marker second);
            document = document.LinkPathNodes(first.Id, second.Id);

            document = document.MirrorObjects([ObjectRef.Marker(mass.Id), ObjectRef.Marker(spawn.Id), ObjectRef.Marker(first.Id), ObjectRef.Marker(second.Id)], Four, out ImmutableArray<ObjectRef> added);

            Assert.AreEqual(12, added.Length);
            Assert.AreEqual(4, document.Markers.Resources.Count(resource => resource.Kind == ResourceKind.Mass));
            Assert.IsTrue(document.Markers.Resources.Any(resource => resource.Position.X == 195.5f && resource.Position.Z == 100.5f), "at the ogrid's centre");
            Assert.AreEqual(4, document.Markers.Resources.Select(resource => resource.Name).Distinct().Count(), "named apart");
            Assert.AreEqual(spawns + 4, document.Markers.Spawns.Length, "each image its own army");
            Assert.AreEqual(spawns + 4, document.Scenario.PlayableArmies.Length);
            Assert.AreEqual(8, document.Markers.PathNodes.Length);
            Assert.IsTrue(document.Markers.PathNodes.All(node => node.AdjacentTo.Length == 1), "each image linked to the image of its neighbour");
            Assert.AreEqual(0, SymmetryChecks.NotSymmetric(document, Four).Count(problem => problem.Message.Contains("Mass")));
        }

        [TestMethod]
        public void TheCheckNamesDepositsWithoutAPartner()
        {
            MapDocument document = Blank().AddMarker(new ResourceMarker { Id = 0, Name = "Mass 01", Kind = ResourceKind.Mass, Position = new Vector3(100.5f, 30, 60.5f) }, out Marker mass);
            document = document.AddProps([Rock(30, 200), Rock(32, 200)], out _);

            Problem[] problems = [.. SymmetryChecks.NotSymmetric(document, Diagonal)];

            Problem deposit = problems.Single(problem => problem.Thing == ObjectRef.Marker(mass.Id));
            Assert.AreEqual(ProblemSeverity.Warning, deposit.Severity);
            StringAssert.Contains(deposit.Message, "60.5, 100.5");
            Assert.IsTrue(problems.Any(problem => problem.Severity == ProblemSeverity.Info && problem.Message.StartsWith("2 props")), "props by blueprint");
            Assert.AreEqual(0, SymmetryChecks.NotSymmetric(document, [MapSymmetryTransform.Identity]).Count(), "no symmetry, no problem");
        }

        [TestMethod]
        public void ScatteredPropsComeWithTheirImages()
        {
            MapDocument document = Blank();
            PropScatter scatter = new PropScatter([], [new PaletteEntry("/env/rock.bp", 1)], new PropPlacement { RandomTurn = true }, new Random(3))
            {
                Density = 20,
                Spacing = 1,
                Images = Four,
            };
            List<Prop> props = scatter.Dab(document.Terrain.Heightmap, 60, 60, 10, 30);
            Assert.IsTrue(props.Count >= 8 && props.Count % 4 == 0, $"in fours: {props.Count}");
            MapDocument painted = document.AddProps(props, out ImmutableArray<int> ids);
            Assert.AreEqual(0, SymmetryChecks.NotSymmetric(painted, Four).Count(problem => problem.Thing?.Kind == ObjectKind.Prop), "the two spawns are not for four");
        }

        private static Vector2 Flat(Vector3 place) => new Vector2(MathF.Round(place.X, 3), MathF.Round(place.Z, 3));
    }
}
