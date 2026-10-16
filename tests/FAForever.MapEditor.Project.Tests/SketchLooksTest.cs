using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// How the sketch's marks read at small sizes and on any terrain (user stories N-1, N-3, N-4): a mass is never a
    /// ring (it cannot be taken for a spawn when small), a small symbol is drawn with a thin line, and black and white
    /// have the other tone as their halo and rim.
    /// </summary>
    [TestClass]
    public class SketchLooksTest
    {
        private static ImmutableArray<SketchStroke> Lines(SketchSymbolKind kind, float size) =>
            SketchLines.Of(new SketchSymbol(kind, new Vector2(50, 50), SketchColour.Green, size, 0));

        [TestMethod]
        public void AMassIsADiamondAndADotNeverARing()
        {
            ImmutableArray<SketchStroke> mass = Lines(SketchSymbolKind.Mass, 14);
            Assert.AreEqual(2, mass.Length, "a diamond and a dot");
            Assert.AreEqual(5, mass[0].Points.Length, "four corners, closed");
            Assert.AreEqual(1, mass[1].Points.Length, "the dot");
            // a spawn is a ring and a dot: the two differ in their outline
            ImmutableArray<SketchStroke> spawn = Lines(SketchSymbolKind.Spawn, 14);
            Assert.IsTrue(spawn[0].Points.Length > 8, "a ring has many places");
        }

        [TestMethod]
        public void ASmallSymbolIsDrawnThinAndALargeOneMedium()
        {
            foreach (SketchSymbolKind kind in Enum.GetValues<SketchSymbolKind>())
            {
                Assert.IsTrue(Lines(kind, SketchLook.SymbolOgrids(SketchSize.Small)).All(line => line.Width == SketchWidth.Thin), $"{kind} small");
                Assert.IsTrue(Lines(kind, SketchLook.SymbolOgrids(SketchSize.Medium)).All(line => line.Width == SketchWidth.Thin), $"{kind} medium");
                Assert.IsTrue(Lines(kind, SketchLook.SymbolOgrids(SketchSize.Large)).All(line => line.Width == SketchWidth.Medium), $"{kind} large");
            }
        }

        [TestMethod]
        public void BlackAndWhiteHaveTheOtherToneAsTheirHaloAndRim()
        {
            Assert.AreEqual(SketchLook.Argb(SketchColour.White), SketchLook.HaloArgb(SketchColour.Black));
            Assert.AreEqual(SketchLook.Argb(SketchColour.Black), SketchLook.HaloArgb(SketchColour.White));
            foreach (SketchColour colour in new[] { SketchColour.Red, SketchColour.Yellow, SketchColour.Green, SketchColour.Blue })
            {
                Assert.IsNull(SketchLook.HaloArgb(colour), $"{colour} shows on its own");
            }
            // the rim of every colour is told apart from the colour itself, in both themes' panels
            Assert.AreEqual(SketchLook.Argb(SketchColour.White), SketchLook.RimArgb(SketchColour.Black));
            Assert.AreEqual(SketchLook.Argb(SketchColour.Black), SketchLook.RimArgb(SketchColour.White));
            Assert.AreNotEqual(SketchLook.Argb(SketchColour.Red), SketchLook.RimArgb(SketchColour.Red));
        }
    }
}
