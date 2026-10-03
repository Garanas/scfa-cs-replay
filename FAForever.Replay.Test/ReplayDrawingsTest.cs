using FAForever.Replay;

namespace FAForever.Replay.Test
{
    /// <summary>
    /// Drawings (SharePaintingBrushStroke callbacks) are newer than the replay assets, so these
    /// tests build the callbacks by hand, mirroring a stroke seen in a real game.
    /// </summary>
    [TestClass]
    public class ReplayDrawingsTest
    {
        private static ReplayInput Stroke(int tick, int sourceId, params double[] samples)
        {
            Dictionary<string, LuaData> interleaved = new();
            for (int i = 0; i < samples.Length; i++)
            {
                interleaved[(i + 1).ToString()] = new LuaData.Number(samples[i]);
            }

            LuaData.Table painting = new(new Dictionary<string, LuaData>
            {
                ["PaintingAdapterIdentifier"] = new LuaData.String("table: 1EE0C208"),
                ["PeerName"] = new LuaData.String("Jip"),
                ["Samples"] = new LuaData.Table(interleaved),
                ["ShareId"] = new LuaData.Number(1),
            });
            LuaData.Table parameters = new(new Dictionary<string, LuaData> { ["ShareablePainting"] = painting });
            return new ReplayInput.SimCallback(tick, sourceId, "SharePaintingBrushStroke", parameters, new CommandUnits(0));
        }

        [TestMethod]
        public void ExtractsTheMapPlaneOfEveryStroke()
        {
            List<ReplayDrawing> drawings = ReplaySemantics.GetDrawings(
            [
                Stroke(125, 2, 339.761, 42.19, 157.658, 329.843, 41.9, 152.427, 317.2, 40.758, 150.1),
            ]);

            Assert.AreEqual(1, drawings.Count);
            ReplayDrawing drawing = drawings[0];
            Assert.AreEqual(TimeSpan.FromSeconds(12.5), drawing.Timestamp);
            Assert.AreEqual(2, drawing.SourceId);
            Assert.AreEqual("Jip", drawing.PeerName);
            Assert.AreEqual(1, drawing.ShareId);
            Assert.AreEqual(3, drawing.Points.Count);
            Assert.AreEqual(339.761, drawing.Points[0].X, 0.001);
            Assert.AreEqual(157.658, drawing.Points[0].Z, 0.001);
            Assert.AreEqual(317.2, drawing.Points[2].X, 0.001);
            Assert.AreEqual(150.1, drawing.Points[2].Z, 0.001);
        }

        [TestMethod]
        public void SkipsStrokesWithoutALine()
        {
            LuaData.Table unrelated = new(new Dictionary<string, LuaData> { ["Type"] = new LuaData.String("Move") });

            List<ReplayDrawing> drawings = ReplaySemantics.GetDrawings(
            [
                Stroke(10, 0, 1, 2, 3),
                Stroke(20, 0, 1, 2),
                new ReplayInput.SimCallback(30, 0, "SharePaintingBrushStroke", unrelated, new CommandUnits(0)),
                new ReplayInput.SimCallback(40, 0, "SpawnPing", unrelated, new CommandUnits(0)),
                Stroke(50, 1, 1, 2, 3, 4, 5, 6, 7),
            ]);

            // Only the last stroke has two complete samples; its trailing partial sample is ignored.
            Assert.AreEqual(1, drawings.Count);
            Assert.AreEqual(1, drawings[0].SourceId);
            Assert.AreEqual(2, drawings[0].Points.Count);
        }
    }
}
