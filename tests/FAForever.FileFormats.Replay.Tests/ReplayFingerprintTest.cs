namespace FAForever.FileFormats.Replay.Tests;

/// <summary>
/// Snapshot tests for the parser output. The parser is performance-tuned; these tests pin the
/// complete parsed result of every asset, so an optimisation cannot silently change what is read.
/// When a change to the output is intended, update the expected fingerprints in the same commit.
/// </summary>
[TestClass]
public class ReplayFingerprintTest
{
    [TestMethod]
    [DataRow("assets/faforever/23225104.fafreplay", "5F6B6D67302D2A6ADC107B91CB1A43A97B5297E199840F3200D368866C83E935")]
    [DataRow("assets/faforever/23225323.fafreplay", "089AC8286C87381CD9541409C309F9F49FAC69B3EE8D257021E049ADD92E386D")]
    [DataRow("assets/faforever/23225440.fafreplay", "259D95EF3F13B29341D502EA471142B0C34B169057294B98AC91F3A9EF1B19E6")]
    [DataRow("assets/faforever/23225508.fafreplay", "707FF369494A81CF02572069252C6BCDBCDA18D02B8FAB38178071D3774E0ADD")]
    [DataRow("assets/faforever/23225685.fafreplay", "652ACFEE91D3B6DD30446B56895F42E274B3C5D1F53880E50A53424DED626C7D")]
    [DataRow("assets/faforever/TestCommands01.fafreplay", "D543F2FB72578B288C41A200E2D7F841EEEA02AA68CEE20F7BFBCB1392286EF5")]
    [DataRow("assets/faforever/mods.fafreplay", "68E1141D35D84F1DEB6E2E9F62AEA0B3C75C578DA542DDAAF20153B2B02A64B1")]
    [DataRow("assets/faforever/gzip/22451957.fafreplay", "53457B6610EE91FFAAA82BE27D7B43968D5298C61613EFE89D0FAD237F008A96")]
    [DataRow("assets/faforever/gzip/22453414.fafreplay", "FBB21C22E324E4349F821F8D2FA99197A814A104CFDB00241B2B9EAB9D2D80F1")]
    [DataRow("assets/faforever/gzip/22453511.fafreplay", "8D34E25EF57A176BBE2D709850E6AA201F22BAB8AE16B5020451F02E17D3E6EE")]
    [DataRow("assets/faforever/gzip/20802415.fafreplay", "3994FCF028C7AC6C5490A94224A368B1AD5A96F8DF2A66973D630CBAC1A51DCF")]
    [DataRow("assets/faforever/zstd/22338092.fafreplay", "ADFB4898D148CED5F09A861655B0D6CA76955170001FB3B21324137B692D1242")]
    [DataRow("assets/faforever/zstd/22373098.fafreplay", "6BBF40C5B4035E8A9A66A2AA84B4B32F643025BD771BCCCD93FEB4DA49543FD8")]
    [DataRow("assets/faforever/zstd/22425616.fafreplay", "79ABAD5833A74251700BC66362182B56C84136139F8D2207BA0F060073D39CFB")]
    [DataRow("assets/scfa/21stGameOceanScampsV2.SCFAReplay", "9270103A971AD04ED1D75B19F71D85B2301237F18D97AE118D0ABF9F9C3E4BB6")]
    [DataRow("assets/scfa/22338092.scfareplay", "ADFB4898D148CED5F09A861655B0D6CA76955170001FB3B21324137B692D1242")]
    [DataRow("assets/scfa/22373098.scfareplay", "6BBF40C5B4035E8A9A66A2AA84B4B32F643025BD771BCCCD93FEB4DA49543FD8")]
    [DataRow("assets/scfa/22425616.scfareplay", "79ABAD5833A74251700BC66362182B56C84136139F8D2207BA0F060073D39CFB")]
    [DataRow("assets/scfa/balthazar-01.SCFAReplay", "FEE4C5EE56514EE80A7A4A385FCC8EE167ED6B6DECE812D7B01CF3E4F5215553")]
    [DataRow("assets/scfa/balthazar-02.SCFAReplay", "9C299709CDC772463CBE9B4F9A89E63A967CACB8265EBC3BF58E8423CD2EDD88")]
    [DataRow("assets/scfa/balthazar-03.SCFAReplay", "51F3968DFB908EED345D65D97913F6B66B41A5541B1F5BCB43A6627234ACBBA7")]
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
    [DataRow("assets/faforever/gzip/20802415.fafreplay")]
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
