
using System.Data;
using System.Text;
using ZstdSharp;

using System.Text.Json;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;

namespace FAForever.Replay
{
    public static class ReplayLoader
    {

        public static List<EventToken> TokenizeBody(ReplayBinaryReader reader)
        {
            int tokenHeaderLength = 3;


            List<EventToken> tokens = new List<EventToken>();
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                EventType type = (EventType)reader.ReadByte();
                int numberOfBytes = reader.ReadInt16();
                byte[] bytes = reader.ReadBytes(numberOfBytes - tokenHeaderLength);
                tokens.Add(new EventToken(type, bytes));
            }

            return tokens;
        }

        public static EventCommandData ParseEventCommandData(ReplayBinaryReader reader)
        {
            int commandId = reader.ReadInt32();

            // unknown
            byte[] arg1 = reader.ReadBytes(4);

            EventCommandType commandType = (EventCommandType)reader.ReadByte();

            // unknown
            byte[] arg2 = reader.ReadBytes(4);

            EventCommandTarget target = ParseEventCommandTarget(reader);

            // unknown
            byte[] arg3 = reader.ReadBytes(1);

            EventCommandFormation formation = ParseEventCommandFormation(reader);

            string blueprintId = reader.ReadSCStringNullTerminated();

            // unknown
            byte[] arg4 = reader.ReadBytes(12);

            LuaData luaData = reader.ReadLuaData();

            Boolean addToQueue = reader.ReadByte() > 0;

            return new EventCommandData
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
            };
        }

        public static EventCommandUnits ParseEventCommandUnits(ReplayBinaryReader reader)
        {
            int numberOfEntities = reader.ReadInt32();
            int[] entityIds = new int[numberOfEntities];
            for (int i = 0; i < numberOfEntities; i++)
            {
                entityIds[i] = reader.ReadInt32();
            }

            return new EventCommandUnits(entityIds);
        }

        public static EventCommandTarget ParseEventCommandTarget(ReplayBinaryReader reader)
        {
            EventCommandTargetType eventCommandTargetType = (EventCommandTargetType)reader.ReadByte();
            switch (eventCommandTargetType)
            {
                case EventCommandTargetType.Entity:
                    return new EventCommandTarget.Entity
                    {
                        EntityId = reader.ReadInt32()
                    };

                case EventCommandTargetType.Position:
                    return new EventCommandTarget.Position
                    {
                        X = reader.ReadSingle(),
                        Y = reader.ReadSingle(),
                        Z = reader.ReadSingle()
                    };

                default:
                    return new EventCommandTarget.None();
            }
        }

        public static EventCommandFormation ParseEventCommandFormation(ReplayBinaryReader reader)
        {
            int formationId = reader.ReadInt32();
            if (formationId == -1)
            {
                return new EventCommandFormation.NoFormation();
            }

            return new EventCommandFormation.Formation(
                FormationIdentifier: formationId,
                               Heading: reader.ReadSingle(),
                                              X: reader.ReadSingle(),
                                                             Y: reader.ReadSingle(),
                                                                            Z: reader.ReadSingle(),
                                                                                           Scale: reader.ReadSingle()
                                                                                                      );
        }

        public static EventInstance ParseToken(EventToken token)
        {
            using (ReplayBinaryReader reader = new ReplayBinaryReader(new MemoryStream(token.Bytes)))
            {
                switch (token.Type)
                {
                    case EventType.Advance:
                        return new EventInstance.Advance {
                            TicksToAdvance = reader.ReadInt32()
                        };

                    case EventType.SetCommandSource:
                        return new EventInstance.SetCommandSource { SourceId = reader.ReadByte() };

                    case EventType.CommandSourceTerminated:
                        return new EventInstance.CommandSourceTerminated();

                    case EventType.VerifyChecksum:
                        byte[] hash = reader.ReadBytes(16);
                        int tick = reader.ReadInt32();
                        return new EventInstance.VerifyChecksum { Hash = hash, Tick = tick };

                    case EventType.RequestPause:
                        return new EventInstance.RequestPause();

                    case EventType.RequestResume:
                        return new EventInstance.RequestResume();

                    case EventType.SingleStep:
                        return new EventInstance.SingleStep();

                    case EventType.CreateUnit:
                        return new EventInstance.CreateUnit
                        {
                            ArmyId = reader.ReadByte(),
                            BlueprintId = reader.ReadSCStringNullTerminated(),
                            X = reader.ReadSingle(),
                            Z = reader.ReadSingle(),
                            Heading = reader.ReadSingle()
                        };

                    case EventType.CreateProp:
                        return new EventInstance.CreateProp
                        {
                            BlueprintId = reader.ReadSCStringNullTerminated(),
                            X = reader.ReadSingle(),
                            Z = reader.ReadSingle(),
                            Heading = reader.ReadSingle()
                        };

                    case EventType.DestroyEntity:
                        return new EventInstance.DestroyEntity { EntityId = reader.ReadInt32() };

                    case EventType.WarpEntity:
                        return new EventInstance.WarpEntity
                        {
                            EntityId = reader.ReadInt32(),
                            X = reader.ReadSingle(),
                            Y = reader.ReadSingle(),
                            Z = reader.ReadSingle()
                        };

                    case EventType.ProcessInfoPair:
                        return new EventInstance.ProcessInfoPair
                        {
                            Arg1 = reader.ReadSCStringNullTerminated(),
                            Arg2 = reader.ReadSCStringNullTerminated()
                        };

                    case EventType.IssueCommand:
                        return new EventInstance.IssueCommand
                        {
                            Units = ParseEventCommandUnits(reader),
                            Data = ParseEventCommandData(reader)
                        };

                    case EventType.IssueFactoryCommand:
                        return new EventInstance.IssueFactoryCommand
                        {
                            Factories = ParseEventCommandUnits(reader),
                            Data = ParseEventCommandData(reader)
                        };

                    case EventType.IncreaseCommandCount:
                        return new EventInstance.IncreaseCommandCount
                        {
                            CommandId = reader.ReadInt32(),
                            Delta = reader.ReadInt32()
                        };

                    case EventType.DecreaseCommandCount:
                        return new EventInstance.DecreaseCommandCount(
                            CommandId: reader.ReadInt32(),
                            Delta: reader.ReadInt32()
                        );
                    case EventType.UpdateCommandTarget:
                        return new EventInstance.UpdateCommandTarget
                        {
                            CommandId = reader.ReadInt32(),
                            Target = ParseEventCommandTarget(reader)
                        };

                    case EventType.UpdateCommandType:
                        return new EventInstance.UpdateCommandType
                        {
                            CommandId = reader.ReadInt32(),
                            Type = (EventCommandType)reader.ReadInt32()
                        };

                    case EventType.UpdateCommandParameters:
                        return new EventInstance.UpdateCommandLuaParameters
                        {
                            CommandId = reader.ReadInt32(),
                            LuaParameters = reader.ReadLuaData(),
                            X = reader.ReadSingle(),
                            Y = reader.ReadSingle(),
                            Z = reader.ReadSingle()
                        };

                    case EventType.RemoveFromCommandQueue:
                        return new EventInstance.RemoveCommandFromQueue
                        {
                            CommandId = reader.ReadInt32(),
                            EntityId = reader.ReadInt32()
                        };

                    case EventType.DebugCommand:
                        return new EventInstance.DebugCommand
                        {
                            Command = reader.ReadSCStringNullTerminated(),
                            X = reader.ReadSingle(),
                            Y = reader.ReadSingle(),
                            Z = reader.ReadSingle(),
                            FocusArmy = reader.ReadByte(),
                            Units = ParseEventCommandUnits(reader)
                        };

                    case EventType.ExecuteLuaInSim:
                        return new EventInstance.ExecuteLuaInSim
                        {
                            LuaCode = reader.ReadSCStringNullTerminated()
                        };

                    case EventType.Simcallback:
                        string endpoint = reader.ReadSCStringNullTerminated();
                        LuaData luaParameters = reader.ReadLuaData();
                        EventCommandUnits units = ParseEventCommandUnits(reader);

                        byte[] unknown1 = reader.ReadBytes(4);
                        byte[] unknown2 = reader.ReadBytes(3);


                        return new EventInstance.SimCallback
                        {
                            Endpoint = endpoint,
                            LuaParameters = luaParameters,
                            Units = units,

                            Unknown1 = unknown1,
                            Unknown2 = unknown2
                        };

                    case EventType.EndGame:
                        return new EventInstance.EndGame();

                    default: 
                        return new EventInstance.Unknown(token.Type, token.Bytes);
                }
            }
        }

        public static List<EventInstance> ParseTokens(List<EventToken> tokens)
        {
            return tokens.Select(ParseToken).ToList();
        }

        public static ReplayHeader ParseReplayHeader(ReplayBinaryReader reader)
        {
            string gameVersion = reader.ReadSCStringNullTerminated();

            // Always \r\n
            string Unknown1 = reader.ReadSCStringNullTerminated();

            String[] replayVersionAndScenario = reader.ReadSCStringNullTerminated().Split("\r\n");
            String replayVersion= replayVersionAndScenario[0];
            String pathToScenario = replayVersionAndScenario[1];

            // Always \r\n and an unknown character
            string Unknown2 = reader.ReadSCStringNullTerminated();

            int numberOfBytesForMods = reader.ReadInt32();
            byte[] mods = reader.ReadBytes(numberOfBytesForMods);

            int numberOfBytesForGameOptions = reader.ReadInt32();
            byte[] gameOptions = reader.ReadBytes(numberOfBytesForGameOptions);

            byte numberOfClients = reader.ReadByte();
            ReplaySource[] clients = new ReplaySource[numberOfClients];
            for (int i = 0; i < numberOfClients; i++)
            {
                clients[i] = new ReplaySource(PlayerName: reader.ReadSCStringNullTerminated(), PlayerId: reader.ReadInt32());
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
            List<EventToken> tokens = TokenizeBody(reader);
            List<EventInstance> replayEvents = ParseTokens(tokens);
            List<GameEvent> gameEvents = ReplaySemantics.ConvertToGameEvents(replayEvents);

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
                    }
                }
                else
                {
                    string base64 = new StreamReader(stream).ReadToEnd();
                    byte[] bytes = Convert.FromBase64String(base64);
                    byte[] skipped = new byte[bytes.Length - 4];
                    Array.Copy(bytes, 4, skipped, 0, skipped.Length);
                    using (MemoryStream memoryStream = new MemoryStream(skipped))
                    {
                        using (InflaterInputStream decompressor = new InflaterInputStream(memoryStream))
                        {
                            decompressor.CopyTo(replayStream);
                            replayStream.Position = 0;
                        }
                    }
                }


                return LoadSCFAReplayFromStream(replayStream);
            }

        }

        public static Replay LoadSCFAReplayFromStream(Stream stream)
        {
            using (ReplayBinaryReader reader = new ReplayBinaryReader(stream))
            {
                return ParseReplay(reader);
            }
        }

        public static Replay LoadFAFReplayFromDisk(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open))
            {
                return LoadFAFReplayFromMemory(stream);
            }

        }

        public static Replay LoadSCFAReplayFromDisk(string path)
        {
            using (FileStream reader = new FileStream(path, FileMode.Open))
            {
                return LoadSCFAReplayFromStream(reader);
            }
        }
    }
}
