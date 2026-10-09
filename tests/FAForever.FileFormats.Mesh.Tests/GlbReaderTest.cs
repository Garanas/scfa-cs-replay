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
                Vector4 tangent = GlbWriter.Tangent(expected);
                Assert.AreEqual(new Vector3(tangent.X, tangent.Y, tangent.Z), actual.Tangent);
                // the binormal comes back across the normal and tangent, on the same side
                Assert.AreEqual(tangent.W, Vector3.Dot(Vector3.Cross(actual.Normal, actual.Tangent), actual.Binormal) < 0 ? -1 : 1);
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
