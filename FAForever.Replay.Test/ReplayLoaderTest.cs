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
    [DataRow("assets/faforever/23225508.fafreplay", 2916)]
    [DataRow("assets/faforever/23225323.fafreplay", 17166)]
    [DataRow("assets/faforever/23225440.fafreplay", 8042)]
    [DataRow("assets/faforever/23225685.fafreplay", 17040)]
    [DataRow("assets/faforever/23225104.fafreplay", 55923)]
    public void UserInputCountTest(string file, int expectedCount)
    {
        Replay replay = ReplayLoader.LoadFAFReplayFromDisk(file);
        Assert.AreEqual(expectedCount, replay.Events.Count);
    }

    [TestMethod]
    [DataRow("assets/faforever/TestCommands01.fafreplay", 106)]
    [DataRow("assets/faforever/gzip/22451957.fafreplay", 0)]
    [DataRow("assets/faforever/gzip/22453511.fafreplay", 3)]
    public void ChatMessageCountTest(string file, int expectedCount)
    {
        Replay replay = ReplayLoader.LoadFAFReplayFromDisk(file);
        List<ReplayChatMessage> chatMessages = ReplaySemantics.GetChatMessages(replay.Events);
        Assert.AreEqual(expectedCount, chatMessages.Count);
    }
}