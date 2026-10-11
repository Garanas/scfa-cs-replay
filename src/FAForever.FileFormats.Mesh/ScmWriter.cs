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
    /// <para>
    /// The sections are written in the order of the game's exporter (names, bones, vertices,
    /// triangles, notes), each starting on a multiple of 32 bytes. The padding is the byte 0xC5,
    /// at least one of it, followed by the name of the next section. The bone names start at byte
    /// 0x40, where the engine reads them.
    /// </para>
    /// <para>
    /// The writer refuses what the engine cannot draw (faf-re, Draiget/faf-re commit 2ba55b2): a bone
    /// before its parent (<c>CAniPose.cpp</c> updates the pose in one forward pass), more than
    /// 65,535 vertices (a prop's batch holds <c>0xFFFF / vertices</c> instances, <c>MeshBatch.cpp</c>)
    /// and more skinned bones than the 80 palette slots of a draw (<c>Mesh.h</c>,
    /// <c>MeshBatch.cpp</c>).
    /// </para>
    /// </remarks>
    public static class ScmWriter
    {
        /// <summary>
        /// The most bones a mesh can skin its vertices with: the engine shares 80 bone matrices
        /// (<c>kPaletteCapacity = 0x50</c>) between the instances of a draw, and a unit with more
        /// skinned bones gets none. The game's largest is 58 (<c>UEL0401_lod0</c>), FAF's 64.
        /// </summary>
        public const int MaxSkinnedBones = 80;

        /// <summary>
        /// The most vertices a mesh can have. The indices are 16 bits, and the engine draws a prop
        /// in batches of <c>0xFFFF / vertices</c> instances, none at 65,536.
        /// </summary>
        public const int MaxVertices = ushort.MaxValue;

        private const int Alignment = 32;

        private const byte Padding = 0xc5;

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
        /// <exception cref="ArgumentException">The mesh cannot be stored or drawn: more than 65,535
        /// vertices, a number of indices that is not a multiple of three, an index, bone or parent
        /// that points at nothing, a bone before its parent, or a skinned bone count below the bones
        /// the vertices use, above the bones there are or above <see cref="MaxSkinnedBones"/>.</exception>
        public static void Write(Scm scm, Stream stream)
        {
            Validate(scm);

            // the sections are laid out first: the header points at all of them
            int namesOffset = ScmParser.NamesOffset;
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
            w.Int32(scm.SkinnedBoneCount ?? WeightedBoneCount(scm));
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
                w.Int32(bone.FirstVertex);
                w.Int32(bone.TriangleCount);
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
        /// The number of bones that vertices move with, as the game's exporter stores it in the
        /// header: one more than the highest bone a vertex uses. The exporter puts those bones first.
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
            if (scm.Vertices.Count > MaxVertices)
            {
                throw new ArgumentException($"A mesh has at most {MaxVertices:N0} vertices, this one has {scm.Vertices.Count}", nameof(scm));
            }
            if (scm.Indices.Length % 3 != 0)
            {
                throw new ArgumentException($"The number of indices ({scm.Indices.Length}) is not a multiple of three", nameof(scm));
            }
            if (scm.Indices.Any(index => index >= scm.Vertices.Count))
            {
                throw new ArgumentException("An index is past the last vertex", nameof(scm));
            }
            int weighted = WeightedBoneCount(scm);
            if (weighted > scm.Bones.Count)
            {
                throw new ArgumentException("A vertex moves with a bone the mesh does not have", nameof(scm));
            }
            if (scm.SkinnedBoneCount is int skinned && (skinned < weighted || skinned > scm.Bones.Count))
            {
                throw new ArgumentException($"The skinned bone count {skinned} must lie between the bones the vertices use ({weighted}) and the bones there are ({scm.Bones.Count}); leave it null to derive it", nameof(scm));
            }
            if ((scm.SkinnedBoneCount ?? weighted) > MaxSkinnedBones)
            {
                throw new ArgumentException($"The vertices move with {scm.SkinnedBoneCount ?? weighted} bones, the engine skins with at most {MaxSkinnedBones}", nameof(scm));
            }
            for (int i = 0; i < scm.Bones.Count; i++)
            {
                int parent = scm.Bones[i].Parent;
                if (parent < -1 || parent >= scm.Bones.Count)
                {
                    throw new ArgumentException($"The parent of bone {i} is not a bone", nameof(scm));
                }
                if (parent >= i)
                {
                    throw new ArgumentException($"Bone {i} comes before its parent {parent}; the engine needs every parent first", nameof(scm));
                }
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
