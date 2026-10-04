
using System.Globalization;
using FAForever.FileFormats.Lua;

namespace FAForever.FileFormats.Replay
{
    public static class LuaDataLoader
    {
        // Lua values are immutable records, so the values without content can be shared.
        private static readonly LuaData.Nil Nil = new LuaData.Nil();
        private static readonly LuaData.Bool True = new LuaData.Bool(true);
        private static readonly LuaData.Bool False = new LuaData.Bool(false);

        /// <summary>
        /// Array-like tables use the keys 1, 2, 3, ... Caching their string representation avoids an allocation per entry.
        /// </summary>
        private static readonly string[] IntegerKeys = Enumerable.Range(0, 257).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();

        public static LuaData ReadLuaData(ReplayBinaryReader reader)
        {
            LuaDataType type = (LuaDataType)reader.ReadByte();

            switch (type)
            {
                case LuaDataType.Nil:
                    return Nil;

                case LuaDataType.Bool:
                    // Note: 0 is false, anything else is true. The reference implementation
                    // (faf-java-commons LoadUtils.parseLua) reads this inverted; verified
                    // against replays where known human players must have Human == true.
                    return reader.ReadByte() != 0 ? True : False;

                case LuaDataType.Number:
                    return new LuaData.Number(reader.ReadSingle());

                case LuaDataType.String:
                    return new LuaData.String(reader.ReadNullTerminatedString());

                case LuaDataType.TableStart:
                    Dictionary<String, LuaData> table = new Dictionary<String, LuaData>();
                    while (true)
                    {
                        // read the key directly, without allocating a Lua value for it
                        LuaDataType keyType = (LuaDataType)reader.ReadByte();
                        switch (keyType)
                        {
                            case LuaDataType.String:
                                table.Add(reader.ReadNullTerminatedString(), ReadLuaData(reader));
                                break;

                            case LuaDataType.Number:
                                int index = (int)reader.ReadSingle();
                                string key = (uint)index < (uint)IntegerKeys.Length ? IntegerKeys[index] : index.ToString(CultureInfo.InvariantCulture);
                                table.Add(key, ReadLuaData(reader));
                                break;

                            case LuaDataType.Nil:
                            case LuaDataType.TableEnd:
                                return new LuaData.Table(table);

                            default:
                                throw new Exception("Invalid key type in table");
                        }
                    }

                case LuaDataType.TableEnd:
                    return Nil;

                default:
                    throw new Exception("Invalid LuaDataType");
            }
        }

    }
}
