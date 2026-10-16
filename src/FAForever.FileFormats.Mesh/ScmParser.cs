using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace FAForever.FileFormats.Mesh
{
    /// <summary>
    /// Thrown when the bytes are not a mesh this parser understands.
    /// </summary>
    public sealed class ScmFormatException(string message, long offset)
        : FormatException($"At byte {offset}: {message}")
    {
        /// <summary>
        /// The position in the file where the problem was found.
        /// </summary>
        public long Offset { get; } = offset;
    }

    /// <summary>
    /// Reads the mesh format of the game (<c>.scm</c>), version 5.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A header of twelve 32-bit numbers (<c>MODL</c>, the version, then the offset or size of each
    /// section) and the sections it points to: the bone names (<c>NAME</c>, right after the header,
    /// at byte 0x40), the bones (<c>SKEL</c>), the vertices (<c>VTXL</c>), the triangles
    /// (<c>TRIS</c>), extra vertex data (<c>VEXT</c>, which no exporter writes) and the exporter's
    /// notes (<c>INFO</c>). Each section is preceded by a four-letter name, which ends the padding of
    /// the section before it.
    /// </para>
    /// <para>
    /// The parser reads the file as the engine does: it ignores the sections' names (four of FAF's
    /// meshes name the vertices <c>VRTX</c>), reads the bone names one after the other from byte
    /// 0x40 (the engine ignores the name offset of each bone) and keeps the header's skinned bone
    /// count (<see cref="Scm.SkinnedBoneCount"/>). It checks more than the engine, which only
    /// rejects a file shorter than its header: the magic number, the version and that every offset,
    /// index and parent points inside the file.
    /// </para>
    /// <para>
    /// The layout follows the header files GPG released with the game's tools
    /// (https://ftp.zx.net.nz/pub/Game-Files/SupCom/util/), the Blender exporter
    /// (Solstice245/scstudio) and the engine's reconstruction faf-re (Draiget/faf-re, commit 2ba55b2:
    /// <c>src/sdk/moho/resource/SScmFile.h</c>, <c>ResourceFactory.cpp</c>,
    /// <c>moho/animation/CAniSkel.cpp</c>). The library's guide (<c>AGENTS.md</c> next to this file)
    /// describes every field and how the engine uses it. A vertex stores its normal before its
    /// tangent: the normals point along the faces of the triangles.
    /// </para>
    /// </remarks>
    public static class ScmParser
    {
        /// <summary>
        /// The first four bytes of a mesh: "MODL".
        /// </summary>
        public const int Magic = 0x4c444f4d;

        /// <summary>
        /// The version of the format the game reads and this parser reads.
        /// </summary>
        public const int Version = 5;

        /// <summary>
        /// Where the bone names start: right after the header and the name <c>NAME</c>. The engine
        /// reads them from here, so every mesh must have them here.
        /// </summary>
        internal const int NamesOffset = 0x40;

        /// <summary>
        /// The size of a bone in the <c>SKEL</c> section.
        /// </summary>
        internal const int BoneSize = 64 + 12 + 16 + 4 * 4;

        /// <summary>
        /// The size of a vertex in the <c>VTXL</c> section.
        /// </summary>
        internal const int VertexSize = 4 * 12 + 2 * 8 + 4;

        /// <summary>
        /// Reads a mesh from a stream, from its current position to the end of the stream.
        /// </summary>
        public static Scm Parse(Stream stream)
        {
            using MemoryStream memory = new MemoryStream();
            stream.CopyTo(memory);
            return Parse(memory.GetBuffer().AsSpan(0, (int)memory.Length));
        }

        /// <summary>
        /// Reads a mesh. The offsets in its header are relative to the first byte.
        /// </summary>
        public static Scm Parse(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < 48)
            {
                throw new ScmFormatException("Not a mesh (shorter than its header)", 0);
            }

            Reader header = new Reader(bytes, 0);
            if (header.Int32() != Magic)
            {
                throw new ScmFormatException("Not a mesh (the magic number MODL is missing)", 0);
            }

            int version = header.Int32();
            if (version != Version)
            {
                throw new ScmFormatException($"Unsupported mesh version {version}, expected {Version}", 4);
            }

            int boneOffset = header.Int32();
            int skinnedBoneCount = header.Count();
            int vertexOffset = header.Int32();
            header.Int32(); // the extra vertex data, which no exporter writes
            int vertexCount = header.Count();
            int indexOffset = header.Int32();
            int indexCount = header.Count();
            int informationOffset = header.Int32();
            int informationSize = header.Count();
            int boneCount = header.Count();

            if (indexCount % 3 != 0)
            {
                throw new ScmFormatException($"The number of indices ({indexCount}) is not a multiple of three", 32);
            }

            // the engine reads the names one after the other from byte 0x40, whatever a bone's offset says
            Reader names = new Reader(bytes, NamesOffset);
            Reader r = Section(bytes, boneOffset, "SKEL");
            ScmBone[] bones = new ScmBone[boneCount];
            for (int i = 0; i < bones.Length; i++)
            {
                Matrix4x4 restPoseInverse = r.Matrix4x4();
                Vector3 position = r.Vector3();
                float w = r.Single();
                Quaternion rotation = new Quaternion(r.Single(), r.Single(), r.Single(), w);
                r.Int32(); // the name's offset
                int parent = r.Int32();
                if (parent < -1 || parent >= boneCount)
                {
                    throw new ScmFormatException($"Bone {i} has parent {parent}, which is not a bone", r.Position - 4);
                }
                string name = names.String();
                bones[i] = new ScmBone(name, restPoseInverse, position, rotation, parent, r.Int32(), r.Int32());
            }

            r = Section(bytes, vertexOffset, "VTXL");
            ScmVertex[] vertices = new ScmVertex[vertexCount];
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = new ScmVertex(
                    Position: r.Vector3(),
                    Normal: r.Vector3(),
                    Tangent: r.Vector3(),
                    Binormal: r.Vector3(),
                    Uv0: r.Vector2(),
                    Uv1: r.Vector2(),
                    Bone0: r.Byte(),
                    Bone1: r.Byte(),
                    Bone2: r.Byte(),
                    Bone3: r.Byte());
            }

            r = Section(bytes, indexOffset, "TRIS");
            ushort[] indices = new ushort[indexCount];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = r.UInt16();
                if (indices[i] >= vertexCount)
                {
                    throw new ScmFormatException($"Index {indices[i]} is past the last vertex ({vertexCount - 1})", r.Position - 2);
                }
            }

            List<string> information = [];
            if (informationOffset != 0)
            {
                r = Section(bytes, informationOffset, "INFO");
                int end = informationOffset + informationSize;
                if (end > bytes.Length)
                {
                    throw new ScmFormatException($"The notes end past the end of the file ({informationSize} bytes)", informationOffset);
                }
                while (r.Position < end)
                {
                    information.Add(r.String());
                }
            }

            return new Scm
            {
                Bones = bones,
                Vertices = vertices,
                Indices = indices,
                Information = information,
                SkinnedBoneCount = skinnedBoneCount,
            };
        }

        /// <summary>
        /// A reader at the start of a section. The four bytes before it usually hold the section's
        /// name (<paramref name="name"/>), but the engine never reads them and some exporters write
        /// another (<c>VRTX</c> for <c>VTXL</c>), so neither does the parser.
        /// </summary>
        private static Reader Section(ReadOnlySpan<byte> bytes, int offset, string name)
        {
            if (offset < 0 || offset > bytes.Length)
            {
                throw new ScmFormatException($"The section {name} starts outside the file", offset);
            }
            return new Reader(bytes, offset);
        }

        private ref struct Reader(ReadOnlySpan<byte> bytes, int position)
        {
            private readonly ReadOnlySpan<byte> _bytes = bytes;

            public int Position { get; private set; } = position;

            private ReadOnlySpan<byte> Take(int length)
            {
                if (Position < 0 || length > _bytes.Length - Position)
                {
                    throw new ScmFormatException($"Unexpected end of the file (needed {length} bytes)", Position);
                }
                ReadOnlySpan<byte> slice = _bytes.Slice(Position, length);
                Position += length;
                return slice;
            }

            public byte Byte() => Take(1)[0];

            public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

            public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

            public float Single() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));

            /// <summary>
            /// A count or size, which cannot be negative.
            /// </summary>
            public int Count()
            {
                int start = Position;
                int count = Int32();
                if (count < 0)
                {
                    throw new ScmFormatException($"Negative count {count}", start);
                }
                return count;
            }

            public Vector2 Vector2() => new Vector2(Single(), Single());

            public Vector3 Vector3() => new Vector3(Single(), Single(), Single());

            /// <summary>
            /// Sixteen numbers, row by row.
            /// </summary>
            public Matrix4x4 Matrix4x4() => new Matrix4x4(
                Single(), Single(), Single(), Single(),
                Single(), Single(), Single(), Single(),
                Single(), Single(), Single(), Single(),
                Single(), Single(), Single(), Single());

            /// <summary>
            /// A string that ends with a zero byte.
            /// </summary>
            public string String()
            {
                if (Position < 0 || Position > _bytes.Length)
                {
                    throw new ScmFormatException("A string starts outside the file", Position);
                }
                int end = _bytes[Position..].IndexOf((byte)0);
                if (end < 0)
                {
                    throw new ScmFormatException("Unexpected end of the file in a string", Position);
                }
                string text = Encoding.UTF8.GetString(Take(end));
                Position++;
                return text;
            }
        }
    }
}
