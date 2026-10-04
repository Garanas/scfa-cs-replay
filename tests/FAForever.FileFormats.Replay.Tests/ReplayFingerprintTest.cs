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
    [DataRow("assets/faforever/23225104.fafreplay", "5B9904AD2C9E72B592279EBD523DC204FF89A40B95E2C12BBEEC04E3B9461576")]
    [DataRow("assets/faforever/23225323.fafreplay", "7B5FDF1DFAF64EF71F5A9DE87912BD6FB7C2431B31E876304D1B0CB6017D7F87")]
    [DataRow("assets/faforever/23225440.fafreplay", "7B20C430D119108FCC1C7FE17BC4B08E048935789827795C5984859090DC156D")]
    [DataRow("assets/faforever/23225508.fafreplay", "CC2844A19BE0632C73A79BF8C578F201A9A550EB012B0EA7F68355ABE39506B8")]
    [DataRow("assets/faforever/23225685.fafreplay", "E3A9D99B013D21BDE68186534E6DF3097DAEE83DD2A95C38544C5008F5E740FB")]
    [DataRow("assets/faforever/TestCommands01.fafreplay", "C9B9524DD526906A4B448CDCE006994365CA23ABD7D89F8C4B7B19769BC8BA2F")]
    [DataRow("assets/faforever/mods.fafreplay", "67D05834E8735DD9CAD3BE7964C185BECF962A84B3663C20BAAA0DC108EF0F20")]
    [DataRow("assets/faforever/gzip/22451957.fafreplay", "BB91B0865D5300AE2987F6B3498BB9DEE62F04633DB379C769F64EC4B2A40085")]
    [DataRow("assets/faforever/gzip/22453414.fafreplay", "970ED5B0BE3CE81F8D4FE2C329CBC703FE2602887760B1323CB15CA4A1E706A9")]
    [DataRow("assets/faforever/gzip/22453511.fafreplay", "2E63F947EE4CE90B245EF5E70EE53FE514583261DCEFED114BF3C7736DA04ECA")]
    [DataRow("assets/faforever/zstd/22338092.fafreplay", "735E6EE463AF2CDCAA297133C6A67D7B7FD3959A71071F82E8E181305ACB71D3")]
    [DataRow("assets/faforever/zstd/22373098.fafreplay", "DD2CEA02EA0CF62D40174BACFD70124E546DA698DEEB5EE4B362CCEC16580115")]
    [DataRow("assets/faforever/zstd/22425616.fafreplay", "FB1235A1DDB436D2B918FFAB5F809D3B3BA70D9D438B966BC1F8A7DDAE15B07B")]
    [DataRow("assets/scfa/21stGameOceanScampsV2.SCFAReplay", "CDB56488670ED4849E74D30090DDD68CD0E1E7481DD188D07AB0664C2BA27B7E")]
    [DataRow("assets/scfa/22338092.scfareplay", "735E6EE463AF2CDCAA297133C6A67D7B7FD3959A71071F82E8E181305ACB71D3")]
    [DataRow("assets/scfa/22373098.scfareplay", "DD2CEA02EA0CF62D40174BACFD70124E546DA698DEEB5EE4B362CCEC16580115")]
    [DataRow("assets/scfa/22425616.scfareplay", "FB1235A1DDB436D2B918FFAB5F809D3B3BA70D9D438B966BC1F8A7DDAE15B07B")]
    [DataRow("assets/scfa/balthazar-01.SCFAReplay", "DCA30C9DEC86CB4476FBF6A89DFE033FC800902268F83A3BEC8423E10BB28819")]
    [DataRow("assets/scfa/balthazar-02.SCFAReplay", "2AC2F5D21DE8782859B246DA6679F99ACF17A222C06171CAE25F9D26A7344B48")]
    [DataRow("assets/scfa/balthazar-03.SCFAReplay", "312B78DEAFED0B57EDECF46FD88C1D668A953BF3433279C636AE978D84A3BACF")]
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
