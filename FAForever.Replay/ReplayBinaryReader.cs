
using System;
using System.IO;
using System.Text;

namespace FAForever.Replay
{
    public class ReplayBinaryReader : BinaryReader
    {
        public ReplayBinaryReader(Stream input) : base(input) { }

        public ReplayBinaryReader(Stream input, Encoding encoding) : base(input, encoding) { }

        public ReplayBinaryReader(Stream input, Encoding encoding, bool leaveOpen) : base(input, encoding, leaveOpen) { }

        /// <summary>
        /// Reads bytes until it finds a null byte. Advances the stream with the size of the string.
        /// </summary>
        /// <returns></returns>
        public string ReadNullTerminatedString()
        {
            StringBuilder sb = new StringBuilder();

            char c;
            while ((c = this.ReadChar()) != '\0')
            {
                sb.Append(c);
            }


            return sb.ToString();
        }
    }
}
