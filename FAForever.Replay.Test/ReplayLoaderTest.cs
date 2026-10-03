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
    [DataRow("assets/faforever/zstd/22338092.fafreplay", 12)]
    [DataRow("assets/faforever/zstd/22373098.fafreplay", 123)]
    [DataRow("assets/faforever/zstd/22425616.fafreplay", 99)]
    [DataRow("assets/faforever/gzip/22451957.fafreplay", 9)]
    [DataRow("assets/faforever/gzip/22453414.fafreplay", 22)]
    [DataRow("assets/faforever/gzip/22453511.fafreplay", 20)]
    [DataRow("assets/faforever/TestCommands01.fafreplay", 13439)]
    [DataRow("assets/faforever/23225508.fafreplay", 2032)]
    [DataRow("assets/faforever/23225323.fafreplay", 14296)]
    [DataRow("assets/faforever/23225440.fafreplay", 6630)]
    [DataRow("assets/faforever/23225685.fafreplay", 14178)]
    [DataRow("assets/faforever/23225104.fafreplay", 51017)]
    public void FAForeverUserInputCountTest(string file, int expectedCount)
    {
        Replay replay = ReplayLoader.LoadFAFReplayFromDisk(file);
        Assert.AreEqual(expectedCount, replay.Body.UserInput.Count);
    }

    [TestMethod]
    [DataRow("assets/scfa/balthazar-01.SCFAReplay", 6290)]
    [DataRow("assets/scfa/balthazar-02.SCFAReplay", 8377)]
    [DataRow("assets/scfa/balthazar-03.SCFAReplay", 12077)]
    public void SCFAUserInputCountTest(string file, int expectedCount)
    {
        Replay replay = ReplayLoader.LoadSCFAReplayFromDisk(file);
        Assert.AreEqual(expectedCount, replay.Body.UserInput.Count);
    }

    [TestMethod]
    [DataRow("assets/faforever/TestCommands01.fafreplay", 106)]
    [DataRow("assets/faforever/gzip/22451957.fafreplay", 0)]
    [DataRow("assets/faforever/gzip/22453511.fafreplay", 3)]
    public void FAForeverChatMessageCountTest(string file, int expectedCount)
    {
        Replay replay = ReplayLoader.LoadFAFReplayFromDisk(file);
        List<ReplayChatMessage> chatMessages = ReplaySemantics.GetChatMessages(replay);
        Assert.AreEqual(expectedCount, chatMessages.Count);
    }

    [TestMethod]
    [DataRow("assets/faforever/mods.fafreplay", 2)]
    public void FAForeverModCountTest(string file, int expectedCount)
    {
        Replay replay = ReplayLoader.LoadFAFReplayFromDisk(file);

        Assert.AreEqual(expectedCount, replay.Header.Mods.Length);
    }

    [TestMethod]
    [DataRow("assets/scfa/21stGameOceanScampsV2.SCFAReplay", 12)]
    public void SCFAModCountTest(string file, int expectedCount)
    {
        Replay replay = ReplayLoader.LoadSCFAReplayFromDisk(file);

        Assert.AreEqual(expectedCount, replay.Header.Mods.Length);
    }

    /// <summary>
    /// Every client records its checksum of the same tick; none of these games desynced, so all of
    /// those agree. (The flag used to be stuck on "desync" for every replay.)
    /// </summary>
    [TestMethod]
    [DataRow("assets/faforever/23225104.fafreplay")]
    [DataRow("assets/faforever/23225508.fafreplay")]
    [DataRow("assets/faforever/TestCommands01.fafreplay")]
    [DataRow("assets/faforever/mods.fafreplay")]
    [DataRow("assets/faforever/zstd/22338092.fafreplay")]
    [DataRow("assets/faforever/gzip/22453414.fafreplay")]
    public void FAForeverInSyncTest(string file)
    {
        Replay replay = ReplayLoader.LoadFAFReplayFromDisk(file);

        Assert.IsTrue(replay.Body.InSync);
    }

    [TestMethod]
    [DataRow("assets/scfa/21stGameOceanScampsV2.SCFAReplay")]
    [DataRow("assets/scfa/balthazar-01.SCFAReplay")]
    public void SCFAInSyncTest(string file)
    {
        Replay replay = ReplayLoader.LoadSCFAReplayFromDisk(file);

        Assert.IsTrue(replay.Body.InSync);
    }

    /// <summary>
    /// A desync is two clients recording a different checksum for the same tick. The test changes
    /// one byte of a second checksum halfway through the game; the replay must then be out of sync,
    /// and stay so although the checksums after it agree again.
    /// </summary>
    [TestMethod]
    [DataRow("assets/scfa/21stGameOceanScampsV2.SCFAReplay")]
    public void SCFADesyncTest(string file)
    {
        byte[] bytes = File.ReadAllBytes(file);
        ReplayLoadingStage.WithScenario atBody = (ReplayLoadingStage.WithScenario)ReplayLoader.ProcessReplayStage(
            new ReplayLoadingStage.Decompressed(new MemoryStream(bytes), null));
        int position = (int)atBody.Stream.BaseStream.Position;

        // Walk the inputs (type byte, int16 length including those 3 bytes) to the second checksum
        // of a tick past the middle: VerifyChecksum = 16 bytes of hash, then the int32 tick.
        const int halfway = 10_000; // ticks; this game lasts about 38,000
        int? previousTick = null;
        int changed = -1;
        while (position < bytes.Length && changed < 0)
        {
            byte type = bytes[position];
            int length = BitConverter.ToInt16(bytes, position + 1);
            if (type == (byte)ReplayInputType.VerifyChecksum)
            {
                int tick = BitConverter.ToInt32(bytes, position + 3 + 16);
                if (tick == previousTick && tick >= halfway)
                {
                    changed = position + 3;
                }
                previousTick = tick;
            }
            position += length;
        }

        Assert.IsTrue(changed >= 0, "The replay has no tick with two checksums past the middle.");
        bytes[changed] ^= 0xFF;

        ReplayLoadingStage stage = new ReplayLoadingStage.Decompressed(new MemoryStream(bytes), null);
        while (stage is not ReplayLoadingStage.Complete)
        {
            stage = stage switch
            {
                ReplayLoadingStage.Decompressed decompressed => ReplayLoader.ProcessReplayStage(decompressed),
                ReplayLoadingStage.WithScenario withScenario => ReplayLoader.ProcessReplayStage(withScenario),
                ReplayLoadingStage.AtInput atInput => ReplayLoader.ProcessReplayStage(atInput),
                ReplayLoadingStage.Failed failed => throw new AssertFailedException(failed.Message),
                _ => throw new AssertFailedException($"Unexpected stage {stage.GetType().Name}"),
            };
        }

        Assert.IsFalse(((ReplayLoadingStage.Complete)stage).Body.InSync);
    }
}
