using System.Diagnostics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The stacks of coverage on the largest maps (81 km, 4096 ogrids): adding a layer that changes
    /// nothing composites nothing, and a stroke of the strata brush over a stack of several layers
    /// (one reading another stratum) stays quick per move of the mouse.
    /// </summary>
    [TestClass]
    public class CoverageStacksSpeedTest
    {
        private static readonly LayerTarget Stratum2 = LayerTarget.Stratum(2);

        /// <summary>
        /// How much longer a step may take on CI (GitHub sets <c>CI</c>): its runners have four
        /// processors shared with the other test projects, which <c>dotnet test</c> runs alongside,
        /// and a composite over every tile runs on all of them (a mask reference took 200 ms there,
        /// 12 ms here).
        /// </summary>
        private static readonly double Slack = Environment.GetEnvironmentVariable("CI") is "true" ? 5 : 1;

        private static TimeSpan Limit(double milliseconds) => TimeSpan.FromMilliseconds(milliseconds * Slack);

        private static EditSession Large()
        {
            MapDocument document = NewMap.Create(new NewMapSettings { Size = 4096, Height = 35 });
            return new EditSession(document);
        }

        /// <summary>
        /// The time of one call, the best of three runs (the first warms up).
        /// </summary>
        private static TimeSpan Time(Action action)
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

        [TestMethod]
        public void AddingALayerThatChangesNothingCompositesNothing()
        {
            EditSession session = Large();
            MapDocument start = session.Document;
            TiledBuffer<byte> mask = start.Terrain.StratumMasks.Layers[1];

            // the first layer of a grid without a stack, then another on the stack kept
            TimeSpan first = Time(() =>
            {
                MapDocument added = CoverageLayers.Follow(start, CoverageLayerEdits.Add(start, Stratum2, CoverageLayerEdits.NewPainted(start, Stratum2, "Painted 2")));
                Assert.AreSame(mask, added.Terrain.StratumMasks.Layers[1], "a layer painted nowhere leaves the grid as it was");
            });
            session.Edit("Add", d => CoverageLayerEdits.Add(d, Stratum2, CoverageLayerEdits.NewPainted(d, Stratum2, "Painted 2")));
            MapDocument stacked = session.Document;
            TimeSpan second = Time(() =>
            {
                MapDocument added = CoverageLayers.Follow(stacked, CoverageLayerEdits.Add(stacked, Stratum2, CoverageLayerEdits.NewPainted(stacked, Stratum2, "Painted 3")));
                Assert.AreSame(mask, added.Terrain.StratumMasks.Layers[1]);
            });
            Console.WriteLine($"4096 map, stratum mask {mask.Width} by {mask.Height}: first layer {first.TotalMilliseconds:0.0} ms, another {second.TotalMilliseconds:0.0} ms");
            Assert.IsTrue(first < Limit(60), $"the first layer took {first.TotalMilliseconds:0} ms");
            Assert.IsTrue(second < Limit(60), $"another layer took {second.TotalMilliseconds:0} ms");
        }

        [TestMethod]
        public void ALayerRemovedChangesOnlyWhereItWorked()
        {
            EditSession session = new EditSession(NewMap.Create(new NewMapSettings { Size = 512, Height = 35 }));
            session.Edit("Add", d => CoverageLayerEdits.Add(d, Stratum2, CoverageLayerEdits.NewPainted(d, Stratum2, "Painted 2")));
            session.PaintLayers[Stratum2.Key] = "Painted 2";
            using (EditTransaction stroke = session.Begin("Paint"))
            {
                new MaskBrush { Radius = 8, Strength = 1 }.Apply(stroke.StratumMask(2), 512, 512, 100, 100);
                stroke.Commit();
            }
            TiledBuffer<byte> painted = session.Document.Terrain.StratumMasks.Layers[1];
            Assert.IsTrue(painted[50, 50] > 0, "painted at (100, 100) of the map");

            session.Edit("Remove", d => CoverageLayerEdits.Remove(d, Stratum2, "Painted 2"));
            TiledBuffer<byte> removed = session.Document.Terrain.StratumMasks.Layers[1];
            Assert.AreEqual(0, removed[50, 50], "the layer's work is gone with it");
            Assert.IsTrue(removed.ChangedTiles(painted).Count() <= 4, "only the tiles it painted are made again");
        }

        [TestMethod]
        public void AMaskOfTheGridsSizeIsReadPixelForPixel()
        {
            MapDocument document = NewMap.Create(new NewMapSettings { Size = 512, Height = 35 });
            TiledBuffer<byte>.Builder third = document.Terrain.StratumMasks.Layers[2].ToBuilder();
            for (int z = 0; z < third.Height; z++)
            {
                for (int x = 0; x < third.Width; x++)
                {
                    third[x, z] = (byte)((x * 7 + z * 13) % 256);
                }
            }
            document = document with
            {
                Terrain = document.Terrain with { StratumMasks = document.Terrain.StratumMasks with { Layers = document.Terrain.StratumMasks.Layers.SetItem(2, third.ToBuffer()) } },
            };
            MapDocument referenced = CoverageLayers.Follow(document, CoverageLayerEdits.Add(document, Stratum2, CoverageLayerEdits.NewMaskRef(document, "Stratum 3", "stratum:3")));
            CollectionAssert.AreEqual(third.ToBuffer().ToArray(), referenced.Terrain.StratumMasks.Layers[1].ToArray(), "the maximum of nothing and stratum 3 is stratum 3");
        }

        [TestMethod]
        public void AMaskReferenceOfTheSameSizeCompositesQuickly()
        {
            EditSession session = Large();
            MapDocument start = session.Document;
            TimeSpan added = Time(() => CoverageLayers.Follow(start, CoverageLayerEdits.Add(start, Stratum2, CoverageLayerEdits.NewMaskRef(start, "Stratum 3", "stratum:3"))));
            Console.WriteLine($"4096 map: a mask reference of the same size {added.TotalMilliseconds:0.0} ms");
            Assert.IsTrue(added < Limit(150), $"{added.TotalMilliseconds:0} ms");
        }

        [TestMethod]
        public void AStrataStrokeOverAStackStaysQuick()
        {
            EditSession session = Large();
            session.Edit("Add", d => CoverageLayerEdits.Add(d, Stratum2, CoverageLayerEdits.NewMaskRef(d, "Stratum 3", "stratum:3")));
            session.Edit("Add", d => CoverageLayerEdits.Add(d, Stratum2, CoverageLayerEdits.NewPainted(d, Stratum2, "Painted 2")));
            session.PaintLayers[Stratum2.Key] = "Painted 2";
            MaskBrush brush = new MaskBrush { Radius = 12, Strength = 0.3f };
            MapDocument document = session.Document;

            using EditTransaction stroke = session.Begin("Paint layer 2");
            List<double> moves = [];
            float x = 1000;
            for (int move = 0; move < 60; move++)
            {
                Stopwatch watch = Stopwatch.StartNew();
                // a move of the mouse paints a few dabs a quarter of the radius apart, then the views read the document
                for (int dab = 0; dab < 4; dab++)
                {
                    brush.Apply(stroke.StratumMask(2), document.Width, document.Height, x, 1000 + move * 0.5f);
                    x += 3;
                }
                _ = stroke.Document;
                moves.Add(watch.Elapsed.TotalMilliseconds);
            }
            stroke.Commit();
            double mean = moves.Skip(10).Average();
            Console.WriteLine($"4096 map, three layers: a move of four dabs {mean:0.00} ms on average, at most {moves.Skip(10).Max():0.00} ms");
            Assert.IsTrue(session.Document.CoverageStacks[Stratum2.Key].Find("Painted 2")!.Alpha![500, 500] > 0, "the stroke went into the chosen layer");
            Assert.IsTrue(mean < 8 * Slack, $"{mean:0.0} ms a move");
        }
    }
}
