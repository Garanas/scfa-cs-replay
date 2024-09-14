
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
            // the implementation is complicated because it favors performance over 
            // readability. We're dealing with null terminated strings. That means we
            // do not know how long a string is until we've processed it as a whole.
            // 
            // There are various ways to solve this problem:
            // 1. Read and store each character in a (byte) array until we find the null character ('\0').
            // 2. Read and store each character in a string builder until we find the null character ('\0').
            // 3. Read the stream twice: once to determine the length of the string, and a second time to read the string in one go.
            // 
            // We chose (3) because it is the most performant solution. To prevent 
            // access to the heap, we allocate a buffer on the stack. From an allocation
            // point of view that is not much different from sharing a reference to a byte 
            // array and/or a string builder, but this approach is thread safe and it 
            // performs better in terms of computing time. 

            // determine the length
            long start = this.BaseStream.Position;
            while (this.ReadByte() != 0);
            long end = this.BaseStream.Position;

            // read it into a buffer that lives on the stack
            int diff = (int)(end - start - 1);
            Span<byte> buffer = stackalloc byte[(int)(end - start - 1)];
            this.BaseStream.Position = start;
            for (int k = 0; k < diff; k++) {
                buffer[k] = this.ReadByte();
            }

            // reset the stream
            this.BaseStream.Position = end;

            // interpret the buffer
            return Encoding.UTF8.GetString(buffer);
        }
    }
}
