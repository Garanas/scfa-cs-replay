using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace FAForever.FileFormats.Mesh
{
    /// <summary>
    /// Writes the mesh format of the game (<c>.scm</c>, version 5): the reverse of
    /// <see cref="ScmParser"/>. A mesh exported by the game's own tools writes back to the same bytes.
    /// </summary>
    /// <remarks>
    /// The sections are written in the order of the game's exporter (names, bones, vertices,
    /// triangles, notes), each starting on a multiple of 32 bytes. The padding is the byte 0xC5,
    /// at least one of it, followed by the name of the next section.
    /// </remarks>
    public static class ScmWriter
    {
        private const int Alignment = 32;

        private const byte Padding = 0xc5;

        private const int HeaderSize = 12 * 4;

        /// <summary>
        /// Writes a mesh to a new array.
        /// </summary>
        public static byte[] Write(Scm scm)
        {
            using MemoryStream memory = new MemoryStream();
            Write(scm, memory);
            return memory.ToArray();
        }

        /// <summary>
        /// Writes a mesh to a stream. The offsets in the header are relative to the stream's current
        /// position.
        /// </summary>
        /// <exception cref="ArgumentException">The mesh cannot be stored: more than 65,536 vertices, a
        /// number of indices that is not a multiple of three, or an index, bone or parent that points
        /// at nothing.</exception>
        public static void Write(Scm scm, Stream stream)
        {
            Validate(scm);

            // the sections are laid out first: the header points at all of them
            int namesOffset = Align(HeaderSize);
            int[] nameOffsets = new int[scm.Bones.Count];
            int position = namesOffset;
            for (int i = 0; i < scm.Bones.Count; i++)
            {
                nameOffsets[i] = position;
                position += Encoding.UTF8.GetByteCount(scm.Bones[i].Name) + 1;
            }
            int boneOffset = Align(position);
            int vertexOffset = Align(boneOffset + scm.Bones.Count * ScmParser.BoneSize);
            int indexOffset = Align(vertexOffset + scm.Vertices.Count * ScmParser.VertexSize);
            int informationSize = scm.Information.Sum(text => Encoding.UTF8.GetByteCount(text) + 1);
            int informationOffset = scm.Information.Count == 0 ? 0 : Align(indexOffset + scm.Indices.Length * 2);

            Writer w = new Writer(stream);

            w.Int32(ScmParser.Magic);
            w.Int32(ScmParser.Version);
            w.Int32(boneOffset);
            w.Int32(WeightedBoneCount(scm));
            w.Int32(vertexOffset);
            w.Int32(0);
            w.Int32(scm.Vertices.Count);
            w.Int32(indexOffset);
            w.Int32(scm.Indices.Length);
            w.Int32(informationOffset);
            w.Int32(informationSize);
            w.Int32(scm.Bones.Count);

            w.Section(namesOffset, "NAME");
            foreach (ScmBone bone in scm.Bones)
            {
                w.String(bone.Name);
            }

            w.Section(boneOffset, "SKEL");
            for (int i = 0; i < scm.Bones.Count; i++)
            {
                ScmBone bone = scm.Bones[i];
                w.Matrix4x4(bone.RestPoseInverse);
                w.Vector3(bone.Position);
                w.Single(bone.Rotation.W);
                w.Single(bone.Rotation.X);
                w.Single(bone.Rotation.Y);
                w.Single(bone.Rotation.Z);
                w.Int32(nameOffsets[i]);
                w.Int32(bone.Parent);
                w.Int32(bone.Reserved1);
                w.Int32(bone.Reserved2);
            }

            w.Section(vertexOffset, "VTXL");
            foreach (ScmVertex vertex in scm.Vertices)
            {
                w.Vector3(vertex.Position);
                w.Vector3(vertex.Normal);
                w.Vector3(vertex.Tangent);
                w.Vector3(vertex.Binormal);
                w.Vector2(vertex.Uv0);
                w.Vector2(vertex.Uv1);
                w.Byte(vertex.Bone0);
                w.Byte(vertex.Bone1);
                w.Byte(vertex.Bone2);
                w.Byte(vertex.Bone3);
            }

            w.Section(indexOffset, "TRIS");
            foreach (ushort index in scm.Indices)
            {
                w.UInt16(index);
            }

            if (informationOffset != 0)
            {
                w.Section(informationOffset, "INFO");
                foreach (string text in scm.Information)
                {
                    w.String(text);
                }
            }
        }

        /// <summary>
        /// The number of bones that vertices move with, as the header stores it: one more than the
        /// highest bone a vertex uses. The game's exporter puts those bones first.
        /// </summary>
        public static int WeightedBoneCount(Scm scm)
        {
            int highest = -1;
            foreach (ScmVertex vertex in scm.Vertices)
            {
                highest = Math.Max(highest, Math.Max(Math.Max(vertex.Bone0, vertex.Bone1), Math.Max(vertex.Bone2, vertex.Bone3)));
            }
            return highest + 1;
        }

        /// <summary>
        /// The first multiple of the alignment that leaves room for a section's name and at least one
        /// byte of padding before it, as the game's exporter does.
        /// </summary>
        private static int Align(int end) => (end + 5 + Alignment - 1) / Alignment * Alignment;

        private static void Validate(Scm scm)
        {
            if (scm.Vertices.Count > ushort.MaxValue + 1)
            {
                throw new ArgumentException($"A mesh has at most 65,536 vertices, this one has {scm.Vertices.Count}", nameof(scm));
            }
            if (scm.Indices.Length % 3 != 0)
            {
                throw new ArgumentException($"The number of indices ({scm.Indices.Length}) is not a multiple of three", nameof(scm));
            }
            if (scm.Indices.Any(index => index >= scm.Vertices.Count))
            {
                throw new ArgumentException("An index is past the last vertex", nameof(scm));
            }
            if (WeightedBoneCount(scm) > scm.Bones.Count)
            {
                throw new ArgumentException("A vertex moves with a bone the mesh does not have", nameof(scm));
            }
            if (scm.Bones.Any(bone => bone.Parent < -1 || bone.Parent >= scm.Bones.Count))
            {
                throw new ArgumentException("A bone's parent is not a bone", nameof(scm));
            }
            if (scm.Bones.Any(bone => bone.Name.Contains('\0')) || scm.Information.Any(text => text.Contains('\0')))
            {
                throw new ArgumentException("A name or note contains a zero character, which ends it in the file", nameof(scm));
            }
        }

        private sealed class Writer(Stream stream)
        {
            private readonly long _start = stream.Position;

            private readonly byte[] _buffer = new byte[4];

            /// <summary>
            /// Pads up to a section and writes its name in the last four bytes of the padding.
            /// </summary>
            public void Section(int offset, string name)
            {
                while (stream.Position - _start < offset - 4)
                {
                    stream.WriteByte(Padding);
                }
                stream.Write(Encoding.ASCII.GetBytes(name));
            }

            public void Byte(byte value) => stream.WriteByte(value);

            public void UInt16(ushort value)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(_buffer, value);
                stream.Write(_buffer, 0, 2);
            }

            public void Int32(int value)
            {
                BinaryPrimitives.WriteInt32LittleEndian(_buffer, value);
                stream.Write(_buffer, 0, 4);
            }

            public void Single(float value)
            {
                BinaryPrimitives.WriteSingleLittleEndian(_buffer, value);
                stream.Write(_buffer, 0, 4);
            }

            public void Vector2(Vector2 value)
            {
                Single(value.X);
                Single(value.Y);
            }

            public void Vector3(Vector3 value)
            {
                Single(value.X);
                Single(value.Y);
                Single(value.Z);
            }

            public void Matrix4x4(Matrix4x4 m)
            {
                for (int row = 0; row < 4; row++)
                {
                    for (int column = 0; column < 4; column++)
                    {
                        Single(m[row, column]);
                    }
                }
            }

            /// <summary>
            /// A string followed by a zero byte.
            /// </summary>
            public void String(string text)
            {
                stream.Write(Encoding.UTF8.GetBytes(text));
                stream.WriteByte(0);
            }
        }
    }
}
