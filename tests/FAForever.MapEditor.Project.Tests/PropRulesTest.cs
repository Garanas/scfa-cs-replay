using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// Props from rules (user story P-4): density, spacing, layers that allow and forbid, slope,
    /// water, clustering, generating again without touching the props placed by hand, the rules'
    /// file, and an export that is the same as without rules.
    /// </summary>
    [TestClass]
    public class PropRulesTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private const string Pine = "/env/evergreen/props/trees/pine06_prop.bp";
        private const string Rock = "/env/evergreen/props/rocks/rock01_prop.bp";

        private static PropRule Forest(string name = "Forest") => new PropRule
        {
            Name = name,
            Palette = [new PaletteEntry(Pine, 1)],
            Density = 4,
            Spacing = 2,
            AboveWater = false,
        };

        private static MapDocument Flat() => NewMap.Create(new NewMapSettings { Size = 256, Water = false });

        private static ImmutableArray<Prop> Generate(MapDocument document) => PropRules.Generate(document);

        private static float NearestDistance(IReadOnlyList<Prop> props)
        {
            PropGrid grid = new PropGrid(props);
            float least = float.MaxValue;
            foreach (Prop prop in props)
            {
                foreach ((int id, float x, float z, string _) in grid.Near(prop.Position.X, prop.Position.Z, 10))
                {
                    if (id != prop.Id)
                    {
                        least = Math.Min(least, MathF.Sqrt((x - prop.Position.X) * (x - prop.Position.X) + (z - prop.Position.Z) * (z - prop.Position.Z)));
                    }
                }
            }
            return least;
        }

        private static ImmutableArray<Prop> Numbered(ImmutableArray<Prop> props) => [.. props.Select((prop, index) => prop with { Id = index + 1 })];

        [TestMethod]
        public void PlacesAboutTheDensityKeepingTheSpacing()
        {
            MapDocument document = Flat().AddPropRule(Forest() with { Spacing = 0 });
            ImmutableArray<Prop> props = Generate(document);
            // 4 per 100 square ogrids over 256 by 256
            Assert.AreEqual(4 * 256 * 256 / 100, props.Length, 1, "without spacing, every place is kept");
            Assert.IsTrue(props.All(prop => prop.Rule == "Forest" && prop.Blueprint == Pine));
            Assert.IsTrue(props.All(prop => prop.Position.X >= 0 && prop.Position.X <= 256 && prop.Position.Z >= 0 && prop.Position.Z <= 256));

            ImmutableArray<Prop> spaced = Numbered(Generate(Flat().AddPropRule(Forest() with { Spacing = 3 })));
            Assert.IsTrue(NearestDistance(spaced) >= 3, "no two closer than the spacing");
            Assert.IsTrue(spaced.Length < props.Length);

            CollectionAssert.AreEqual(props.ToArray(), Generate(document).ToArray(), "the same rule on the same map, the same props");
            Assert.AreNotEqual(props[0].Position, Generate(Flat().AddPropRule(Forest() with { Spacing = 0, Seed = 2 }))[0].Position, "another seed");
        }

        [TestMethod]
        public void GrowsWhereALayerAllowsAndNotWhereAnotherForbids()
        {
            MapDocument document = MaskLayers.Add(MaskLayers.Add(Flat(), "west"), "road");
            TiledBuffer<byte>.Builder west = document.Masks["west"].ToBuilder();
            TiledBuffer<byte>.Builder road = document.Masks["road"].ToBuilder();
            for (int z = 0; z < 256; z++)
            {
                for (int x = 0; x < 128; x++)
                {
                    west[x, z] = 255;
                }
                for (int x = 60; x < 70; x++)
                {
                    road[x, z] = 255;
                }
            }
            document = document with { Masks = document.Masks.SetItem("west", west.ToBuffer()).SetItem("road", road.ToBuffer()) };
            document = document.AddPropRule(Forest() with { Conditions = [new PropRuleCondition("west"), new PropRuleCondition("road", 0.5f, Forbid: true)] });

            ImmutableArray<Prop> props = Generate(document);
            Assert.IsTrue(props.Length > 500);
            Assert.IsTrue(props.All(prop => prop.Position.X < 128.01f), "only in the west");
            Assert.IsFalse(props.Any(prop => prop.Position.X >= 60 && prop.Position.X < 70), "not on the road");

            Assert.ThrowsException<ArgumentException>(() => Generate(Flat().AddPropRule(Forest() with { Conditions = [new PropRuleCondition("missing")] })));
        }

        [TestMethod]
        public void FadesWithTheLayerAndKeepsToSlopeAndWater()
        {
            MapDocument half = MaskLayers.Add(Flat(), "thin");
            half = half with { Masks = half.Masks.SetItem("thin", TiledBuffer<byte>.Create(256, 256, 128)) };
            int full = Generate(Flat().AddPropRule(Forest() with { Spacing = 0 })).Length;
            int faded = Generate(half.AddPropRule(Forest() with { Spacing = 0, Conditions = [new PropRuleCondition("thin", 0.1f)] })).Length;
            Assert.AreEqual(full / 2.0, faded, full * 0.06, "a layer at half grows half as many");
            int unfaded = Generate(half.AddPropRule(Forest() with { Spacing = 0, Fade = false, Conditions = [new PropRuleCondition("thin", 0.1f)] })).Length;
            Assert.AreEqual(full, unfaded);

            MapDocument ramp = MaskRulesTest.Ramp();
            ImmutableArray<Prop> flatOnly = Generate(ramp.AddPropRule(Forest() with { MaxSlope = 10 }));
            Assert.IsTrue(flatOnly.All(prop => prop.Position.X >= 127), "the ramp is too steep");
            ImmutableArray<Prop> dry = Generate(ramp.AddPropRule(Forest() with { AboveWater = true }));
            Assert.IsTrue(dry.All(prop => prop.Position.X >= 59.9f), "none under the water");
        }

        [TestMethod]
        public void ClustersGatherThePropsInPatches()
        {
            MapDocument document = Flat();
            foreach (PropClustering clustering in new[] { PropClustering.Noise, PropClustering.Clusters })
            {
                ImmutableArray<Prop> props = Generate(document.AddPropRule(Forest() with { Spacing = 0, Clustering = clustering, ClusterSize = 16, ClusterCover = 0.3f }));
                int all = 4 * 256 * 256 / 100;
                Assert.IsTrue(props.Length > all * 0.1 && props.Length < all * 0.6, $"{clustering}: {props.Length} of {all}");
                // counted in squares of 16 ogrids: many empty, some full
                int[] counts = new int[16 * 16];
                foreach (Prop prop in props)
                {
                    counts[Math.Min(15, (int)(prop.Position.Z / 16)) * 16 + Math.Min(15, (int)(prop.Position.X / 16))]++;
                }
                Assert.IsTrue(counts.Count(count => count == 0) > 20, $"{clustering}: open ground between the patches");
                Assert.IsTrue(counts.Max() > 6, $"{clustering}: dense patches");
            }
        }

        [TestMethod]
        public void RulesKeepTheirSpacingToEachOtherAndStandByCategory()
        {
            MapDocument ramp = MaskRulesTest.Ramp(water: false);
            MapDocument document = ramp
                .AddPropRule(Forest() with { Spacing = 3 })
                .AddPropRule(new PropRule { Name = "Rocks", Palette = [new PaletteEntry(Rock, 1)], Density = 4, Spacing = 3 });
            ImmutableArray<Prop> props = Numbered(Generate(document));
            Assert.IsTrue(props.Any(prop => prop.Rule == "Rocks"));
            Assert.IsTrue(NearestDistance(props) >= 3, "a later rule fills around the earlier one's props");

            Prop tree = props.First(prop => prop.Rule == "Forest" && prop.Position.X < 100);
            Prop rock = props.First(prop => prop.Rule == "Rocks" && prop.Position.X < 100);
            Assert.AreEqual(1, tree.RotationY.Y, 1e-4, "a tree stands upright on the ramp");
            Assert.IsTrue(rock.RotationY.Y < 0.95f, "a rock tilts with it");
        }

        [TestMethod]
        public void GeneratingAgainReplacesOnlyThePropsOfTheRules()
        {
            MapDocument document = Flat();
            Prop byHand = new Prop { Id = 1, Blueprint = Rock, Position = new Vector3(10, 35, 10), RotationX = Vector3.UnitX, RotationY = Vector3.UnitY, RotationZ = Vector3.UnitZ, Scale = Vector3.One };
            document = document.AddProp(byHand).AddPropRule(Forest());
            PropRulesGenerator generator = new PropRulesGenerator();
            MapDocument first = generator.Apply(document, generator.Generate(document));
            int generated = first.CountRuleProps("Forest");
            Assert.IsTrue(generated > 1000);
            Assert.AreEqual(generated + 1, first.Props.Length);
            Assert.AreEqual(first.Props.Length, first.Props.Select(prop => prop.Id).Distinct().Count(), "every prop has its own id");

            MapDocument denser = first.UpdatePropRule("Forest", rule => rule with { Density = 1 });
            MapDocument second = generator.Apply(denser, generator.Generate(denser));
            Assert.IsTrue(second.CountRuleProps() < generated, "the rule's props replaced");
            Assert.AreEqual(1, second.Props.Count(prop => prop.Rule is null));
            Assert.AreEqual(byHand with { Id = second.Props.First(prop => prop.Rule is null).Id }, second.Props.First(prop => prop.Rule is null), "the prop placed by hand stays as it was");

            MapDocument renamed = second.UpdatePropRule("Forest", rule => rule with { Name = "Pines" });
            Assert.AreEqual(second.CountRuleProps(), renamed.CountRuleProps("Pines"), "its props follow the new name");
            Assert.ThrowsException<ArgumentException>(() => renamed.AddPropRule(Forest("pines")), "names differ by more than case");

            MapDocument kept = renamed.RemovePropRule("Pines", keepProps: true);
            Assert.AreEqual(renamed.Props.Length, kept.Props.Length);
            Assert.AreEqual(0, kept.CountRuleProps());
            MapDocument gone = renamed.RemovePropRule("Pines");
            Assert.AreEqual(1, gone.Props.Length);
        }

        [TestMethod]
        public void IsOutOfDateWhenTheRulesOrTheLayersTheyReadChange()
        {
            MapDocument document = MaskLayers.Add(MaskLayers.Add(Flat(), "forest"), "other");
            document = document.AddPropRule(Forest() with { Conditions = [new PropRuleCondition("forest", 0)] });
            PropRulesGenerator generator = new PropRulesGenerator();
            GeneratedResult result = GeneratedResult.Of(generator, document, generator.Generate(document));
            MapDocument applied = generator.Apply(document, result.Value);
            Assert.IsFalse(result.IsStale(applied), "its own props in place");
            Assert.IsFalse(result.IsStale(applied with { Masks = applied.Masks.SetItem("other", TiledBuffer<byte>.Create(256, 256, 3)) }));
            Assert.IsTrue(result.IsStale(applied with { Masks = applied.Masks.SetItem("forest", TiledBuffer<byte>.Create(256, 256, 3)) }));
            Assert.IsTrue(result.IsStale(applied.UpdatePropRule("Forest", rule => rule with { Density = 1 })));
        }

        [TestMethod]
        public void SavesAndLoadsTheRulesAndTheirProps()
        {
            MapDocument document = Flat().AddPropRule(Forest() with
            {
                Conditions = [new PropRuleCondition("stratum:1", 0.3f), new PropRuleCondition("stratum:2", 0.5f, Forbid: true)],
                Clustering = PropClustering.Clusters,
                MinSlope = 2,
                Placement = new PropPlacement { Alignment = PropAlignment.Terrain, RandomTilt = 5, ScaleMin = 0.8f, ScaleMax = 1.2f, Sink = 0.5f },
            });
            document = document with { Props = [new Prop { Id = 1, Blueprint = Pine, Rule = "Forest", Scale = Vector3.One }, new Prop { Id = 2, Blueprint = Rock, Scale = Vector3.One }] };
            string project = ProjectFolders.New("prop-rules");
            MapProjectStore.Save(document, project);
            Assert.IsTrue(File.Exists(Path.Combine(project, "prop-rules.json")));

            MapDocument loaded = MapProjectStore.Load(project);
            Assert.AreEqual(1, loaded.PropRules.Length);
            PropRule rule = loaded.PropRules[0];
            Assert.AreEqual(document.PropRules[0] with { Palette = rule.Palette, Conditions = rule.Conditions }, rule);
            CollectionAssert.AreEqual(document.PropRules[0].Palette.ToArray(), rule.Palette.ToArray());
            CollectionAssert.AreEqual(document.PropRules[0].Conditions.ToArray(), rule.Conditions.ToArray());
            Assert.AreEqual("Forest", loaded.Props[0].Rule);
            Assert.IsNull(loaded.Props[1].Rule);
            Assert.AreSame(loaded, MapProjectStore.Reload(loaded, project, ["prop-rules.json"]));

            MapDocument none = loaded.RemovePropRule("Forest");
            MapProjectStore.Save(none, project, previous: loaded);
            Assert.IsFalse(File.Exists(Path.Combine(project, "prop-rules.json")), "no rules, no file");
            Assert.IsTrue(MapProjectStore.Reload(loaded, project, ["prop-rules.json"]).PropRules.IsEmpty);
        }

        [TestMethod]
        public void ExportsPropsOfRulesAsOrdinaryProps()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa);
            Assert.IsTrue(document.Props.All(prop => prop.Rule is null));
            MapDocument marked = MaskLayers.Add(document with { Props = [.. document.Props.Select(prop => prop with { Rule = "Imported" })] }, "m")
                .AddPropRule(Forest("Imported"))
                .AddMaskRule("m", MaskRule.Of(MaskRuleKind.Noise));

            IReadOnlyDictionary<string, byte[]> expected = MapExporter.ToBytes(document);
            IReadOnlyDictionary<string, byte[]> actual = MapExporter.ToBytes(marked);
            CollectionAssert.AreEquivalent(expected.Keys.ToList(), actual.Keys.ToList());
            foreach ((string path, byte[] bytes) in expected)
            {
                CollectionAssert.AreEqual(bytes, actual[path], path);
            }

            string project = ProjectFolders.New("no-rules");
            MapProjectStore.Save(document, project);
            Assert.IsFalse(File.Exists(Path.Combine(project, "prop-rules.json")), "a map without rules has no file of them");
            StringAssert.DoesNotMatch(File.ReadAllText(Path.Combine(project, "props.json")), new System.Text.RegularExpressions.Regex("\"rule\""));
        }

        [TestMethod]
        public void GeneratesTensOfThousandsOfPropsOnALargeMapFast()
        {
            MapDocument document = NewMap.Create(new NewMapSettings { Size = 1024, Water = false })
                .AddPropRule(Forest() with { Density = 3, Spacing = 1.5f, Clustering = PropClustering.Noise, ClusterCover = 0.6f })
                .AddPropRule(new PropRule { Name = "Rocks", Palette = [new PaletteEntry(Rock, 1)], Density = 1, Spacing = 2 });
            PropRules.Generate(document); // warm up
            Stopwatch watch = Stopwatch.StartNew();
            ImmutableArray<Prop> props = PropRules.Generate(document);
            watch.Stop();
            Assert.IsTrue(props.Length > 20_000, $"{props.Length} props");
            Assert.IsTrue(watch.ElapsedMilliseconds < 3000, $"{props.Length} props on a 20 km map in {watch.ElapsedMilliseconds} ms");
            Console.WriteLine($"{props.Length} props on a 20 km map in {watch.ElapsedMilliseconds} ms");
        }
    }
}
