
using System.Text;
using ZstdSharp;

using System.Text.Json;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using FAForever.FileFormats.Lua;

namespace FAForever.FileFormats.Replay
{
    public enum ReplayCompression { Gzip, Zstd }

    public static class ReplayLoader
    {
        // Commands are immutable records, so the values without (or with little) content can be shared.
        private static readonly CommandTarget.None NoTarget = new CommandTarget.None();
        private static readonly CommandFormation.NoFormation NoFormation = new CommandFormation.NoFormation();

        /// <summary>
        /// Retrieves a command from the stream.
        /// </summary>
        private static CommandData LoadCommandData(ReplayBinaryReader reader)
        {
            int commandId = reader.ReadInt32();

            // unknown
            int arg1 = reader.ReadInt32();

            CommandType commandType = (CommandType)reader.ReadByte();

            // unknown
            int arg2 = reader.ReadInt32();

            CommandTarget target = LoadCommandTarget(reader);

            // unknown
            byte arg3 = reader.ReadByte();

            CommandFormation formation = LoadCommandFormation(reader);

            string blueprintId = reader.ReadNullTerminatedString();

            // unknown
            int arg4 = reader.ReadInt32();
            int arg5 = reader.ReadInt32();
            int arg6 = reader.ReadInt32();

            LuaData luaData = LuaDataLoader.ReadLuaData(reader);

            // 1 = the order replaces the queue, 0 = queued after the current orders (shift).
            bool clearQueue = reader.ReadByte() > 0;

            return new CommandData(commandId, commandType, target, formation, blueprintId, luaData, clearQueue, arg1, arg2, arg3, arg4, arg5, arg6);
        }

        /// <summary>
        /// Retrieves the selection of a command from the stream.
        /// </summary>
        private static CommandUnits LoadCommandUnits(ReplayBinaryReader reader, EntityIdBuffer entityIds, int source)
        {
            // The ids link the orders given to the same unit; see EntityIdBuffer for how they are stored.
            return entityIds.Read(reader, source);
        }

        /// <summary>
        /// Retrieves the target of a command from the stream.
        /// </summary>
        private static CommandTarget LoadCommandTarget(ReplayBinaryReader reader)
        {
            CommandTargetType eventCommandTargetType = (CommandTargetType)reader.ReadByte();
            switch (eventCommandTargetType)
            {
                case CommandTargetType.Entity:
                    {
                        int entityId = reader.ReadInt32();
                        return new CommandTarget.Entity(entityId);
                    }

                case CommandTargetType.Position:
                    {
                        float x = reader.ReadSingle();
                        float y = reader.ReadSingle();
                        float z = reader.ReadSingle();
                        return new CommandTarget.Position(x, y, z);
                    }

                default:
                    return NoTarget;
            }
        }

        /// <summary>
        /// Retrieves the formation of a command from the stream.
        /// </summary>
        private static CommandFormation LoadCommandFormation(ReplayBinaryReader reader)
        {
            int formationId = reader.ReadInt32();
            if (formationId == -1)
            {
                return NoFormation;
            }

            float heading = reader.ReadSingle();
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            float scale = reader.ReadSingle();

            return new CommandFormation.Formation(formationId, heading, x, y, z, scale);
        }

