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
    [DataRow("assets/faforever/23225104.fafreplay", "4520A1AB0161B0E3FFD84F4B0F66CFFC7CC4737B272805687704F48AC930025A")]
    [DataRow("assets/faforever/23225323.fafreplay", "A07E7867427BEB7116F662DB0A180787DA64BED35DBDF108D2C30498EFD1013B")]
    [DataRow("assets/faforever/23225440.fafreplay", "F56464C00BC3BA24D643AA6F8A33239D826F9B67A9CE4581317A68ECA97010EB")]
    [DataRow("assets/faforever/23225508.fafreplay", "03C06EAB89C7AF59C72D34D980224B37D84511AE160E417029FA1AF4D53ADFE9")]
    [DataRow("assets/faforever/23225685.fafreplay", "42726A82D5BE95CD728071080F7DF81769BEF3B695D1C3E9ADDA3FDED7F23C1D")]
    [DataRow("assets/faforever/TestCommands01.fafreplay", "7073BFF4A9854EE0AF997783EC889CF632D5ECE970802DD8AD3FFD96966A043D")]
    [DataRow("assets/faforever/mods.fafreplay", "D5ECBB50E76DF183F25FF7C8E1D7E83164272AAA966044D3EEB8C9896DBC13B4")]
    [DataRow("assets/faforever/gzip/22451957.fafreplay", "2B06C2BD424734426311BA2AF58F0B4ED9DA34D8BB060E6A5BC3462CE78F886F")]
    [DataRow("assets/faforever/gzip/22453414.fafreplay", "360AE549689D0BB416042358F677955D8A92CD2F62616F008494EAD09A4A2360")]
    [DataRow("assets/faforever/gzip/22453511.fafreplay", "EACFEBF53520028CAA9A912B14515ABBE9310FEF7666068E53C14A2B9C4B438C")]
    [DataRow("assets/faforever/zstd/22338092.fafreplay", "E47EE9C510A787F0A7D1C963BAA2183F876C523EB561E71B124A171E4746B4D4")]
    [DataRow("assets/faforever/zstd/22373098.fafreplay", "40AFE355CE3F0823FF118693AA5251DF4D9BF9B4F14EF92FACFD4987689C8635")]
    [DataRow("assets/faforever/zstd/22425616.fafreplay", "8E40463BB5EBEFF1DF3F54B8B8D1F17159AA9A471F43DDED58874BF3C2D2DE09")]
    [DataRow("assets/scfa/21stGameOceanScampsV2.SCFAReplay", "6E592FF92834DE72C3A95BF45A27C80F1063857B982ECF49898237FC61023C74")]
    [DataRow("assets/scfa/22338092.scfareplay", "E47EE9C510A787F0A7D1C963BAA2183F876C523EB561E71B124A171E4746B4D4")]
    [DataRow("assets/scfa/22373098.scfareplay", "40AFE355CE3F0823FF118693AA5251DF4D9BF9B4F14EF92FACFD4987689C8635")]
    [DataRow("assets/scfa/22425616.scfareplay", "8E40463BB5EBEFF1DF3F54B8B8D1F17159AA9A471F43DDED58874BF3C2D2DE09")]
    [DataRow("assets/scfa/balthazar-01.SCFAReplay", "1A5D381C46E857A414AEA7E3DB3880A7DF447716BF9B99CD943E4ABF1DB04966")]
    [DataRow("assets/scfa/balthazar-02.SCFAReplay", "4A1EC700D41992AE69719E3C5BF5E2FA140764129285CE43835D90377C204870")]
    [DataRow("assets/scfa/balthazar-03.SCFAReplay", "AC1CCFDFC77BB8B086D1BD008C9225F205E1E873317944D16004F66E1503ACC8")]
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
