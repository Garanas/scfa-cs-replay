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
    [DataRow("assets/faforever/23225104.fafreplay", "B49064A3284799AA8341BC03221FC211C7E45EAD8399321F4FA95DD4E08A453B")]
    [DataRow("assets/faforever/23225323.fafreplay", "F145E7A4734284C820C8293F9660FF0B0F1F03846DA6DF0B98EB0F87957C6A9F")]
    [DataRow("assets/faforever/23225440.fafreplay", "B1D59BA521CAF711E20F3B2FC5AF0966F930AA474F06A59EC85EC2BAFF5E1F0D")]
    [DataRow("assets/faforever/23225508.fafreplay", "38DF64FA5A6F52B2FB38D32B30B759E9267B16BFCA893DA4A89ECC475CA478B4")]
    [DataRow("assets/faforever/23225685.fafreplay", "F14CEEA86896C79BFE23A40A8561FC9344E9B59355CD21D79DE3CBC4B7222523")]
    [DataRow("assets/faforever/TestCommands01.fafreplay", "240F6C58D93C255890CBEDD913DF1FECDD9161710958CFDE23D174E00275E5B8")]
    [DataRow("assets/faforever/mods.fafreplay", "601FD06809974C3E7A1A78693E1796950C5A05D39691A16A2E12D0D0428B839F")]
    [DataRow("assets/faforever/gzip/22451957.fafreplay", "367028E12341D7469640B5F433FDDFE55FB4C50F510CD903C76B7AACFC740BFB")]
    [DataRow("assets/faforever/gzip/22453414.fafreplay", "6B2E2BF1E8EE7C98FB6493A93C748351C2CCF5DFEE15816FA15BBAC7B27B5FF8")]
    [DataRow("assets/faforever/gzip/22453511.fafreplay", "2F8D940FA08B62191F41F1707125EECE91C3B7D19797399274D9238B07B2AA98")]
    [DataRow("assets/faforever/zstd/22338092.fafreplay", "56D0B11FCE251066BE23966E563C1EACA5B8865A40DC42D49A5FADE47B2D69C1")]
    [DataRow("assets/faforever/zstd/22373098.fafreplay", "7FCB61677C2507150BBB119BC98DE5EA6D80AC90D11478D36E6C234F2EF78C97")]
    [DataRow("assets/faforever/zstd/22425616.fafreplay", "1A067C2F45018049CDB0E51CC918C9CA06958DB886EDC1C246C3F147886B4B10")]
    [DataRow("assets/scfa/21stGameOceanScampsV2.SCFAReplay", "5AC0420366C12A0012EE1181EDA303DF9BB2F67BC415E876B22713D2084F7197")]
    [DataRow("assets/scfa/22338092.scfareplay", "56D0B11FCE251066BE23966E563C1EACA5B8865A40DC42D49A5FADE47B2D69C1")]
    [DataRow("assets/scfa/22373098.scfareplay", "7FCB61677C2507150BBB119BC98DE5EA6D80AC90D11478D36E6C234F2EF78C97")]
    [DataRow("assets/scfa/22425616.scfareplay", "1A067C2F45018049CDB0E51CC918C9CA06958DB886EDC1C246C3F147886B4B10")]
    [DataRow("assets/scfa/balthazar-01.SCFAReplay", "C2A3715C6FC89A045CF71BACB0EF1DDE7297FB31448B442C643B50C6C964BEC7")]
    [DataRow("assets/scfa/balthazar-02.SCFAReplay", "CAD7C4A7DEA86288B55DDE685CBA1C1B4849539A298C8EE7C7867CE5500CE074")]
    [DataRow("assets/scfa/balthazar-03.SCFAReplay", "12A3CFFD4826DA3BCEAE34928953D2497519310D1ADC651F4E11AD4C75E272A5")]
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