        /// <summary>
        /// Loads all the user input up to the threshold. If no threshold is defined then all input is loaded by default.
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="invariant"></param>
        /// <param name="inputToProcess"></param>
        private static ReplayBodyInvariant LoadReplayInputs(ReplayBinaryReader reader, ReplayBodyInvariant? invariant, int? inputToProcess)
        {
            // setup the locals based on the (optional) invariant
            int estimatedNumberOfInputsRemaining = (int)(20 * Math.Sqrt(reader.BaseStream.Length - reader.BaseStream.Position));
            var (input, tick, source, inSync, hashTick, hashValue, _, startingPointOfStream, _) = invariant ?? new ReplayBodyInvariant(
                input: new List<ReplayInput>(estimatedNumberOfInputsRemaining),
                tick: 0,
                source: 0,
                inSync: true,
                // No checksum seen yet; -1 so the first one, at tick 0, starts a new tick.
                hashTick: -1,
                hashValue: 0,
                endOfStream: false,
                startingPointOfStream: reader.BaseStream.Position,
                percentageProcessed: 0
            );

            int inputProcessed = 0;
            EntityIdBuffer entityIds = invariant?.EntityIds ?? new EntityIdBuffer();
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                ReplayInputType type = (ReplayInputType)reader.ReadByte();
                // includes the type and this number of bytes, which is a bit confusing. 
                int numberOfBytes = reader.ReadInt16() - (1 + 2);

                switch (type)
                {
                    case ReplayInputType.Advance:
                        {
                            int ticksToAdvance = reader.ReadInt32();
                            tick += ticksToAdvance;
                            break;
                        }

                    case ReplayInputType.SetCommandSource:
                        {
                            int sourceId = reader.ReadByte();
                            source = sourceId;
                            break;
                        }

                    case ReplayInputType.CommandSourceTerminated:
                        input.Add(new ReplayInput.CommandSourceTerminated(tick, source));
                        break;

                    case ReplayInputType.VerifyChecksum:
                        {
                            // Every client records its checksum of the same tick (every 50 ticks); the
                            // game is in sync while they agree. The first one of a tick is the
                            // reference, and once they disagree the replay stays out of sync.
                            long hash = reader.ReadInt64() ^ reader.ReadInt64();
                            int atTick = reader.ReadInt32();
                            if (hashTick != atTick)
                            {
                                hashTick = atTick;
                                hashValue = hash;
                            }
                            else if (hashValue != hash)
                            {
                                inSync = false;
                            }
                            break;
                        }

                    case ReplayInputType.RequestPause:
                        input.Add(new ReplayInput.RequestPause(tick, source));
                        break;

                    case ReplayInputType.RequestResume:
                        input.Add(new ReplayInput.RequestResume(tick, source));
                        break;

                    case ReplayInputType.SingleStep:
                        input.Add(new ReplayInput.SingleStep(tick, source));
                        break;

                    case ReplayInputType.CreateUnit:
                        {
                            int armyId = reader.ReadByte();
                            string blueprintId = reader.ReadNullTerminatedString();
                            float x = reader.ReadSingle();
                            float z = reader.ReadSingle();
                            float heading = reader.ReadSingle();
                            input.Add(new ReplayInput.CreateUnit(tick, source, armyId, blueprintId, x, z, heading));
                            break;
                        }

                    case ReplayInputType.CreateProp:
                        {
                            string blueprintId = reader.ReadNullTerminatedString();
                            float x = reader.ReadSingle();
                            float z = reader.ReadSingle();
                            float heading = reader.ReadSingle();
                            input.Add(new ReplayInput.CreateProp(tick, source, blueprintId, x, z, heading));
                            break;
                        }

                    case ReplayInputType.DestroyEntity:
                        {
                            int entityId = reader.ReadInt32();
                            input.Add(new ReplayInput.DestroyEntity(tick, source, entityId));
                            break;
                        }

                    case ReplayInputType.WarpEntity:
                        {
                            int entityId = reader.ReadInt32();
                            float x = reader.ReadSingle();
                            float y = reader.ReadSingle();
                            float z = reader.ReadSingle();
                            input.Add(new ReplayInput.WarpEntity(tick, source, entityId, x, y, z));
                            break;
                        }

                    case ReplayInputType.ProcessInfoPair:
                        {
                            int entityId = reader.ReadInt32();
                            string name = reader.ReadNullTerminatedString();
                            string value = reader.ReadNullTerminatedString();
                            input.Add(new ReplayInput.ProcessInfoPair(tick, source, entityId, name, value));
                            break;
                        }

                    case ReplayInputType.IssueCommand:
                        {
                            CommandUnits units = LoadCommandUnits(reader, entityIds, source);
                            CommandData data = LoadCommandData(reader);
                            input.Add(new ReplayInput.IssueCommand(tick, source, units, data));
                            break;
                        }

                    case ReplayInputType.IssueFactoryCommand:
                        {
                            CommandUnits factories = LoadCommandUnits(reader, entityIds, source);
                            CommandData data = LoadCommandData(reader);
                            input.Add(new ReplayInput.IssueFactoryCommand(tick, source, factories, data));
                            break;
                        }

                    case ReplayInputType.IncreaseCommandCount:
                        {
                            int commandId = reader.ReadInt32();
                            int delta = reader.ReadInt32();
                            input.Add(new ReplayInput.IncreaseCommandCount(tick, source, commandId, delta));
                            break;
                        }

                    case ReplayInputType.DecreaseCommandCount:
                        {
                            int commandId = reader.ReadInt32();
                            int delta = reader.ReadInt32();
                            input.Add(new ReplayInput.DecreaseCommandCount(tick, source, commandId, delta));
                            break;
                        }

                    case ReplayInputType.UpdateCommandTarget:
                        {
                            int commandId = reader.ReadInt32();
                            CommandTarget target = LoadCommandTarget(reader);
                            input.Add(new ReplayInput.UpdateCommandTarget(tick, source, commandId, target));
                            break;
                        }

                    case ReplayInputType.UpdateCommandType:
                        {
                            int commandId = reader.ReadInt32();
                            CommandType commandType = (CommandType)reader.ReadInt32();
                            input.Add(new ReplayInput.UpdateCommandType(tick, source, commandId, commandType));
                            break;
                        }

                    case ReplayInputType.UpdateCommandParameters:
                        {
                            int commandId = reader.ReadInt32();
                            LuaData luaParameters = LuaDataLoader.ReadLuaData(reader);
                            float x = reader.ReadSingle();
                            float y = reader.ReadSingle();
                            float z = reader.ReadSingle();
                            input.Add(new ReplayInput.UpdateCommandLuaParameters(tick, source, commandId, luaParameters, x, y, z));
                            break;

                        }

                    case ReplayInputType.RemoveFromCommandQueue:
                        {
                            int commandId = reader.ReadInt32();
                            int entityId = reader.ReadInt32();
                            input.Add(new ReplayInput.RemoveCommandFromQueue(tick, source, commandId, entityId));
                            break;
                        }

                    case ReplayInputType.DebugCommand:
                        {
                            string command = reader.ReadNullTerminatedString();
                            float x = reader.ReadSingle();
                            float y = reader.ReadSingle();
                            float z = reader.ReadSingle();
                            byte focusArmy = reader.ReadByte();
                            CommandUnits debugUnits = LoadCommandUnits(reader, entityIds, source);
                            input.Add(new ReplayInput.DebugCommand(tick, source, command, x, y, z, focusArmy, debugUnits));
                            break;
                        }

                    case ReplayInputType.ExecuteLuaInSim:
                        {
                            string luaCode = reader.ReadNullTerminatedString();
                            input.Add(new ReplayInput.ExecuteLuaInSim(tick, source, luaCode));
                            break;
                        }

                    case ReplayInputType.Simcallback:
                        {
                            string endpoint = reader.ReadNullTerminatedString();
                            LuaData luaParameters = LuaDataLoader.ReadLuaData(reader);
                            CommandUnits units = LoadCommandUnits(reader, entityIds, source);
                            input.Add(new ReplayInput.SimCallback(tick, source, endpoint, luaParameters, units));
                            break;
                        }

                    case ReplayInputType.EndGame:
                        input.Add(new ReplayInput.EndGame(tick, source));
                        break;

                    default:
                        throw new Exception("Unknown replay input type");
                }

                // break out of the loop if we have processed the desired number of inputs
                inputProcessed++;
                if (inputToProcess.HasValue && inputProcessed >= inputToProcess.Value)
                {
                    break;
                }
            }

