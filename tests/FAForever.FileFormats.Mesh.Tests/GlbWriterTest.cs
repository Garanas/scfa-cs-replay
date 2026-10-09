using System.Numerics;
using System.Text;
using System.Text.Json;
using FAForever.FileFormats.Mesh;

namespace FAForever.FileFormats.Mesh.Tests
{
    [TestClass]
    public class GlbWriterTest
    {
        /// <summary>
        /// A written file, taken apart: its JSON and its binary buffer.
        /// </summary>
        private sealed record Glb(JsonElement Json, byte[] Binary)
        {
            public static Glb Read(byte[] bytes)
            {
                Assert.AreEqual("glTF", Encoding.ASCII.GetString(bytes, 0, 4));
                Assert.AreEqual(2, BitConverter.ToInt32(bytes, 4));
                Assert.AreEqual(bytes.Length, BitConverter.ToInt32(bytes, 8));
                int jsonLength = BitConverter.ToInt32(bytes, 12);
                Assert.AreEqual("JSON", Encoding.ASCII.GetString(bytes, 16, 4));
                Assert.AreEqual(0, jsonLength % 4);
                JsonElement json = JsonDocument.Parse(bytes.AsMemory(20, jsonLength)).RootElement;
                int binaryStart = 20 + jsonLength;
                int binaryLength = BitConverter.ToInt32(bytes, binaryStart);
                Assert.AreEqual("BIN\0", Encoding.ASCII.GetString(bytes, binaryStart + 4, 4));
                Assert.AreEqual(bytes.Length, binaryStart + 8 + binaryLength);
                return new Glb(json, bytes.AsSpan(binaryStart + 8, binaryLength).ToArray());
            }

            public JsonElement Accessor(int index) => Json.GetProperty("accessors")[index];

            public JsonElement Attribute(string name) => Accessor(Json.GetProperty("meshes")[0].GetProperty("primitives")[0].GetProperty("attributes").GetProperty(name).GetInt32());

            /// <summary>
            /// The bytes of an accessor, as many as its count of elements of the given size.
            /// </summary>
            public ReadOnlySpan<byte> Data(JsonElement accessor, int elementSize)
            {
                JsonElement view = Json.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
                int offset = view.GetProperty("byteOffset").GetInt32();
                Assert.AreEqual(0, offset % 4, "aligned to four bytes");
                int length = accessor.GetProperty("count").GetInt32() * elementSize;
                Assert.AreEqual(length, view.GetProperty("byteLength").GetInt32());
                return Binary.AsSpan(offset, length);
            }

            public float[] Floats(JsonElement accessor, int components)
            {
                ReadOnlySpan<byte> data = Data(accessor, components * 4);
                float[] values = new float[data.Length / 4];
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = BitConverter.ToSingle(data[(i * 4)..]);
                }
                return values;
            }
        }

        private static Scm Parse(string path) => ScmParser.Parse(File.ReadAllBytes(path));

