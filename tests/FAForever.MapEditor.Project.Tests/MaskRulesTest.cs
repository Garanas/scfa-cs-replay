using System.Collections.Immutable;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// Masks made from rules (user story K-2): each kind of rule, how rules combine, masks that read
    /// other masks, the generator's inputs, and the rules' files in the project.
    /// </summary>
    [TestClass]
    public class MaskRulesTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        /// <summary>
        /// A map of 256 ogrids: a ramp rising 0.5 a ogrid from the west edge to x = 128 (about 26.6
        /// degrees), then flat at 64; water at 30 covers the ramp up to x = 60.
        /// </summary>
        internal static MapDocument Ramp(bool water = true)
        {
            MapDocument document = NewMap.Create(new NewMapSettings { Size = 256, Water = water, WaterElevation = 30 });
            Heightmap heightmap = document.Terrain.Heightmap;
            int samples = heightmap.Width + 1;
            ushort[] heights = new ushort[samples * samples];
            for (int z = 0; z < samples; z++)
            {
                for (int x = 0; x < samples; x++)
                {
                    heights[z * samples + x] = (ushort)MathF.Round(Math.Min(x, 128) * 0.5f / heightmap.Scale);
                }
            }
            return document with { Terrain = document.Terrain with { Heightmap = heightmap with { Samples = TiledBuffer<ushort>.FromArray(samples, samples, heights) } } };
        }

        private static float At(MapDocument document, MaskRuleSet rules, int x, int z)
        {
            float[] values = MaskRules.Evaluate(document, rules, document.Width, document.Height);
            return values[z * document.Width + x];
        }

        private static MaskRuleSet Rules(params MaskRule[] rules) => new MaskRuleSet { Rules = [.. rules] };

        [TestMethod]
        public void SlopeHoldsWithinItsRangeAndFadesOverItsSoftEdge()
        {
            MapDocument document = Ramp();
            MaskRuleSet steep = Rules(new MaskRule { Kind = MaskRuleKind.Slope, Min = 20, Max = 90 });
            Assert.AreEqual(1, At(document, steep, 60, 100), "the ramp is 26.6 degrees");
            Assert.AreEqual(0, At(document, steep, 200, 100), "flat ground");

            MaskRuleSet soft = Rules(new MaskRule { Kind = MaskRuleKind.Slope, Min = 30, Max = 90, Soft = 10 });
            float edge = At(document, soft, 60, 100);
            Assert.IsTrue(edge > 0 && edge < 1, $"3.4 degrees short of the range, inside the soft edge: {edge}");

            MaskRuleSet inverted = Rules(new MaskRule { Kind = MaskRuleKind.Slope, Min = 20, Max = 90, Invert = true });
            Assert.AreEqual(1, At(document, inverted, 200, 100), "inverted: the flat ground");
        }

        [TestMethod]
        public void HeightAndDistanceToWater()
        {
            MapDocument document = Ramp();
            MaskRuleSet high = Rules(new MaskRule { Kind = MaskRuleKind.Height, Min = 50, Max = 1000 });
            Assert.AreEqual(1, At(document, high, 200, 10));
            Assert.AreEqual(0, At(document, high, 20, 10));

            float[] distance = MaskRules.WaterDistance(document, 256, 256, 1, 1);
            Assert.AreEqual(0, distance[10 * 256 + 30], "under the water");
            Assert.AreEqual(40, distance[10 * 256 + 100], 1.01, "the shore is at x = 60");

            MaskRuleSet shore = Rules(new MaskRule { Kind = MaskRuleKind.WaterDistance, Min = 1, Max = 10 });
            Assert.AreEqual(1, At(document, shore, 65, 10), "a strip beside the water");
            Assert.AreEqual(0, At(document, shore, 100, 10));
            Assert.AreEqual(0, At(document, shore, 30, 10), "not in it");

            float[] dry = MaskRules.WaterDistance(Ramp(water: false), 256, 256, 1, 1);
            Assert.AreEqual(MaskRules.Far, dry[0], "a map without water is far from it everywhere");
        }

        [TestMethod]
        public void LayersCombineAndNoiseFollowsItsSeed()
        {
            MapDocument document = MaskLayers.Add(Ramp(), "left");
            TiledBuffer<byte>.Builder left = document.Masks["left"].ToBuilder();
            for (int z = 0; z < 256; z++)
            {
                for (int x = 0; x < 128; x++)
                {
                    left[x, z] = 255;
                }
            }
            document = document with { Masks = document.Masks.SetItem("left", left.ToBuffer()) };

            MaskRule layer = new MaskRule { Kind = MaskRuleKind.Layer, Source = "left" };
            MaskRule high = new MaskRule { Kind = MaskRuleKind.Height, Min = 40, Max = 1000 };
            Assert.AreEqual(1, At(document, Rules(layer), 10, 10));
            Assert.AreEqual(0, At(document, Rules(layer), 200, 10));
            Assert.AreEqual(1, At(document, Rules(layer, high with { Combine = MaskCombine.Multiply }), 100, 10), "both");
            Assert.AreEqual(0, At(document, Rules(layer, high with { Combine = MaskCombine.Multiply }), 200, 10), "high, but not left");
            Assert.AreEqual(1, At(document, Rules(layer, high with { Combine = MaskCombine.Max }), 200, 10), "either");
            Assert.AreEqual(0, At(document, Rules(layer, high with { Combine = MaskCombine.Subtract }), 100, 10), "left without the high ground");
            Assert.AreEqual(1, At(document, Rules(layer, high with { Combine = MaskCombine.Subtract }), 10, 10));
            Assert.AreEqual(0, At(document, Rules(layer, new MaskRule { Kind = MaskRuleKind.Invert }), 10, 10));

            MaskRuleSet strata = Rules(new MaskRule { Kind = MaskRuleKind.Layer, Source = MaskRuleSources.Stratum(1) });
            Assert.AreEqual(0, At(document, strata, 10, 10), "a new map's first stratum is empty");
            Assert.ThrowsException<ArgumentException>(() => At(document, Rules(layer with { Source = "missing" }), 0, 0));

            MaskRuleSet noise = Rules(new MaskRule { Kind = MaskRuleKind.Noise, Scale = 16, Seed = 3 });
            float[] a = MaskRules.Evaluate(document, noise, 256, 256);
            float[] b = MaskRules.Evaluate(document, noise, 256, 256);
            float[] c = MaskRules.Evaluate(document, Rules(new MaskRule { Kind = MaskRuleKind.Noise, Scale = 16, Seed = 4 }), 256, 256);
            CollectionAssert.AreEqual(a, b, "the same seed, the same noise");
            CollectionAssert.AreNotEqual(a, c);
            Assert.IsTrue(a.Min() < 0.3f && a.Max() > 0.7f, $"spread over 0 to 1: {a.Min()} to {a.Max()}");
        }

        [TestMethod]
        public void BlurGrowShrinkAndThreshold()
        {
            MapDocument document = Ramp();
            // the flat ground east of x = 128
            MaskRule flat = new MaskRule { Kind = MaskRuleKind.Slope, Min = 0, Max = 5 };
            Assert.AreEqual(0, At(document, Rules(flat), 124, 50));
            Assert.AreEqual(1, At(document, Rules(flat, new MaskRule { Kind = MaskRuleKind.Grow, Radius = 6 }), 124, 50), "grown towards the ramp");
            Assert.AreEqual(0, At(document, Rules(flat, new MaskRule { Kind = MaskRuleKind.Shrink, Radius = 6 }), 132, 50), "shrunk away from it");
            float blurred = At(document, Rules(flat, new MaskRule { Kind = MaskRuleKind.Blur, Radius = 8 }), 128, 50);
            Assert.IsTrue(blurred > 0.2f && blurred < 0.8f, $"the edge blurred: {blurred}");
            Assert.AreEqual(1, At(document, Rules(flat, new MaskRule { Kind = MaskRuleKind.Blur, Radius = 8 }, new MaskRule { Kind = MaskRuleKind.Threshold, Min = 0.2f, Soft = 0 }), 128, 50), "made sharp again");
        }

        [TestMethod]
        public void GeneratesMasksAfterTheMasksTheyReadAtTheirOwnSize()
        {
            MapDocument document = MaskLayers.Add(MaskLayers.Add(Ramp(), "high"), "high edge", half: true);
            document = document.SetMaskRules("high", [new MaskRule { Kind = MaskRuleKind.Height, Min = 50, Max = 1000 }]);
            document = document.SetMaskRules("high edge", [new MaskRule { Kind = MaskRuleKind.Layer, Source = "high" }, new MaskRule { Kind = MaskRuleKind.Invert }]);
            CollectionAssert.AreEqual(new[] { "mask-high", "mask-high edge" }, MaskRules.Order(document).ToArray());

            IReadOnlyList<(string Target, string Layer, TiledBuffer<byte> Grid)> generated = MaskRules.GenerateAll(document);
            Dictionary<string, TiledBuffer<byte>> made = generated.ToDictionary(entry => entry.Target["mask-".Length..], entry => entry.Grid);
            Assert.AreEqual(256, made["high"].Width);
            Assert.AreEqual(128, made["high edge"].Width, "a mask keeps its size");
            Assert.AreEqual(255, made["high"][200, 10]);
            Assert.AreEqual(0, made["high edge"][100, 5], "read from what 'high' became, not the empty mask");
            Assert.AreEqual(255, made["high edge"][10, 5]);

            MaskRulesGenerator generator = new MaskRulesGenerator();
            MapDocument applied = generator.Apply(document, generated);
            CollectionAssert.AreEqual(made["high"].ToArray(), applied.Masks["high"].ToArray(), "the rules layer replaces: the mask is what the rules made");

            // read each other in a circle
            MapDocument circle = applied.SetMaskRules("high", [new MaskRule { Kind = MaskRuleKind.Layer, Source = "high edge" }]);
            Assert.ThrowsException<ArgumentException>(() => MaskRules.GenerateAll(circle));
        }

        [TestMethod]
        public void IsOutOfDateWhenTheTerrainOrRulesChangeButNotWhenItsOwnMaskIsPutInPlace()
        {
            MapDocument document = MaskLayers.Add(MaskLayers.Add(Ramp(), "high"), "painted");
            document = document.SetMaskRules("high", [new MaskRule { Kind = MaskRuleKind.Height, Min = 50, Max = 1000 }]);
            GeneratedResults results = new GeneratedResults();
            MaskRulesGenerator generator = new MaskRulesGenerator();
            results.Register(generator);
            GeneratedResult result = results.Generate(generator.Id, document);
            MapDocument applied = generator.Apply(document, result.Value);

            Assert.IsFalse(result.IsStale(applied), "its own mask in place");
            Assert.IsFalse(result.IsStale(applied with { Masks = applied.Masks.SetItem("painted", TiledBuffer<byte>.Create(256, 256, 9)) }), "a mask no rule reads");
            Assert.IsTrue(result.IsStale(applied.UpdateMaskRule("high", 0, rule => rule with { Min = 40 })), "a rule changed");
            MapDocument reading = applied.AddMaskRule("high", new MaskRule { Kind = MaskRuleKind.Layer, Source = "painted", Combine = MaskCombine.Max });
            GeneratedResult again = results.Generate(generator.Id, reading);
            CollectionAssert.Contains(again.ChangedInputs(reading with { Masks = reading.Masks.SetItem("painted", TiledBuffer<byte>.Create(256, 256, 9)) }).ToList(), "masks the rules read");
        }

        [TestMethod]
        public void EditsRulesAndFollowsRenamedMasks()
        {
            MapDocument document = MaskLayers.Add(MaskLayers.Add(Ramp(), "a"), "b");
            document = document.AddMaskRule("a", MaskRule.Of(MaskRuleKind.Slope));
            document = document.AddMaskRule("a", MaskRule.Of(MaskRuleKind.Blur));
            document = document.AddMaskRule("b", new MaskRule { Kind = MaskRuleKind.Layer, Source = "a" });
            document = document.MoveMaskRule("a", 1, 0);
            Assert.AreEqual(MaskRuleKind.Blur, document.MaskRules["a"].Rules[0].Kind);
            Assert.ThrowsException<ArgumentException>(() => document.RemoveMaskRule("a", 5));
            Assert.ThrowsException<ArgumentException>(() => document.AddMaskRule("missing", MaskRule.Of(MaskRuleKind.Noise)));

            MapDocument renamed = MaskLayers.Rename(document, "a", "slopes");
            Assert.IsTrue(renamed.MaskRules.ContainsKey("slopes"));
            Assert.AreEqual("slopes", renamed.MaskRules["b"].Rules[0].Source, "a rule reads the new name");

            MapDocument removed = MaskLayers.Remove(renamed, "slopes");
            Assert.IsFalse(removed.MaskRules.ContainsKey("slopes"));

            MapDocument none = document.RemoveMaskRule("b", 0);
            Assert.IsFalse(none.MaskRules.ContainsKey("b"), "without its last rule a mask is painted by hand");
        }

        [TestMethod]
        public void SavesLoadsAndReloadsTheRules()
        {
            MapDocument document = MaskLayers.Add(MaskLayers.Add(Ramp(), "forest"), "plain");
            document = document.SetMaskRules("forest", [
                new MaskRule { Kind = MaskRuleKind.Slope, Min = 0, Max = 20, Soft = 4 },
                new MaskRule { Kind = MaskRuleKind.Noise, Scale = 24, Seed = 7, Combine = MaskCombine.Multiply, Invert = true },
                new MaskRule { Kind = MaskRuleKind.Layer, Source = "stratum:2", Combine = MaskCombine.Max },
                new MaskRule { Kind = MaskRuleKind.Blur, Radius = 3 },
                new MaskRule { Kind = MaskRuleKind.Threshold, Min = 0.4f, Soft = 0.2f },
            ]);
            string project = ProjectFolders.New("mask-rules");
            MapProjectStore.Save(document, project);
            // the rules live in the mask's stack, as a rules layer
            string file = Path.Combine(project, "stacks", "mask-forest.json");
            Assert.IsTrue(File.Exists(file));
            Assert.IsFalse(File.Exists(Path.Combine(project, "stacks", "mask-plain.json")), "a mask without layers of its own has no stack file");
            StringAssert.Contains(File.ReadAllText(file), "\"source\": \"rules\"");
            StringAssert.DoesNotMatch(File.ReadAllText(file), new System.Text.RegularExpressions.Regex("\"radius\": 2\\b"), "a rule writes only what its kind reads");

            MapDocument loaded = MapProjectStore.Load(project);
            Assert.AreEqual(document.MaskRules["forest"].Rules.Length, loaded.MaskRules["forest"].Rules.Length);
            CollectionAssert.AreEqual(document.MaskRules["forest"].Rules.ToArray(), loaded.MaskRules["forest"].Rules.ToArray());
            Assert.AreSame(loaded, MapProjectStore.Reload(loaded, project, ["stacks/mask-forest.json"]), "the file holds what the document holds");

            MapDocument changed = loaded.RemoveMaskRule("forest", 4);
            MapProjectStore.Save(changed, project, previous: loaded);
            Assert.AreEqual(4, MapProjectStore.Reload(loaded, project, ["stacks/mask-forest.json"]).MaskRules["forest"].Rules.Length, "another tool changed the rules");

            MapDocument painted = MaskLayers.Remove(changed, "forest");
            MapProjectStore.Save(painted, project, previous: changed);
            Assert.IsFalse(File.Exists(file), "the stack of a removed mask goes with it");
        }

        [TestMethod]
        public void TheRulesOfAnOlderProjectBecomeARulesLayerHoldingTheMask()
        {
            MapDocument document = MaskLayers.Add(Ramp(), "forest");
            document = document with { Masks = document.Masks.SetItem("forest", TiledBuffer<byte>.Create(256, 256, 200)) };
            string project = ProjectFolders.New("mask-rules-legacy");
            MapProjectStore.Save(document, project);
            // an older project kept a mask's rules beside its PNG
            string legacy = Path.Combine(project, "masks", "forest.rules.json");
            File.WriteAllText(legacy, "{ \"rules\": [ { \"kind\": \"Height\", \"min\": 50, \"max\": 1000 } ] }");

            MapDocument loaded = MapProjectStore.Load(project);
            Layer rules = CoverageLayers.StackOf(loaded, LayerTarget.Mask("forest")).Layers.Single(layer => layer.Source is RulesSource);
            Assert.AreEqual(1, loaded.MaskRules["forest"].Rules.Length);
            Assert.AreEqual(200, rules.Coverage![10, 10], "the layer holds what the rules made last: the mask's PNG");
            Assert.AreEqual(200, loaded.Masks["forest"][10, 10], "the mask is unchanged");

            MapProjectStore.Save(loaded, project);
            Assert.IsFalse(File.Exists(legacy), "the first save writes the stack and removes the old file");
            Assert.IsTrue(File.Exists(Path.Combine(project, "stacks", "mask-forest.json")));
            Assert.AreEqual(1, MapProjectStore.Load(project).MaskRules["forest"].Rules.Length);
        }
    }
}
