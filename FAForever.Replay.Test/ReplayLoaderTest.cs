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
        IProgress<string> progress = new Progress<string>();
        Replay replay = ReplayLoader.LoadFAFReplayFromDisk(file, progress);
        Assert.IsNotNull(replay);
    }

    [TestMethod]
    [DataRow("assets/faforever/gzip/22451957.fafreplay")]
    [DataRow("assets/faforever/gzip/22453414.fafreplay")]
    [DataRow("assets/faforever/gzip/22453511.fafreplay")]
    public void FAForeverGZipTest(string file)
    {
        IProgress<string> progress = new Progress<string>();
        Replay replay = ReplayLoader.LoadFAFReplayFromDisk(file, progress);
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
    public void FAForeverUserInputCountTest(string file, int expectedCount)
    {
        IProgress<string> progress = new Progress<string>();
        Replay replay = ReplayLoader.LoadFAFReplayFromDisk(file, progress);
        Assert.AreEqual(expectedCount, replay.Body.UserInput.Length);
    }

    [TestMethod]
    [DataRow("assets/faforever/TestCommands01.fafreplay", 106)]
    [DataRow("assets/faforever/gzip/22451957.fafreplay", 0)]
    [DataRow("assets/faforever/gzip/22453511.fafreplay", 3)]
    public void FAForeverChatMessageCountTest(string file, int expectedCount)
    {
        IProgress<string> progress = new Progress<string>();
        Replay replay = ReplayLoader.LoadFAFReplayFromDisk(file, progress);
        List<ReplayChatMessage> chatMessages = ReplaySemantics.GetChatMessages(replay);
        Assert.AreEqual(expectedCount, chatMessages.Count);
    }

    [TestMethod]
    [DataRow("assets/faforever/mods.fafreplay", 2)]
    public void FAForeverModCountTest(string file, int expectedCount)
    {
        IProgress<string> progress = new Progress<string>();
        Replay replay = ReplayLoader.LoadFAFReplayFromDisk(file, progress);
        List<ReplayChatMessage> chatMessages = ReplaySemantics.GetChatMessages(replay);
        //Assert.AreEqual(expectedCount, chatMessages.Count);
    }

    [TestMethod]
    [DataRow("assets/scfa/21stGameOceanScampsV2.SCFAReplay", 0)]
    public void SCFAModCountTest(string file, int expectedCount)
    {
        IProgress<string> progress = new Progress<string>();
        Replay replay = ReplayLoader.LoadSCFAReplayFromDisk(file, progress);
        List<ReplayChatMessage> chatMessages = ReplaySemantics.GetChatMessages(replay);
        //Assert.AreEqual(expectedCount, chatMessages.Count);
    }
}