            int completionPercentage = (int)Math.Round(100 * ((float)reader.BaseStream.Position - startingPointOfStream) / (reader.BaseStream.Length - startingPointOfStream));
            return new ReplayBodyInvariant(input, tick, source, inSync, hashTick, hashValue, reader.BaseStream.Position == reader.BaseStream.Length, startingPointOfStream, completionPercentage)
            {
                EntityIds = entityIds,
            };
        }

        /// <summary>
        /// Loads in the scenario information from the replay.
        /// 
        /// In practice this is a copy of the information from the `_scenario.lua` file of the chosen map. Because of that the content is fairly reliable, but at the same time it can contain anything.
        /// </summary>
        /// <param name="luaScenario"></param>
        /// <returns></returns>
        private static ReplayScenarioMap LoadScenarioMap(LuaData.Table luaScenario)
        {
            LuaData.Table? sizeTable = luaScenario.TryGetTableValue("size", out var luaSize) ? luaSize : null;
            int? sizeX = sizeTable != null && sizeTable.TryGetNumberValue("1", out var luaSizeX) ? (int)luaSizeX!.Value : null;
            int? sizeZ = sizeTable != null && sizeTable.TryGetNumberValue("2", out var luaSizeZ) ? (int)luaSizeZ!.Value : null;

            LuaData.Table? reclaimTable = luaScenario.TryGetTableValue("reclaim", out var luaReclaim) ? luaReclaim : null;
            int? massReclaim = reclaimTable != null && reclaimTable.TryGetNumberValue("1", out var luamassReclaim) ? (int)luamassReclaim!.Value : null;
            int? energyReclaim = reclaimTable != null && reclaimTable.TryGetNumberValue("2", out var luaEnergyReclaim) ? (int)luaEnergyReclaim!.Value : null;

            return new ReplayScenarioMap(
                luaScenario.TryGetStringValue("name", out var name) ? name : null,
                luaScenario.TryGetStringValue("description", out var description) ? description : null,
                luaScenario.TryGetStringValue("map", out var scmap) ? scmap : null,
                luaScenario.TryGetStringValue("preview", out var preview) ? preview : null,
                luaScenario.TryGetStringValue("repository", out var repository) ? repository : null,
                luaScenario.TryGetNumberValue("map_version", out var version) ? (int)version! : null,
                sizeX, sizeZ, massReclaim, energyReclaim);
        }

