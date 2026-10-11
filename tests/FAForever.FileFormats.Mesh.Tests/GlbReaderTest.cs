using System.Numerics;
using System.Text;
using FAForever.FileFormats.Mesh;

namespace FAForever.FileFormats.Mesh.Tests
{
    [TestClass]
    public class GlbReaderTest
    {
        private static Scm Parse(string path) => ScmParser.Parse(File.ReadAllBytes(path));

        [TestMethod]
        [DataRow(ScmParserTest.Ual0101Lod0)]
        [DataRow(ScmParserTest.Uel0401Lod1)]
        [DataRow(ScmParserTest.Xsl0001Lod1)]
        [DataRow(ScmParserTest.Blender)]
        [DataRow(ScmParserTest.Max)]
        public void ReadsWhatTheWriterWrote(string path)
        {
            Scm scm = Parse(path);
            Scm read = GlbReader.Read(GlbWriter.Write(scm));

            CollectionAssert.AreEqual(scm.Indices, read.Indices);
            CollectionAssert.AreEqual(scm.Information.ToArray(), read.Information.ToArray());
            Assert.AreEqual(scm.Vertices.Count, read.Vertices.Count);
            for (int i = 0; i < scm.Vertices.Count; i++)
            {
                ScmVertex expected = scm.Vertices[i], actual = read.Vertices[i];
                Assert.AreEqual(expected.Position, actual.Position);
                Assert.AreEqual(expected.Uv0, actual.Uv0);
                Assert.AreEqual(expected.Uv1, actual.Uv1);
                Assert.AreEqual((expected.Bone0, expected.Bone1, expected.Bone2, expected.Bone3), (actual.Bone0, actual.Bone1, actual.Bone2, actual.Bone3));
                Assert.AreEqual(GlbWriter.Normal(expected), actual.Normal);
                // the tangent comes back turned round into the game's convention, the binormal
                // across the normal and tangent, on the side of the game's binormal
                Vector4 tangent = GlbWriter.Tangent(expected);
                Assert.AreEqual(-new Vector3(tangent.X, tangent.Y, tangent.Z), actual.Tangent);
                Assert.AreEqual(Vector3.Cross(actual.Normal, -actual.Tangent) * tangent.W, actual.Binormal);
                Assert.IsTrue(Vector4.Distance(tangent, GlbWriter.Tangent(actual)) < 1e-5f, $"written again, the same: {tangent} {GlbWriter.Tangent(actual)}");
            }
            Assert.AreEqual(scm.Bones.Count, read.Bones.Count);
            for (int i = 0; i < scm.Bones.Count; i++)
            {
                ScmBone expected = scm.Bones[i], actual = read.Bones[i];
                Assert.AreEqual(expected.Name, actual.Name);
                Assert.AreEqual(expected.Parent, actual.Parent);
                Assert.AreEqual(expected.Position, actual.Position);
                Assert.AreEqual(expected.RestPoseInverse, actual.RestPoseInverse);
                Assert.AreEqual(Quaternion.Normalize(expected.Rotation), actual.Rotation);
            }
        }

        [TestMethod]
        public void ReadsAMeshWithoutBones()
        {
            Scm scm = Parse(ScmParserTest.Ual0101Lod1) with { Bones = [] };

            Scm read = GlbReader.Read(GlbWriter.Write(scm));

            Assert.AreEqual(0, read.Bones.Count);
            CollectionAssert.AreEqual(scm.Indices, read.Indices);
        }

        [TestMethod]
        [DataRow(5123, 5126)]
        [DataRow(5121, 5121)]
        [DataRow(5123, 5123)]
        public void TakesTheHeaviestJointAsTheBone(int jointType, int weightType)
        {
            // another tool's file: 16-bit joints, weights as floats or normalised, the main joint not first
            int[][] joints = [[0, 2, 0, 0], [1, 2, 0, 0], [2, 1, 0, 0]];
            float[][] weights = [[1, 0, 0, 0], [0.25f, 0.75f, 0, 0], [0.5f, 0.5f, 0, 0]];

            Scm scm = GlbReader.Read(Skinned(joints, weights, jointType, weightType));

            Assert.AreEqual((0, 2, 0, 0), Bones(scm.Vertices[0]));
            Assert.AreEqual((2, 1, 0, 0), Bones(scm.Vertices[1]), "the joint of weight 0.75 first, the others in their order");
            Assert.AreEqual((2, 1, 0, 0), Bones(scm.Vertices[2]), "the first of equal weights");
        }

