namespace FAForever.FileFormats.Replay.Tests;

[TestClass]
public class ReplayEntityIdTest
{
    private static Replay Load(string file)
        => file.EndsWith(".fafreplay") ? ReplayLoader.LoadFAFReplayFromDisk(file) : ReplayLoader.LoadSCFAReplayFromDisk(file);

    [TestMethod]
    [DataRow(0x00000000, ReplayEntityFamily.Unit, 0, 0)]
    [DataRow(0x00300012, ReplayEntityFamily.Unit, 3, 0x12)]
    [DataRow(0x10200001, ReplayEntityFamily.Projectile, 2, 1)]
    [DataRow(0x2FF00ABC, ReplayEntityFamily.Prop, 0xFF, 0xABC)]
    [DataRow(0x30100007, ReplayEntityFamily.ReconBlip, 1, 7)]
    [DataRow(0x40000003, ReplayEntityFamily.Shield, 0, 3)]
    [DataRow(0x50000000, ReplayEntityFamily.ScriptEntity, 0, 0)]
    public void SplitsAnIdIntoItsPartsTest(int entityId, ReplayEntityFamily expectedFamily, int expectedIndex, int expectedSerial)
    {
        Assert.AreEqual(expectedFamily, ReplayEntityId.GetFamily(entityId));
        Assert.AreEqual(expectedIndex, ReplayEntityId.GetIndex(entityId));
        Assert.AreEqual(expectedSerial, ReplayEntityId.GetSerial(entityId));
    }

    /// <summary>
    /// The family of a target: reclaim goes to props (index 0xFF), attacks on enemies go to recon blips
    /// (index: the army of the unit the blip shows), and selections hold only units. The counts were
    /// cross-checked with an independent decoder.
    /// </summary>
    [TestMethod]
    [DataRow("assets/faforever/23225104.fafreplay", 1487, 1130, 1416)]
    [DataRow("assets/faforever/TestCommands01.fafreplay", 600, 760, 426)]
    [DataRow("assets/scfa/balthazar-01.SCFAReplay", 620, 84, 345)]
    public void TargetsAreUnitsPropsOrReconBlipsTest(string file, int expectedUnits, int expectedProps, int expectedBlips)
    {
        Replay replay = Load(file);
        List<(CommandUnits Units, CommandData Data)> orders = replay.Body.UserInput
            .Select(input => input switch
            {
                ReplayInput.IssueCommand command => (command.Units, command.Data),
                ReplayInput.IssueFactoryCommand command => (command.Factories, command.Data),
                _ => (null!, null!),
            })
            .Where(order => order.Data is not null)
            .ToList();

        List<(int EntityId, CommandType Type)> targets = orders
            .Where(order => order.Data.Target is CommandTarget.Entity)
            .Select(order => (((CommandTarget.Entity)order.Data.Target).EntityId, order.Data.Type))
            .ToList();
        Assert.AreEqual(expectedUnits, targets.Count(target => ReplayEntityId.GetFamily(target.EntityId) == ReplayEntityFamily.Unit));
        Assert.AreEqual(expectedProps, targets.Count(target => ReplayEntityId.GetFamily(target.EntityId) == ReplayEntityFamily.Prop));
        Assert.AreEqual(expectedBlips, targets.Count(target => ReplayEntityId.GetFamily(target.EntityId) == ReplayEntityFamily.ReconBlip));
        Assert.AreEqual(expectedUnits + expectedProps + expectedBlips, targets.Count, "No other family is targeted.");

        int armies = replay.Header.Armies.Length;
        Assert.IsTrue(targets.Where(target => ReplayEntityId.GetFamily(target.EntityId) == ReplayEntityFamily.Prop).All(target => ReplayEntityId.GetIndex(target.EntityId) == 0xFF));
        Assert.IsTrue(targets.Where(target => ReplayEntityId.GetFamily(target.EntityId) == ReplayEntityFamily.ReconBlip).All(target => ReplayEntityId.GetIndex(target.EntityId) < armies));
        Assert.IsTrue(targets.Any(target => ReplayEntityId.GetFamily(target.EntityId) == ReplayEntityFamily.Prop && target.Type == CommandType.IssueReclaim));
        Assert.IsTrue(targets.Any(target => ReplayEntityId.GetFamily(target.EntityId) == ReplayEntityFamily.ReconBlip && target.Type == CommandType.IssueAttack));

        foreach ((CommandUnits units, CommandData _) in orders)
        {
            foreach (int id in units.EntityIds.Span)
            {
                Assert.AreEqual(ReplayEntityFamily.Unit, ReplayEntityId.GetFamily(id));
            }
        }
    }

    /// <summary>
    /// ProcessInfoPair names one unit per message (the field holds an entity id).
    /// </summary>
    [TestMethod]
    [DataRow("assets/faforever/23225104.fafreplay")]
    [DataRow("assets/faforever/TestCommands01.fafreplay")]
    public void ProcessInfoPairNamesAUnitTest(string file)
    {
        Replay replay = Load(file);
        List<ReplayInput.ProcessInfoPair> pairs = replay.Body.UserInput.OfType<ReplayInput.ProcessInfoPair>().ToList();

        Assert.IsTrue(pairs.Count > 0);
        Assert.IsTrue(pairs.All(pair => ReplayEntityId.GetFamily(pair.EntityId) == ReplayEntityFamily.Unit));
        Assert.IsTrue(pairs.Select(pair => pair.EntityId).Distinct().Count() > 1);
    }
}
