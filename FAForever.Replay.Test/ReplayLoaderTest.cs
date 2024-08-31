namespace FAForever.Replay.Test;

[TestClass]
public class ReplayLoaderTest
{
    [TestMethod]
    [DataRow("assets/22338092.scfareplay")]
    [DataRow("assets/22373098.scfareplay")]
    [DataRow("assets/22425616.scfareplay")]
    public void TestMethod1(string scfaReplayFile)
    {

        using (FileStream stream = new FileStream("assets/scfa/22338092.scfareplay", FileMode.Open))
        {
            ReplayBinaryReader reader = new ReplayBinaryReader(stream);
            Replay replay = ReplayLoader.ParseReplay(reader);
            Assert.IsNotNull(replay);
            Assert.IsNotNull(replay.Header);
            Assert.IsNotNull(replay.Events);
        }


        using (FileStream stream = new FileStream("assets/faforever/22338092.fafreplay", FileMode.Open))
        {
            Replay replay = ReplayLoader.ParseFAFReplayFromMemory(stream);
            Assert.IsNotNull(replay);
            Assert.IsNotNull(replay.Header);
            Assert.IsNotNull(replay.Events);
        }

    }

    [TestMethod]
    [DataRow("assets/faforever/22338092.fafreplay", 15)]
    [DataRow("assets/faforever/22373098.fafreplay", 185)]
    [DataRow("assets/faforever/22425616.fafreplay", 104)]
    [DataRow("assets/faforever/22451957.fafreplay", 14)]
    [DataRow("assets/faforever/22453414.fafreplay", 29)]
    [DataRow("assets/faforever/22453511.fafreplay", 25)]
    [DataRow("assets/faforever/TestCommands01.fafreplay", 15640)]
    public void EventCountFAForeverTest(string file, int expectedCount)
    {
        Replay replay = ReplayLoader.LoadFAFReplayFromDisk(file);
        Assert.AreEqual(replay.Events.Count, expectedCount);
    }
}