        [TestMethod]
        public void RejectsAJointPastTheBonesAMeshCanName()
        {
            int[][] joints = [[300, 0, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0]];
            float[][] weights = [[1, 0, 0, 0], [1, 0, 0, 0], [1, 0, 0, 0]];

            Assert.ThrowsException<GlbFormatException>(() => GlbReader.Read(Skinned(joints, weights, 5123, 5126)));
        }

        private static (int, int, int, int) Bones(ScmVertex vertex) => (vertex.Bone0, vertex.Bone1, vertex.Bone2, vertex.Bone3);

        /// <summary>
        /// A triangle skinned to three joints, as a tool other than <see cref="GlbWriter"/> may write it.
        /// </summary>
        private static byte[] Skinned(int[][] joints, float[][] weights, int jointType, int weightType)
        {
            using MemoryStream binary = new MemoryStream();
            using BinaryWriter w = new BinaryWriter(binary);
            int Align()
            {
                while (binary.Length % 4 != 0)
                {
                    w.Write((byte)0);
                }
                return (int)binary.Length;
            }
            int positions = Align();
            foreach (Vector3 p in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitZ })
            {
                w.Write(p.X); w.Write(p.Y); w.Write(p.Z);
            }
            int indices = Align();
            foreach (ushort index in new ushort[] { 0, 2, 1 })
            {
                w.Write(index);
            }
            int jointsAt = Align();
            foreach (int joint in joints.SelectMany(j => j))
            {
                if (jointType == 5123) { w.Write((ushort)joint); } else { w.Write((byte)joint); }
            }
            int weightsAt = Align();
            foreach (float weight in weights.SelectMany(v => v))
            {
                switch (weightType)
                {
                    case 5126: w.Write(weight); break;
                    case 5123: w.Write((ushort)MathF.Round(weight * 65535)); break;
                    default: w.Write((byte)MathF.Round(weight * 255)); break;
                }
            }
            int end = Align();
            w.Flush();
            int jointSize = jointType == 5123 ? 2 : 1;
            int weightSize = weightType == 5126 ? 4 : weightType == 5123 ? 2 : 1;
            string json = $$"""
                {"asset":{"version":"2.0"},
                 "nodes":[{"name":"A","children":[1,2]},{"name":"B"},{"name":"C"},{"mesh":0,"skin":0}],
                 "meshes":[{"primitives":[{"attributes":{"POSITION":0,"JOINTS_0":2,"WEIGHTS_0":3},"indices":1}]}],
                 "skins":[{"joints":[0,1,2]}],
                 "accessors":[
                  {"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"},
                  {"bufferView":1,"componentType":5123,"count":3,"type":"SCALAR"},
                  {"bufferView":2,"componentType":{{jointType}},"count":3,"type":"VEC4"},
                  {"bufferView":3,"componentType":{{weightType}},"normalized":{{(weightType == 5126 ? "false" : "true")}},"count":3,"type":"VEC4"}],
                 "bufferViews":[
                  {"buffer":0,"byteOffset":{{positions}},"byteLength":36},
                  {"buffer":0,"byteOffset":{{indices}},"byteLength":6},
                  {"buffer":0,"byteOffset":{{jointsAt}},"byteLength":{{12 * jointSize}}},
                  {"buffer":0,"byteOffset":{{weightsAt}},"byteLength":{{12 * weightSize}}}],
                 "buffers":[{"byteLength":{{end}}}]}
                """;
            byte[] text = Encoding.UTF8.GetBytes(json.PadRight((json.Length + 3) / 4 * 4));
            using MemoryStream glb = new MemoryStream();
            using BinaryWriter g = new BinaryWriter(glb);
            g.Write(0x46546c67u);
            g.Write(2u);
            g.Write((uint)(12 + 8 + text.Length + 8 + end));
            g.Write((uint)text.Length);
            g.Write(0x4e4f534au);
            g.Write(text);
            g.Write((uint)end);
            g.Write(0x004e4942u);
            g.Write(binary.ToArray(), 0, end);
            g.Flush();
            return glb.ToArray();
        }

        [TestMethod]
        public void RejectsWhatIsNoGlb()
        {
            byte[] glb = GlbWriter.Write(Parse(ScmParserTest.Ual0101Lod1));
            byte[] broken = (byte[])glb.Clone();
            broken[20] = (byte)']';

            Assert.ThrowsException<GlbFormatException>(() => GlbReader.Read(File.ReadAllBytes(ScmParserTest.Ual0101Lod1)));
            Assert.ThrowsException<GlbFormatException>(() => GlbReader.Read(Encoding.ASCII.GetBytes("not a mesh at all")));
            Assert.ThrowsException<GlbFormatException>(() => GlbReader.Read(glb.AsSpan(0, glb.Length / 2)));
            Assert.ThrowsException<GlbFormatException>(() => GlbReader.Read(broken));
        }
    }
}
