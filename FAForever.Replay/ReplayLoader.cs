
using System.Data;
using System.Text;
using ZstdSharp;

using System.Text.Json;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;

namespace FAForever.Replay
{

    public enum ReplayLoadStatus { NotStarted, Pending, Success, NotApplicable }

    public record ReplayLoadProgression(ReplayLoadStatus Decompression, ReplayLoadStatus Metadata, ReplayLoadStatus Header, ReplayLoadStatus Scenario, ReplayLoadStatus Mods, ReplayLoadStatus Clients, ReplayLoadStatus Armies, ReplayLoadStatus Input);

    public static class ReplayLoader
    {
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

            bool addToQueue = reader.ReadByte() > 0;

            return new CommandData(commandId, commandType, target, formation, blueprintId, luaData, addToQueue, arg1, arg2, arg3, arg4, arg5, arg6);
        }

        private static CommandUnits LoadCommandUnits(ReplayBinaryReader reader)
        {
            int numberOfEntities = reader.ReadInt32();
            int[] entityIds = new int[numberOfEntities];
            for (int i = 0; i < numberOfEntities; i++)
            {
                entityIds[i] = reader.ReadInt32();
            }

            return new CommandUnits(entityIds);
        }

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
                    return new CommandTarget.None();
            }
        }

        private static CommandFormation LoadCommandFormation(ReplayBinaryReader reader)
        {
            int formationId = reader.ReadInt32();
            if (formationId == -1)
            {
                return new CommandFormation.NoFormation();
            }

            float heading = reader.ReadSingle();
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            float scale = reader.ReadSingle();

            return new CommandFormation.Formation(formationId, heading, x, y, z, scale);
        }

        private static ReplayBody LoadReplayInputs(ReplayBinaryReader reader, IProgress<string> progress)
        {
            progress.Report("Loading replay input");

            // state 
            int currentTick = 0;
            int currentSource = 0;

            List<ReplayProcessedInput> replayInputs = new List<ReplayProcessedInput>();
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                ReplayInputType type = (ReplayInputType)reader.ReadByte();
                // includes the type and this number of bytes, which is a bit confusing.
                int numberOfBytes = reader.ReadInt16();

                switch (type)
                {
                    case ReplayInputType.Advance:
                        {
                            int ticksToAdvance = reader.ReadInt32();
                            currentTick += ticksToAdvance;
                            break;
                        }

                    case ReplayInputType.SetCommandSource:
                        {
                            int sourceId = reader.ReadByte();
                            currentSource = sourceId;
                            break;
                        }

                    case ReplayInputType.CommandSourceTerminated:
                        replayInputs.Add(new ReplayProcessedInput(currentTick, currentSource, new ReplayInput.CommandSourceTerminated()));
                        break;

                    case ReplayInputType.VerifyChecksum:
                        {
                            int hash1 = reader.ReadInt32();
                            int hash2 = reader.ReadInt32();
                            int hash3 = reader.ReadInt32();
                            int hash4 = reader.ReadInt32();
                            int tick = reader.ReadInt32();
                            replayInputs.Add(new ReplayProcessedInput(currentTick, currentSource, new ReplayInput.VerifyChecksum(hash1, hash2, hash3, hash4, tick)));
                            break;
                        }

                    case ReplayInputType.RequestPause:
                        replayInputs.Add(new ReplayProcessedInput(currentTick, currentSource, new ReplayInput.RequestPause()));
                        break;

                    case ReplayInputType.RequestResume:
                        replayInputs.Add(new ReplayProcessedInput(currentTick, currentSource, new ReplayInput.RequestResume()));
                        break;

                    case ReplayInputType.SingleStep:
                        replayInputs.Add(new ReplayProcessedInput(currentTick, currentSource, new ReplayInput.SingleStep()));
                        break;

                    case ReplayInputType.CreateUnit:
                        {
                            int armyId = reader.ReadByte();
                            string blueprintId = reader.ReadNullTerminatedString();
                            float x = reader.ReadSingle();
                            float z = reader.ReadSingle();
                            float heading = reader.ReadSingle();
                            replayInputs.Add(new ReplayProcessedInput(currentTick, currentSource, new ReplayInput.CreateUnit(armyId, blueprintId, x, z, heading)));
                            break;
                        }

                    case ReplayInputType.CreateProp:
                        {
                            string blueprintId = reader.ReadNullTerminatedString();
                            float x = reader.ReadSingle();
                            float z = reader.ReadSingle();
                            float heading = reader.ReadSingle();
                            replayInputs.Add(new ReplayProcessedInput(currentTick, currentSource, new ReplayInput.CreateProp(blueprintId, x, z, heading)));
                            break;
                        }

                    case ReplayInputType.DestroyEntity:
                        {
                            int entityId = reader.ReadInt32();
                            replayInputs.Add(new ReplayProcessedInput(currentTick, currentSource, new ReplayInput.DestroyEntity(entityId)));
                            break;
                        }

                    case ReplayInputType.WarpEntity:
                        {
                            int entityId = reader.ReadInt32();
                            float x = reader.ReadSingle();
                            float y = reader.ReadSingle();
                            float z = reader.ReadSingle();
                            replayInputs.Add(new ReplayProcessedInput(currentTick, currentSource, new ReplayInput.WarpEntity(entityId, x, y, z)));
                            break;
                        }

                    case ReplayInputType.ProcessInfoPair:
                        {
                            int entityId = reader.ReadInt32();
                            string name = reader.ReadNullTerminatedString();
                            string value = reader.ReadNullTerminatedString();
                            replayInputs.Add(new ReplayProcessedInput(currentTick, currentSource, new ReplayInput.ProcessInfoPair(entityId, name, value)));
                            break;
                        }

                    case ReplayInputType.IssueCommand:
                        {
                            CommandUnits units = LoadCommandUnits(reader);
                            CommandData data = LoadCommandData(reader);
                            replayInputs.Add(new ReplayProcessedInput(currentTick, currentSource, new ReplayInput.IssueCommand(units, data)));
                            break;
                        }

                    case ReplayInputType.IssueFactoryCommand:
                        {
                            CommandUnits factories = LoadCommandUnits(reader);
                            CommandData data = LoadCommandData(reader);
                            replayInputs.Add(new ReplayProcessedInput(currentTick, currentSource, new ReplayInput.IssueFactoryCommand(factories, data)));
                            break;
                        }

                    case ReplayInputType.IncreaseCommandCount:
                        {
                            int commandId = reader.ReadInt32();
                            int delta = reader.ReadInt32();
                            replayInputs.Add(new ReplayProcessedInput(currentTick, currentSource, new ReplayInput.IncreaseCommandCount(commandId, delta)));
                            break;
                        }

                    case ReplayInputType.DecreaseCommandCount:
                        {
                            int commandId = reader.ReadInt32();
                            int delta = reader.ReadInt32();
                            replayInputs.Add(new ReplayProcessedInput(currentTick, currentSource, new ReplayInput.DecreaseCommandCount(commandId, delta)));
                            break;
                        }

                    case ReplayInputType.UpdateCommandTarget:
                        {
                            int commandId = reader.ReadInt32();
                            CommandTarget target = LoadCommandTarget(reader);
                            replayInputs.Add(new ReplayProcessedInput(currentTick, currentSource, new ReplayInput.UpdateCommandTarget(commandId, target)));
                            break;
                        }

                    case ReplayInputType.UpdateCommandType:
                        {
                            int commandId = reader.ReadInt32();
                            CommandType commandType = (CommandType)reader.ReadInt32();
                            replayInputs.Add(new ReplayProcessedInput(currentTick, currentSource, new ReplayInput.UpdateCommandType(commandId, commandType)));
                            break;
                        }

                    case ReplayInputType.UpdateCommandParameters:
                        {
                            int commandId = reader.ReadInt32();
                            LuaData luaParameters = LuaDataLoader.ReadLuaData(reader);
                            float x = reader.ReadSingle();
                            float y = reader.ReadSingle();
                            float z = reader.ReadSingle();
                            replayInputs.Add(new ReplayProcessedInput(currentTick, currentSource, new ReplayInput.UpdateCommandLuaParameters(commandId, luaParameters, x, y, z)));
                            break;

                        }

                    case ReplayInputType.RemoveFromCommandQueue:
                        {
                            int commandId = reader.ReadInt32();
                            int entityId = reader.ReadInt32();
                            replayInputs.Add(new ReplayProcessedInput(currentTick, currentSource, new ReplayInput.RemoveCommandFromQueue(commandId, entityId)));
                            break;
                        }

                    case ReplayInputType.DebugCommand:
                        {
                            string command = reader.ReadNullTerminatedString();
                            float x = reader.ReadSingle();
                            float y = reader.ReadSingle();
                            float z = reader.ReadSingle();
                            byte focusArmy = reader.ReadByte();
                            CommandUnits debugUnits = LoadCommandUnits(reader);
                            replayInputs.Add(new ReplayProcessedInput(currentTick, currentSource, new ReplayInput.DebugCommand(command, x, y, z, focusArmy, debugUnits)));
                            break;
                        }

                    case ReplayInputType.ExecuteLuaInSim:
                        {
                            string luaCode = reader.ReadNullTerminatedString();
                            replayInputs.Add(new ReplayProcessedInput(currentTick, currentSource, new ReplayInput.ExecuteLuaInSim(luaCode)));
                            break;
                        }

                    case ReplayInputType.Simcallback:
                        {
                            string endpoint = reader.ReadNullTerminatedString();
                            LuaData luaParameters = LuaDataLoader.ReadLuaData(reader);
                            CommandUnits units = LoadCommandUnits(reader);
                            replayInputs.Add(new ReplayProcessedInput(currentTick, currentSource, new ReplayInput.SimCallback(endpoint, luaParameters, units)));
                            break;
                        }

                    case ReplayInputType.EndGame:
                        replayInputs.Add(new ReplayProcessedInput(currentTick, currentSource, new ReplayInput.EndGame()));
                        break;

                    default:
                        throw new Exception("Unknown replay input type");
                }
            }

            return new ReplayBody(replayInputs.ToArray(), true);
        }

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

        private static ReplayScenarioOptions LoadScenarioOptions(LuaData.Table luaScenario)
        {
            return new ReplayScenarioOptions();
        }

        private static ReplayScenario LoadScenario(ReplayBinaryReader reader)
        {
            LuaData luaScenario = LuaDataLoader.ReadLuaData(reader);
            if (!(luaScenario is LuaData.Table scenario))
            {
                throw new Exception("Scenario is not a table");
            }

            return new ReplayScenario(LoadScenarioOptions(scenario), LoadScenarioMap(scenario), scenario.TryGetStringValue("type", out var type) ? type! : null);
        }

        private static ReplayHeader LoadReplayHeader(ReplayBinaryReader reader, IProgress<string> progress)
        {
            progress.Report("Loading replay header");
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

            int numberOfPlayerOptions = reader.ReadByte();
            for (int i = 0; i < numberOfPlayerOptions; i++)
            {
                int numberOfBytesPlayerOptions = reader.ReadInt32();
                LuaData playerOptionsData = LuaDataLoader.ReadLuaData(reader);
                //byte[] playerOptions = reader.ReadBytes(numberOfBytesPlayerOptions);

                int playerSource = reader.ReadByte();

                // ???
                if (playerSource != 255)
                {
                    byte[] Unknown3 = reader.ReadBytes(1); // always -1
                }
            }

            int seed = reader.ReadInt32();

            return new ReplayHeader(scenario, clients, mods.ToArray(), new LuaData[] { });
        }

        private static Replay LoadReplay(ReplayBinaryReader reader, IProgress<string> progress)
        {
            progress.Report("Loading replay");
            ReplayHeader replayHeader = LoadReplayHeader(reader, progress);
            ReplayBody replayEvents = LoadReplayInputs(reader, progress);
            progress.Report("Done!");
            return new Replay(
                Header: replayHeader,
                Body: replayEvents
            );
        }

        public static Replay LoadFAFReplayFromMemory(Stream stream, IProgress<string> progress)
        {
            progress.Report("Loading FAForever metadata");
            BinaryReader reader = new BinaryReader(stream);
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

            var dictionary = JsonSerializer.Deserialize<Dictionary<string, object>>(json.ToString());
            if (dictionary == null)
            {
                return null;
            }

            // todo: parse meta data

            using (MemoryStream replayStream = new MemoryStream())
            {
                if (dictionary.ContainsKey("compression") && dictionary["compression"] != null && dictionary["compression"].ToString() == "zstd")
                {
                    progress.Report("Decompressing ZSTD stream");
                    using (DecompressionStream decompressor = new DecompressionStream(stream))
                    {
                        decompressor.CopyTo(replayStream);
                        replayStream.Position = 0;
                        return LoadSCFAReplayFromStream(replayStream, progress);
                    }
                }
                else
                {
                    progress.Report("Decompressing ZLIB stream");
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
                            return LoadSCFAReplayFromStream(replayStream, progress);
                        }
                    }
                }
            }
        }

        public static Replay LoadSCFAReplayFromStream(Stream stream, IProgress<string> progress)
        {
            progress.Report("Loading SCFA replay from stream");
            using (ReplayBinaryReader reader = new ReplayBinaryReader(stream))
            {
                return LoadReplay(reader, progress);
            }
        }

        /// <summary>
        /// Loads a replay from disk that is expected to be in the compressed format of FAForever.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static Replay LoadFAFReplayFromDisk(string path, IProgress<string> progress)
        {
            progress.Report("Loading FAForever replay from disk");
            using (FileStream stream = new FileStream(path, FileMode.Open))
            {
                return LoadFAFReplayFromMemory(stream, progress);
            }

        }

        /// <summary>
        /// Loads a replay from disk that is expected to be in the uncompressed format of Supreme Commander: Forged Alliance.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static Replay LoadSCFAReplayFromDisk(string path, IProgress<string> progress)
        {
            progress.Report("Loading SCFA replay from disk");
            using (FileStream reader = new FileStream(path, FileMode.Open))
            {
                return LoadSCFAReplayFromStream(reader, progress);
            }
        }

        /// <summary>
        /// Loads a replay from disk. Attempts to infer the replay type from the file extension.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static Replay LoadReplayFromDisk(string path, IProgress<string> progress)
        {
            progress.Report("Determining replay type");

            string extension = Path.GetExtension(path);
            switch (extension)
            {
                case ".fafreplay":
                    return LoadFAFReplayFromDisk(path, progress);

                case ".scfareplay":
                    return LoadSCFAReplayFromDisk(path, progress);

                default:
                    throw new ArgumentException("Unknown replay extension. Expected '.fafreplay' or '.scfareplay'");

            }
        }
    }
}
