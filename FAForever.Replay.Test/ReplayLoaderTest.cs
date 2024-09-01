namespace FAForever.Replay.Test;

[TestClass]
public class ReplayLoaderTest
{
    [TestMethod]
    [DataRow("assets/faforever/zstd/22338092.fafreplay")]
    [DataRow("assets/faforever/zstd/22373098.fafreplay")]
    [DataRow("assets/faforever/zstd/22425616.fafreplay")]
    public void FAForeverZSTDTest(string file)
    {
        Replay replay = ReplayLoader.LoadFAFReplayFromDisk(file);
        Assert.IsNotNull(replay);
    }

    [TestMethod]
    [DataRow("assets/faforever/gzip/22451957.fafreplay")]
    [DataRow("assets/faforever/gzip/22453414.fafreplay")]
    [DataRow("assets/faforever/gzip/22453511.fafreplay")]
    public void FAForeverGZipTest(string file)
    {
        Replay replay = ReplayLoader.LoadFAFReplayFromDisk(file);
        Assert.IsNotNull(replay);
    }

    [TestMethod]
    [DataRow("assets/faforever/zstd/22338092.fafreplay", 15)]
    [DataRow("assets/faforever/zstd/22373098.fafreplay", 185)]
    [DataRow("assets/faforever/zstd/22425616.fafreplay", 104)]
    [DataRow("assets/faforever/gzip/22451957.fafreplay", 14)]
    [DataRow("assets/faforever/gzip/22453414.fafreplay", 29)]
    [DataRow("assets/faforever/gzip/22453511.fafreplay", 25)]
    [DataRow("assets/faforever/TestCommands01.fafreplay", 15640)]
    public void UserInputCountTest(string file, int expectedCount)
    {
        Replay replay = ReplayLoader.LoadFAFReplayFromDisk(file);
        Assert.AreEqual(replay.Events.Count, expectedCount);
    }
}