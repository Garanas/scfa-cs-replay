using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;

namespace FAForever.FileFormats.Mesh
{
    /// <summary>
    /// Thrown when the bytes are not a glTF file this reader understands.
    /// </summary>
    public sealed class GlbFormatException(string message) : FormatException(message);

    /// <summary>
    /// Reads a mesh back from glTF 2.0 in its binary form (<c>.glb</c>), as <see cref="GlbWriter"/>
    /// writes it: the first primitive of the first mesh, and the skin's joints as the bones.
    /// </summary>
    /// <remarks>
    /// The normals and tangents come back at length 1 (as the writer wrote them), the binormal as
    /// <c>cross(normal, tangent)</c> on the side the tangent's fourth component gives. A file
    /// without tangents, a second set of texture coordinates or a skin reads with zeros there and
    /// no bones. The indices must be 16-bit, as the game's are.
    /// </remarks>
    public static class GlbReader
    {
        private const uint Magic = 0x46546c67; // "glTF"
        private const uint JsonChunk = 0x4e4f534a; // "JSON"
        private const uint BinaryChunk = 0x004e4942; // "BIN\0"

        private const int Float = 5126;
        private const int UnsignedByte = 5121;
        private const int UnsignedShort = 5123;

        /// <summary>
        /// Reads a mesh.
        /// </summary>
        /// <exception cref="GlbFormatException">The bytes are no glTF file, or one with what a mesh of
        /// the game cannot hold (no triangles, 32-bit indices, an index past the last vertex).</exception>
        public static Scm Read(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic)
            {
                throw new GlbFormatException("Not a glTF file");
            }
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != 2)
            {
                throw new GlbFormatException("Not glTF version 2");
            }
            int jsonLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]) != JsonChunk || jsonLength < 0 || 20 + jsonLength > bytes.Length)
            {
                throw new GlbFormatException("The file does not start with its JSON");
            }
            ReadOnlySpan<byte> binary = [];
            int binaryStart = 20 + jsonLength;
            if (binaryStart + 8 <= bytes.Length && BinaryPrimitives.ReadUInt32LittleEndian(bytes[(binaryStart + 4)..]) == BinaryChunk)
            {
                int binaryLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[binaryStart..]);
                if (binaryLength < 0 || binaryStart + 8 + binaryLength > bytes.Length)
                {
                    throw new GlbFormatException("The binary chunk is longer than the file");
                }
                binary = bytes.Slice(binaryStart + 8, binaryLength);
            }

            using JsonDocument document = Json(bytes.Slice(20, jsonLength));
            try
            {
                return Read(new Gltf(document.RootElement, binary));
            }
            catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or ArgumentOutOfRangeException or FormatException and not GlbFormatException)
            {
                throw new GlbFormatException($"The JSON is not what a mesh needs: {exception.Message}");
            }
        }

        private static JsonDocument Json(ReadOnlySpan<byte> json)
        {
            try
            {
                return JsonDocument.Parse(json.ToArray());
            }
            catch (JsonException exception)
            {
                throw new GlbFormatException($"The JSON cannot be read: {exception.Message}");
            }
        }

        private static Scm Read(Gltf gltf)
        {
            JsonElement primitive = gltf.Root.GetProperty("meshes")[0].GetProperty("primitives")[0];
            if (primitive.TryGetProperty("mode", out JsonElement mode) && mode.GetInt32() != 4)
            {
                throw new GlbFormatException("The mesh is not made of triangles");
            }
            JsonElement attributes = primitive.GetProperty("attributes");
            float[] positions = gltf.Floats(attributes.GetProperty("POSITION").GetInt32(), "VEC3");
            int count = positions.Length / 3;
            float[]? normals = Optional(gltf, attributes, "NORMAL", "VEC3");
            float[]? tangents = Optional(gltf, attributes, "TANGENT", "VEC4");
            float[]? uv0 = Optional(gltf, attributes, "TEXCOORD_0", "VEC2");
            float[]? uv1 = Optional(gltf, attributes, "TEXCOORD_1", "VEC2");
            byte[]? joints = attributes.TryGetProperty("JOINTS_0", out JsonElement jointsAccessor) ? gltf.Bytes(jointsAccessor.GetInt32()) : null;

            ScmVertex[] vertices = new ScmVertex[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 normal = normals is null ? Vector3.Zero : new Vector3(normals[i * 3], normals[i * 3 + 1], normals[i * 3 + 2]);
                Vector3 tangent = tangents is null ? Vector3.Zero : new Vector3(tangents[i * 4], tangents[i * 4 + 1], tangents[i * 4 + 2]);
                float side = tangents is null ? 1 : tangents[i * 4 + 3];
                vertices[i] = new ScmVertex(
                    new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]),
                    normal,
                    tangent,
                    Vector3.Cross(normal, tangent) * side,
                    uv0 is null ? Vector2.Zero : new Vector2(uv0[i * 2], uv0[i * 2 + 1]),
                    uv1 is null ? Vector2.Zero : new Vector2(uv1[i * 2], uv1[i * 2 + 1]),
                    joints is null ? (byte)0 : joints[i * 4],
                    joints is null ? (byte)0 : joints[i * 4 + 1],
                    joints is null ? (byte)0 : joints[i * 4 + 2],
                    joints is null ? (byte)0 : joints[i * 4 + 3]);
            }

            ushort[] indices = gltf.Indices(primitive.GetProperty("indices").GetInt32());
            if (indices.Length == 0 || indices.Length % 3 != 0)
            {
                throw new GlbFormatException($"A mesh needs whole triangles, this one has {indices.Length} indices");
            }
            if (indices.Any(index => index >= count))
            {
                throw new GlbFormatException("An index is past the last vertex");
            }

            List<string> information = [];
            if (gltf.Root.GetProperty("asset").TryGetProperty("extras", out JsonElement extras)
                && extras.TryGetProperty("information", out JsonElement notes))
            {
                information.AddRange(notes.EnumerateArray().Select(note => note.GetString() ?? ""));
            }

            return new Scm
            {
                Bones = Bones(gltf),
                Vertices = vertices,
                Indices = indices,
                Information = information,
            };
        }

        private static float[]? Optional(Gltf gltf, JsonElement attributes, string name, string type) =>
            attributes.TryGetProperty(name, out JsonElement accessor) ? gltf.Floats(accessor.GetInt32(), type) : null;

        /// <summary>
        /// The skin's joints as bones, in the skin's order, with their parents among them.
        /// </summary>
        private static List<ScmBone> Bones(Gltf gltf)
        {
            if (!gltf.Root.TryGetProperty("skins", out JsonElement skins) || skins.GetArrayLength() == 0)
            {
                return [];
            }
            JsonElement skin = skins[0];
            int[] joints = [.. skin.GetProperty("joints").EnumerateArray().Select(joint => joint.GetInt32())];
            float[]? inverseBinds = skin.TryGetProperty("inverseBindMatrices", out JsonElement accessor) ? gltf.Floats(accessor.GetInt32(), "MAT4") : null;
            JsonElement nodes = gltf.Root.GetProperty("nodes");

            Dictionary<int, int> boneOfNode = [];
            for (int i = 0; i < joints.Length; i++)
            {
                boneOfNode[joints[i]] = i;
            }
            int[] parents = Enumerable.Repeat(-1, joints.Length).ToArray();
            for (int i = 0; i < joints.Length; i++)
            {
                if (nodes[joints[i]].TryGetProperty("children", out JsonElement children))
                {
                    foreach (JsonElement child in children.EnumerateArray())
                    {
                        if (boneOfNode.TryGetValue(child.GetInt32(), out int bone))
                        {
                            parents[bone] = i;
                        }
                    }
                }
            }

            List<ScmBone> bones = [];
            for (int i = 0; i < joints.Length; i++)
            {
                JsonElement node = nodes[joints[i]];
                float[] t = node.TryGetProperty("translation", out JsonElement translation) ? Numbers(translation) : [0, 0, 0];
                float[] r = node.TryGetProperty("rotation", out JsonElement rotation) ? Numbers(rotation) : [0, 0, 0, 1];
                Matrix4x4 inverseBind = Matrix4x4.Identity;
                if (inverseBinds is not null)
                {
                    // glTF's column-major order of a column-vector matrix is the game's row by row for row vectors
                    for (int k = 0; k < 16; k++)
                    {
                        inverseBind[k / 4, k % 4] = inverseBinds[i * 16 + k];
                    }
                }
                bones.Add(new ScmBone(
                    node.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? "" : "",
                    inverseBind,
                    new Vector3(t[0], t[1], t[2]),
                    new Quaternion(r[0], r[1], r[2], r[3]),
                    parents[i]));
            }
            return bones;
        }

        private static float[] Numbers(JsonElement array) => [.. array.EnumerateArray().Select(value => value.GetSingle())];

        /// <summary>
        /// The JSON of a file and its binary chunk, with the accessors read from both.
        /// </summary>
        private readonly ref struct Gltf(JsonElement root, ReadOnlySpan<byte> binary)
        {
            public JsonElement Root { get; } = root;

            private readonly ReadOnlySpan<byte> _binary = binary;

            public float[] Floats(int accessor, string type)
            {
                ReadOnlySpan<byte> data = Data(accessor, Float, type, out int count);
                float[] values = new float[count * Components(type)];
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = BinaryPrimitives.ReadSingleLittleEndian(data[(i * 4)..]);
                }
                return values;
            }

            public byte[] Bytes(int accessor) => Data(accessor, UnsignedByte, "VEC4", out int count)[..(count * 4)].ToArray();

            public ushort[] Indices(int accessor)
            {
                ReadOnlySpan<byte> data = Data(accessor, UnsignedShort, "SCALAR", out int count);
                ushort[] values = new ushort[count];
                for (int i = 0; i < count; i++)
                {
                    values[i] = BinaryPrimitives.ReadUInt16LittleEndian(data[(i * 2)..]);
                }
                return values;
            }

            /// <summary>
            /// The bytes of an accessor of the given component and type, packed (no stride).
            /// </summary>
            private ReadOnlySpan<byte> Data(int index, int componentType, string type, out int count)
            {
                JsonElement accessor = Root.GetProperty("accessors")[index];
                if (accessor.GetProperty("componentType").GetInt32() != componentType || accessor.GetProperty("type").GetString() != type)
                {
                    throw new GlbFormatException($"Accessor {index} is not of the kind its attribute needs ({type} of {componentType})");
                }
                count = accessor.GetProperty("count").GetInt32();
                JsonElement view = Root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
                if (view.TryGetProperty("byteStride", out JsonElement stride) && stride.GetInt32() != Components(type) * Size(componentType))
                {
                    throw new GlbFormatException($"Accessor {index} is interleaved");
                }
                long offset = (view.TryGetProperty("byteOffset", out JsonElement viewOffset) ? viewOffset.GetInt64() : 0)
                    + (accessor.TryGetProperty("byteOffset", out JsonElement accessorOffset) ? accessorOffset.GetInt64() : 0);
                long length = (long)count * Components(type) * Size(componentType);
                if (count < 0 || offset < 0 || offset + length > _binary.Length)
                {
                    throw new GlbFormatException($"Accessor {index} is past the end of the binary chunk");
                }
                return _binary.Slice((int)offset, (int)length);
            }

            private static int Components(string type) => type switch
            {
                "SCALAR" => 1,
                "VEC2" => 2,
                "VEC3" => 3,
                "VEC4" => 4,
                "MAT4" => 16,
                _ => throw new GlbFormatException($"Accessors of type {type} are not read"),
            };

            private static int Size(int componentType) => componentType switch
            {
                UnsignedByte => 1,
                UnsignedShort => 2,
                _ => 4,
            };
        }
    }
}
