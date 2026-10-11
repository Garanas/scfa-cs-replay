using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The width and fade lines of a curve (user story T-4, wave 18): where the window draws them
    /// agrees with what the rasterizer makes.
    /// </summary>
    [TestClass]
    public class CurveOutlinesTest
    {
        private const int Size = 256;

        private static HeightCurve Straight(CurveProfile profile) => new HeightCurve
        {
            Profile = profile, Width = 8, Falloff = 12,
            Points = [new CurvePoint(40, 128, 10), new CurvePoint(128, 128, 10), new CurvePoint(216, 128, 10)],
        };

        private static float MiddleZ(CurveOutline outline) => outline.Points[outline.Points.Count / 2].Y;

        [TestMethod]
        public void ALineHasItsEdgesAndFadesOnBothSides()
        {
            IReadOnlyList<CurveOutline> outlines = CurveOutlines.Of(Straight(CurveProfile.Ridge), Size, Size);
            Assert.AreEqual(4, outlines.Count);
            CollectionAssert.AreEquivalent(new[] { 124f, 132f }, outlines.Where(o => o.Kind == CurveOutlineKind.Edge).Select(MiddleZ).ToArray());
            CollectionAssert.AreEquivalent(new[] { 112f, 144f }, outlines.Where(o => o.Kind == CurveOutlineKind.Fade).Select(MiddleZ).ToArray());

            // the rasterizer agrees: full inside the edge, nothing beyond the fade
            float[] values = HeightCurves.Rasterize(Straight(CurveProfile.Ridge), Size, Size).Values.ToArray();
            Assert.AreEqual(10, values[124 * (Size + 1) + 128], 1e-3);
            Assert.AreEqual(0, values[111 * (Size + 1) + 128], 1e-3);
        }

        [TestMethod]
        public void APointsOwnWidthAndFalloffMoveTheLines()
        {
            HeightCurve curve = Straight(CurveProfile.Ridge);
            curve = curve with { Points = curve.Points.SetItem(1, curve.Points[1] with { Width = 20, FalloffRight = 2 }) };
            IReadOnlyList<CurveOutline> outlines = CurveOutlines.Of(curve, Size, Size);
            float[] edges = [.. outlines.Where(o => o.Kind == CurveOutlineKind.Edge).Select(MiddleZ)];
            CollectionAssert.AreEquivalent(new[] { 118f, 138f }, edges);
            // the right of travel along +x is +z: the fade 2 beyond the edge there
            Assert.AreEqual(140, outlines.Where(o => o.Kind == CurveOutlineKind.Fade).Select(MiddleZ).Max(), 1e-3);
        }

        [TestMethod]
        public void ACliffShowsItsHighSideOnly()
        {
            IReadOnlyList<CurveOutline> outlines = CurveOutlines.Of(Straight(CurveProfile.Cliff), Size, Size);
            Assert.AreEqual(2, outlines.Count);
            // the left of travel along +x is -z: the high side
            Assert.AreEqual(120, MiddleZ(outlines.Single(o => o.Kind == CurveOutlineKind.Edge)), 1e-3);
            Assert.AreEqual(108, MiddleZ(outlines.Single(o => o.Kind == CurveOutlineKind.Fade)), 1e-3);
            float[] values = HeightCurves.Rasterize(Straight(CurveProfile.Cliff), Size, Size).Values.ToArray();
            Assert.AreEqual(10, values[121 * (Size + 1) + 128], 1e-3, "full on the high side");
            Assert.AreEqual(0, values[134 * (Size + 1) + 128], 1e-3, "nothing on the low side");
        }

        [TestMethod]
        public void AFillShowsTheRimOutside()
        {
            HeightCurve ring = new HeightCurve
            {
                Profile = CurveProfile.Ridge, Width = 4, Falloff = 10, Closed = true,
                Points = [new CurvePoint(100, 100, 5), new CurvePoint(156, 100, 5), new CurvePoint(156, 156, 5), new CurvePoint(100, 156, 5)],
            };
            Assert.IsTrue(ring.Fills);
            IReadOnlyList<CurveOutline> outlines = CurveOutlines.Of(ring, Size, Size);
            Assert.AreEqual(2, outlines.Count);
            Vector2 middle = new Vector2(128, 128);
            float Radius(CurveOutline outline) => outline.Points.Min(p => Vector2.Distance(p, middle));
            float line = CurveOutlines.Of(ring with { Width = 0, Falloff = 0 }, Size, Size)[0].Points.Min(p => Vector2.Distance(p, middle));
            CurveOutline edge = outlines.Single(o => o.Kind == CurveOutlineKind.Edge);
            CurveOutline fade = outlines.Single(o => o.Kind == CurveOutlineKind.Fade);
            Assert.AreEqual(line + 2, Radius(edge), 0.5, "half the width outside the line");
            Assert.AreEqual(line + 12, Radius(fade), 0.5, "and the falloff beyond");
        }

        [TestMethod]
        public void AMirroredCurveHasTheLinesOfItsImages()
        {
            HeightCurve curve = Straight(CurveProfile.Ridge) with
            {
                Points = [new CurvePoint(20, 40, 10), new CurvePoint(60, 50, 10)],
                Symmetry = new SymmetrySetting(MapSymmetryKind.Rotational, 4),
            };
            IReadOnlyList<CurveOutline> outlines = CurveOutlines.Of(curve, Size, Size);
            Assert.AreEqual(16, outlines.Count, "four lines for each of four parts");
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, outlines.Select(o => o.Part).Distinct().ToArray());
        }
    }
}