        [TestMethod]
        [DataRow(ScmParserTest.Ual0101Lod0)]
        [DataRow(ScmParserTest.Uel0401Lod1)]
        [DataRow(ScmParserTest.Blender)]
        [DataRow(ScmParserTest.Max)]
        public void WritesTheVerticesAndTriangles(string path)
        {
            Scm scm = Parse(path);
            Glb glb = Glb.Read(GlbWriter.Write(scm));

            Assert.AreEqual("2.0", glb.Json.GetProperty("asset").GetProperty("version").GetString());
            float[] positions = glb.Floats(glb.Attribute("POSITION"), 3);
            Assert.AreEqual(scm.Vertices.Count * 3, positions.Length);
            for (int i = 0; i < scm.Vertices.Count; i++)
            {
                Assert.AreEqual(scm.Vertices[i].Position, new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]));
            }
            float[] uv = glb.Floats(glb.Attribute("TEXCOORD_0"), 2);
            Assert.AreEqual(scm.Vertices[^1].Uv0, new Vector2(uv[^2], uv[^1]));

            JsonElement primitive = glb.Json.GetProperty("meshes")[0].GetProperty("primitives")[0];
            ReadOnlySpan<byte> indices = glb.Data(glb.Accessor(primitive.GetProperty("indices").GetInt32()), 2);
            for (int i = 0; i < scm.Indices.Length; i++)
            {
                Assert.AreEqual(scm.Indices[i], BitConverter.ToUInt16(indices[(i * 2)..]));
            }

            ReadOnlySpan<byte> joints = glb.Data(glb.Attribute("JOINTS_0"), 4);
            Assert.AreEqual(scm.Vertices[^1].Bone0, joints[^4]);
            CollectionAssert.AreEqual(new[] { 1f, 0, 0, 0 }, glb.Floats(glb.Attribute("WEIGHTS_0"), 4)[..4]);

            JsonElement max = glb.Attribute("POSITION").GetProperty("max");
            Assert.AreEqual(scm.Vertices.Max(v => v.Position.Y), max[1].GetSingle());
        }

        [TestMethod]
        [DataRow(ScmParserTest.Ual0101Lod0)]
        [DataRow(ScmParserTest.Xsl0001Lod1)]
        [DataRow(ScmParserTest.Blender)]
        [DataRow(ScmParserTest.Max)]
        public void WritesNormalsAndTangentsOfLengthOne(string path)
        {
            // the Blender exporter leaves some tangents at 0, the 3ds Max one some not a number
            Glb glb = Glb.Read(GlbWriter.Write(Parse(path)));

            float[] normals = glb.Floats(glb.Attribute("NORMAL"), 3);
            float[] tangents = glb.Floats(glb.Attribute("TANGENT"), 4);
            for (int i = 0; i < normals.Length / 3; i++)
            {
                Vector3 normal = new Vector3(normals[i * 3], normals[i * 3 + 1], normals[i * 3 + 2]);
                Vector3 tangent = new Vector3(tangents[i * 4], tangents[i * 4 + 1], tangents[i * 4 + 2]);
                // a tolerance lets not-a-number through: check it first
                Assert.IsTrue(float.IsFinite(normal.Length()) && float.IsFinite(tangent.Length()), $"vertex {i}");
                Assert.AreEqual(1, normal.Length(), 1e-4);
                Assert.AreEqual(1, tangent.Length(), 1e-4);
                Assert.AreEqual(0, Vector3.Dot(normal, tangent), 1e-4);
                Assert.AreEqual(1, MathF.Abs(tangents[i * 4 + 3]));
            }
        }

        [TestMethod]
        public void KeepsTheSideOfTheBinormal()
        {
            ScmVertex vertex = new ScmVertex(Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, Vector2.Zero, Vector2.Zero, 0);

            // cross(y, x) is -z
            Assert.AreEqual(new Vector4(Vector3.UnitX, -1), GlbWriter.Tangent(vertex));
            Assert.AreEqual(new Vector4(Vector3.UnitX, 1), GlbWriter.Tangent(vertex with { Binormal = -Vector3.UnitZ }));
        }

        [TestMethod]
        [DataRow(ScmParserTest.Ual0101Lod0)]
        [DataRow(ScmParserTest.Uel0401Lod1)]
        [DataRow(ScmParserTest.Xsl0001Lod1)]
        [DataRow(ScmParserTest.Blender)]
        [DataRow(ScmParserTest.Max)]
        public void PutsTheSkeletonInTheRestPose(string path)
        {
            // a bone's place from the nodes, times its inverse bind matrix, is no change at all:
            // the vertices are drawn where the mesh has them
            Scm scm = Parse(path);
            Glb glb = Glb.Read(GlbWriter.Write(scm));
            JsonElement nodes = glb.Json.GetProperty("nodes");
            Assert.AreEqual(scm.Bones.Count + 1, nodes.GetArrayLength());

            Matrix4x4[] world = new Matrix4x4[scm.Bones.Count];
            Dictionary<int, int> parents = [];
            for (int i = 0; i < scm.Bones.Count; i++)
            {
                if (nodes[i].TryGetProperty("children", out JsonElement children))
                {
                    foreach (JsonElement child in children.EnumerateArray())
                    {
                        parents[child.GetInt32()] = i;
                    }
                }
            }
            float[] inverseBinds = glb.Floats(glb.Accessor(glb.Json.GetProperty("skins")[0].GetProperty("inverseBindMatrices").GetInt32()), 16);
            for (int i = 0; i < scm.Bones.Count; i++)
            {
                Assert.AreEqual(scm.Bones[i].Name, nodes[i].GetProperty("name").GetString());
                Assert.AreEqual(scm.Bones[i].Parent, parents.GetValueOrDefault(i, -1));
                float[] t = [.. nodes[i].GetProperty("translation").EnumerateArray().Select(v => v.GetSingle())];
                float[] r = [.. nodes[i].GetProperty("rotation").EnumerateArray().Select(v => v.GetSingle())];
                // System.Numerics multiplies row vectors: the local transform comes first
                Matrix4x4 local = Matrix4x4.CreateFromQuaternion(new Quaternion(r[0], r[1], r[2], r[3])) * Matrix4x4.CreateTranslation(t[0], t[1], t[2]);
                int parent = scm.Bones[i].Parent;
                world[i] = parent < 0 ? local : local * world[parent];

                // glTF's column-major order of a column-vector matrix is the row-major order of a row-vector one
                Matrix4x4 inverseBind = new Matrix4x4(
                    inverseBinds[i * 16], inverseBinds[i * 16 + 1], inverseBinds[i * 16 + 2], inverseBinds[i * 16 + 3],
                    inverseBinds[i * 16 + 4], inverseBinds[i * 16 + 5], inverseBinds[i * 16 + 6], inverseBinds[i * 16 + 7],
                    inverseBinds[i * 16 + 8], inverseBinds[i * 16 + 9], inverseBinds[i * 16 + 10], inverseBinds[i * 16 + 11],
                    inverseBinds[i * 16 + 12], inverseBinds[i * 16 + 13], inverseBinds[i * 16 + 14], inverseBinds[i * 16 + 15]);
                Matrix4x4 rest = inverseBind * world[i];
                for (int row = 0; row < 4; row++)
                {
                    for (int column = 0; column < 4; column++)
                    {
                        Assert.AreEqual(row == column ? 1 : 0, rest[row, column], 1e-3, $"bone {scm.Bones[i].Name}");
                    }
                }
            }
        }

        [TestMethod]
        public void KeepsTheNotes()
        {
            Glb glb = Glb.Read(GlbWriter.Write(Parse(ScmParserTest.Ual0101Lod1)));

            JsonElement information = glb.Json.GetProperty("asset").GetProperty("extras").GetProperty("information");
            Assert.AreEqual("Machine:JALBERT-203", information[0].GetString());
        }

        [TestMethod]
        public void WritesAMeshWithoutBonesWithoutASkin()
        {
            Scm scm = ScmParser.Parse(File.ReadAllBytes(ScmParserTest.Ual0101Lod1));
            Glb glb = Glb.Read(GlbWriter.Write(scm with { Bones = [] }));

            Assert.IsFalse(glb.Json.TryGetProperty("skins", out _));
            Assert.AreEqual(scm.Indices.Length, glb.Accessor(glb.Json.GetProperty("meshes")[0].GetProperty("primitives")[0].GetProperty("indices").GetInt32()).GetProperty("count").GetInt32());
        }

        [TestMethod]
        public void RejectsAMeshItCannotWrite()
        {
            Scm scm = Parse(ScmParserTest.Ual0101Lod1);

            Assert.ThrowsException<ArgumentException>(() => GlbWriter.Write(scm with { Indices = [] }));
            Assert.ThrowsException<ArgumentException>(() => GlbWriter.Write(scm with { Indices = [0, 1, 999] }));
            Assert.ThrowsException<ArgumentException>(() => GlbWriter.Write(scm with { Bones = [scm.Bones[0] with { Parent = 1 }, scm.Bones[1], scm.Bones[2]] }));
        }
    }
}
