
namespace FAForever.Replay
{
    public static class LuaDataLoader
    {

        public static LuaData ReadLuaData(ReplayBinaryReader reader)
        {
            LuaDataType type = (LuaDataType)reader.ReadByte();

            switch (type)
            {
                case LuaDataType.Nil:
                    return new LuaData.Nil();

                case LuaDataType.Bool:
                    return new LuaData.Bool(reader.ReadByte() == 0);

                case LuaDataType.Number:
                    return new LuaData.Number(reader.ReadSingle());

                case LuaDataType.String:
                    return new LuaData.String(reader.ReadNullTerminatedString());

                case LuaDataType.TableStart:
                    Dictionary<String, LuaData> table = new Dictionary<String, LuaData>();
                    while (true)
                    {
                        LuaData key = ReadLuaData(reader);
                        switch (key)
                        {
                            case LuaData.String s:
                                table.Add(s.Value, ReadLuaData(reader));
                                break;

                            case LuaData.Number n:
                                table.Add(((int)n.Value).ToString(), ReadLuaData(reader));
                                break;

                            case LuaData.Nil:
                                return new LuaData.Table(table);

                            default:
                                throw new Exception("Invalid key type in table");
                        }
                    }

                    throw new Exception("Invalid state exception");

                case LuaDataType.TableEnd:
                    return new LuaData.Nil();

                default:
                    throw new Exception("Invalid LuaDataType");
            }
        }

    }
}
