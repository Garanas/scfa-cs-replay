using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace FAForever.FileFormats.Map
{
    /// <summary>
    /// A file in a map archive (the vault's <c>.zip</c> of a map folder).
    /// </summary>
    /// <param name="Name">The path in the archive, e.g. <c>theta_passage.v0001/theta_passage_save.lua</c>.</param>
    /// <param name="Method">The compression: 0 stored, 8 deflated.</param>
    /// <param name="LocalHeaderOffset">Where the file's local header starts in the archive.</param>
    public sealed record MapArchiveEntry(string Name, int Method, long CompressedSize, long UncompressedSize, long LocalHeaderOffset)
    {
        /// <summary>
        /// How many bytes from <see cref="LocalHeaderOffset"/> hold the local header and the
        /// compressed data, assuming a local extra field of at most <paramref name="extraAllowance"/> bytes.
        /// </summary>
        public long SpanLength(int extraAllowance = 1024) => MapArchive.LocalHeaderSize + Encoding.UTF8.GetByteCount(Name) + extraAllowance + CompressedSize;
    }

    /// <summary>
    /// Reads the files of a map archive piece by piece, so a caller can fetch only the bytes it
    /// needs (with range requests): first the end of the archive, which holds the directory, then
    /// the files it wants. Map folders often hold large textures besides the three files a map is
    /// made of. ZIP64 archives are not supported; no map comes near 4 GB.
    /// </summary>
    public static class MapArchive
    {
        /// <summary>The size of a local file header before its name and extra field.</summary>
        public const int LocalHeaderSize = 30;

        /// <summary>
        /// How much of the end of an archive to read to find its directory: the end record (22 bytes
        /// plus a comment of up to 64 KB) and, for a map, usually the whole directory.
        /// </summary>
        public const int TailLength = 64 * 1024;

        private const uint EndOfDirectorySignature = 0x06054b50;
        private const uint DirectoryEntrySignature = 0x02014b50;
        private const uint LocalHeaderSignature = 0x04034b50;

        /// <summary>
        /// Finds the directory in the last bytes of an archive.
        /// </summary>
        /// <param name="tail">The last bytes of the archive.</param>
        /// <param name="tailOffset">Where <paramref name="tail"/> starts in the archive.</param>
        /// <returns>The directory's offset and length in the archive.</returns>
        public static (long Offset, long Length) FindDirectory(ReadOnlySpan<byte> tail, long tailOffset)
        {
            for (int i = tail.Length - 22; i >= 0; i--)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(tail[i..]) != EndOfDirectorySignature)
                {
                    continue;
                }

                uint length = BinaryPrimitives.ReadUInt32LittleEndian(tail[(i + 12)..]);
                uint offset = BinaryPrimitives.ReadUInt32LittleEndian(tail[(i + 16)..]);
                if (offset == uint.MaxValue || length == uint.MaxValue)
                {
                    throw new FormatException("ZIP64 archives are not supported");
                }
                return (offset, length);
            }

            throw new FormatException("Not a zip archive: the end of the directory is missing");
        }

        /// <summary>
        /// Reads the entries of a directory.
        /// </summary>
        /// <param name="directory">The bytes of the directory, as located by <see cref="FindDirectory"/>.</param>
        public static IReadOnlyList<MapArchiveEntry> ReadDirectory(ReadOnlySpan<byte> directory)
        {
            List<MapArchiveEntry> entries = [];
            int position = 0;
            while (position + 46 <= directory.Length && BinaryPrimitives.ReadUInt32LittleEndian(directory[position..]) == DirectoryEntrySignature)
            {
                ReadOnlySpan<byte> header = directory[position..];
                int method = BinaryPrimitives.ReadUInt16LittleEndian(header[10..]);
                long compressed = BinaryPrimitives.ReadUInt32LittleEndian(header[20..]);
                long uncompressed = BinaryPrimitives.ReadUInt32LittleEndian(header[24..]);
                int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);
                int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[30..]);
                int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
                long offset = BinaryPrimitives.ReadUInt32LittleEndian(header[42..]);
                if (compressed == uint.MaxValue || uncompressed == uint.MaxValue || offset == uint.MaxValue)
                {
                    throw new FormatException("ZIP64 archives are not supported");
                }

                string name = Encoding.UTF8.GetString(header.Slice(46, nameLength));
                entries.Add(new MapArchiveEntry(name, method, compressed, uncompressed, offset));
                position += 46 + nameLength + extraLength + commentLength;
            }
            return entries;
        }

        /// <summary>
        /// Unpacks a file from the bytes that start at its local header.
        /// </summary>
        /// <param name="local">The archive's bytes from <see cref="MapArchiveEntry.LocalHeaderOffset"/> on, at
        /// least the header and the compressed data.</param>
        public static byte[] ReadEntry(ReadOnlySpan<byte> local, MapArchiveEntry entry)
        {
            if (local.Length < LocalHeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(local) != LocalHeaderSignature)
            {
                throw new FormatException($"No local header for {entry.Name}");
            }

            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(local[26..]);
            int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(local[28..]);
            long start = LocalHeaderSize + nameLength + extraLength;
            if (start + entry.CompressedSize > local.Length)
            {
                throw new FormatException($"The data of {entry.Name} is incomplete: {local.Length} bytes, {start + entry.CompressedSize} needed");
            }

            ReadOnlySpan<byte> data = local.Slice((int)start, (int)entry.CompressedSize);
            switch (entry.Method)
            {
                case 0:
                    return data.ToArray();
                case 8:
                    using (MemoryStream compressed = new MemoryStream(data.ToArray()))
                    using (DeflateStream inflater = new DeflateStream(compressed, CompressionMode.Decompress))
                    using (MemoryStream output = new MemoryStream((int)entry.UncompressedSize))
                    {
                        inflater.CopyTo(output);
                        return output.ToArray();
                    }
                default:
                    throw new FormatException($"Compression method {entry.Method} of {entry.Name} is not supported");
            }
        }

        /// <summary>
        /// The scenario of the map: the <c>_scenario.lua</c> closest to the root of the archive (some
        /// archives hold variants of the map in subfolders).
        /// </summary>
        public static MapArchiveEntry? FindScenario(IReadOnlyList<MapArchiveEntry> entries) => entries
            .Where(entry => entry.Name.EndsWith("_scenario.lua", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Name.Count(c => c == '/'))
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .FirstOrDefault();

        /// <summary>
        /// The entry of a path as the game writes it in a scenario, e.g.
        /// <c>/maps/theta_passage.v0001/theta_passage_save.lua</c>; case is ignored, as the game does.
        /// When the folder differs (a map renamed after it was made), the file name in the
        /// scenario's folder is used.
        /// </summary>
        public static MapArchiveEntry? FindByGamePath(IReadOnlyList<MapArchiveEntry> entries, string gamePath, MapArchiveEntry scenario)
        {
            string path = gamePath.Replace('\\', '/').TrimStart('/');
            if (path.StartsWith("maps/", StringComparison.OrdinalIgnoreCase))
            {
                path = path["maps/".Length..];
            }

            MapArchiveEntry? exact = entries.FirstOrDefault(entry => string.Equals(entry.Name, path, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }

            int folderEnd = scenario.Name.LastIndexOf('/');
            string folder = folderEnd >= 0 ? scenario.Name[..(folderEnd + 1)] : "";
            string file = path[(path.LastIndexOf('/') + 1)..];
            return entries.FirstOrDefault(entry => string.Equals(entry.Name, folder + file, StringComparison.OrdinalIgnoreCase));
        }
    }
}
