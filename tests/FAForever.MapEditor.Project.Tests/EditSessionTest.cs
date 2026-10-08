using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class EditSessionTest
    {
        private static MapDocument original = null!;

        [ClassInitialize]
        public static void Initialize(TestContext _) => original = MapImporter.ImportFolder(ProjectFolders.HardFfa);

        private static Func<MapDocument, MapDocument> MoveProp(int id, Vector3 position) =>
            document => document.UpdateProp(id, prop => prop with { Position = position });

        [TestMethod]
        public void UndoesAndRedoesAnEdit()
        {
            EditSession session = new EditSession(original);

            Assert.IsTrue(session.Edit("Move a prop", MoveProp(1, Vector3.One)));
            Assert.AreEqual(Vector3.One, session.Document.Props[0].Position);
            Assert.AreEqual("Move a prop", session.UndoDescription);

            Assert.IsTrue(session.Undo());
            Assert.AreSame(original, session.Document);
            Assert.AreEqual("Move a prop", session.RedoDescription);

            Assert.IsTrue(session.Redo());
            Assert.AreEqual(Vector3.One, session.Document.Props[0].Position);
            Assert.IsFalse(session.Redo());
        }

        [TestMethod]
        public void ForgetsWhatCouldBeRedoneAfterANewEdit()
        {
            EditSession session = new EditSession(original);
            session.Edit("First", MoveProp(1, Vector3.One));
            session.Undo();

            session.Edit("Second", MoveProp(2, Vector3.One));

            Assert.IsFalse(session.CanRedo);
            Assert.AreEqual("Second", session.UndoDescription);
        }

        [TestMethod]
        public void KeepsNoEditThatChangesNothing()
        {
            EditSession session = new EditSession(original);

            Assert.IsFalse(session.Edit("Nothing", document => document with { }));
            Assert.IsFalse(session.CanUndo);
        }

        [TestMethod]
        public void ForgetsTheOldestEditsPastItsLimit()
        {
            EditSession session = new EditSession(original, historyLimit: 2);

            for (int i = 1; i <= 3; i++)
            {
                session.Edit($"Move {i}", MoveProp(i, Vector3.One));
            }

            Assert.IsTrue(session.Undo());
            Assert.IsTrue(session.Undo());
            Assert.IsFalse(session.Undo());
            Assert.AreEqual(Vector3.One, session.Document.Props[0].Position, "the first edit can no longer be undone");
        }

        [TestMethod]
        public void TellsWhatChanged()
        {
            EditSession session = new EditSession(original);
            List<DocumentChange> changes = [];
            session.Changed += (_, change) => changes.Add(change);

            session.Edit("Move a prop", MoveProp(1, Vector3.One));
            session.Undo();

            Assert.AreEqual(2, changes.Count);
            Assert.AreEqual(ChangeReason.Edit, changes[0].Reason);
            Assert.AreSame(original, changes[0].Previous);
            Assert.AreSame(original.Terrain, changes[0].Current.Terrain, "what the edit did not touch is the same object");
            Assert.AreEqual(ChangeReason.Undo, changes[1].Reason);
            Assert.AreSame(original, changes[1].Current);
        }

        [TestMethod]
        public void MakesABrushStrokeOneStep()
        {
            EditSession session = new EditSession(original);

            using (EditTransaction stroke = session.Begin("Raise terrain"))
            {
                for (int x = 10; x < 20; x++)
                {
                    stroke.Heightmap[x, 10] = 4096;
                }
                stroke.StratumMask(1)[0, 0] = 255;
                stroke.Commit();
            }

            Heightmap heightmap = session.Document.Terrain.Heightmap;
            Assert.AreEqual(32f, heightmap.GetHeight(15, 10));
            Assert.AreEqual(255, session.Document.Terrain.StratumMasks.Layers[0][0, 0]);
            Assert.AreEqual(1, heightmap.Samples.ChangedTiles(original.Terrain.Heightmap.Samples).Count(), "the stroke copied one tile");

            session.Undo();
            Assert.AreSame(original, session.Document);
        }

        [TestMethod]
        public void ThrowsAwayATransactionThatIsNotCommitted()
        {
            EditSession session = new EditSession(original);

            using (EditTransaction transaction = session.Begin("Cancelled"))
            {
                transaction.Heightmap[0, 0] = 1;
                transaction.Apply(MoveProp(1, Vector3.One));
            }

            Assert.AreSame(original, session.Document);
            Assert.IsFalse(session.CanUndo);
            Assert.IsFalse(session.InTransaction);
        }

        [TestMethod]
        public void LetsEditsAndBrushesWorkTogetherInATransaction()
        {
            EditSession session = new EditSession(original);

            using (EditTransaction transaction = session.Begin("Plugin"))
            {
                transaction.Heightmap[0, 0] = 1;
                transaction.Apply(document => document.AddProp(document.Props[0]));
                transaction.Heightmap[1, 0] = 2;
                transaction.Commit();
            }

            MapDocument document = session.Document;
            Assert.AreEqual(1, document.Terrain.Heightmap.Samples[0, 0]);
            Assert.AreEqual(2, document.Terrain.Heightmap.Samples[1, 0]);
            Assert.AreEqual(original.Props.Length + 1, document.Props.Length);
            Assert.AreEqual(original.Props.Length + 1, document.Props[^1].Id, "a new prop gets a new id");
            Assert.AreEqual("Plugin", session.UndoDescription);
        }

        [TestMethod]
        public void AllowsNothingElseWhileATransactionIsOpen()
        {
            EditSession session = new EditSession(original);

            using EditTransaction transaction = session.Begin("Stroke");

            Assert.ThrowsException<InvalidOperationException>(() => session.Edit("Other", MoveProp(1, Vector3.One)));
            Assert.ThrowsException<InvalidOperationException>(() => session.Undo());
            Assert.ThrowsException<InvalidOperationException>(() => session.Begin("Another"));
        }

        [TestMethod]
        public void MergesEditsWithTheSameKeyIntoOneStep()
        {
            EditSession session = new EditSession(original);

            for (int i = 1; i <= 5; i++)
            {
                float elevation = i;
                session.Edit("Water elevation", document => document with { Water = document.Water with { Elevation = elevation } }, mergeKey: "water.elevation");
            }
            session.Edit("Move a prop", MoveProp(1, Vector3.One));
            session.Edit("Water elevation", document => document with { Water = document.Water with { Elevation = 9 } }, mergeKey: "water.elevation");

            Assert.AreEqual(3, session.History.Count, "the slider, the prop, the slider again");
            session.Undo();
            session.Undo();
            Assert.AreEqual(5, session.Document.Water.Elevation, "the first drag ended at its last value");
            session.Undo();
            Assert.AreSame(original, session.Document);
        }

        [TestMethod]
        public void KnowsWhetherTheDocumentWasSaved()
        {
            EditSession session = new EditSession(original);
            int notices = 0;
            session.StateChanged += (_, _) => notices++;
            Assert.IsFalse(session.IsModified);

            session.Edit("Move a prop", MoveProp(1, Vector3.One));
            Assert.IsTrue(session.IsModified);

            session.MarkSaved();
            Assert.IsFalse(session.IsModified);

            session.Undo();
            Assert.IsTrue(session.IsModified, "the saved version is the one undone");
            session.Redo();
            Assert.IsFalse(session.IsModified);
            Assert.AreEqual(4, notices);
        }

        [TestMethod]
        public void ListsItsHistory()
        {
            EditSession session = new EditSession(original);
            session.Edit("First", MoveProp(1, Vector3.One));
            session.Edit("Second", MoveProp(2, Vector3.One));
            session.Undo();

            CollectionAssert.AreEqual(
                new[] { new HistoryStep("First", true), new HistoryStep("Second", false) },
                session.History.ToList());
        }

        [TestMethod]
        public void JumpsToAStepOfTheHistoryInOneGo()
        {
            EditSession session = new EditSession(original);
            session.Edit("First", MoveProp(1, Vector3.One));
            session.Edit("Second", MoveProp(2, Vector3.One));
            session.Edit("Third", MoveProp(3, Vector3.One));
            List<DocumentChange> changes = [];
            session.Changed += (_, change) => changes.Add(change);

            Assert.IsTrue(session.GoTo(-1));
            Assert.AreSame(original, session.Document);
            Assert.IsTrue(session.GoTo(1));
            Assert.AreEqual("Second", session.UndoDescription);
            Assert.IsFalse(session.GoTo(1), "already there");

            Assert.AreEqual(2, changes.Count, "one change per jump");
            Assert.AreEqual(ChangeReason.Undo, changes[0].Reason);
            Assert.AreEqual("3 steps", changes[0].Description);
            Assert.AreEqual(ChangeReason.Redo, changes[1].Reason);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => session.GoTo(3));
        }

        [TestMethod]
        public void NamesWhatAnEditCannotFind()
        {
            EditSession session = new EditSession(original);

            KeyNotFoundException exception = Assert.ThrowsException<KeyNotFoundException>(() => session.Edit("Move", MoveProp(99999, Vector3.One)));

            StringAssert.Contains(exception.Message, "prop 99999");
            Assert.AreSame(original, session.Document);
        }
    }
}
