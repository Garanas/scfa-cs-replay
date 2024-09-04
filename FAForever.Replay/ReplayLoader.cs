
using System.Data;
using System.Text;
using ZstdSharp;

using System.Text.Json;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;

namespace FAForever.Replay
{
    public static class ReplayLoader
    {
        public static CommandData ParseEventCommandData(ReplayBinaryReader reader)
        {
            int commandId = reader.ReadInt32();

            // unknown
            int arg1 = reader.ReadInt32();

            CommandType commandType = (CommandType)reader.ReadByte();

            // unknown
            int arg2 = reader.ReadInt32();

            CommandTarget target = ParseEventCommandTarget(reader);

            // unknown
            byte arg3 = reader.ReadByte();

            CommandFormation formation = ParseEventCommandFormation(reader);

            string blueprintId = reader.ReadNullTerminatedString();

            // unknown
            int arg4 = reader.ReadInt32();
            int arg5 = reader.ReadInt32();
            int arg6 = reader.ReadInt32();

            LuaData luaData = LuaDataLoader.ReadLuaData(reader);

            bool addToQueue = reader.ReadByte() > 0;

            return new CommandData(commandId, commandType, target, formation, blueprintId, luaData, addToQueue, arg1, arg2, arg3, arg4, arg5, arg6);
        }

        public static CommandUnits ParseEventCommandUnits(ReplayBinaryReader reader)
        {
            int numberOfEntities = reader.ReadInt32();
            int[] entityIds = new int[numberOfEntities];
            for (int i = 0; i < numberOfEntities; i++)
            {
                entityIds[i] = reader.ReadInt32();
            }

            return new CommandUnits(entityIds);
        }

        public static CommandTarget ParseEventCommandTarget(ReplayBinaryReader reader)
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

        public static CommandFormation ParseEventCommandFormation(ReplayBinaryReader reader)
        {
            int formationId = reader.ReadInt32();
            if (formationId == -1)
            {
                return new CommandFormation.NoFormation();
            }

            return new CommandFormation.Formation(
                FormationIdentifier: formationId,
                Heading: reader.ReadSingle(),
                X: reader.ReadSingle(),
                Y: reader.ReadSingle(),
                Z: reader.ReadSingle(),
                Scale: reader.ReadSingle()
            );
        }

        public static ReplayInput LoadReplayInput(ReplayBinaryReader reader, ReplayInputType type)
        {
            switch (type)
            {
                case ReplayInputType.Advance:
                    {
                        int ticksToAdvance = reader.ReadInt32();
                        return new ReplayInput.Advance(ticksToAdvance);
                    }

                case ReplayInputType.SetCommandSource:
                    {
                        int sourceId = reader.ReadByte();
                        return new ReplayInput.SetCommandSource(sourceId);
                    }

                case ReplayInputType.CommandSourceTerminated:
                    return new ReplayInput.CommandSourceTerminated();

                case ReplayInputType.VerifyChecksum:
                    {
                        byte[] hash = reader.ReadBytes(16);
                        int tick = reader.ReadInt32();
                        return new ReplayInput.VerifyChecksum(hash, tick);
                    }

                case ReplayInputType.RequestPause:
                    return new ReplayInput.RequestPause();

                case ReplayInputType.RequestResume:
                    return new ReplayInput.RequestResume();

                case ReplayInputType.SingleStep:
                    return new ReplayInput.SingleStep();

                case ReplayInputType.CreateUnit:
                    {
                        int armyId = reader.ReadByte();
                        string blueprintId = reader.ReadNullTerminatedString();
                        float x = reader.ReadSingle();
                        float z = reader.ReadSingle();
                        float heading = reader.ReadSingle();
                        return new ReplayInput.CreateUnit(armyId, blueprintId, x, z, heading);
                    }

                case ReplayInputType.CreateProp:
                    {
                        string blueprintId = reader.ReadNullTerminatedString();
                        float x = reader.ReadSingle();
                        float z = reader.ReadSingle();
                        float heading = reader.ReadSingle();
                        return new ReplayInput.CreateProp(blueprintId, x, z, heading);
                    }

                case ReplayInputType.DestroyEntity:
                    {
                        int entityId = reader.ReadInt32();
                        return new ReplayInput.DestroyEntity(entityId);
                    }

                case ReplayInputType.WarpEntity:
                    {
                        int entityId = reader.ReadInt32();
                        float x = reader.ReadSingle();
                        float y = reader.ReadSingle();
                        float z = reader.ReadSingle();
                        return new ReplayInput.WarpEntity(entityId, x, y, z);
                    }

                case ReplayInputType.ProcessInfoPair:
                    {
                        int entityId = reader.ReadInt32();
                        string name = reader.ReadNullTerminatedString();
                        string value = reader.ReadNullTerminatedString();
                        return new ReplayInput.ProcessInfoPair(entityId, name, value);
                    }

                case ReplayInputType.IssueCommand:
                    {
                        CommandUnits units = ParseEventCommandUnits(reader);
                        CommandData data = ParseEventCommandData(reader);
                        return new ReplayInput.IssueCommand(units, data);
                    }

                case ReplayInputType.IssueFactoryCommand:
                    {
                        CommandUnits factories = ParseEventCommandUnits(reader);
                        CommandData data = ParseEventCommandData(reader);
                        return new ReplayInput.IssueFactoryCommand(factories, data);
                    }

                case ReplayInputType.IncreaseCommandCount:
                    {
                        int commandId = reader.ReadInt32();
                        int delta = reader.ReadInt32();
                        return new ReplayInput.IncreaseCommandCount(commandId, delta);
                    }

                case ReplayInputType.DecreaseCommandCount:
                    {
                        int commandId = reader.ReadInt32();
                        int delta = reader.ReadInt32();
                        return new ReplayInput.DecreaseCommandCount(commandId, delta);
                    }

                case ReplayInputType.UpdateCommandTarget:
                    {
                        int commandId = reader.ReadInt32();
                        CommandTarget target = ParseEventCommandTarget(reader);
                        return new ReplayInput.UpdateCommandTarget(commandId, target);
                    }

                case ReplayInputType.UpdateCommandType:
                    {
                        int commandId = reader.ReadInt32();
                        CommandType commandType = (CommandType)reader.ReadInt32();
                        return new ReplayInput.UpdateCommandType(commandId, commandType);
                    }

                case ReplayInputType.UpdateCommandParameters:
                    {
                        int commandId = reader.ReadInt32();
                        LuaData luaParameters = LuaDataLoader.ReadLuaData(reader);
                        float x = reader.ReadSingle();
                        float y = reader.ReadSingle();
                        float z = reader.ReadSingle();
                        return new ReplayInput.UpdateCommandLuaParameters(commandId, luaParameters, x, y, z);

                    }

                case ReplayInputType.RemoveFromCommandQueue:
                    {
                        int commandId = reader.ReadInt32();
                        int entityId = reader.ReadInt32();
                        return new ReplayInput.RemoveCommandFromQueue(commandId, entityId);
                    }

                case ReplayInputType.DebugCommand:
                    {
                        string command = reader.ReadNullTerminatedString();
                        float x = reader.ReadSingle();
                        float y = reader.ReadSingle();
                        float z = reader.ReadSingle();
                        byte focusArmy = reader.ReadByte();
                        CommandUnits debugUnits = ParseEventCommandUnits(reader);
                        return new ReplayInput.DebugCommand(command, x, y, z, focusArmy, debugUnits);
                    }

                case ReplayInputType.ExecuteLuaInSim:
                    {
                        string luaCode = reader.ReadNullTerminatedString();
                        return new ReplayInput.ExecuteLuaInSim(luaCode);
                    }

                case ReplayInputType.Simcallback:
                    {
                        string endpoint = reader.ReadNullTerminatedString();
                        LuaData luaParameters = LuaDataLoader.ReadLuaData(reader);
                        CommandUnits units = ParseEventCommandUnits(reader);
                        return new ReplayInput.SimCallback(endpoint, luaParameters, units);
                    }

                case ReplayInputType.EndGame:
                    return new ReplayInput.EndGame();

                default:
                    throw new Exception("Unknown replay input type");
            }
        }

