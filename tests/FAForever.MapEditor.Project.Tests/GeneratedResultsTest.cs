using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Generation;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class GeneratedResultsTest
    {
        private static MapDocument original = null!;

        [ClassInitialize]
        public static void Initialize(TestContext _) => original = MapImporter.ImportFolder(ProjectFolders.HardFfa);

        /// <summary>
        /// Counts the heights above a level, from the heightmap and the water: as walkability will.
        /// </summary>
        private sealed class HighGround : IGenerator
        {
            public int Runs { get; private set; }

            public string Id => "high-ground";

            public string Title => "High ground";

            public string Story => "F-7";

            public IReadOnlyList<GeneratedInput> Inputs { get; } = [GeneratedInput.Heightmap, GeneratedInput.Water];

            public object Generate(MapDocument document)
            {
                Runs++;
                Heightmap heightmap = document.Terrain.Heightmap;
                int high = 0;
                for (int z = 0; z <= heightmap.Height; z += 8)
                {
                    for (int x = 0; x <= heightmap.Width; x += 8)
                    {
                        high += heightmap.GetHeight(x, z) > document.Water.Elevation ? 1 : 0;
                    }
                }
                return high;
            }
        }

        [TestMethod]
        public void TellsWhichInputsChangedSinceAResultWasGenerated()
        {
            GeneratedResults results = new GeneratedResults();
            HighGround generator = new HighGround();
            results.Register(generator);
            EditSession session = new EditSession(original);

            Assert.AreEqual(GeneratedStatus.Missing, results.States(session.Document).Single().Status);
            Assert.AreEqual(0, generator.Runs, "nothing generates by itself");

            GeneratedResult result = results.Generate("high-ground", session.Document);
            Assert.AreEqual(1, generator.Runs);
            Assert.IsInstanceOfType<int>(result.Value);
            Assert.AreEqual(GeneratedStatus.Current, results.States(session.Document).Single().Status);

            session.Edit("Move a prop", document => document.UpdateProp(1, prop => prop with { Position = Vector3.One }));
            Assert.AreEqual(0, results.Stale(session.Document).Count, "a part it does not read changed");

            using (EditTransaction edit = session.Begin("Raise terrain"))
            {
                edit.Heightmap[10, 10] = (ushort)(edit.Heightmap[10, 10] + 100);
                edit.Commit();
            }
            session.Edit("Lower the water", document => document with { Water = document.Water with { Elevation = document.Water.Elevation - 1 } });
            GeneratedState stale = results.Stale(session.Document).Single();
            CollectionAssert.AreEqual(new[] { "heightmap", "water" }, stale.ChangedInputs.ToArray());
            Assert.AreEqual(1, generator.Runs, "it only says so");

            session.GoTo(0);
            Assert.AreEqual(0, results.Stale(session.Document).Count, "undone back to the version it was made from");

            results.Forget();
            Assert.IsNull(results.ResultOf("high-ground"));
            Assert.ThrowsException<ArgumentException>(() => results.Register(new HighGround()));
            Assert.IsTrue(results.Unregister("high-ground"));
            Assert.AreEqual(0, results.Generators.Count);
        }

        [TestMethod]
        public void KeepsAResultMadeElsewhereFromTheVersionItWasMadeFrom()
        {
            GeneratedResults results = new GeneratedResults();
            HighGround generator = new HighGround();
            results.Register(generator);
            MapDocument from = original;
            MapDocument now = original with { Water = original.Water with { Elevation = 3 } };

            // generated on a thread of the pool from the version current when it started
            GeneratedResult result = GeneratedResult.Of(generator, from, generator.Generate(from));
            results.Keep(result);

            CollectionAssert.AreEqual(new[] { "water" }, results.Stale(now).Single().ChangedInputs.ToArray());
        }

        /// <summary>
        /// A generator that reads and writes what it is told, and remembers the results handed to it.
        /// </summary>
        private sealed class Step(string id, GeneratedInput[] inputs, GeneratedInput[]? outputs = null, string[]? reads = null) : IGenerator
        {
            public List<string> Handed { get; } = [];

            public string Id => id;

            public string Title => id;

            public string Story => "F-7";

            public IReadOnlyList<GeneratedInput> Inputs => inputs;

            public IReadOnlyList<GeneratedInput> Outputs => outputs ?? [];

            public IReadOnlyList<string> Reads => reads ?? [];

            public void Read(GeneratedResult result) => Handed.Add(result.Generator.Id);

            public object Generate(MapDocument document) => id;
        }

        [TestMethod]
        public void GeneratorsComeAfterThoseThatFeedThem()
        {
            GeneratedResults results = new GeneratedResults();
            Step props = new Step("props", [GeneratedInput.Heightmap, GeneratedInput.Props]);
            Step network = new Step("network", [GeneratedInput.Heightmap], reads: ["walk"]);
            Step masks = new Step("masks", [GeneratedInput.Heightmap], outputs: [GeneratedInput.Props]);
            Step walk = new Step("walk", [GeneratedInput.Heightmap]);
            foreach (Step step in new[] { props, network, masks, walk })
            {
                results.Register(step);
            }

            CollectionAssert.AreEqual(new[] { "masks", "props", "walk", "network" }, results.InOrder().Select(generator => generator.Id).ToArray());
            Assert.IsTrue(GeneratedResults.Feeds(masks, props));
            Assert.IsFalse(GeneratedResults.Feeds(props, masks));

            foreach (IGenerator generator in results.InOrder())
            {
                results.Generate(generator.Id, original);
            }
            CollectionAssert.AreEqual(new[] { "walk" }, network.Handed, "the result it reads, handed to it");
            EditSession session = new EditSession(original);
            using (EditTransaction edit = session.Begin("Raise terrain"))
            {
                edit.Heightmap[10, 10] = (ushort)(edit.Heightmap[10, 10] + 100);
                edit.Commit();
            }
            CollectionAssert.AreEqual(new[] { "masks", "props", "walk", "network" }, results.Stale(session.Document).Select(state => state.Generator.Id).ToArray(), "out of date in that order");
        }

        /// <summary>
        /// A generator whose result holds a buffer of a given size.
        /// </summary>
        private sealed class Sized(string id, long bytes) : IGenerator
        {
            public string Id => id;

            public string Title => id;

            public string Story => "F-7";

            public IReadOnlyList<GeneratedInput> Inputs { get; } = [GeneratedInput.Water];

            public object Generate(MapDocument document) => new object();

            public IEnumerable<(object Buffer, long Bytes)> MemoryOf(object value) => [(value, bytes)];
        }

        private static MapDocument WithWater(float elevation) => original with { Water = original.Water with { Elevation = elevation } };

        [TestMethod]
        public void RecallsTheResultRememberedForTheVersionGoneBackTo()
        {
            GeneratedResults results = new GeneratedResults();
            HighGround generator = new HighGround();
            results.Register(generator);
            GeneratedResult first = results.Generate("high-ground", WithWater(1));
            GeneratedResult second = results.Generate("high-ground", WithWater(2));

            Assert.AreEqual(0, results.Recall(WithWater(2)).Count, "the last result is current");
            CollectionAssert.AreEqual(new[] { generator }, results.Recall(WithWater(1)).ToArray());
            Assert.AreSame(first, results.ResultOf("high-ground"));
            Assert.AreEqual(0, results.Recall(WithWater(5)).Count, "none was made for that version");
            Assert.AreSame(first, results.ResultOf("high-ground"));
            results.Recall(WithWater(2));
            Assert.AreSame(second, results.ResultOf("high-ground"));
            Assert.AreEqual(2, generator.Runs, "nothing generated again");

            for (int i = 0; i < GeneratedResults.RecentPerGenerator + 2; i++)
            {
                results.Generate("high-ground", WithWater(10 + i));
            }
            Assert.AreEqual(GeneratedResults.RecentPerGenerator, results.RecentOf("high-ground").Count);
            Assert.AreEqual(0, results.Recall(WithWater(1)).Count, "the oldest are forgotten");

            results.Forget();
            Assert.AreEqual(0, results.RecentOf("high-ground").Count);
        }

        [TestMethod]
        public void KeepsTheRememberedResultsWithinTheirBudget()
        {
            GeneratedResults results = new GeneratedResults { RecentBudget = 250 };
            Sized big = new Sized("big", 100);
            Sized small = new Sized("small", 10);
            results.Register(big);
            results.Register(small);

            results.Generate("big", WithWater(1));
            results.Generate("small", WithWater(1));
            results.Generate("big", WithWater(2));
            Assert.AreEqual(210, results.MemoryOfRecent());
            results.Generate("big", WithWater(3));

            Assert.AreEqual(2, results.RecentOf("big").Count, "the one remembered first went");
            Assert.AreEqual(1, results.RecentOf("small").Count);
            Assert.IsTrue(results.MemoryOfRecent() <= 250);
            Assert.AreEqual(0, results.Recall(WithWater(1)).Count(generator => generator == big));
        }
    }
}
