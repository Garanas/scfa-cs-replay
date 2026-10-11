using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The places of many units placed at once (user story U-7): lines, rectangles, rings and scatters.
    /// </summary>
    [TestClass]
    public class UnitLayoutsTest
    {
        private static readonly (double, double) Wall = (1, 1);
        private static readonly (double, double) Factory = (3, 2);

        [TestMethod]
        public void ALineOfWallsButtsTogetherOnTheBuildGridFacingAlongIt()
        {
            ImmutableArray<UnitPlace> walls = UnitLayouts.Line(new Vector2(10.2f, 10.3f), new Vector2(23.1f, 10.4f), Wall, spacing: 4, faceAlong: true, heading: 0, isStructure: true);
            Assert.AreEqual(13, walls.Length, "one wall per ogrid of the line, whatever the spacing");
            Assert.AreEqual((10.5f, 10.5f), (walls[0].X, walls[0].Z), "a 1 by 1 structure on a half ogrid");
            Assert.AreEqual((22.5f, 10.5f), (walls[^1].X, walls[^1].Z));
            Assert.IsTrue(walls.All(wall => wall.Heading == 0), "along x: no turn");
            for (int i = 1; i < walls.Length; i++)
            {
                Assert.AreEqual(1, walls[i].X - walls[i - 1].X, 1e-5, "they touch");
            }

            ImmutableArray<UnitPlace> down = UnitLayouts.Line(new Vector2(10, 10), new Vector2(10, 20.2f), Wall, 1, true, 0, true);
            Assert.AreEqual(11, down.Length);
            Assert.AreEqual(MathF.PI / 2, down[0].Heading, 1e-5, "along +z the line faces a quarter turn");

            ImmutableArray<UnitPlace> diagonal = UnitLayouts.Line(new Vector2(10, 10), new Vector2(14, 14), Wall, 1, true, 0, true);
            Assert.AreEqual(5, diagonal.Length, "a diagonal wall: one per ogrid along both axes, touching at the corners");
            CollectionAssert.AreEqual(new[] { 10.5f, 11.5f, 12.5f, 13.5f, 14.5f }, diagonal.Select(wall => wall.X).ToArray());
            CollectionAssert.AreEqual(new[] { 10.5f, 11.5f, 12.5f, 13.5f, 14.5f }, diagonal.Select(wall => wall.Z).ToArray());

            ImmutableArray<UnitPlace> slanted = UnitLayouts.Line(new Vector2(100, 100), new Vector2(130, 102), Wall, 1, true, 0, true);
            Assert.AreEqual(31, slanted.Length, "a line nearly along x: one wall per ogrid of x, none skipped");
            for (int i = 1; i < slanted.Length; i++)
            {
                Assert.AreEqual(1, slanted[i].X - slanted[i - 1].X, 1e-5, "no gap");
            }
            ImmutableArray<UnitPlace> steep = UnitLayouts.Line(new Vector2(10, 10), new Vector2(15, 20), Wall, 1, true, 0, true);
            Assert.AreEqual(11, steep.Length, "a line mostly along z: one wall per ogrid of z");
            Assert.AreEqual(MathF.PI / 2, steep[0].Heading, 1e-5);
        }

        [TestMethod]
        public void ALineOfStructuresStepsByTheFootprintAlongTheLineAndMayFaceAFixedHeading()
        {
            ImmutableArray<UnitPlace> factories = UnitLayouts.Line(new Vector2(10, 10), new Vector2(22, 10), Factory, 1, faceAlong: true, heading: 0, isStructure: true);
            Assert.AreEqual(5, factories.Length, "3 wide along x: every 3 ogrids over 12");
            Assert.AreEqual((10.5f, 10f), (factories[0].X, factories[0].Z), "odd along x on a half ogrid, even along z on a whole");
            Assert.AreEqual(3, factories[1].X - factories[0].X, 1e-5);

            ImmutableArray<UnitPlace> turned = UnitLayouts.Line(new Vector2(10, 10), new Vector2(22, 10), Factory, 1, faceAlong: false, heading: MathF.PI / 2 + 0.1f, isStructure: true);
            Assert.AreEqual(7, turned.Length, "turned a quarter, 2 ogrids lie along x: every 2 over 12");
            Assert.AreEqual(MathF.PI / 2, turned[0].Heading, 1e-5, "the fixed heading, in quarter turns");
            Assert.AreEqual((10f, 10.5f), (turned[0].X, turned[0].Z), "its sides swapped on the grid");

            ImmutableArray<UnitPlace> one = UnitLayouts.Line(new Vector2(10, 10), new Vector2(10, 10), Wall, 1, true, 0, true);
            Assert.AreEqual(1, one.Length, "a click without a drag is one unit");
        }

        [TestMethod]
        public void ALineOfMobileUnitsStandsTheSpacingApartFacingAlongItOrAHeading()
        {
            ImmutableArray<UnitPlace> tanks = UnitLayouts.Line(new Vector2(10, 10), new Vector2(10, 19), null, spacing: 3, faceAlong: true, heading: 1, isStructure: false);
            Assert.AreEqual(4, tanks.Length, "at 10, 13, 16 and 19");
            Assert.AreEqual((10f, 13f), (tanks[1].X, tanks[1].Z), "not snapped");
            Assert.AreEqual(MathF.PI / 2, tanks[0].Heading, 1e-5, "facing +z, the way the line goes");

            ImmutableArray<UnitPlace> fixedHeading = UnitLayouts.Line(new Vector2(10, 10), new Vector2(10, 19), null, 3, faceAlong: false, heading: 1, isStructure: false);
            Assert.IsTrue(fixedHeading.All(tank => tank.Heading == 1));
            ImmutableArray<UnitPlace> close = UnitLayouts.Line(new Vector2(0, 0), new Vector2(1, 0), null, 0, true, 0, false);
            Assert.AreEqual(5, close.Length, "the spacing is at least a quarter ogrid");
        }

        [TestMethod]
        public void ShiftKeepsALineToEighthTurns()
        {
            Vector2 to = UnitLayouts.Constrain(new Vector2(10, 10), new Vector2(20, 11), UnitLayouts.EighthTurn);
            Assert.AreEqual(20.05f, to.X, 0.01, "along x, as long as before");
            Assert.AreEqual(10, to.Y, 1e-4);
            Vector2 diagonal = UnitLayouts.Constrain(new Vector2(0, 0), new Vector2(10, 9), UnitLayouts.EighthTurn);
            Assert.AreEqual(diagonal.X, diagonal.Y, 1e-4, "near 45 degrees it is 45 degrees");
            Assert.AreEqual(new Vector2(0, 0), UnitLayouts.Constrain(new Vector2(0, 0), new Vector2(0, 0), UnitLayouts.EighthTurn), "no line yet");
        }

        [TestMethod]
        public void ARectangleFillsRowsOfTheFootprintOrOnlyItsBorder()
        {
            ImmutableArray<UnitPlace> filled = UnitLayouts.Rectangle(new Vector2(10.2f, 20.1f), new Vector2(16.1f, 23.9f), Wall, hollow: false, spacing: 1, heading: 0, isStructure: true);
            Assert.AreEqual(6 * 4, filled.Length, "6 by 4 walls from the corners rounded to whole ogrids");
            Assert.AreEqual((10.5f, 20.5f), (filled[0].X, filled[0].Z));
            Assert.AreEqual((15.5f, 23.5f), (filled[^1].X, filled[^1].Z));
            Assert.AreEqual(6 * 4, filled.Distinct().Count(), "each on its own place");

            ImmutableArray<UnitPlace> hollow = UnitLayouts.Rectangle(new Vector2(16.1f, 23.9f), new Vector2(10.2f, 20.1f), Wall, hollow: true, spacing: 1, heading: 0, isStructure: true);
            Assert.AreEqual(2 * 6 + 2 * 2, hollow.Length, "the corners in any order; only the border for walls");
            Assert.IsTrue(hollow.All(wall => wall.X is 10.5f or 15.5f || wall.Z is 20.5f or 23.5f));

            ImmutableArray<UnitPlace> factories = UnitLayouts.Rectangle(new Vector2(0, 0), new Vector2(9, 4), Factory, false, 1, heading: 0, isStructure: true);
            Assert.AreEqual(3 * 2, factories.Length, "3 wide and 2 deep in a box of 9 by 4");
            Assert.AreEqual((1.5f, 1f), (factories[0].X, factories[0].Z), "on the build grid");
            Assert.AreEqual((7.5f, 3f), (factories[^1].X, factories[^1].Z));

            ImmutableArray<UnitPlace> tanks = UnitLayouts.Rectangle(new Vector2(10, 10), new Vector2(16, 14), null, false, spacing: 2, heading: 0.5f, isStructure: false);
            Assert.AreEqual(4 * 3, tanks.Length, "at 10, 12, 14, 16 by 10, 12, 14");
            Assert.IsTrue(tanks.All(tank => tank.Heading == 0.5f), "the heading given");
            Assert.AreEqual(1, UnitLayouts.Rectangle(new Vector2(10, 10), new Vector2(10, 10), Wall, false, 1, 0, true).Length, "a click is one unit");
        }

        [TestMethod]
        public void ARingGoesAroundTheBoundsAtADistance()
        {
            // a 3 by 3 structure centred at 10.5, 10.5: its footprint from 9 to 12
            ImmutableArray<UnitPlace> walls = UnitLayouts.Ring((9, 9, 12, 12), Wall, distance: 1, spacing: 1, heading: 0, isStructure: true);
            Assert.AreEqual(4 * 7 - 4, walls.Length, "7 walls a side, the corners once");
            Assert.AreEqual(7.5f, walls.Min(wall => wall.X), "the inner edge of the walls at 8: one ogrid from the footprint");
            Assert.AreEqual(13.5f, walls.Max(wall => wall.X));
            Assert.IsTrue(walls.All(wall => wall.X is 7.5f or 13.5f || wall.Z is 7.5f or 13.5f), "hollow");

            ImmutableArray<UnitPlace> tight = UnitLayouts.Ring((9, 9, 12, 12), Wall, distance: 0, spacing: 1, heading: 0, isStructure: true);
            Assert.AreEqual(4 * 5 - 4, tight.Length, "touching the footprint");

            ImmutableArray<UnitPlace> tanks = UnitLayouts.Ring((10, 10, 20, 20), null, distance: 2, spacing: 4, heading: 0, isStructure: false);
            Assert.AreEqual(4 * 5 - 4, tanks.Length, "from 8 to 22 about 4 apart: 5 a side (3.5 apart), spread so the corners meet");
            Assert.AreEqual(8, tanks.Min(tank => tank.X));
            Assert.AreEqual(22, tanks.Max(tank => tank.Z));
        }

        private static readonly (double, double) PowerGenerator = (1, 1);
        private static readonly (double, double) PowerGeneratorSkirt = (2, 2);
        private static readonly (double, double) LandFactory = (5, 5);
        private static readonly (double, double) LandFactorySkirt = (8, 8);

        private static bool Overlap(UnitPlace a, UnitPlace b, double skirt) =>
            Math.Abs(a.X - b.X) < skirt - 0.01 && Math.Abs(a.Z - b.Z) < skirt - 0.01;

        private static void AssertNoneOverlap(ImmutableArray<UnitPlace> places, double skirt)
        {
            for (int i = 0; i < places.Length; i++)
            {
                for (int j = i + 1; j < places.Length; j++)
                {
                    Assert.IsFalse(Overlap(places[i], places[j], skirt), $"{places[i]} and {places[j]} overlap");
                }
            }
        }

        [TestMethod]
        public void StructuresInALineButtSkirtToSkirt()
        {
            ImmutableArray<UnitPlace> generators = UnitLayouts.Line(new Vector2(10, 10), new Vector2(20.5f, 10), PowerGenerator, 1, true, 0, true, PowerGeneratorSkirt);
            Assert.AreEqual(6, generators.Length, "a skirt of 2 every 2 ogrids");
            Assert.AreEqual(2f, generators[1].X - generators[0].X);
            Assert.AreEqual(0.5f, generators[0].X % 1, "still on the build grid");
            AssertNoneOverlap(generators, 2);

            ImmutableArray<UnitPlace> factories = UnitLayouts.Line(new Vector2(10, 10), new Vector2(60, 10), LandFactory, 1, true, 0, true, LandFactorySkirt);
            Assert.AreEqual(7, factories.Length);
            Assert.AreEqual(8f, factories[1].X - factories[0].X);
            AssertNoneOverlap(factories, 8);

            ImmutableArray<UnitPlace> down = UnitLayouts.Line(new Vector2(10, 10), new Vector2(10, 40), LandFactory, 1, true, 0, true, LandFactorySkirt);
            AssertNoneOverlap(down, 8);
            ImmutableArray<UnitPlace> slanted = UnitLayouts.Line(new Vector2(10, 10), new Vector2(50, 30), PowerGenerator, 1, true, 0, true, PowerGeneratorSkirt);
            AssertNoneOverlap(slanted, 2);
            ImmutableArray<UnitPlace> diagonal = UnitLayouts.Line(new Vector2(10, 10), new Vector2(40, 40), LandFactory, 1, true, 0, true, LandFactorySkirt);
            AssertNoneOverlap(diagonal, 8);

            ImmutableArray<UnitPlace> walls = UnitLayouts.Line(new Vector2(10, 10), new Vector2(20, 10), Wall, 1, true, 0, true, (1, 1));
            Assert.AreEqual(11, walls.Length, "a wall's skirt is its footprint: one wall");
        }

        [TestMethod]
        public void StructuresInARectangleButtSkirtToSkirt()
        {
            ImmutableArray<UnitPlace> generators = UnitLayouts.Rectangle(new Vector2(10, 10), new Vector2(20, 16), PowerGenerator, false, 1, 0, true, PowerGeneratorSkirt);
            Assert.AreEqual(5 * 3, generators.Length, "5 by 3 generators of 2");
            Assert.IsTrue(generators.All(place => place.X % 1 == 0.5f && place.Z % 1 == 0.5f), "on the build grid");
            AssertNoneOverlap(generators, 2);

            ImmutableArray<UnitPlace> factories = UnitLayouts.Rectangle(new Vector2(0, 0), new Vector2(40, 24), LandFactory, false, 1, 0, true, LandFactorySkirt);
            Assert.AreEqual(5 * 3, factories.Length);
            AssertNoneOverlap(factories, 8);
            AssertNoneOverlap(UnitLayouts.Rectangle(new Vector2(0, 0), new Vector2(40, 24), LandFactory, false, 1, MathF.PI / 2, true, LandFactorySkirt), 8);
        }

        [TestMethod]
        public void ARingCountsItsDistanceFromTheOuterEdgeOfTheSkirtsItGoesAround()
        {
            // the skirt of a factory at 100.5: 96.5 to 104.5; walls 1 ogrid from it have their inner edge at 95 and 106
            ImmutableArray<UnitPlace> walls = UnitLayouts.Ring((96.5f, 96.5f, 104.5f, 104.5f), Wall, distance: 1, spacing: 1, heading: 0, isStructure: true, skirt: (1, 1));

            Assert.AreEqual(94.5f, walls.Min(wall => wall.X));
            Assert.AreEqual(106.5f, walls.Max(wall => wall.X));
            Assert.IsTrue(walls.All(wall => wall.X <= 95.5f || wall.X >= 105.5f || wall.Z <= 95.5f || wall.Z >= 105.5f), "none inside the skirt or its gap");

            ImmutableArray<UnitPlace> generators = UnitLayouts.Ring((96.5f, 96.5f, 104.5f, 104.5f), PowerGenerator, distance: 0, spacing: 1, heading: 0, isStructure: true, skirt: PowerGeneratorSkirt);
            Assert.IsTrue(generators.All(place => place.X - 1 >= 104.5f - 0.01f && place.X > 100 || place.X + 1 <= 96.5f + 0.01f && place.X < 100 || place.Z - 1 >= 104.5f - 0.01f && place.Z > 100 || place.Z + 1 <= 96.5f + 0.01f && place.Z < 100), "their skirts touch the factory's and never reach into it");
            AssertNoneOverlap(generators, 2);
        }

        [TestMethod]
        public void AScatterKeepsTheLeastDistanceAndRepeatsWithItsSeed()
        {
            ImmutableArray<UnitPlace> first = UnitLayouts.Scatter(new Vector2(50, 50), radius: 10, count: 12, leastDistance: 3, seed: 7);
            Assert.AreEqual(12, first.Length);
            foreach (UnitPlace place in first)
            {
                Assert.IsTrue(Vector2.Distance(new Vector2(place.X, place.Z), new Vector2(50, 50)) <= 10, "within the circle");
                Assert.IsTrue(place.Heading >= -MathF.PI && place.Heading <= MathF.PI);
            }
            for (int i = 0; i < first.Length; i++)
            {
                for (int j = i + 1; j < first.Length; j++)
                {
                    Assert.IsTrue(Vector2.Distance(new Vector2(first[i].X, first[i].Z), new Vector2(first[j].X, first[j].Z)) >= 3, "none closer than the least distance");
                }
            }
            Assert.IsTrue(first.Select(place => place.Heading).Distinct().Count() > 1, "random headings");

            ImmutableArray<UnitPlace> again = UnitLayouts.Scatter(new Vector2(50, 50), 10, 12, 3, 7);
            CollectionAssert.AreEqual(first.ToArray(), again.ToArray(), "the same seed gives the same places");
            ImmutableArray<UnitPlace> other = UnitLayouts.Scatter(new Vector2(50, 50), 10, 12, 3, 8);
            CollectionAssert.AreNotEqual(first.ToArray(), other.ToArray(), "another seed gives others");

            ImmutableArray<UnitPlace> full = UnitLayouts.Scatter(new Vector2(0, 0), radius: 2, count: 50, leastDistance: 3, seed: 1);
            Assert.AreEqual(1, full.Length, "a circle that holds one unit at that distance gives one");

            ImmutableArray<UnitPlace> away = UnitLayouts.Scatter(new Vector2(0, 0), 10, 5, 4, 3, existing: [new Vector2(0, 0)]);
            Assert.IsTrue(away.All(place => new Vector2(place.X, place.Z).Length() >= 4), "away from what was placed before");
        }
    }
}
