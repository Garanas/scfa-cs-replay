namespace FAForever.Replay.Test;

/// <summary>
/// Snapshot tests for the parser output. The parser is performance-tuned; these tests pin the
/// complete parsed result of every asset, so an optimisation cannot silently change what is read.
/// When a change to the output is intended, update the expected fingerprints in the same commit.
/// </summary>
[TestClass]
public class ReplayFingerprintTest
{
    [TestMethod]
    [DataRow("assets/faforever/23225104.fafreplay", "24E45D81497CB122199CCA33DA50ECBABCFE17676D86AE13F6B8C3B1F0FEF67D")]
    [DataRow("assets/faforever/23225323.fafreplay", "860AB44795C4340585FC51D3FEF3DFA96936D82BD393B6FAC28C23622B647B2F")]
    [DataRow("assets/faforever/23225440.fafreplay", "CC5ADA84A1B3A3D332DC5B93261E62808D3CFCA2998D75BD1F46A21CFE5B01BC")]
    [DataRow("assets/faforever/23225508.fafreplay", "88913535B1C1B69E63AE14FC2E7187B4DAB96FA19413EC3F42586960AF569FC3")]
    [DataRow("assets/faforever/23225685.fafreplay", "0BDC295408933AD44FE8DE4614BAFB4B94D7ECD6945976D4CDD8BAD8A7F7E304")]
    [DataRow("assets/faforever/TestCommands01.fafreplay", "3E010C0468AC97635F24AA67BCB662BD391FD7B86AAADB72B30D4124546F3480")]
    [DataRow("assets/faforever/mods.fafreplay", "2E8D1334516EA27AD8A00C519F72CC1CE3E7816E57B48F5271680D44D610B6F6")]
    [DataRow("assets/faforever/gzip/22451957.fafreplay", "18436E97984668917A7E4AC44FFDC3EAED77FDF0E323F842B70D3651CE62DCE5")]
    [DataRow("assets/faforever/gzip/22453414.fafreplay", "258A0FF35F1DBB01FE7CEBE8F107DCAD9554A072E850900E2064F90967AAC5E5")]
    [DataRow("assets/faforever/gzip/22453511.fafreplay", "95A54C6DFFFD7D47E19A9493C5F4FF7DEEE7A6D783252CD10D8B5C2684010092")]
    [DataRow("assets/faforever/zstd/22338092.fafreplay", "008D49E5E210D995C33A0F6906B36E1ADB94507991FBFD11D1C35BE64FDC76C1")]
    [DataRow("assets/faforever/zstd/22373098.fafreplay", "EFA8BB988636687326C3170CD87DE619751CB9A37E624A5851A0076FBCBB0E70")]
    [DataRow("assets/faforever/zstd/22425616.fafreplay", "0FB5DB72818F9F5D8C7BDA0A8D6A56D54C25D84A923CFF433CD3210175019837")]
    [DataRow("assets/scfa/21stGameOceanScampsV2.SCFAReplay", "A781C00761A1E03962F7CD834CA15CA78C558E5C20D2228BEF3A26A96B69A7D2")]
    [DataRow("assets/scfa/22338092.scfareplay", "008D49E5E210D995C33A0F6906B36E1ADB94507991FBFD11D1C35BE64FDC76C1")]
    [DataRow("assets/scfa/22373098.scfareplay", "EFA8BB988636687326C3170CD87DE619751CB9A37E624A5851A0076FBCBB0E70")]
    [DataRow("assets/scfa/22425616.scfareplay", "0FB5DB72818F9F5D8C7BDA0A8D6A56D54C25D84A923CFF433CD3210175019837")]
    [DataRow("assets/scfa/balthazar-01.SCFAReplay", "0C0AA9D632BA778F1812E6322E75BD5F43BFDDBD16F1C99B2F03DE09F5999C0A")]
    [DataRow("assets/scfa/balthazar-02.SCFAReplay", "9A5809FBCA28372B43FAA8C6029ABB79DC0BED1A7B3ADC00A42312E336F8F571")]
    [DataRow("assets/scfa/balthazar-03.SCFAReplay", "6011E9E40E771533FC2B9CD26ABA0F12B7DD2484179F60E5777603FD8A68AFAA")]
    public void FingerprintTest(string file, string expectedFingerprint)
    {
        Replay replay = Load(file);
        Assert.AreEqual(expectedFingerprint, ReplayFingerprint.Compute(replay.Header, replay.Body));
    }

    /// <summary>
    /// The staged API (used by the Viewer) must produce exactly the same result as loading in
    /// one go. An odd batch size makes the batch boundaries land on every kind of input.
    /// </summary>
    [TestMethod]
    [DataRow("assets/faforever/23225104.fafreplay")]
    [DataRow("assets/faforever/TestCommands01.fafreplay")]
    [DataRow("assets/faforever/gzip/22453414.fafreplay")]
    [DataRow("assets/scfa/balthazar-01.SCFAReplay")]
    public void StagedLoadingMatchesOneShotTest(string file)
    {
        const int batchSize = 97;
        Replay replay = Load(file);

        MemoryStream stream = new MemoryStream(File.ReadAllBytes(file));
        ReplayLoadingStage stage = IsFAForeverReplay(file)
            ? new ReplayLoadingStage.NotStarted(stream)
            : new ReplayLoadingStage.Decompressed(stream, null);

        while (stage is not ReplayLoadingStage.Complete)
        {
            stage = stage switch
            {
                ReplayLoadingStage.NotStarted notStarted => ReplayLoader.ProcessReplayStage(notStarted),
                ReplayLoadingStage.WithMetadata withMetadata => ReplayLoader.ProcessReplayStage(withMetadata),
                ReplayLoadingStage.Decompressed decompressed => ReplayLoader.ProcessReplayStage(decompressed),
                ReplayLoadingStage.WithScenario withScenario => ReplayLoader.ProcessReplayStage(withScenario, batchSize),
                ReplayLoadingStage.AtInput atInput => ReplayLoader.ProcessReplayStage(atInput, batchSize),
                ReplayLoadingStage.Failed failed => throw new AssertFailedException(failed.Message),
                _ => throw new AssertFailedException($"Unexpected stage {stage.GetType().Name}"),
            };
        }

        ReplayLoadingStage.Complete complete = (ReplayLoadingStage.Complete)stage;
        Assert.AreEqual(
            ReplayFingerprint.Compute(replay.Header, replay.Body),
            ReplayFingerprint.Compute(complete.Header, complete.Body));
    }

    private static bool IsFAForeverReplay(string file)
        => Path.GetExtension(file).Equals(".fafreplay", StringComparison.OrdinalIgnoreCase);

    // ReplayLoader.LoadReplayFromDisk matches extensions case-sensitively, which rejects *.SCFAReplay.
    private static Replay Load(string file)
        => IsFAForeverReplay(file) ? ReplayLoader.LoadFAFReplayFromDisk(file) : ReplayLoader.LoadSCFAReplayFromDisk(file);
}
