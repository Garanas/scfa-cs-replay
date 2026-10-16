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
        [DataRow(ScmParserTest.FafVrtx)]
        public void WritesAMeshOfAnotherExporterBackToTheSameMesh(string path)
        {
            // these exporters align to 16 bytes, pad with other bytes or name a section differently
            byte[] bytes = File.ReadAllBytes(path);
            Scm scm = ScmParser.Parse(bytes);
            byte[] writtenBytes = ScmWriter.Write(scm);
            Scm written = ScmParser.Parse(writtenBytes);

            Assert.AreEqual(BitConverter.ToInt32(bytes, 12), BitConverter.ToInt32(writtenBytes, 12), "the skinned bone count as it was");
            Assert.AreEqual(scm.SkinnedBoneCount, written.SkinnedBoneCount);

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

        [TestMethod]
        [DataRow(ScmParserTest.Ual0101Lod0)]
        [DataRow(ScmParserTest.Blender)]
        public void RejectsABoneBeforeItsParent(string path)
        {
            // the engine updates the pose in one pass from the first bone to the last
            Scm scm = ScmParser.Parse(File.ReadAllBytes(path));
            ScmBone[] bones = [.. scm.Bones];
            bones[1] = bones[1] with { Parent = bones.Length - 1 };

            Assert.ThrowsException<ArgumentException>(() => ScmWriter.Write(scm with { Bones = bones }));
            Assert.ThrowsException<ArgumentException>(() => ScmWriter.Write(scm with { Bones = [.. scm.Bones.Select((bone, i) => i == 0 ? bone with { Parent = 0 } : bone)] }));
        }

        [TestMethod]
        [DataRow(ScmParserTest.Ual0101Lod0)]
        [DataRow(ScmParserTest.FafVrtx)]
        public void RejectsASkinnedBoneCountTheEngineCannotUse(string path)
        {
            Scm scm = ScmParser.Parse(File.ReadAllBytes(path));
            int weighted = ScmWriter.WeightedBoneCount(scm);

            Assert.ThrowsException<ArgumentException>(() => ScmWriter.Write(scm with { SkinnedBoneCount = weighted - 1 }));
            Assert.ThrowsException<ArgumentException>(() => ScmWriter.Write(scm with { SkinnedBoneCount = scm.Bones.Count + 1 }));
            Assert.AreEqual(weighted, BitConverter.ToInt32(ScmWriter.Write(scm with { SkinnedBoneCount = null }), 12), "null derives it");
            Assert.AreEqual(scm.Bones.Count, BitConverter.ToInt32(ScmWriter.Write(scm with { SkinnedBoneCount = scm.Bones.Count }), 12));
        }

        [TestMethod]
        public void RejectsMoreSkinnedBonesThanThePaletteHolds()
        {
            // the engine shares 80 bone matrices between the instances of a draw
            Scm scm = Triangle();
            ScmBone[] bones = [.. Enumerable.Range(0, ScmWriter.MaxSkinnedBones + 1).Select(i => scm.Bones[0] with { Name = $"Bone{i}", Parent = i - 1 })];
            ScmVertex[] vertices = [.. scm.Vertices.Select((vertex, i) => vertex with { Bone0 = (byte)(i == 0 ? ScmWriter.MaxSkinnedBones : 0) })];

            Assert.ThrowsException<ArgumentException>(() => ScmWriter.Write(scm with { Bones = bones, Vertices = vertices }));
            Assert.ThrowsException<ArgumentException>(() => ScmWriter.Write(scm with { Bones = bones, SkinnedBoneCount = bones.Length }));
            Assert.AreEqual(ScmWriter.MaxSkinnedBones, BitConverter.ToInt32(ScmWriter.Write(scm with { Bones = bones, SkinnedBoneCount = ScmWriter.MaxSkinnedBones }), 12));
        }

        [TestMethod]
        public void RejectsMoreThan65535Vertices()
        {
            // a prop's batch holds 0xFFFF / vertices instances: none at 65,536
            Scm scm = Triangle();
            ScmVertex[] vertices = [.. Enumerable.Repeat(scm.Vertices[0], ScmWriter.MaxVertices + 1)];

            Assert.ThrowsException<ArgumentException>(() => ScmWriter.Write(scm with { Vertices = vertices }));
            Assert.AreEqual(ScmWriter.MaxVertices, ScmParser.Parse(ScmWriter.Write(scm with { Vertices = vertices[1..] })).Vertices.Count);
        }

        [TestMethod]
        [DataRow(ScmParserTest.Blender)]
        [DataRow(ScmParserTest.FafVrtx)]
        public void PutsTheBoneNamesWhereTheEngineReadsThem(string path)
        {
            // the engine reads the names one after the other from byte 0x40
            Scm scm = ScmParser.Parse(File.ReadAllBytes(path));
            byte[] bytes = ScmWriter.Write(scm);

            Assert.AreEqual("NAME", System.Text.Encoding.ASCII.GetString(bytes, 0x3C, 4));
            int at = 0x40;
            int boneOffset = BitConverter.ToInt32(bytes, 0x08);
            for (int i = 0; i < scm.Bones.Count; i++)
            {
                int end = Array.IndexOf(bytes, (byte)0, at);
                Assert.AreEqual(scm.Bones[i].Name, System.Text.Encoding.UTF8.GetString(bytes, at, end - at));
                Assert.AreEqual(at, BitConverter.ToInt32(bytes, boneOffset + i * 108 + 0x5C), "and each bone's offset says so");
                at = end + 1;
            }
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
