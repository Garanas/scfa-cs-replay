using System.Numerics;
using FAForever.FileFormats.Mesh;

namespace FAForever.FileFormats.Mesh.Tests
{
    [TestClass]
    public class ScmWriterTest
    {
        [TestMethod]
        [DataRow(ScmParserTest.Ual0101Lod0)]
        [DataRow(ScmParserTest.Ual0101Lod1)]
        [DataRow(ScmParserTest.Uel0401Lod1)]
        [DataRow(ScmParserTest.Xsl0001Lod1)]
        public void WritesAGameMeshBackToTheSameBytes(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);

            CollectionAssert.AreEqual(bytes, ScmWriter.Write(ScmParser.Parse(bytes)));
        }

        [TestMethod]
        [DataRow(ScmParserTest.Blender)]
        [DataRow(ScmParserTest.Max)]
        public void WritesAMeshOfAnotherExporterBackToTheSameMesh(string path)
        {
            // these exporters align to 16 bytes and count the weighted bones differently
            Scm scm = ScmParser.Parse(File.ReadAllBytes(path));
            Scm written = ScmParser.Parse(ScmWriter.Write(scm));

            CollectionAssert.AreEqual(scm.Bones.ToArray(), written.Bones.ToArray());
            CollectionAssert.AreEqual(scm.Vertices.ToArray(), written.Vertices.ToArray());
            CollectionAssert.AreEqual(scm.Indices, written.Indices);
            CollectionAssert.AreEqual(scm.Information.ToArray(), written.Information.ToArray());
        }

        [TestMethod]
        public void WritesAMeshWithoutNotes()
        {
            Scm scm = Triangle() with { Information = [] };

            byte[] bytes = ScmWriter.Write(scm);
            Scm written = ScmParser.Parse(bytes);

            Assert.AreEqual(0, written.Information.Count);
            Assert.AreEqual("Root", written.Bones[0].Name);
            CollectionAssert.AreEqual(scm.Vertices.ToArray(), written.Vertices.ToArray());
            Assert.AreEqual(1, BitConverter.ToInt32(bytes, 12)); // the weighted bones
        }

        [TestMethod]
        public void WritesAtTheStreamsPosition()
        {
            using MemoryStream stream = new MemoryStream();
            stream.Write([1, 2, 3]);
            ScmWriter.Write(Triangle(), stream);

            Assert.AreEqual(Triangle().Vertices.Count, ScmParser.Parse(stream.ToArray().AsSpan(3)).Vertices.Count);
        }

        [TestMethod]
        public void RejectsAMeshItCannotStore()
        {
            Scm scm = Triangle();

            Assert.ThrowsException<ArgumentException>(() => ScmWriter.Write(scm with { Indices = [0, 1] }));
            Assert.ThrowsException<ArgumentException>(() => ScmWriter.Write(scm with { Indices = [0, 1, 3] }));
            Assert.ThrowsException<ArgumentException>(() => ScmWriter.Write(scm with { Vertices = [.. scm.Vertices.Select(v => v with { Bone0 = 1 })] }));
            Assert.ThrowsException<ArgumentException>(() => ScmWriter.Write(scm with { Bones = [scm.Bones[0] with { Parent = 1 }] }));
        }

        private static Scm Triangle() => new Scm
        {
            Bones = [new ScmBone("Root", Matrix4x4.Identity, Vector3.Zero, Quaternion.Identity, -1)],
            Vertices =
            [
                new ScmVertex(new Vector3(0, 0, 0), Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, new Vector2(0, 0), Vector2.Zero, 0),
                new ScmVertex(new Vector3(1, 0, 0), Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, new Vector2(1, 0), Vector2.Zero, 0),
                new ScmVertex(new Vector3(0, 0, 1), Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, new Vector2(0, 1), Vector2.Zero, 0),
            ],
            Indices = [0, 2, 1],
            Information = ["Written by a test"],
        };
    }
}
