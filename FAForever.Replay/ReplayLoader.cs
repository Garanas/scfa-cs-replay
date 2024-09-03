
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

            Boolean addToQueue = reader.ReadByte() > 0;

            return new CommandData
            {
                AddToQueue = addToQueue,
                BlueprintId = blueprintId,
                Formation = formation,
                Identifier = commandId,
                LuaParameters = luaData,
                Type = commandType,

                Unknown1 = arg1,
                Unknown2 = arg2,
                Unknown3 = arg3,
                Unknown4 = arg4,
                Unknown5 = arg5,
                Unknown6 = arg6,
            };
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
                    return new CommandTarget.Entity
                    {
                        EntityId = reader.ReadInt32()
                    };

                case CommandTargetType.Position:
                    return new CommandTarget.Position
                    {
                        X = reader.ReadSingle(),
                        Y = reader.ReadSingle(),
                        Z = reader.ReadSingle()
                    };

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
                    return new ReplayInput.Advance
                    {
                        TicksToAdvance = reader.ReadInt32()
                    };

                case ReplayInputType.SetCommandSource:
                    return new ReplayInput.SetCommandSource { SourceId = reader.ReadByte() };

                case ReplayInputType.CommandSourceTerminated:
                    return new ReplayInput.CommandSourceTerminated();

                case ReplayInputType.VerifyChecksum:
                    byte[] hash = reader.ReadBytes(16);
                    int tick = reader.ReadInt32();
                    return new ReplayInput.VerifyChecksum { Hash = hash, Tick = tick };

                case ReplayInputType.RequestPause:
                    return new ReplayInput.RequestPause();

                case ReplayInputType.RequestResume:
                    return new ReplayInput.RequestResume();

                case ReplayInputType.SingleStep:
                    return new ReplayInput.SingleStep();

                case ReplayInputType.CreateUnit:
                    return new ReplayInput.CreateUnit
                    {
                        ArmyId = reader.ReadByte(),
                        BlueprintId = reader.ReadNullTerminatedString(),
                        X = reader.ReadSingle(),
                        Z = reader.ReadSingle(),
                        Heading = reader.ReadSingle()
                    };

                case ReplayInputType.CreateProp:
                    return new ReplayInput.CreateProp
                    {
                        BlueprintId = reader.ReadNullTerminatedString(),
                        X = reader.ReadSingle(),
                        Z = reader.ReadSingle(),
                        Heading = reader.ReadSingle()
                    };

                case ReplayInputType.DestroyEntity:
                    return new ReplayInput.DestroyEntity { EntityId = reader.ReadInt32() };

                case ReplayInputType.WarpEntity:
                    return new ReplayInput.WarpEntity
                    {
                        EntityId = reader.ReadInt32(),
                        X = reader.ReadSingle(),
                        Y = reader.ReadSingle(),
                        Z = reader.ReadSingle()
                    };

                case ReplayInputType.ProcessInfoPair:
                    int entityId = reader.ReadInt32();
                    string name = reader.ReadNullTerminatedString();
                    string value = reader.ReadNullTerminatedString();
                    return new ReplayInput.ProcessInfoPair
                    {
                        EntityId = entityId,
                        Name = name,
                        Value = value
                    };

                case ReplayInputType.IssueCommand:
                    return new ReplayInput.IssueCommand
                    {
                        Units = ParseEventCommandUnits(reader),
                        Data = ParseEventCommandData(reader)
                    };

                case ReplayInputType.IssueFactoryCommand:
                    return new ReplayInput.IssueFactoryCommand
                    {
                        Factories = ParseEventCommandUnits(reader),
                        Data = ParseEventCommandData(reader)
                    };

                case ReplayInputType.IncreaseCommandCount:
                    return new ReplayInput.IncreaseCommandCount
                    {
                        CommandId = reader.ReadInt32(),
                        Delta = reader.ReadInt32()
                    };

                case ReplayInputType.DecreaseCommandCount:
                    return new ReplayInput.DecreaseCommandCount(
                        CommandId: reader.ReadInt32(),
                        Delta: reader.ReadInt32()
                    );
                case ReplayInputType.UpdateCommandTarget:
                    return new ReplayInput.UpdateCommandTarget
                    {
                        CommandId = reader.ReadInt32(),
                        Target = ParseEventCommandTarget(reader)
                    };

                case ReplayInputType.UpdateCommandType:
                    return new ReplayInput.UpdateCommandType
                    {
                        CommandId = reader.ReadInt32(),
                        Type = (CommandType)reader.ReadInt32()
                    };

                case ReplayInputType.UpdateCommandParameters:
                    return new ReplayInput.UpdateCommandLuaParameters
                    {
                        CommandId = reader.ReadInt32(),
                        LuaParameters = LuaDataLoader.ReadLuaData(reader),
                        X = reader.ReadSingle(),
                        Y = reader.ReadSingle(),
                        Z = reader.ReadSingle()
                    };

                case ReplayInputType.RemoveFromCommandQueue:
                    return new ReplayInput.RemoveCommandFromQueue
                    {
                        CommandId = reader.ReadInt32(),
                        EntityId = reader.ReadInt32()
                    };

                case ReplayInputType.DebugCommand:
                    return new ReplayInput.DebugCommand
                    {
                        Command = reader.ReadNullTerminatedString(),
                        X = reader.ReadSingle(),
                        Y = reader.ReadSingle(),
                        Z = reader.ReadSingle(),
                        FocusArmy = reader.ReadByte(),
                        Units = ParseEventCommandUnits(reader)
                    };

                case ReplayInputType.ExecuteLuaInSim:
                    return new ReplayInput.ExecuteLuaInSim
                    {
                        LuaCode = reader.ReadNullTerminatedString()
                    };

                case ReplayInputType.Simcallback:
                    string endpoint = reader.ReadNullTerminatedString();
                    LuaData luaParameters = LuaDataLoader.ReadLuaData(reader);
                    CommandUnits units = ParseEventCommandUnits(reader);

                    return new ReplayInput.SimCallback
                    {
                        Endpoint = endpoint,
                        LuaParameters = luaParameters,
                        Units = units,
                    };

                case ReplayInputType.EndGame:
                    return new ReplayInput.EndGame();

                default:
                    throw new Exception("Unknown replay input type");
            }
        }

        public static List<ReplayInput> LoadReplayInputs(ReplayBinaryReader reader) {
            List<ReplayInput> replayInputs = new List<ReplayInput>();
            while(reader.BaseStream.Position < reader.BaseStream.Length) {
                long position = reader.BaseStream.Position;

                ReplayInputType type = (ReplayInputType)reader.ReadByte();
                // includes the type and this number of bytes, which is a bit confusing.
                int numberOfBytes = reader.ReadInt16();

                ReplayInput replayInput = LoadReplayInput(reader, type);

                if (reader.BaseStream.Position < position + numberOfBytes)
                {
                    reader.ReadBytes(numberOfBytes - (int)(reader.BaseStream.Position - position));
                } else if (reader.BaseStream.Position > position + numberOfBytes)
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
            String replayVersion= replayVersionAndScenario[0];
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
            for(int i = 0; i < numberOfPlayerOptions; i++)
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

            while(true)
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
            switch(extension)
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
