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
    }
}