        /// <summary>
        /// Loads in the scenario options as defined in the lobby. 
        /// 
        /// These options reflect the following file: https://github.com/FAForever/fa/blob/develop/lua/ui/lobby/lobbyOptions.lua
        /// In practice however, all mod options are sent to the scenario by default too. As a result this Lua table can contain quite literally anything. 
        /// </summary>
        /// <param name="luaScenario"></param>
        /// <returns></returns>
        private static ReplayScenarioOptions LoadScenarioOptions(LuaData.Table luaScenario)
        {
            if (!luaScenario.TryGetTableValue("Options", out LuaData.Table? options) || options is null)
            {
                return new ReplayScenarioOptions();
            }

            return new ReplayScenarioOptions(
                Victory: GetString(options, "Victory"),
                Share: GetString(options, "Share"),
                UnitCap: GetInt(options, "UnitCap"),
                CheatsEnabled: GetFlexibleBool(options, "CheatsEnabled"),
                PrebuiltUnits: GetFlexibleBool(options, "PrebuiltUnits"),
                AllowObservers: GetFlexibleBool(options, "AllowObservers"),
                RevealCivilians: GetFlexibleBool(options, "RevealCivilians"),
                Score: GetFlexibleBool(options, "Score"),
                AutoTeams: GetString(options, "AutoTeams"),
                TeamLock: GetString(options, "TeamLock"),
                TeamSpawn: GetString(options, "TeamSpawn"),
                Unranked: GetString(options, "Unranked"),
                ScenarioFile: GetString(options, "ScenarioFile"),
                Raw: options);
        }

        /// <summary>
        /// Loads the lobby options of a single army. The keys mirror the PlayerOptions record
        /// of faf-java-commons; everything stays reachable through the raw table.
        /// </summary>
        private static ReplayPlayerOptions LoadPlayerOptions(LuaData.Table army, int? sourceId)
        {
            return new ReplayPlayerOptions(
                SourceId: sourceId,
                PlayerName: GetString(army, "PlayerName"),
                Faction: GetInt(army, "Faction"),
                Team: GetInt(army, "Team"),
                StartSpot: GetInt(army, "StartSpot"),
                Human: GetBool(army, "Human"),
                Civilian: GetBool(army, "Civilian"),
                AIPersonality: GetString(army, "AIPersonality"),
                PlayerColor: GetInt(army, "PlayerColor"),
                ArmyColor: GetInt(army, "ArmyColor"),
                Country: GetString(army, "Country"),
                Clan: GetString(army, "PlayerClan"),
                RatingMean: GetNumber(army, "MEAN"),
                RatingDeviation: GetNumber(army, "DEV"),
                RatedGames: GetInt(army, "NG"),
                Raw: army);
        }

        private static string? GetString(LuaData.Table table, string key)
            => table.TryGetStringValue(key, out string? value) ? value : null;

        private static double? GetNumber(LuaData.Table table, string key)
            => table.TryGetNumberValue(key, out double? value) ? value : null;

        private static bool? GetBool(LuaData.Table table, string key)
            => table.TryGetBooleanValue(key, out bool? value) ? value : null;

