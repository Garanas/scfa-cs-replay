using System.Collections.Immutable;
using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// Decals along a height curve (user story T-4, D2): how many, how far apart, turned with the
    /// curve, beside it, at random, on the map, and the images of a curve drawn by a symmetry.
    /// </summary>
    [TestClass]
    public class DecalsAlongCurveTest
    {
        private static MapDocument Flat() => NewMap.Create(new NewMapSettings { Size = 256, Height = 20 });

        private static Decal Pattern(MapDocument document) =>
            DecalEdits.NewDecal(document.Terrain.Heightmap, "env/common/decals/road01_albedo.dds", 0, 0, 6, 12, 0, cutOffLod: 700) with
            {
                Textures = ["/env/common/decals/road01_albedo.dds", "/env/common/decals/road01_specular.dds"],
            };

        /// <summary>
        /// A straight curve along +x at z 128, from x 20 to 220 (200 ogrids).
        /// </summary>
        private static HeightCurve Straight(SymmetrySetting? symmetry = null) => new HeightCurve
        {
            Points = [new CurvePoint(20, 128, 5), new CurvePoint(220, 128, 5)],
            Symmetry = symmetry,
        };

        private static Decal[] Placed(MapDocument document, ImmutableArray<ObjectRef> added) =>
            [.. added.Select(thing => (Decal)document.Find(thing)!)];

        private static Vector3 Centre(Decal decal) => MapObjects.DecalCentre(decal);

        [TestMethod]
        public void DecalsLieEvenlyAlongTheCurveOneStepAtATime()
        {
            MapDocument document = Flat();
            MapDocument result = document.AlongCurve(Straight(), Pattern(document), new DecalsAlongCurve { Spacing = 20 }, out ImmutableArray<ObjectRef> added);

            Decal[] decals = Placed(result, added);
            Assert.AreEqual(10, decals.Length, "200 ogrids in pieces of 20");
            Assert.AreEqual(document.Decals.Length + 10, result.Decals.Length, "added on top of the others");
            for (int i = 0; i < decals.Length; i++)
            {
                Vector3 centre = Centre(decals[i]);
                Assert.AreEqual(30 + 20 * i, centre.X, 0.05, $"decal {i}: in the middle of its piece");
                Assert.AreEqual(128, centre.Z, 0.05);
            }
        }

        [TestMethod]
        public void TheSpacingFitsTheCurveAndDefaultsToTheLength()
        {
            MapDocument document = Flat();
            // 200 / 30 = 6.7: seven pieces of 28.6
            Decal[] seven = Placed(document.AlongCurve(Straight(), Pattern(document), new DecalsAlongCurve { Spacing = 30 }, out ImmutableArray<ObjectRef> a), a);
            Assert.AreEqual(7, seven.Length);
            Assert.AreEqual(200f / 7, Centre(seven[1]).X - Centre(seven[0]).X, 0.05);

            // no spacing: the decals' length, end to end
            MapDocument endToEnd = document.AlongCurve(Straight(), Pattern(document), new DecalsAlongCurve { Length = 25 }, out ImmutableArray<ObjectRef> b);
            Assert.AreEqual(8, b.Length);
        }

        [TestMethod]
        public void EachDecalIsTurnedSoItsLengthRunsAlongTheCurveAndKeepsThePatternsLook()
        {
            MapDocument document = Flat();
            HeightCurve diagonal = new HeightCurve { Points = [new CurvePoint(40, 40, 5), new CurvePoint(140, 140, 5)] };
            MapDocument result = document.AlongCurve(diagonal, Pattern(document), new DecalsAlongCurve { Spacing = 20, Width = 4, Length = 10 }, out ImmutableArray<ObjectRef> added);

            foreach (Decal decal in Placed(result, added))
            {
                Assert.AreEqual(new Vector3(4, 4, 10), decal.Scale, "the settings' width and length");
                (Vector2 _, Vector2 length) = MapObjects.DecalEdges(decal.Scale, decal.Rotation.Y);
                Vector2 direction = Vector2.Normalize(length);
                Assert.AreEqual(MathF.Sqrt(0.5f), direction.X, 1e-3, "its length along the direction of travel");
                Assert.AreEqual(MathF.Sqrt(0.5f), direction.Y, 1e-3);
                CollectionAssert.AreEqual(new[] { "/env/common/decals/road01_albedo.dds", "/env/common/decals/road01_specular.dds" }, decal.Textures.ToArray());
                Assert.AreEqual(700, decal.CutOffLod, "the pattern's cut-off when the settings leave it out");
                Assert.AreEqual(20, decal.Position.Y, 1e-3, "on the terrain");
            }
            Decal[] far = Placed(document.AlongCurve(diagonal, Pattern(document), new DecalsAlongCurve { Spacing = 20, CutOff = 300 }, out ImmutableArray<ObjectRef> other), other);
            Assert.AreEqual(300, far[0].CutOffLod);
            Assert.AreEqual(new Vector3(6, 6, 12), far[0].Scale, "the pattern's size");
        }

        [TestMethod]
        public void TheOffsetPutsThemOnTheLeftAndAlternatingOnBothSides()
        {
            MapDocument document = Flat();
            // walking along +x, the left in the top view is -z (the side a cliff raises)
            Decal[] left = Placed(document.AlongCurve(Straight(), Pattern(document), new DecalsAlongCurve { Spacing = 20, Offset = 6 }, out ImmutableArray<ObjectRef> a), a);
            Assert.IsTrue(left.All(decal => MathF.Abs(Centre(decal).Z - 122) < 0.05f));

            Decal[] right = Placed(document.AlongCurve(Straight(), Pattern(document), new DecalsAlongCurve { Spacing = 20, Offset = -6 }, out ImmutableArray<ObjectRef> b), b);
            Assert.IsTrue(right.All(decal => MathF.Abs(Centre(decal).Z - 134) < 0.05f));

            Decal[] both = Placed(document.AlongCurve(Straight(), Pattern(document), new DecalsAlongCurve { Spacing = 20, Offset = 6, Alternate = true }, out ImmutableArray<ObjectRef> c), c);
            for (int i = 0; i < both.Length; i++)
            {
                Assert.AreEqual(i % 2 == 0 ? 122 : 134, Centre(both[i]).Z, 0.05, $"decal {i}");
            }
        }

        [TestMethod]
        public void TheRandomTurnAndJitterStayWithinTheirBoundsAndFollowTheSeed()
        {
            MapDocument document = Flat();
            DecalsAlongCurve settings = new DecalsAlongCurve { Spacing = 10, RandomTurn = 30, Jitter = 2, Seed = 7 };
            Decal[] first = Placed(document.AlongCurve(Straight(), Pattern(document), settings, out ImmutableArray<ObjectRef> a), a);
            Decal[] again = Placed(document.AlongCurve(Straight(), Pattern(document), settings, out ImmutableArray<ObjectRef> b), b);
            Decal[] other = Placed(document.AlongCurve(Straight(), Pattern(document), settings with { Seed = 8 }, out ImmutableArray<ObjectRef> c), c);

            CollectionAssert.AreEqual(first.Select(Centre).ToArray(), again.Select(Centre).ToArray(), "the same seed, the same decals");
            CollectionAssert.AreNotEqual(first.Select(Centre).ToArray(), other.Select(Centre).ToArray(), "another seed, other places");
            bool turned = false;
            for (int i = 0; i < first.Length; i++)
            {
                Vector3 centre = Centre(first[i]);
                Assert.IsTrue(MathF.Abs(centre.X - (25 + 10 * i)) <= 2.01f && MathF.Abs(centre.Z - 128) <= 2.01f, $"decal {i} within the jitter");
                float turn = (first[i].Rotation.Y + MathF.PI / 2) * 180 / MathF.PI;
                Assert.IsTrue(MathF.Abs(turn) <= 30.01f, $"decal {i} turned {turn} degrees");
                turned |= MathF.Abs(turn) > 1;
            }
            Assert.IsTrue(turned);
        }

        [TestMethod]
        public void DecalsBesideTheMapAreLeftOut()
        {
            MapDocument document = Flat();
            HeightCurve across = new HeightCurve { Points = [new CurvePoint(-100, 50, 5), new CurvePoint(100, 50, 5)] };
            Decal[] decals = Placed(document.AlongCurve(across, Pattern(document), new DecalsAlongCurve { Spacing = 20 }, out ImmutableArray<ObjectRef> added), added);

            Assert.AreEqual(5, decals.Length, "the half on the map: middles at 10, 30, 50, 70 and 90");
            Assert.IsTrue(decals.All(decal => Centre(decal).X is >= 0 and <= 256));
        }

        [TestMethod]
        public void ACurveDrawnByASymmetryGetsTheImagesOfItsDecals()
        {
            MapDocument document = Flat();
            HeightCurve curve = new HeightCurve { Points = [new CurvePoint(20, 40, 5), new CurvePoint(60, 70, 5), new CurvePoint(100, 60, 5)] };
            foreach ((MapSymmetryKind kind, int count) in new[] { (MapSymmetryKind.Rotational, 4), (MapSymmetryKind.MirroredLeftRight, 2) })
            {
                DecalsAlongCurve settings = new DecalsAlongCurve { Spacing = 15, Offset = 3, RandomTurn = 20, Jitter = 1, Seed = 3 };
                Decal[] alone = Placed(document.AlongCurve(curve, Pattern(document), settings, out ImmutableArray<ObjectRef> a), a);
                Decal[] all = Placed(document.AlongCurve(curve with { Symmetry = new SymmetrySetting(kind, count) }, Pattern(document), settings, out ImmutableArray<ObjectRef> b), b);

                Assert.AreEqual(alone.Length * count, all.Length, $"{kind}: every decal in every part");
                IReadOnlyList<MapSymmetryTransform> transforms = MapSymmetry.Transforms(kind, count, 256, 256);
                for (int t = 1; t < count; t++)
                {
                    for (int i = 0; i < alone.Length; i++)
                    {
                        Decal image = all[alone.Length + i * (count - 1) + (t - 1)];
                        Vector2 expected = transforms[t].Apply(new Vector2(Centre(alone[i]).X, Centre(alone[i]).Z));
                        Assert.AreEqual(expected.X, Centre(image).X, 0.05, $"{kind}: image {t} of decal {i}");
                        Assert.AreEqual(expected.Y, Centre(image).Z, 0.05);
                        (float sin, float cos) = MathF.SinCos(alone[i].Rotation.Y);
                        Vector2 width = transforms[t].ApplyDirection(new Vector2(cos, sin));
                        Assert.AreEqual(MathF.Atan2(width.Y, width.X), image.Rotation.Y, 1e-3, "its width along the image of the width");
                    }
                }
            }
        }

        [TestMethod]
        public void AClosedCurveIsCoveredAllRoundAndALineOfNoLengthGetsNone()
        {
            MapDocument document = Flat();
            HeightCurve ring = new HeightCurve { Points = [new CurvePoint(100, 100, 5), new CurvePoint(160, 100, 5), new CurvePoint(160, 160, 5), new CurvePoint(100, 160, 5)], Closed = true };
            IReadOnlyList<DecalPlace> places = DecalEdits.PlacesAlong(HeightCurves.Spline(ring), new DecalsAlongCurve { Spacing = 10 });
            Assert.IsTrue(places.Count >= 20);
            float gap = Vector2.Distance(new Vector2(places[^1].X, places[^1].Z), new Vector2(places[0].X, places[0].Z));
            float step = Vector2.Distance(new Vector2(places[1].X, places[1].Z), new Vector2(places[0].X, places[0].Z));
            Assert.AreEqual(step, gap, 1.5, "the last decal is as far from the first as the others from each other");

            Assert.AreEqual(0, DecalEdits.PlacesAlong([new CurvePoint(5, 5, 0)], new DecalsAlongCurve()).Count);
            Assert.AreEqual(0, DecalEdits.PlacesAlong([new CurvePoint(5, 5, 0), new CurvePoint(5, 5, 0)], new DecalsAlongCurve()).Count);
        }
    }
}
