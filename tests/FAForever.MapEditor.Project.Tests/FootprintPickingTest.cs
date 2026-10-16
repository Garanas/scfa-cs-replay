using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// Picking a structure by its footprint (user story U-10): a click inside its rectangle, turned by
    /// its heading, and a box that touches it; mobile units keep their point.
    /// </summary>
    [TestClass]
    public class FootprintPickingTest
    {
        // a factory 5 by 3 ogrids at (100, 100) turned by the heading given, a tank 20 ogrids away
        private static MapDocument Map(float factoryHeading = 0)
        {
            MapDocument document = NewMap.Create(new NewMapSettings { Size = 256 });
            return document
                .AddUnit("NEUTRAL_CIVILIAN", ["INITIAL"], new Unit { Name = "FACTORY", Blueprint = "fac", Position = new Vector3(100, 30, 100), Orientation = new Vector3(0, factoryHeading, 0) })
                .AddUnit("NEUTRAL_CIVILIAN", ["INITIAL"], new Unit { Name = "TANK", Blueprint = "tank", Position = new Vector3(120, 30, 100) });
        }

        private static (double, double)? Footprints(string blueprint) => blueprint == "fac" ? (5, 3) : null;

        private static string? Pick(MapDocument document, float x, float z, float ogridsPerPixel = 0.1f, bool footprints = true) =>
            document.Nearest(x, z, ogridsPerPixel, ObjectKinds.Units, footprints: footprints ? Footprints : null)?.Name;

        [TestMethod]
        public void AClickInsideAFactoryAwayFromItsCentrePicksIt()
        {
            MapDocument document = Map();
            // at a tenth of an ogrid a pixel the old reach is 0.7 ogrids; these lie 2.8 and 2.0 from the centre
            Assert.AreEqual("FACTORY", Pick(document, 102.4f, 101.4f));
            Assert.AreEqual("FACTORY", Pick(document, 97.6f, 98.6f));
            Assert.IsNull(Pick(document, 102.6f, 101.6f), "just outside the corner");
        }

        [TestMethod]
        public void ThePointStillPicksWhenZoomedFarOut()
        {
            // the footprint is a pixel across, the 7 pixels reach 7 ogrids
            Assert.AreEqual("FACTORY", Pick(Map(), 106, 100, ogridsPerPixel: 1));
        }

        [TestMethod]
        public void TheFootprintTurnsWithTheHeading()
        {
            MapDocument turned = Map(MathF.PI / 2);
            Assert.AreEqual("FACTORY", Pick(turned, 100, 102.4f), "along the long side, which now runs along z");
            Assert.IsNull(Pick(turned, 102.4f, 100), "across the short side");
            MapDocument straight = Map();
            Assert.AreEqual("FACTORY", Pick(straight, 102.4f, 100));
            Assert.IsNull(Pick(straight, 100, 102.4f));
        }

        [TestMethod]
        public void WithoutFootprintsOnlyTheCentreCounts()
        {
            MapDocument document = Map();
            Assert.IsNull(Pick(document, 102.4f, 101.4f, footprints: false), "inside the footprint, 2.8 ogrids from the centre");
            Assert.AreEqual("FACTORY", Pick(document, 100.3f, 100, footprints: false));
        }

        [TestMethod]
        public void AMobileUnitIsAPointWhateverTheCallerKnows()
        {
            MapDocument document = Map();
            Assert.AreEqual("TANK", Pick(document, 120.2f, 100));
            Assert.IsNull(Pick(document, 123, 100));
        }

        [TestMethod]
        public void OverlappingFootprintsGiveTheNearerCentre()
        {
            MapDocument document = Map().AddUnit("NEUTRAL_CIVILIAN", ["INITIAL"], new Unit { Name = "NEXT", Blueprint = "fac", Position = new Vector3(103, 30, 100) });
            Assert.AreEqual("NEXT", Pick(document, 102, 100));
            Assert.AreEqual("FACTORY", Pick(document, 101, 100));
        }

        [TestMethod]
        public void ABoxTouchingAFootprintTakesTheStructure()
        {
            MapDocument document = Map();
            string[] Box(float x0, float z0, float x1, float z1, bool footprints = true) =>
                [.. document.Inside(x0, z0, x1, z1, ObjectKinds.Units, footprints: footprints ? Footprints : null).Select(thing => thing.Name!)];
            CollectionAssert.AreEqual(new[] { "FACTORY" }, Box(101, 90, 110, 99), "the box lies beside the centre and touches the corner");
            CollectionAssert.AreEqual(Array.Empty<string>(), Box(103, 90, 110, 99), "past the footprint");
            CollectionAssert.AreEqual(Array.Empty<string>(), Box(101, 90, 110, 99, footprints: false), "by the centre");
            CollectionAssert.AreEqual(new[] { "FACTORY", "TANK" }, Box(90, 90, 130, 110));
        }

        [TestMethod]
        public void ATurnedFootprintIsNotItsSpan()
        {
            // a 6 by 1 footprint turned 45 degrees: its span is a square, its body a diagonal; a box in the span's corner misses it
            MapDocument document = NewMap.Create(new NewMapSettings { Size = 256 })
                .AddUnit("NEUTRAL_CIVILIAN", ["INITIAL"], new Unit { Name = "WALL", Blueprint = "wall", Position = new Vector3(100, 30, 100), Orientation = new Vector3(0, MathF.PI / 4, 0) });
            (double, double)? Wall(string blueprint) => (6, 1);
            string[] Box(float x0, float z0, float x1, float z1) => [.. document.Inside(x0, z0, x1, z1, ObjectKinds.Units, footprints: Wall).Select(thing => thing.Name!)];
            // the wall runs from about (97.5, 97.5) to (102.5, 102.5); the span's corner at (102.5, 97.5) is far from it
            CollectionAssert.AreEqual(Array.Empty<string>(), Box(101.8f, 97, 103, 98.2f));
            CollectionAssert.AreEqual(new[] { "WALL" }, Box(101.8f, 101.8f, 103, 103));
        }
    }
}