        /// <summary>
        /// Reads a boolean lobby option. Lobby options encode booleans inconsistently
        /// (see lua/ui/lobby/lobbyOptions.lua): AllowObservers is a real boolean, while
        /// CheatsEnabled is 'false'/'true', PrebuiltUnits is 'Off'/'On' and
        /// RevealCivilians is 'No'/'Yes'.
        /// </summary>
        private static bool? GetFlexibleBool(LuaData.Table table, string key)
        {
            if (table.TryGetBooleanValue(key, out bool? value))
            {
                return value;
            }

            if (table.TryGetStringValue(key, out string? text))
            {
                if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(text, "on", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(text, "off", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(text, "no", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return null;
        }

        /// <summary>
        /// Reads an integer value that mods and older lobbies sometimes store as a string
        /// (e.g. UnitCap).
        /// </summary>
        private static int? GetInt(LuaData.Table table, string key)
        {
            if (table.TryGetNumberValue(key, out double? number) && number is { } numberValue)
            {
                return (int)numberValue;
            }

            if (table.TryGetStringValue(key, out string? text) && int.TryParse(text, out int parsed))
            {
                return parsed;
            }

            return null;
        }

        /// <summary>
        /// Loads in the scenario from the stream.
        /// </summary>
        private static ReplayScenario LoadScenario(ReplayBinaryReader reader)
        {
            LuaData luaScenario = LuaDataLoader.ReadLuaData(reader);
            if (!(luaScenario is LuaData.Table scenario))
            {
                throw new Exception("Scenario is not a table");
            }

            return new ReplayScenario(LoadScenarioOptions(scenario), LoadScenarioMap(scenario), scenario.TryGetStringValue("type", out var type) ? type! : null);
        }

        /// <summary>
        /// Loads in the replay header from the stream.
        /// </summary>
        private static ReplayHeader LoadReplayHeader(ReplayBinaryReader reader)
        {
            string gameVersion = reader.ReadNullTerminatedString();

            // Always \r\n
            string Unknown1 = reader.ReadNullTerminatedString();

            String[] replayVersionAndScenario = reader.ReadNullTerminatedString().Split("\r\n");
            String replayVersion = replayVersionAndScenario[0];
            String pathToScenario = replayVersionAndScenario[1];

            // Always \r\n and an unknown character
            string Unknown2 = reader.ReadNullTerminatedString();

            int numberOfBytesForMods = reader.ReadInt32();
            List<LuaData> mods = new List<LuaData>();
            LuaData luaMods = LuaDataLoader.ReadLuaData(reader);
            if (luaMods is LuaData.Table modsTable)
            {
                foreach (var mod in modsTable.Value)
                {
                    if (mod.Value is LuaData.Table modTable)
                    {
                        mods.Add(modTable);
                    }
                }
            }

            int numberOfBytesScenario = reader.ReadInt32();
            ReplayScenario scenario = LoadScenario(reader);


            byte numberOfClients = reader.ReadByte();
            ReplaySource[] clients = new ReplaySource[numberOfClients];
            for (int i = 0; i < numberOfClients; i++)
            {
                clients[i] = new ReplaySource(PlayerName: reader.ReadNullTerminatedString(), PlayerId: reader.ReadInt32());
            }

            Boolean cheatsEnabled = reader.ReadByte() > 0;

            int numberOfArmies = reader.ReadByte();
            List<ReplayPlayerOptions> armies = new List<ReplayPlayerOptions>(numberOfArmies);
            for (int i = 0; i < numberOfArmies; i++)
            {
                int numberOfBytesPlayerOptions = reader.ReadInt32();
                LuaData playerOptionsData = LuaDataLoader.ReadLuaData(reader);

                // 255 means that no client controls the army: an AI or a civilian army.
                int playerSource = reader.ReadByte();
                if (playerOptionsData is LuaData.Table armyTable)
                {
                    armies.Add(LoadPlayerOptions(armyTable, playerSource != 255 ? playerSource : null));
                }

                // ???
                if (playerSource != 255)
                {
                    byte[] Unknown3 = reader.ReadBytes(1); // always -1
                }
            }

            int seed = reader.ReadInt32();

            return new ReplayHeader(gameVersion, replayVersion, pathToScenario, scenario, clients, mods.ToArray(), armies.ToArray(), cheatsEnabled, seed);
        }

        private static Replay LoadReplay(ReplayBinaryReader reader)
        {
            ReplayHeader replayHeader = LoadReplayHeader(reader);
            ReplayBodyInvariant replayEvents = LoadReplayInputs(reader, null, null);

            return new Replay(
                Header: replayHeader,
                Body: new ReplayBody(replayEvents.Input, replayEvents.InSync)
            );
        }

        private static ReplayMetadata? LoadReplayMetadata(ReplayBinaryReader reader)
        {
            StringBuilder json = new StringBuilder();

            while (true)
            {
                char c = reader.ReadChar();
                if (c == '\n')
                {
                    break;
                }

                json.Append(c);
            }

            return JsonSerializer.Deserialize<ReplayMetadata>(json.ToString());
        }

        /// <summary>
        /// The reader is significantly faster on a memory stream that exposes its buffer (see
        /// <see cref="ReplayBinaryReader.ReadNullTerminatedString"/>). Decompressed replays always are;
        /// for any other stream the remainder is copied into one. The copy is cheap compared to parsing.
        /// </summary>
        private static Stream WithAccessibleBuffer(Stream stream)
        {
            if (stream is MemoryStream memoryStream && memoryStream.TryGetBuffer(out _))
            {
                return stream;
            }

            MemoryStream copy = stream.CanSeek ? new MemoryStream((int)(stream.Length - stream.Position)) : new MemoryStream();
            stream.CopyTo(copy);
            copy.Position = 0;
            return copy;
        }

        /// <summary>
        /// Decompresses a zstd body in one go into a single buffer. Its size comes from the frame
        /// header: the exact size when the frame stores it, otherwise an upper bound (FAForever
        /// replays do not store it; the bound is a few percent too large). Returns null (without
        /// consuming the stream) when no size is available or the body does not fit, so the caller
        /// can fall back to streaming decompression.
        /// Avoids the repeated growing and copying of a memory stream that is written in chunks.
        /// </summary>
        private static MemoryStream? TryDecompressZstdInOneGo(Stream stream)
        {
            if (!(stream is MemoryStream memoryStream))
            {
                return null;
            }

            // the compressed body is small compared to the decompressed one, copying it is cheap
            long start = memoryStream.Position;
            ReadOnlySpan<byte> compressed;
            if (memoryStream.TryGetBuffer(out ArraySegment<byte> buffer))
            {
                compressed = buffer.AsSpan((int)start);
            }
            else
            {
                byte[] copy = new byte[memoryStream.Length - start];
                memoryStream.ReadExactly(copy);
                memoryStream.Position = start;
                compressed = copy;
            }

            ulong size;
            try
            {
                size = Decompressor.GetDecompressedSize(compressed);
            }
            catch (ZstdException)
            {
                return null;
            }

            if (size == 0 || size > int.MaxValue)
            {
                return null;
            }

            byte[] decompressed = GC.AllocateUninitializedArray<byte>((int)size);
            int written;
            using (Decompressor decompressor = new Decompressor())
            {
                if (!decompressor.TryUnwrap(compressed, decompressed, out written))
                {
                    return null;
                }
            }

            memoryStream.Position = memoryStream.Length;
            return new MemoryStream(decompressed, 0, written, writable: false, publiclyVisible: true);
        }

        private static MemoryStream? DecompressReplay(Stream stream, ReplayCompression compression)
        {
            MemoryStream replayStream = new MemoryStream();

            switch (compression)
            {
                case ReplayCompression.Gzip:

                    string base64 = new StreamReader(stream).ReadToEnd();
                    byte[] bytes = Convert.FromBase64String(base64);
                    byte[] skipped = new byte[bytes.Length - 4];
                    Array.Copy(bytes, 4, skipped, 0, skipped.Length);
                    using (MemoryStream memoryStream = new MemoryStream(skipped, false))
                    {
                        using (InflaterInputStream decompressor = new InflaterInputStream(memoryStream))
                        {
                            decompressor.CopyTo(replayStream);
                            replayStream.Position = 0;
                            return replayStream;
                        }
                    }

                case ReplayCompression.Zstd:
                    MemoryStream? decompressed = TryDecompressZstdInOneGo(stream);
                    if (decompressed != null)
                    {
                        return decompressed;
                    }

                    using (DecompressionStream decompressor = new DecompressionStream(stream))
                    {
                        decompressor.CopyTo(replayStream);
                        replayStream.Position = 0;
                        return replayStream;
                    }

                default:
                    return null;
            }
        }

        /// <summary>
        /// Processes the metadata of a replay from FAForever and returns the intermediate results. 
        /// 
        /// This allows the process to exit periodically, make room for other code to run, and then continue where it left off. This is useful for single threaded environments such as WebAssembly that is used by Blazor.
        /// </summary>
        /// <param name="stage"></param>
        /// <returns></returns>
        public static ReplayLoadingStage ProcessReplayStage(ReplayLoadingStage.NotStarted stage)
        {
            ReplayBinaryReader reader = new ReplayBinaryReader(stage.Stream);
            ReplayMetadata? metadata = LoadReplayMetadata(reader);

            if (metadata is null)
            {
                return new ReplayLoadingStage.Failed("No metadata found.");
            }

            return new ReplayLoadingStage.WithMetadata(stage.Stream, metadata);
        }


        /// <summary>
        /// Decompresses the replay and returns the intermediate results.
        /// 
        /// This allows the process to exit periodically, make room for other code to run, and then continue where it left off. This is useful for single threaded environments such as WebAssembly that is used by Blazor.
        /// </summary>
        /// <param name="stage"></param>
        /// <returns></returns>
        public static ReplayLoadingStage ProcessReplayStage(ReplayLoadingStage.WithMetadata stage)
        {
            ReplayCompression replayCompression = ReplayCompression.Gzip;
            if (stage.Metadata.compression == "zstd")
            {
                replayCompression = ReplayCompression.Zstd;
            }

            MemoryStream? replayStream = DecompressReplay(stage.Stream, replayCompression);
            if (replayStream is null)
            {
                return new ReplayLoadingStage.Failed("Decompression failed.");
            }

            // close the old stream
            stage.Stream.Dispose();

            return new ReplayLoadingStage.Decompressed(replayStream, stage.Metadata);
        }

        /// <summary>
        /// Processes the scenario of a replay and returns the intermediate results.
        /// 
        /// This allows the process to exit periodically, make room for other code to run, and then continue where it left off. This is useful for single threaded environments such as WebAssembly that is used by Blazor.
        /// </summary>
        /// <param name="stage"></param>
        /// <returns></returns>
        public static ReplayLoadingStage ProcessReplayStage(ReplayLoadingStage.Decompressed stage)
        {
            ReplayBinaryReader reader = new ReplayBinaryReader(WithAccessibleBuffer(stage.Stream));
            ReplayHeader replayHeader = LoadReplayHeader(reader);
            return new ReplayLoadingStage.WithScenario(reader, stage.Metadata, replayHeader);
        }

        /// <summary>
        /// Processes a portion of the input of a replay and returns the intermediate results.
        /// 
        /// This allows the process to exit periodically, make room for other code to run, and then continue where it left off. This is useful for single threaded environments such as WebAssembly that is used by Blazor.
        /// </summary>
        /// <param name="stage"></param>
        /// <returns></returns>
        public static ReplayLoadingStage ProcessReplayStage(ReplayLoadingStage.WithScenario stage, int batchSize = 1000)
        {
            ReplayBodyInvariant replayBodyInvariant = LoadReplayInputs(stage.Stream, null, batchSize);
            return new ReplayLoadingStage.AtInput(stage.Stream, stage.Metadata, stage.Header, replayBodyInvariant);
        }

        /// <summary>
        /// Processes a portion of the input of a replay and returns the intermediate results.
        /// 
        /// This allows the process to exit periodically, make room for other code to run, and then continue where it left off. This is useful for single threaded environments such as WebAssembly that is used by Blazor.
        /// </summary>
        /// <param name="stage"></param>
        /// <returns></returns>
        public static ReplayLoadingStage ProcessReplayStage(ReplayLoadingStage.AtInput stage, int batchSize = 1000)
        {
            ReplayBodyInvariant replayBodyInvariant = LoadReplayInputs(stage.Stream, stage.BodyInvariant, batchSize);
            if (replayBodyInvariant.EndOfStream)
            {
                return new ReplayLoadingStage.Complete(stage.Stream, stage.Metadata, stage.Header, new ReplayBody(replayBodyInvariant.Input, replayBodyInvariant.InSync));
            }
            else
            {
                return new ReplayLoadingStage.AtInput(stage.Stream, stage.Metadata, stage.Header, replayBodyInvariant);
            }
        }


        /// <summary>
        /// Loads a FAForever replay from memory. 
        /// 
        /// Loads the replay from start to finish, if intermediate steps are required then please see the ProcessReplayStage methods.
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static Replay LoadFAFReplayFromMemory(Stream stream)
        {
            ReplayBinaryReader reader = new ReplayBinaryReader(stream);
            ReplayMetadata replayMetadata = LoadReplayMetadata(reader);


            ReplayCompression replayCompression = ReplayCompression.Gzip;
            if (replayMetadata.compression == "zstd")
            {
                replayCompression = ReplayCompression.Zstd;
            }

            MemoryStream decompressedStream = DecompressReplay(stream, replayCompression);
            using (ReplayBinaryReader replayBinaryReader = new ReplayBinaryReader(decompressedStream))
            {
                return LoadReplay(replayBinaryReader);
            }
        }

        /// <summary>
        /// Reads the path to the scenario ("/maps/{folder}/{folder}_scenario.lua") from the start of a
        /// replay from FAForever, without the rest of the file: for when only the map is wanted, such as a
        /// generated map, whose name the server leaves out of the metadata ("mapname":"None").
        ///
        /// The path is the third string of the header, so a few hundred decompressed bytes hold it. A zstd
        /// body decompresses one block at a time and a block holds up to 128 KB, so the start may need the
        /// first block in full: about 130 KB after the metadata line at most, often far less (32 KB for a
        /// 10 player game). Returns null when the start is too short for it or is not a replay.
        /// </summary>
        public static string? TryReadPathToScenario(ReadOnlySpan<byte> start)
        {
            int endOfMetadata = start.IndexOf((byte)'\n');
            if (endOfMetadata < 0)
            {
                return null;
            }

            ReplayMetadata? metadata;
            try
            {
                metadata = JsonSerializer.Deserialize<ReplayMetadata>(start[..endOfMetadata]);
            }
            catch (JsonException)
            {
                return null;
            }

            if (metadata is null)
            {
                return null;
            }

            ReadOnlySpan<byte> body = start[(endOfMetadata + 1)..];
            byte[] header = new byte[1024];
            int length = 0;
            try
            {
                using Stream decompressor = metadata.compression == "zstd"
                    ? new DecompressionStream(new MemoryStream(body.ToArray(), false))
                    : InflateBase64Start(body);

                int read;
                while (length < header.Length && (read = decompressor.Read(header, length, header.Length - length)) > 0)
                {
                    length += read;
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // a decompressor throws where the start is cut off, after handing out what it had
            }

            // the game version, a line break, then the replay version and the path on two lines
            ReadOnlySpan<byte> remaining = header.AsSpan(0, length);
            for (int i = 0; i < 2; i++)
            {
                int end = remaining.IndexOf((byte)0);
                if (end < 0)
                {
                    return null;
                }

                remaining = remaining[(end + 1)..];
            }

            int endOfVersionAndPath = remaining.IndexOf((byte)0);
            if (endOfVersionAndPath < 0)
            {
                return null;
            }

            string[] versionAndPath = Encoding.UTF8.GetString(remaining[..endOfVersionAndPath]).Split("\r\n");
            return versionAndPath.Length >= 2 && versionAndPath[1].Length > 0 ? versionAndPath[1] : null;
        }

        /// <summary>
        /// The older body: base64 text of a zlib stream after four bytes of size. The start is cut to whole
        /// groups of four characters, as base64 decodes only those.
        /// </summary>
        private static Stream InflateBase64Start(ReadOnlySpan<byte> body)
        {
            string base64 = Encoding.ASCII.GetString(body).TrimEnd();
            byte[] bytes = Convert.FromBase64String(base64[..(base64.Length - base64.Length % 4)]);
            return new InflaterInputStream(new MemoryStream(bytes, 4, Math.Max(0, bytes.Length - 4), false));
        }

        /// <summary>
        /// Loads a SCFA replay from memory.
        /// 
        /// Loads the replay from start to finish, if intermediate steps are required then please see the ProcessReplayStage methods.
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static Replay LoadSCFAReplayFromStream(Stream stream)
        {
            using (ReplayBinaryReader reader = new ReplayBinaryReader(WithAccessibleBuffer(stream)))
            {
                return LoadReplay(reader);
            }
        }

        /// <summary>
        /// Loads a replay from disk that is expected to be in the compressed format of FAForever.
        /// 
        /// Loads the replay from start to finish, if intermediate steps are required then please see the ProcessReplayStage methods.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static Replay LoadFAFReplayFromDisk(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open))
            {
                return LoadFAFReplayFromMemory(stream);
            }
        }

        /// <summary>
        /// Loads a replay from disk that is expected to be in the uncompressed format of Supreme Commander: Forged Alliance.
        /// 
        /// Loads the replay from start to finish, if intermediate steps are required then please see the ProcessReplayStage methods.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static Replay LoadSCFAReplayFromDisk(string path)
        {
            using (FileStream reader = new FileStream(path, FileMode.Open))
            {
                return LoadSCFAReplayFromStream(reader);
            }
        }

        /// <summary>
        /// Loads a replay from disk. Attempts to infer the replay type from the file extension.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static Replay LoadReplayFromDisk(string path)
        {
            string extension = Path.GetExtension(path);
            switch (extension)
            {
                case ".fafreplay":
                    return LoadFAFReplayFromDisk(path);

                case ".scfareplay":
                    return LoadSCFAReplayFromDisk(path);

                default:
                    throw new ArgumentException("Unknown replay extension. Expected '.fafreplay' or '.scfareplay'");

            }
        }
    }
}
