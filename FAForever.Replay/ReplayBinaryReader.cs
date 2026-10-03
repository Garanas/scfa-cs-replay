
using System;
using System.Buffers;
using System.IO;
using System.Text;

namespace FAForever.Replay
{
    public class ReplayBinaryReader : BinaryReader
    {
        /// <summary>
        /// The underlying buffer of the stream, when the stream is a memory stream that exposes it. Allows reading strings without going through the stream byte by byte.
        /// </summary>
        private readonly byte[]? Buffer;

        /// <summary>
        /// The offset of the start of the stream in <see cref="Buffer"/>.
        /// </summary>
        private readonly int BufferOrigin;

        /// <summary>
        /// Strings up to this number of bytes are interned: blueprint ids, sim callback endpoints and
        /// Lua table keys repeat thousands of times in a replay. Longer strings (chat, Lua code) are not.
        /// </summary>
        private const int MaxInternedLength = 64;

        /// <summary>
        /// The interned strings of this reader. The cache lives as long as the reader, which is one replay.
        /// </summary>
        private readonly Dictionary<string, string> InternedStrings = new Dictionary<string, string>();

        private readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> InternedStringsLookup;

        public ReplayBinaryReader(Stream input) : this(input, Encoding.UTF8, false) { }

        public ReplayBinaryReader(Stream input, Encoding encoding) : this(input, encoding, false) { }

        public ReplayBinaryReader(Stream input, Encoding encoding, bool leaveOpen) : base(input, encoding, leaveOpen)
        {
            InternedStringsLookup = InternedStrings.GetAlternateLookup<ReadOnlySpan<char>>();
            if (input is MemoryStream memoryStream && memoryStream.TryGetBuffer(out ArraySegment<byte> segment))
            {
                Buffer = segment.Array;
                BufferOrigin = segment.Offset;
            }
        }

        /// <summary>
        /// Reads consecutive little-endian 32-bit integers into <paramref name="destination"/>, in one
        /// copy when the stream is backed by a buffer.
        /// </summary>
        public void ReadInt32s(Span<int> destination)
        {
            int length = destination.Length * sizeof(int);
            if (Buffer != null && BitConverter.IsLittleEndian)
            {
                long position = BaseStream.Position;
                if (position + length > BaseStream.Length)
                {
                    throw new EndOfStreamException();
                }

                System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(Buffer.AsSpan(BufferOrigin + (int)position, length))
                    .CopyTo(destination);
                BaseStream.Position = position + length;
                return;
            }

            for (int i = 0; i < destination.Length; i++)
            {
                destination[i] = ReadInt32();
            }
        }

        /// <summary>
        /// Reads bytes until it finds a null byte. Advances the stream with the size of the string.
        /// </summary>
        /// <returns></returns>
        public string ReadNullTerminatedString()
        {
            // We're dealing with null terminated strings: we do not know how long a string
            // is until we've found the terminator. When the stream is backed by a buffer we
            // search for the terminator directly in the buffer (vectorized) and decode the
            // string from there, which avoids a (virtual) call per byte.
            if (Buffer != null)
            {
                long position = BaseStream.Position;
                ReadOnlySpan<byte> remaining = Buffer.AsSpan(BufferOrigin + (int)position, (int)(BaseStream.Length - position));
                int length = remaining.IndexOf((byte)0);
                if (length < 0)
                {
                    throw new EndOfStreamException("Unable to find the end of a null terminated string.");
                }

                BaseStream.Position = position + length + 1;
                return length <= MaxInternedLength ? Intern(remaining[..length]) : Encoding.UTF8.GetString(remaining[..length]);
            }

            return ReadNullTerminatedStringFromStream();
        }

        /// <summary>
        /// Decodes the bytes and returns the interned string, allocating only the first time a string is seen.
        /// </summary>
        private string Intern(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty)
            {
                return string.Empty;
            }

            // UTF-8 never decodes to more characters than it has bytes
            Span<char> characters = stackalloc char[MaxInternedLength];
            characters = characters[..Encoding.UTF8.GetChars(bytes, characters)];
            if (!InternedStringsLookup.TryGetValue(characters, out string? value))
            {
                value = new string(characters);
                InternedStrings.Add(value, value);
            }

            return value;
        }

        /// <summary>
        /// Fallback for streams without an accessible buffer: reads the stream twice, once to determine the length of the string and once to read it.
        /// </summary>
        private string ReadNullTerminatedStringFromStream()
        {
            // determine the length
            long start = this.BaseStream.Position;
            while (this.ReadByte() != 0) ;
            long end = this.BaseStream.Position;
            int length = (int)(end - start - 1);

            // small strings live on the stack, large strings in a pooled array
            byte[]? rented = null;
            Span<byte> buffer = length <= 256 ? stackalloc byte[256] : (rented = ArrayPool<byte>.Shared.Rent(length));
            buffer = buffer[..length];

            this.BaseStream.Position = start;
            this.BaseStream.ReadExactly(buffer);
            this.BaseStream.Position = end;

            string value = Encoding.UTF8.GetString(buffer);
            if (rented != null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }

            return value;
        }
    }
}