        public static List<ReplayInput> LoadReplayInputs(ReplayBinaryReader reader)
        {
            List<ReplayInput> replayInputs = new List<ReplayInput>();
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                long position = reader.BaseStream.Position;

                ReplayInputType type = (ReplayInputType)reader.ReadByte();
                // includes the type and this number of bytes, which is a bit confusing.
                int numberOfBytes = reader.ReadInt16();

                ReplayInput replayInput = LoadReplayInput(reader, type);

                if (reader.BaseStream.Position < position + numberOfBytes)
                {
                    reader.ReadBytes(numberOfBytes - (int)(reader.BaseStream.Position - position));
                }
                else if (reader.BaseStream.Position > position + numberOfBytes)
                {
                    throw new DataException("Replay input consumed more bytes than expected");
                }

                replayInputs.Add(replayInput);
            }
            return replayInputs;
        }

        public static ReplayHeader ParseReplayHeader(ReplayBinaryReader reader)
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
            byte[] mods = reader.ReadBytes(numberOfBytesForMods);

            int numberOfBytesForGameOptions = reader.ReadInt32();
            byte[] gameOptions = reader.ReadBytes(numberOfBytesForGameOptions);

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
                byte[] playerOptions = reader.ReadBytes(numberOfBytesPlayerOptions);

                int playerSource = reader.ReadByte();

                // ???
                if (playerSource != 255)
                {
                    byte[] Unknown3 = reader.ReadBytes(1); // always -1
                }
            }

            int seed = reader.ReadInt32();

            return new ReplayHeader();
        }

        public static Replay ParseReplay(ReplayBinaryReader reader)
        {
            ReplayHeader replayHeader = ParseReplayHeader(reader);
            List<ReplayInput> replayEvents = LoadReplayInputs(reader);
            List<ReplayProcessedInput> gameEvents = ReplaySemantics.ConvertToGameEvents(replayEvents);

            return new Replay(
                Header: replayHeader,
                Events: gameEvents
                );
        }

        public static Replay LoadFAFReplayFromMemory(Stream stream)
        {
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
                    using (DecompressionStream decompressor = new DecompressionStream(stream))
                    {
                        decompressor.CopyTo(replayStream);
                        replayStream.Position = 0;
                        return LoadSCFAReplayFromStream(replayStream);
                    }
                }
                else
                {
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
                            return LoadSCFAReplayFromStream(replayStream);
                        }
                    }
                }
            }
        }

        public static Replay LoadSCFAReplayFromStream(Stream stream)
        {
            using (ReplayBinaryReader reader = new ReplayBinaryReader(stream))
            {
                return ParseReplay(reader);
            }
        }

        /// <summary>
        /// Loads a replay from disk that is expected to be in the compressed format of FAForever.
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
