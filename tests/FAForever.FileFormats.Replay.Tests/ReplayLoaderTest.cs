using FAForever.FileFormats.Lua;

namespace FAForever.FileFormats.Replay.Tests;

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
    [DataRow("assets/faforever/zstd/22338092.fafreplay")]
    [DataRow("assets/faforever/zstd/22373098.fafreplay")]
    [DataRow("assets/faforever/gzip/22451957.fafreplay")]
    [DataRow("assets/faforever/gzip/22453414.fafreplay")]
    [DataRow("assets/faforever/mods.fafreplay")]
    [DataRow("assets/faforever/TestCommands01.fafreplay")]
    [DataRow("assets/faforever/23225104.fafreplay")]
    [DataRow("assets/faforever/23225440.fafreplay")]
    public void MapPathFromStartTest(string file)
    {
        byte[] bytes = File.ReadAllBytes(file);
        string expected = ReplayLoader.LoadFAFReplayFromDisk(file).Header.MapPath;

        // the size the Viewer asks for first, and the whole file
        Assert.AreEqual(expected, ReplayLoader.TryReadMapPath(bytes.AsSpan(0, Math.Min(bytes.Length, 64 * 1024))));
        Assert.AreEqual(expected, ReplayLoader.TryReadMapPath(bytes));
    }

    [TestMethod]
    [DataRow("assets/faforever/23225104.fafreplay")]
    [DataRow("assets/faforever/gzip/22451957.fafreplay")]
    public void MapPathFromTooShortStartTest(string file)
    {
        byte[] bytes = File.ReadAllBytes(file);
        int endOfMetadata = Array.IndexOf(bytes, (byte)'\n');

        Assert.IsNull(ReplayLoader.TryReadMapPath(bytes.AsSpan(0, endOfMetadata)));
        Assert.IsNull(ReplayLoader.TryReadMapPath(bytes.AsSpan(0, endOfMetadata + 8)));
        Assert.IsNull(ReplayLoader.TryReadMapPath("not a replay\n"u8));
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

        // Walk the inputs (type byte, uint16 length including those 3 bytes) to the second checksum
        // of a tick past the middle: VerifyChecksum = 16 bytes of hash, then the int32 tick.
        const int halfway = 10_000; // ticks; this game lasts about 38,000
        int? previousTick = null;
        int changed = -1;
        while (position < bytes.Length && changed < 0)
        {
            byte type = bytes[position];
            int length = BitConverter.ToUInt16(bytes, position + 1);
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
    /// <summary>
    /// WarpEntity (TeleportSelectedUnits, cheats) carries an entity id and a VTransform: a quaternion
    /// (w, x, y, z), then a position. Expected values from an independent decoder written to the
    /// engine's layout. The replay is a sandbox game of the owner's (vault id 20802415).
    /// </summary>
    [TestMethod]
    [DataRow("assets/faforever/gzip/20802415.fafreplay", 4, 88, 125.0668f, 17.8906f, 190.5380f)]
    public void WarpEntityTest(string file, int expectedWarps, int expectedFirstTick, float expectedX, float expectedY, float expectedZ)
    {
        Replay replay = ReplayLoader.LoadFAFReplayFromDisk(file);

        List<ReplayInput.WarpEntity> warps = replay.Body.UserInput.OfType<ReplayInput.WarpEntity>().ToList();
        Assert.AreEqual(expectedWarps, warps.Count);
        Assert.IsFalse(replay.Body.UserInput.Any(input => input is ReplayInput.Error or ReplayInput.Unknown));

        ReplayInput.WarpEntity first = warps[0];
        Assert.AreEqual(expectedFirstTick, first.Tick);
        Assert.AreEqual(0x00100000, first.EntityId, "The commander of the second army.");
        Assert.AreEqual(0.8803f, first.Orientation.W, 0.0001f);
        Assert.AreEqual(0f, first.Orientation.X, 0.0001f);
        Assert.AreEqual(0.4745f, first.Orientation.Y, 0.0001f);
        Assert.AreEqual(0f, first.Orientation.Z, 0.0001f);
        Assert.AreEqual(expectedX, first.X, 0.001f);
        Assert.AreEqual(expectedY, first.Y, 0.001f);
        Assert.AreEqual(expectedZ, first.Z, 0.001f);

        // the game goes on after the warps: its last message is the end of the game
        Assert.IsInstanceOfType<ReplayInput.EndGame>(replay.Body.UserInput[^1]);
    }

    /// <summary>
    /// Every message of every asset decodes to exactly its framed length: none is reported as an
    /// error or of an unknown type.
    /// </summary>
    [TestMethod]
    [DataRow("assets/faforever/23225104.fafreplay")]
    [DataRow("assets/faforever/23225323.fafreplay")]
    [DataRow("assets/faforever/23225440.fafreplay")]
    [DataRow("assets/faforever/23225508.fafreplay")]
    [DataRow("assets/faforever/23225685.fafreplay")]
    [DataRow("assets/faforever/TestCommands01.fafreplay")]
    [DataRow("assets/faforever/mods.fafreplay")]
    [DataRow("assets/faforever/gzip/20802415.fafreplay")]
    [DataRow("assets/faforever/gzip/22451957.fafreplay")]
    [DataRow("assets/faforever/zstd/22373098.fafreplay")]
    [DataRow("assets/scfa/21stGameOceanScampsV2.SCFAReplay")]
    [DataRow("assets/scfa/balthazar-01.SCFAReplay")]
    [DataRow("assets/scfa/balthazar-02.SCFAReplay")]
    [DataRow("assets/scfa/balthazar-03.SCFAReplay")]
    public void EveryMessageDecodesToItsLengthTest(string file)
    {
        Replay replay = file.EndsWith(".fafreplay") ? ReplayLoader.LoadFAFReplayFromDisk(file) : ReplayLoader.LoadSCFAReplayFromDisk(file);

        ReplayInput? problem = replay.Body.UserInput.FirstOrDefault(input => input is ReplayInput.Error or ReplayInput.Unknown);
        Assert.IsNull(problem, problem is null ? null : ReplayInputFormatter.Describe(problem));
    }

    /// <summary>
    /// SetCommandCells (op 18): a command id, an int32 count, that many (int16 x, int16 z) cells,
    /// then a position. No asset holds one, so the message is made by hand.
    /// </summary>
    [TestMethod]
    public void SetCommandCellsTest()
    {
        byte[] content = ReplayStreamBuilder.Content(0x00000005, 2, (short)10, (short)-3, (short)11, (short)-4, 100f, 20f, 300f);
        Replay replay = new ReplayStreamBuilder().Header([0])
            .Advance().Message((byte)ReplayInputType.SetCommandCells, content).Advance().EndGame().Load();

        ReplayInput.SetCommandCells cells = replay.Body.UserInput.OfType<ReplayInput.SetCommandCells>().Single();
        Assert.AreEqual(5, cells.CommandId);
        CollectionAssert.AreEqual(new[] { new CommandCell(10, -3), new CommandCell(11, -4) }, cells.Cells.ToArray());
        Assert.AreEqual((100f, 20f, 300f), (cells.X, cells.Y, cells.Z));
        Assert.AreEqual(1, cells.Tick);
        Assert.AreEqual(2, replay.Body.UserInput.OfType<ReplayInput.EndGame>().Single().Tick);
    }

    /// <summary>
    /// ExecuteLuaInSim (op 21): the name of a sim function, then its Lua argument.
    /// </summary>
    [TestMethod]
    public void ExecuteLuaInSimTest()
    {
        // ExecLuaInSim("SetArmyStats", { Army = 1 })
        byte[] argument = [4, 1, .. "Army"u8, 0, 0, .. BitConverter.GetBytes(1f), 5];
        Replay replay = new ReplayStreamBuilder().Header([0])
            .Message((byte)ReplayInputType.ExecuteLuaInSim, ReplayStreamBuilder.Content("SetArmyStats", argument)).Advance().EndGame().Load();

        ReplayInput.ExecuteLuaInSim lua = replay.Body.UserInput.OfType<ReplayInput.ExecuteLuaInSim>().Single();
        Assert.AreEqual("SetArmyStats", lua.FunctionName);
        Assert.IsInstanceOfType<LuaData.Table>(lua.Arguments);
        Assert.IsTrue(((LuaData.Table)lua.Arguments).TryGetNumberValue("Army", out double? army));
        Assert.AreEqual(1.0, army);
        Assert.AreEqual(1, replay.Body.UserInput.OfType<ReplayInput.EndGame>().Single().Tick);
    }

    /// <summary>
    /// An order with every field the engine knows filled in: a coordinating command, an insert
    /// position, a second target, a formation, cells and counts. Real orders leave the second target
    /// and the cell list empty; a reader that steps over them by a fixed size would lose its place.
    /// </summary>
    [TestMethod]
    public void IssueCommandWithEveryFieldTest()
    {
        byte[] content = ReplayStreamBuilder.Content(
            1, 0x00000003,                                // the selection: one unit, entity 3
            0x00000041, 0x00000040,                       // command id, coordinating command id
            (byte)CommandType.IssueAttack, 0x00000039,    // type, insert before command id
            (byte)2, 1f, 2f, 3f,                          // target: a position
            (byte)1, 0x30100007,                          // second target: an entity (a recon blip)
            1, 0.8f, 0f, 0.6f, 0f, 1.5f,                  // formation: script index, quaternion, scale
            "",                                           // no blueprint
            1, (short)7, (short)8,                        // one cell
            4, 2,                                         // max count, count
            (byte)2,                                      // Lua: nil
            (byte)0);                                     // queued
        Replay replay = new ReplayStreamBuilder().Header([0])
            .Message((byte)ReplayInputType.IssueCommand, content).Advance().EndGame().Load();

        CommandData data = replay.Body.UserInput.OfType<ReplayInput.IssueCommand>().Single().Data;
        Assert.AreEqual(0x41, data.Identifier);
        Assert.AreEqual(0x40, data.CoordinatingCommandId);
        Assert.AreEqual(0x39, data.InsertBeforeCommandId);
        Assert.AreEqual(new CommandTarget.Position(1f, 2f, 3f), data.Target);
        Assert.AreEqual(new CommandTarget.Entity(0x30100007), data.SecondTarget);
        CommandFormation.Formation formation = (CommandFormation.Formation)data.Formation;
        Assert.AreEqual(1, formation.ScriptIndex);
        Assert.AreEqual(new ReplayOrientation(0.8f, 0f, 0.6f, 0f), formation.Orientation);
        Assert.AreEqual(1.5f, formation.Scale);
        CollectionAssert.AreEqual(new[] { new CommandCell(7, 8) }, data.Cells.ToArray());
        Assert.AreEqual((4, 2), (data.MaxCount, data.Count));
        Assert.IsFalse(data.ClearQueue);
        Assert.AreEqual(1, replay.Body.UserInput.OfType<ReplayInput.EndGame>().Single().Tick);
    }

    /// <summary>
    /// A message that does not decode to its length is reported as an error and stepped over by its
    /// length; it has no effect (the Advance in error does not advance the tick) and the messages
    /// after it read on. Here: a WarpEntity framed in a shorter layout (three floats), and an
    /// Advance with two bytes too many.
    /// </summary>
    [TestMethod]
    public void MessageOfTheWrongLengthIsReportedAndSteppedOverTest()
    {
        byte[] shortWarp = ReplayStreamBuilder.Content(0x00100000, 1f, 2f, 3f);
        Replay replay = new ReplayStreamBuilder().Header([0])
            .Advance()
            .Message((byte)ReplayInputType.WarpEntity, shortWarp)
            .Message((byte)ReplayInputType.Advance, [1, 0, 0, 0, 0, 0])
            .Advance()
            .Message((byte)ReplayInputType.RequestPause, [])
            .EndGame().Load();

        List<ReplayInput.Error> errors = replay.Body.UserInput.OfType<ReplayInput.Error>().ToList();
        Assert.AreEqual(2, errors.Count);
        Assert.AreEqual(ReplayInputType.WarpEntity, errors[0].Type);
        CollectionAssert.AreEqual(shortWarp, errors[0].Data);
        Assert.AreEqual(ReplayInputType.Advance, errors[1].Type);
        Assert.AreEqual(1, errors[1].Tick);
        Assert.AreEqual(2, replay.Body.UserInput.OfType<ReplayInput.RequestPause>().Single().Tick, "The Advance in error has no effect.");
        Assert.IsInstanceOfType<ReplayInput.EndGame>(replay.Body.UserInput[^1]);
    }

    /// <summary>
    /// A message of a type the engine does not know is kept with its content and stepped over.
    /// </summary>
    [TestMethod]
    public void UnknownMessageIsSteppedOverTest()
    {
        Replay replay = new ReplayStreamBuilder().Header([0])
            .Advance()
            .Message(42, [1, 2, 3, 4, 5])
            .Advance()
            .EndGame().Load();

        ReplayInput.Unknown unknown = replay.Body.UserInput.OfType<ReplayInput.Unknown>().Single();
        Assert.AreEqual((ReplayInputType)42, unknown.Type);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, unknown.Data);
        Assert.AreEqual(2, replay.Body.UserInput.OfType<ReplayInput.EndGame>().Single().Tick);
    }

    /// <summary>
    /// A replay cut off inside its last message (a recorder that stopped mid-write) loads, with the
    /// remainder reported as an error.
    /// </summary>
    [TestMethod]
    public void MessageCutOffByTheEndOfTheStreamIsReportedTest()
    {
        Replay replay = new ReplayStreamBuilder().Header([0])
            .Advance()
            .Message((byte)ReplayInputType.RequestPause, [])
            .Bytes([(byte)ReplayInputType.Advance, 7, 0, 1])
            .Load();

        Assert.IsInstanceOfType<ReplayInput.RequestPause>(replay.Body.UserInput[0]);
        ReplayInput.Error error = (ReplayInput.Error)replay.Body.UserInput[^1];
        Assert.AreEqual(ReplayInputType.Advance, error.Type);
        CollectionAssert.AreEqual(new byte[] { 1 }, error.Data);
    }

    /// <summary>
    /// The staged API (as the Viewer uses it) reports a message in error exactly as a one-shot load
    /// does, also when the error falls on a batch boundary.
    /// </summary>
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void StagedLoadingReportsErrorsTest(int batchSize)
    {
        ReplayStreamBuilder builder = new ReplayStreamBuilder().Header([0])
            .Advance()
            .Message((byte)ReplayInputType.WarpEntity, ReplayStreamBuilder.Content(0x00100000, 1f, 2f, 3f))
            .Message(42, [9])
            .Advance()
            .EndGame();
        Replay replay = builder.Load();

        ReplayLoadingStage stage = new ReplayLoadingStage.Decompressed(new MemoryStream(builder.ToArray()), null);
        while (stage is not ReplayLoadingStage.Complete)
        {
            stage = stage switch
            {
                ReplayLoadingStage.Decompressed decompressed => ReplayLoader.ProcessReplayStage(decompressed),
                ReplayLoadingStage.WithScenario withScenario => ReplayLoader.ProcessReplayStage(withScenario, batchSize),
                ReplayLoadingStage.AtInput atInput => ReplayLoader.ProcessReplayStage(atInput, batchSize),
                _ => throw new AssertFailedException($"Unexpected stage {stage.GetType().Name}"),
            };
        }

        ReplayBody staged = ((ReplayLoadingStage.Complete)stage).Body;
        Assert.AreEqual(ReplayFingerprint.Compute(replay.Header, replay.Body), ReplayFingerprint.Compute(replay.Header, staged));
        Assert.AreEqual(1, staged.UserInput.OfType<ReplayInput.Error>().Count());
        Assert.AreEqual(1, staged.UserInput.OfType<ReplayInput.Unknown>().Count());
    }
}
