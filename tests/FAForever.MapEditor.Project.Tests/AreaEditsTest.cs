using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class AreaEditsTest
    {
        private static readonly Area Square = new Area { Name = "AREA_1", X0 = 10, Z0 = 20, X1 = 50, Z1 = 60 };

        [TestMethod]
        public void FindsTheHandleUnderTheMouse()
        {
            Assert.AreEqual(AreaHandle.TopLeft, AreaEdits.HandleAt(Square, 11, 19, 2));
            Assert.AreEqual(AreaHandle.Right, AreaEdits.HandleAt(Square, 50, 40, 2), "the middle of an edge");
            Assert.AreEqual(AreaHandle.Move, AreaEdits.HandleAt(Square, 30, 30, 2), "inside");
            Assert.AreEqual(AreaHandle.None, AreaEdits.HandleAt(Square, 80, 30, 2));
            Assert.AreEqual(8, AreaEdits.Handles(Square).Count());
        }

        [TestMethod]
        public void DragsWhatAHandleMoves()
        {
            Area wider = AreaEdits.Drag(Square, AreaHandle.Right, 10, 99);
            Assert.AreEqual((10.0, 20.0, 60.0, 60.0), (wider.X0, wider.Z0, wider.X1, wider.Z1), "an edge moves one way only");
            Area moved = AreaEdits.Drag(Square, AreaHandle.Move, 5, -5);
            Assert.AreEqual((15.0, 15.0, 55.0, 55.0), (moved.X0, moved.Z0, moved.X1, moved.Z1));
            Area over = AreaEdits.Drag(Square, AreaHandle.Left, 50, 0);
            Assert.AreEqual((50.0, 60.0), (over.X0, over.X1), "an edge dragged past the other turns the rectangle over");
        }
    }
}
