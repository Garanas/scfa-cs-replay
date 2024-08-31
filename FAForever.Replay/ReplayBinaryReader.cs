
using System;
using System.IO;
using System.Text;


namespace FAForever.Replay
{
    public class ReplayBinaryReader : BinaryReader
    {
        public ReplayBinaryReader(Stream input) : base(input)
        {
            
        }

        public ReplayBinaryReader(Stream input, Encoding encoding) : base(input, encoding)
        {
            
        }

        public ReplayBinaryReader(Stream input, Encoding encoding, bool leaveOpen) : base(input, encoding, leaveOpen)
        {
        }

        /// <summary>
        /// Reads an integer and interprets that as the number of bytes of the string. Advances the string with the size of the string.
        /// </summary>
        /// <returns></returns>
        public string ReadSCString()
        {
            int numberOfBytes = this.ReadInt32();
            return Encoding.ASCII.GetString(this.ReadBytes(numberOfBytes));
        }

        /// <summary>
        /// Reads bytes until it finds a null byte. Advances the stream with the size of the string.
        /// </summary>
        /// <returns></returns>
        public string ReadSCStringNullTerminated()
        {
            List<byte> bytes = new List<byte>();
            byte b;
            while ((b = this.ReadByte()) != 0)
            {
                bytes.Add(b);
            }

            return Encoding.ASCII.GetString(bytes.ToArray());
        }
         
        public LuaData ReadLuaData()
        {
            LuaDataType type = (LuaDataType)this.ReadByte();

            switch(type)
            {
                case LuaDataType.Nil:
                    return new LuaData.Nil();

                case LuaDataType.Bool:
                    return new LuaData.Bool(this.ReadByte() == 0);

                case LuaDataType.Number:
                    return new LuaData.Number(this.ReadSingle());

                case LuaDataType.String:
                    return new LuaData.String(this.ReadSCStringNullTerminated());

                case LuaDataType.TableStart:
                    Dictionary<String, LuaData> table = new Dictionary<String, LuaData>();
                    while (true)
                    {
                        LuaData key = this.ReadLuaData();
                        switch (key)
                        {
                            case LuaData.String s:
                                table.Add(s.Value, this.ReadLuaData());
                                break;

                            case LuaData.Number n:
                                table.Add(((int)n.Value).ToString(), this.ReadLuaData());
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
