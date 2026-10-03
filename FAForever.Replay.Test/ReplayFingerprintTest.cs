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
    [DataRow("assets/faforever/23225104.fafreplay", "18FD8CA8E5A61A1BF7B10EDC6D81FBD11D4F721A1433878E935B39C8737DFBB9")]
    [DataRow("assets/faforever/23225323.fafreplay", "823181B73A77ECD68243619B347B932BE6D67D867E003880BC92E4B9F660AAF8")]
    [DataRow("assets/faforever/23225440.fafreplay", "E9C9407E631A77717CFCE263F0E19E395F06E208979361784985DA0B6BA8143D")]
    [DataRow("assets/faforever/23225508.fafreplay", "73FFA36D4F6B2957EF4C4924E26F0A002714B1DE28893B5D3EFB4F35DD41405B")]
    [DataRow("assets/faforever/23225685.fafreplay", "F0B574DBC74914A5621D9C8794C4A909C9DC8B3E8011B7CAD3595B0881728570")]
    [DataRow("assets/faforever/TestCommands01.fafreplay", "A675236D2F749873ED56A13CE6F16E9BB12DC0214E0B4C9FB8F6C3C10C3B0DF6")]
    [DataRow("assets/faforever/mods.fafreplay", "429A89607D21B2B0467B20A8CAFE0B8C8631FF3BFA905CE7E7EBEE9546D95959")]
    [DataRow("assets/faforever/gzip/22451957.fafreplay", "BB91B0865D5300AE2987F6B3498BB9DEE62F04633DB379C769F64EC4B2A40085")]
    [DataRow("assets/faforever/gzip/22453414.fafreplay", "970ED5B0BE3CE81F8D4FE2C329CBC703FE2602887760B1323CB15CA4A1E706A9")]
    [DataRow("assets/faforever/gzip/22453511.fafreplay", "81CFDE832A65F257C76C8D995F9609FB79E324FAC41FFE44EE0F2AB8C76828D8")]
    [DataRow("assets/faforever/zstd/22338092.fafreplay", "33345D1AA8AC81E578ED9E0AE7C062C5E6641C89D1984EDF441D3AA8197B66B5")]
    [DataRow("assets/faforever/zstd/22373098.fafreplay", "A62665071B1CC78E794AC258C7D3AF2660A4A48F3881C2C93EC00B0E1C302DFC")]
    [DataRow("assets/faforever/zstd/22425616.fafreplay", "FB1235A1DDB436D2B918FFAB5F809D3B3BA70D9D438B966BC1F8A7DDAE15B07B")]
    [DataRow("assets/scfa/21stGameOceanScampsV2.SCFAReplay", "9BC2B55F089FC13BA3FD046BB6F7C2A91B8B7823B5CDD1D41539129A17244E69")]
    [DataRow("assets/scfa/22338092.scfareplay", "33345D1AA8AC81E578ED9E0AE7C062C5E6641C89D1984EDF441D3AA8197B66B5")]
    [DataRow("assets/scfa/22373098.scfareplay", "A62665071B1CC78E794AC258C7D3AF2660A4A48F3881C2C93EC00B0E1C302DFC")]
    [DataRow("assets/scfa/22425616.scfareplay", "FB1235A1DDB436D2B918FFAB5F809D3B3BA70D9D438B966BC1F8A7DDAE15B07B")]
    [DataRow("assets/scfa/balthazar-01.SCFAReplay", "EEE31310F97C47DB71F2576CAA8E112710B60129F0212D58521BAE6A4C25A5A3")]
    [DataRow("assets/scfa/balthazar-02.SCFAReplay", "B344DBFB9A778B4103F49491B46ECE9238FF46B69447A75E6C2051605DBACA1E")]
    [DataRow("assets/scfa/balthazar-03.SCFAReplay", "4E652E2F631D8FF7BFCF7512FD13A3BF345A085060E632190F294F3B75D3701A")]
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
