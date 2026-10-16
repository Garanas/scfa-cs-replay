using System.Diagnostics;
using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The curves' upgrades (user story T-4, wave 17): values per point, smooth heights, sharp
    /// points, reversing, the fade's shapes, the cliff's face, edge noise and variation along the
    /// curve that follow the symmetry, closed curves that fill, rivers, road modes and ramps.
    /// </summary>
    [TestClass]
    public class HeightCurvesTest
    {
        private const int Size = 256;

        private static float[] ValuesOf(HeightCurve curve, int size = Size) => HeightCurves.Rasterize(curve, size, size).Values.ToArray();

        private static float At(float[] values, int x, int z, int size = Size) => values[z * (size + 1) + x];

        private static HeightCurve Line(CurveProfile profile, float height = 10, float width = 8, float falloff = 12) => new HeightCurve
        {
            Profile = profile, Width = width, Falloff = falloff,
            Points = [new CurvePoint(60, 128, height), new CurvePoint(128, 120, height), new CurvePoint(196, 128, height)],
        };

        [TestMethod]
        [DataRow(CurveProfile.Ridge)]
        [DataRow(CurveProfile.Valley)]
        [DataRow(CurveProfile.Road)]
        [DataRow(CurveProfile.Cliff)]
        public void CurvesOfOneHeightKeepTheirExactOutput(CurveProfile profile)
        {
            HeightCurve curve = Line(profile, 12) with { Points = [new CurvePoint(40, 60, 12), new CurvePoint(120, 90, 12), new CurvePoint(150, 200, 12), new CurvePoint(60, 180, 12)] };
            (TiledBuffer<float> values, TiledBuffer<byte>? coverage) = HeightCurves.Rasterize(curve, Size, Size);
            (TiledBuffer<float> legacy, TiledBuffer<byte>? legacyCoverage) = LegacyHeightCurves.Rasterize(curve, Size, Size);
            CollectionAssert.AreEqual(legacy.ToArray(), values.ToArray(), "the values as before the upgrades");
            Assert.AreEqual(legacyCoverage is null, coverage is null);
            if (legacyCoverage is not null)
            {
                CollectionAssert.AreEqual(legacyCoverage.ToArray(), coverage!.ToArray(), "the coverage as before");
            }
        }

        [TestMethod]
        public void HeightsBetweenPointsFollowAMonotoneCubic()
        {
            HeightCurve curve = new HeightCurve { Profile = CurveProfile.Road, Points = [new CurvePoint(0, 0, 0), new CurvePoint(20, 0, 10), new CurvePoint(40, 0, 10), new CurvePoint(60, 0, 30)] };
            IReadOnlyList<CurveSample> samples = HeightCurves.Samples(curve);
            Assert.IsTrue(samples.All(sample => sample.Height >= 0 && sample.Height <= 30), "no height outside the points'");
            Assert.IsTrue(samples.Where(sample => sample.X >= 20 && sample.X <= 40).All(sample => sample.Height == 10), "flat between points of one height");
            for (int i = 1; i < samples.Count; i++)
            {
                Assert.IsTrue(samples[i].Height >= samples[i - 1].Height - 1e-4f, "never falling on a rising road");
            }
            // smooth: the grade into the second point is about the grade out of it
            float before = samples.First(sample => sample.X > 18.5f).Height - samples.Last(sample => sample.X < 18.5f).Height;
            Assert.IsTrue(before < 0.4f, "the grade eases into the flat part");
            CurveSample middle = samples.First(sample => MathF.Abs(sample.X - 10) < 0.6f);
            Assert.AreEqual(5, middle.Height, 1.5f);
        }

        [TestMethod]
        public void APointsOwnWidthFalloffAndStrengthBlendAlongTheCurve()
        {
            HeightCurve curve = new HeightCurve
            {
                Profile = CurveProfile.Ridge, Width = 4, Falloff = 4,
                Points = [new CurvePoint(40, 128, 10, Width: 30), new CurvePoint(216, 128, 10, Strength: 0)],
            };
            float[] values = ValuesOf(curve);
            Assert.AreEqual(10, At(values, 44, 140), 0.5f, "wide at the first point");
            Assert.IsTrue(At(values, 128, 128) is > 2 and < 8, "fading out along the curve");
            Assert.AreEqual(0, At(values, 214, 128), 0.5f, "no strength at the last point");

            HeightCurve sides = new HeightCurve
            {
                Profile = CurveProfile.Ridge, Width = 2, Falloff = 4, FalloffRight = 20,
                Points = [new CurvePoint(40, 128, 10), new CurvePoint(216, 128, 10, FalloffLeft: 4)],
            };
            values = ValuesOf(sides);
            // walking to +x, the left in the top view is -z
            Assert.AreEqual(0, At(values, 128, 120), 1e-3f, "the left fades over 4");
            Assert.IsTrue(At(values, 128, 136) > 3, "the right over 20");
        }

        [TestMethod]
        public void ASharpPointMakesACorner()
        {
            CurvePoint[] points = [new CurvePoint(0, 0, 5), new CurvePoint(10, 0, 5), new CurvePoint(10, 10, 5)];
            IReadOnlyList<CurveSample> smooth = HeightCurves.Samples(new HeightCurve { Points = [.. points] });
            IReadOnlyList<CurveSample> sharp = HeightCurves.Samples(new HeightCurve { Points = [points[0], points[1] with { Sharp = true }, points[2]] });
            Assert.IsTrue(smooth.Where(sample => sample.X < 10).Any(sample => MathF.Abs(sample.Z) > 0.05f), "a smooth bend leaves the straight line");
            Assert.IsTrue(sharp.Where(sample => sample.X <= 10 && sample.Z <= 0).All(sample => MathF.Abs(sample.Z) < 1e-4f), "a corner keeps it");
            Assert.IsTrue(sharp.Where(sample => sample.Z > 0).All(sample => MathF.Abs(sample.X - 10) < 1e-4f), "on both sides");
        }

        [TestMethod]
        public void AReversedCurveKeepsItsLookButACliffFlips()
        {
            HeightCurve ridge = Line(CurveProfile.Ridge) with { FalloffRight = 20, Camber = 2 };
            ridge = ridge with { Points = ridge.Points.SetItem(1, ridge.Points[1] with { FalloffLeft = 3, Width = 12 }) };
            HeightCurve reversed = HeightCurves.Reversed(ridge);
            Assert.AreEqual(20, reversed.Falloff);
            Assert.AreEqual(12, reversed.FalloffRight);
            Assert.AreEqual(-2, reversed.Camber);
            Assert.AreEqual(3, reversed.Points[1].FalloffRight);
            float[] before = ValuesOf(ridge);
            float[] after = ValuesOf(reversed);
            for (int i = 0; i < before.Length; i++)
            {
                Assert.AreEqual(before[i], after[i], 0.05f, $"the same ridge at {i % (Size + 1)}, {i / (Size + 1)}");
            }
            Assert.AreEqual(ridge, HeightCurves.Reversed(reversed), "twice is the curve");

            HeightCurve cliff = Line(CurveProfile.Cliff);
            float[] high = ValuesOf(cliff);
            float[] flipped = ValuesOf(HeightCurves.Reversed(cliff));
            // walking to +x the left is -z
            Assert.AreEqual(10, At(high, 128, 115), 0.1f);
            Assert.AreEqual(0, At(high, 128, 125), 0.1f);
            Assert.AreEqual(0, At(flipped, 128, 115), 0.1f, "the high side flips");
            Assert.AreEqual(10, At(flipped, 128, 125), 0.1f);
        }

        [TestMethod]
        public void TheFadeTakesItsShape()
        {
            Assert.AreEqual(0.5f, HeightCurves.Fade(0.5f, CurveFalloffShape.Linear), 1e-6f);
            Assert.AreEqual(0.5f, HeightCurves.Fade(0.5f, CurveFalloffShape.Smooth), 1e-6f);
            Assert.AreEqual(0.25f, HeightCurves.Fade(0.5f, CurveFalloffShape.EaseIn), 1e-6f);
            Assert.AreEqual(0.75f, HeightCurves.Fade(0.5f, CurveFalloffShape.EaseOut), 1e-6f);
            Assert.AreEqual(1 / 3f, HeightCurves.Fade(0.4f, CurveFalloffShape.Terraced, 3), 1e-6f, "a flat step");
            Assert.AreEqual(1 / 3f, HeightCurves.Fade(0.5f, CurveFalloffShape.Terraced, 3), 1e-6f);
            Assert.AreEqual(1, HeightCurves.Fade(1, CurveFalloffShape.Terraced, 3), 1e-6f);

            HeightCurve linear = new HeightCurve { Profile = CurveProfile.Ridge, Width = 0, Falloff = 10, FalloffShape = CurveFalloffShape.Linear, Points = [new CurvePoint(40, 128, 10), new CurvePoint(216, 128, 10)] };
            Assert.AreEqual(5, At(ValuesOf(linear), 128, 133), 1e-3f, "half way down the fade");
            Assert.AreEqual(CurveFalloffShape.EaseOut, HeightCurves.ShapeOf(new HeightCurve { Profile = CurveProfile.River }), "a river's banks ease out");
            Assert.AreEqual(CurveFalloffShape.Linear, HeightCurves.ShapeOf(new HeightCurve { Profile = CurveProfile.River, FalloffShape = CurveFalloffShape.Linear }));
        }

        [TestMethod]
        public void ACliffsFaceHasAWidthAndCanBeTerraced()
        {
            HeightCurve cliff = new HeightCurve { Profile = CurveProfile.Cliff, Width = 20, Falloff = 10, CliffFace = 8, FalloffShape = CurveFalloffShape.Linear, Points = [new CurvePoint(40, 128, 12), new CurvePoint(216, 128, 12)] };
            float[] values = ValuesOf(cliff);
            // walking to +x the high side is -z
            Assert.AreEqual(0, At(values, 128, 132), 1e-3f, "below the face");
            Assert.AreEqual(6, At(values, 128, 128), 1e-3f, "half way up the face at the line");
            Assert.AreEqual(9, At(values, 128, 126), 1e-3f);
            Assert.AreEqual(12, At(values, 128, 124), 1e-3f, "on top");

            float[] terraced = ValuesOf(cliff with { FalloffShape = CurveFalloffShape.Terraced, TerraceSteps = 2 });
            Assert.AreEqual(6, At(terraced, 128, 127), 1e-3f, "a step half way");
            Assert.AreEqual(6, At(terraced, 128, 128), 1e-3f);
            Assert.AreEqual(0, At(terraced, 128, 131), 1e-3f);
        }

        [TestMethod]
        public void EdgeNoiseMovesTheFade()
        {
            HeightCurve ridge = Line(CurveProfile.Ridge);
            float[] plain = ValuesOf(ridge);
            float[] noisy = ValuesOf(ridge with { NoiseAmplitude = 4, NoiseScale = 8, NoiseSeed = 3 });
            CollectionAssert.AreEqual(noisy, ValuesOf(ridge with { NoiseAmplitude = 4, NoiseScale = 8, NoiseSeed = 3 }), "the same for the same seed");
            CollectionAssert.AreNotEqual(noisy, ValuesOf(ridge with { NoiseAmplitude = 4, NoiseScale = 8, NoiseSeed = 4 }), "another for another seed");
            int changed = Enumerable.Range(0, plain.Length).Count(i => MathF.Abs(plain[i] - noisy[i]) > 0.1f);
            Assert.IsTrue(changed > 200, $"the banks move ({changed} corners)");
            Assert.AreEqual(10, At(noisy, 128, 121), 1e-3f, "the crest stays");
        }

        [TestMethod]
        public void HeightAndWidthVaryAlongTheCurveWithinTheirRanges()
        {
            HeightCurve ridge = new HeightCurve
            {
                Profile = CurveProfile.Ridge, Width = 4, Falloff = 6, VariationScale = 12,
                HeightVariation = new CurveRange(-1, 0), WidthVariation = new CurveRange(0, 6),
                Points = [new CurvePoint(20, 128, 5), new CurvePoint(236, 128, 5)],
            };
            IReadOnlyList<CurveSample> samples = HeightCurves.Samples(ridge);
            Assert.IsTrue(samples.All(sample => sample.Height >= 4 - 1e-4f && sample.Height <= 5 + 1e-4f), "a ridge of 5 with -1 to 0 lies between 4 and 5");
            Assert.IsTrue(samples.Max(sample => sample.Height) - samples.Min(sample => sample.Height) > 0.3f, "and varies");
            Assert.IsTrue(samples.All(sample => sample.Width >= 4 - 1e-4f && sample.Width <= 10 + 1e-4f));
            Assert.IsTrue(samples.Max(sample => sample.Width) - samples.Min(sample => sample.Width) > 1, "the width varies");
            float[] values = ValuesOf(ridge);
            float[] crest = [.. Enumerable.Range(30, 190).Select(x => At(values, x, 128))];
            Assert.IsTrue(crest.All(h => h >= 4 - 1e-3f && h <= 5 + 1e-3f), "the crest between 4 and 5");
        }

        [TestMethod]
        [DataRow(MapSymmetryKind.Rotational, 2)]
        [DataRow(MapSymmetryKind.Rotational, 4)]
        [DataRow(MapSymmetryKind.MirroredLeftRight, 2)]
        [DataRow(MapSymmetryKind.MirroredDiagonal, 2)]
        public void NoiseAndVariationAreTheSameInEveryPart(MapSymmetryKind kind, int count)
        {
            foreach (CurveProfile profile in new[] { CurveProfile.Ridge, CurveProfile.Cliff, CurveProfile.Road })
            {
                HeightCurve curve = new HeightCurve
                {
                    Profile = profile, Width = 6, Falloff = 8, FalloffRight = 4, NoiseAmplitude = 3, NoiseScale = 6, NoiseSeed = 11,
                    HeightVariation = new CurveRange(-3, 2), WidthVariation = new CurveRange(-2, 4), VariationScale = 10,
                    Symmetry = new SymmetrySetting(kind, count),
                    Points = [new CurvePoint(20, 60, 10), new CurvePoint(50, 90, 14), new CurvePoint(60, 130, 12, Width: 10)],
                };
                float[] values = ValuesOf(curve);
                foreach (MapSymmetryTransform transform in MapSymmetry.Transforms(kind, count, Size, Size).Skip(1))
                {
                    int compared = 0;
                    // away from where the parts overlap (there the one that shapes most wins)
                    for (int z = 35; z < 155; z++)
                    {
                        for (int x = 0; x < 90; x++)
                        {
                            Vector2 image = transform.Apply(new Vector2(x, z));
                            int ix = (int)MathF.Round(image.X), iz = (int)MathF.Round(image.Y);
                            if (MathF.Abs(image.X - ix) > 1e-3f || MathF.Abs(image.Y - iz) > 1e-3f || ix < 0 || iz < 0 || ix > Size || iz > Size)
                            {
                                continue;
                            }
                            compared++;
                            Assert.AreEqual(At(values, x, z), At(values, ix, iz), 0.05f, $"{profile}: at {x}, {z} and its image {ix}, {iz}");
                        }
                    }
                    Assert.IsTrue(compared > 1000);
                }
            }
        }

        [TestMethod]
        public void AClosedCurveFillsItsInsideUnlessLineOnly()
        {
            // sharp corners: straight sides
            CurvePoint[] square = [new CurvePoint(80, 80, 10, Sharp: true), new CurvePoint(176, 80, 10, Sharp: true), new CurvePoint(176, 176, 14, Sharp: true), new CurvePoint(80, 176, 14, Sharp: true)];
            HeightCurve ridge = new HeightCurve { Profile = CurveProfile.Ridge, Width = 4, Falloff = 6, Closed = true, Points = [.. square] };
            float[] filled = ValuesOf(ridge);
            float mean = HeightCurves.Samples(ridge).SkipLast(1).Average(sample => sample.Height);
            Assert.AreEqual(mean, At(filled, 128, 128), 1e-3f, "a plateau at the mean height along the curve");
            Assert.AreEqual(mean, At(filled, 82, 128), 1e-3f, "up to the rim");
            Assert.AreEqual(0, At(filled, 60, 128), 1e-3f, "fading outside");
            Assert.IsTrue(At(filled, 80 - 4, 128) > 0, "over the falloff");
            Assert.AreEqual(0, At(ValuesOf(ridge with { LineOnly = true }), 128, 128), 1e-3f, "line only leaves the inside");
            Assert.AreEqual(-mean, At(ValuesOf(ridge with { Profile = CurveProfile.Valley }), 128, 128), 1e-3f, "a basin");

            // either way round
            HeightCurve other = ridge with { Points = [.. square.Reverse()] };
            Assert.AreEqual(mean, At(ValuesOf(other), 128, 128), 1e-3f);

            HeightCurve cliff = ridge with { Profile = CurveProfile.Cliff };
            float[] mesa = ValuesOf(cliff);
            Assert.AreEqual(mean, At(mesa, 128, 128), 1e-3f, "a closed cliff raises its inside");
            Assert.AreEqual(mean, At(mesa, 82, 128), 1e-3f, "with no fade back inside");
            Assert.AreEqual(0, At(mesa, 78, 128), 1e-3f, "and nothing outside the face");
            Assert.AreEqual(0, At(ValuesOf(cliff with { Points = [.. square.Reverse()] }), 78, 128), 1e-3f, "either way round");

            (TiledBuffer<float> pad, TiledBuffer<byte>? coverage) = HeightCurves.Rasterize(ridge with { Profile = CurveProfile.Road }, Size, Size);
            Assert.AreEqual(255, coverage![128, 128], "a road's pad covers its inside");
            Assert.AreEqual(mean, pad[128, 128], 1e-3f);

            float[] ragged = ValuesOf(cliff with { NoiseAmplitude = 3, NoiseScale = 6 });
            Assert.IsTrue(Enumerable.Range(80, 96).Any(z => At(ragged, 82, z) == 0) || Enumerable.Range(80, 96).Any(z => At(ragged, 78, z) > 0), "edge noise makes the rim ragged");
        }

        [TestMethod]
        public void ARiverCutsItsBedAndRoadsCutOrFill()
        {
            Assert.AreEqual(LayerCombine.Minimum, HeightCurves.CombineOf(new HeightCurve { Profile = CurveProfile.River }));
            Assert.AreEqual(LayerCombine.Replace, HeightCurves.CombineOf(new HeightCurve { Profile = CurveProfile.Road }));
            Assert.AreEqual(LayerCombine.Minimum, HeightCurves.CombineOf(new HeightCurve { Profile = CurveProfile.Road, RoadMode = CurveRoadMode.CutOnly }));
            Assert.AreEqual(LayerCombine.Maximum, HeightCurves.CombineOf(new HeightCurve { Profile = CurveProfile.Road, RoadMode = CurveRoadMode.FillOnly }));

            // a map with a hill on its left half: a road at 40 through it cuts the hill only when cutting
            MapDocument document = HeightLayerEdits.Start(NewMap.Create(new NewMapSettings { Size = Size, Height = 35 }));
            TiledBuffer<float>.Builder hill = TiledBuffer<float>.Create(Size + 1, Size + 1).ToBuilder();
            for (int z = 0; z <= Size; z++)
            {
                for (int x = 0; x < 128; x++)
                {
                    hill[x, z] = 20;
                }
            }
            document = HeightLayerEdits.Add(document, new Layer { Name = "hill", Source = LayerSource.Painted, Combine = LayerCombine.Add, Values = hill.ToBuffer() });
            HeightCurve road = new HeightCurve { Profile = CurveProfile.Road, Width = 6, Falloff = 4, Points = [new CurvePoint(20, 128, 40), new CurvePoint(236, 128, 40)] };
            document = HeightLayers.Recomposite(HeightLayerEdits.Add(document, HeightLayerEdits.NewCurve(document, "road", road)));
            Assert.AreEqual(40, document.Terrain.Heightmap.GetHeight(64, 128), 0.05f, "cut");
            Assert.AreEqual(40, document.Terrain.Heightmap.GetHeight(192, 128), 0.05f, "filled");
            MapDocument cut = HeightLayers.Recomposite(HeightLayerEdits.SetCurve(document, "road", road with { RoadMode = CurveRoadMode.CutOnly }));
            Assert.AreEqual(LayerCombine.Minimum, cut.HeightStack.Layers[2].Combine, "the road mode sets the way of combining");
            Assert.AreEqual(40, cut.Terrain.Heightmap.GetHeight(64, 128), 0.05f);
            Assert.AreEqual(35, cut.Terrain.Heightmap.GetHeight(192, 128), 0.05f, "hollows stay");
            MapDocument fill = HeightLayers.Recomposite(HeightLayerEdits.SetCurve(cut, "road", road with { RoadMode = CurveRoadMode.FillOnly }));
            Assert.AreEqual(LayerCombine.Maximum, fill.HeightStack.Layers[2].Combine);
            Assert.AreEqual(55, fill.Terrain.Heightmap.GetHeight(64, 128), 0.05f, "hills stay");
            Assert.AreEqual(40, fill.Terrain.Heightmap.GetHeight(192, 128), 0.05f);
            MapDocument river = HeightLayers.Recomposite(HeightLayerEdits.SetCurve(fill, "road", road with { Profile = CurveProfile.River, Points = [new CurvePoint(20, 128, 30), new CurvePoint(236, 128, 30)] }));
            Assert.AreEqual(LayerCombine.Minimum, river.HeightStack.Layers[2].Combine, "a river cuts");
            Assert.AreEqual(30, river.Terrain.Heightmap.GetHeight(192, 128), 0.05f, "its bed");
            float bank = river.Terrain.Heightmap.GetHeight(192, 134);
            Assert.IsTrue(bank > 30 && bank < 35, "its banks");

            // camber: the left edge higher than the right
            (TiledBuffer<float> cambered, _) = HeightCurves.Rasterize(road with { Camber = 2 }, Size, Size);
            Assert.AreEqual(41, cambered[128, 125], 1e-3f, "the left edge (walking to +x: -z)");
            Assert.AreEqual(39, cambered[128, 131], 1e-3f, "the right edge");
            Assert.AreEqual(40, cambered[128, 128], 1e-3f);
        }

        [TestMethod]
        public void PointsAreSetGradedAndSnappedToTheGround()
        {
            MapDocument document = HeightLayerEdits.Start(NewMap.Create(new NewMapSettings { Size = Size, Height = 20 }));
            TiledBuffer<float>.Builder plateau = TiledBuffer<float>.Create(Size + 1, Size + 1).ToBuilder();
            for (int z = 0; z <= Size; z++)
            {
                for (int x = 150; x <= Size; x++)
                {
                    plateau[x, z] = 30;
                }
            }
            document = HeightLayerEdits.Add(document, new Layer { Name = "plateau", Source = LayerSource.Painted, Combine = LayerCombine.Add, Values = plateau.ToBuffer() });
            HeightCurve road = new HeightCurve
            {
                Profile = CurveProfile.Road, Width = 6, Falloff = 4,
                Points = [new CurvePoint(40, 128, 0), new CurvePoint(80, 128, 0), new CurvePoint(120, 128, 0), new CurvePoint(200, 128, 0)],
            };
            document = HeightLayerEdits.Add(document, HeightLayerEdits.NewCurve(document, "road", road));
            // a layer above the road does not count as ground
            document = HeightLayerEdits.Add(document, new Layer { Name = "above", Source = LayerSource.Painted, Combine = LayerCombine.Add, Values = plateau.ToBuffer() });

            MapDocument ends = HeightLayerEdits.SnapCurve(document, "road", [0, 3]);
            HeightCurve snapped = HeightLayerEdits.CurveOf(ends, "road");
            Assert.AreEqual(20, snapped.Points[0].Height, 1e-3f, "on the low ground");
            Assert.AreEqual(50, snapped.Points[3].Height, 1e-3f, "on the plateau, the layer above not counted");
            Assert.AreEqual(0, snapped.Points[1].Height, "the others stay");
            Assert.AreEqual(50, HeightLayerEdits.CurveOf(HeightLayerEdits.SnapCurve(document, "road"), "road").Points[3].Height, 1e-3f, "all of them");

            MapDocument even = HeightLayerEdits.EvenGrade(ends, "road", 0, 3);
            HeightCurve graded = HeightLayerEdits.CurveOf(even, "road");
            IReadOnlyList<CurveGrade> grades = HeightCurves.Grades(graded);
            Assert.AreEqual(3, grades.Count);
            Assert.AreEqual(grades[0].Percent, grades[1].Percent, 0.5f, "one grade from end to end");
            Assert.AreEqual(grades[1].Percent, grades[2].Percent, 0.5f);
            Assert.AreEqual(30f / 160 * 100, grades.Sum(grade => grade.Rise) / grades.Sum(grade => grade.Run) * 100, 0.5f);
            Assert.AreEqual(MathF.Atan2(grades[0].Rise, grades[0].Run) * 180 / MathF.PI, grades[0].Degrees, 1e-4f);
            Assert.AreEqual(40, grades[0].Run, 0.1f, "the run along the spline");
            Assert.IsTrue(MathF.Abs(grades[0].SteepestPercent) >= MathF.Abs(grades[0].Percent) - 0.5f);

            MapDocument set = HeightLayerEdits.SetCurvePoint(even, "road", 1, new CurvePoint(80, 130, 25, Width: 10, Strength: 0.5f, Sharp: true));
            Assert.AreEqual(new CurvePoint(80, 130, 25, Width: 10, Strength: 0.5f, Sharp: true), HeightLayerEdits.CurveOf(set, "road").Points[1]);
            Assert.ThrowsException<ArgumentException>(() => HeightLayerEdits.SetCurvePoint(even, "road", 4, new CurvePoint(0, 0, 0)));
            Assert.ThrowsException<ArgumentException>(() => HeightLayerEdits.SetCurvePoint(even, "road", 0, new CurvePoint(0, 0, 0, Strength: 2)));
            Assert.ThrowsException<ArgumentException>(() => HeightLayerEdits.EvenGrade(even, "road", 1, 1));

            MapDocument ridge = HeightLayerEdits.SetCurve(document, "road", road with { Profile = CurveProfile.Ridge });
            Assert.ThrowsException<ArgumentException>(() => HeightLayerEdits.SnapCurve(ridge, "road"), "a ridge's heights are a rise, not the ground's");

            MapDocument reversed = HeightLayerEdits.ReverseCurve(document, "road");
            Assert.AreEqual(road.Points[^1], HeightLayerEdits.CurveOf(reversed, "road").Points[0]);
        }

        [TestMethod]
        public void TheProjectKeepsEveryNewSettingAndOldClosedCurvesKeepTheirLook()
        {
            MapDocument document = HeightLayerEdits.Start(NewMap.Create(new NewMapSettings { Size = Size, Height = 20 }));
            HeightCurve full = new HeightCurve
            {
                Profile = CurveProfile.River, Width = 5, Falloff = 7, FalloffRight = 9, FalloffShape = CurveFalloffShape.Terraced, TerraceSteps = 4,
                CliffFace = 3, RoadMode = CurveRoadMode.FillOnly, Camber = 1.5f, NoiseAmplitude = 2, NoiseScale = 5, NoiseSeed = 7,
                HeightVariation = new CurveRange(-1, 0.5f), WidthVariation = new CurveRange(0, 3), VariationScale = 20,
                Points = [new CurvePoint(30, 30, 10, Width: 3, FalloffLeft: 2, FalloffRight: 6, Strength: 0.7f, Sharp: true), new CurvePoint(90, 60, 12), new CurvePoint(60, 120, 11, Sharp: true)],
            };
            HeightCurve closed = new HeightCurve { Profile = CurveProfile.Ridge, Closed = true, Points = [new CurvePoint(150, 150, 8), new CurvePoint(220, 150, 8), new CurvePoint(220, 220, 8), new CurvePoint(150, 220, 8)] };
            document = HeightLayerEdits.Add(document, HeightLayerEdits.NewCurve(document, "river", full));
            document = HeightLayerEdits.Add(document, HeightLayerEdits.NewCurve(document, "plateau", closed));
            document = HeightLayers.Recomposite(document);
            Assert.AreEqual(28, document.Terrain.Heightmap.GetHeight(185, 185), 0.05f, "a new closed curve fills");

            string project = ProjectFolders.New("curves-9");
            MapProjectStore.Save(document, project);
            MapDocument loaded = MapProjectStore.Load(project);
            Assert.AreEqual(full, HeightLayerEdits.CurveOf(loaded, "river"), "every setting kept");
            Assert.AreEqual(closed, HeightLayerEdits.CurveOf(loaded, "plateau"), "a filling closed curve stays one");
            Assert.AreEqual(28, loaded.Terrain.Heightmap.GetHeight(185, 185), 0.05f);
            string index = Path.Combine(project, MapProjectLayout.HeightStackIndex);
            StringAssert.Contains(File.ReadAllText(index), "\"lineOnly\": false");

            // a project of format 8 wrote no lineOnly: its closed curve shaped its line only, and still does
            File.WriteAllText(index, File.ReadAllText(index).Replace("\"lineOnly\": false,", "").Replace(",\n          \"lineOnly\": false", "").Replace("\"lineOnly\": false", "\"closed\": true"));
            StringAssert.DoesNotMatch(File.ReadAllText(index), new System.Text.RegularExpressions.Regex("lineOnly"));
            MapDocument old = MapProjectStore.Load(project);
            HeightCurve kept = HeightLayerEdits.CurveOf(old, "plateau");
            Assert.IsTrue(kept.Closed && kept.LineOnly, "an old closed curve opens as line only");
            Assert.AreEqual(20, old.Terrain.Heightmap.GetHeight(185, 185), 0.05f, "its inside as it was");
            CollectionAssert.AreEqual(LegacyHeightCurves.Rasterize(closed, Size, Size).Values.ToArray(), old.HeightStack.Layers[2].Values!.ToArray(), "its line as it was");
        }

        [TestMethod]
        public void ALongCurveWithNoiseOnTheLargestMapStaysQuick()
        {
            const int large = 4096;
            HeightCurve curve = new HeightCurve
            {
                Profile = CurveProfile.Ridge, Width = 10, Falloff = 16,
                Points = [new CurvePoint(1000, 1000, 20), new CurvePoint(1200, 1100, 24), new CurvePoint(1350, 1300, 20), new CurvePoint(1450, 1450, 18)],
            };
            HeightCurve noisy = curve with { NoiseAmplitude = 4, NoiseScale = 12, HeightVariation = new CurveRange(-2, 2), WidthVariation = new CurveRange(0, 4) };
            TimeSpan Time(Action action)
            {
                TimeSpan best = TimeSpan.MaxValue;
                for (int run = 0; run < 3; run++)
                {
                    Stopwatch watch = Stopwatch.StartNew();
                    action();
                    best = watch.Elapsed < best ? watch.Elapsed : best;
                }
                return best;
            }
            float length = HeightCurves.Samples(curve)[^1].Distance;
            TimeSpan legacy = Time(() => LegacyHeightCurves.Rasterize(curve, large, large));
            TimeSpan plain = Time(() => HeightCurves.Rasterize(curve, large, large));
            TimeSpan withNoise = Time(() => HeightCurves.Rasterize(noisy, large, large));
            Console.WriteLine($"A curve of {length:0} ogrids on a {large} map: before the upgrades {legacy.TotalMilliseconds:0} ms, now {plain.TotalMilliseconds:0} ms, with noise and variation {withNoise.TotalMilliseconds:0} ms");
            Assert.IsTrue(withNoise < TimeSpan.FromSeconds(2), $"{withNoise.TotalMilliseconds:0} ms");
        }
    }
}
