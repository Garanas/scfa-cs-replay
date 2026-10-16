using System.Numerics;
using FAForever.FileFormats.Mesh;

namespace FAForever.FileFormats.Mesh.Tests
{
    [TestClass]
    public class ScmParserTest
    {
        public const string Ual0101Lod0 = "assets/meshes/forged-alliance/ual0101_lod0.scm";
        public const string Ual0101Lod1 = "assets/meshes/forged-alliance/ual0101_lod1.scm";
        public const string Uel0401Lod1 = "assets/meshes/forged-alliance/uel0401_lod1.scm";
        public const string Xsl0001Lod1 = "assets/meshes/forged-alliance/xsl0001_lod1.scm";
        public const string Blender = "assets/meshes/blender/url0302_lod1.scm";
        public const string Max = "assets/meshes/3dsmax/srl0310_lod3.scm";

        /// <summary>
        /// FAF's <c>units/XSA0402/XSA0402_lod1.scm</c> (units.nx2), one of the four meshes that name
        /// their vertices <c>VRTX</c>.
        /// </summary>
        public const string FafVrtx = "assets/meshes/faf/xsa0402_lod1.scm";

        [TestMethod]
        [DataRow(Ual0101Lod0, 5, 636, 556)]
        [DataRow(Ual0101Lod1, 3, 110, 60)]
        [DataRow(Uel0401Lod1, 16, 666, 338)]
        [DataRow(Xsl0001Lod1, 15, 492, 238)]
        [DataRow(Blender, 46, 846, 474)]
        [DataRow(Max, 36, 354, 118)]
        [DataRow(FafVrtx, 12, 1608, 1114)]
        public void ReadsTheCounts(string path, int bones, int vertices, int triangles)
        {
            Scm scm = ScmParser.Parse(File.ReadAllBytes(path));

            Assert.AreEqual(bones, scm.Bones.Count);
            Assert.AreEqual(vertices, scm.Vertices.Count);
            Assert.AreEqual(triangles, scm.TriangleCount);
            Assert.IsTrue(scm.Indices.All(index => index < vertices));
            Assert.AreEqual(-1, scm.Bones[0].Parent);
            Assert.IsTrue(scm.Bones.Skip(1).All(bone => bone.Parent >= 0 && bone.Parent < bones));
        }

        [TestMethod]
        public void ReadsTheBonesAndNotesOfAGameMesh()
        {
            Scm scm = ScmParser.Parse(File.ReadAllBytes(Ual0101Lod0));

            CollectionAssert.AreEqual(
                new[] { "UAL0101", "Turret", "Turret_Barrel" },
                scm.Bones.Take(3).Select(bone => bone.Name).ToArray());
            Assert.AreEqual(0, scm.Bones[1].Parent);
            Assert.AreEqual(1, scm.Bones[2].Parent);
            CollectionAssert.AreEqual(
                new[] { "Machine:JALBERT-203", @"File:D:\work\rts\main\art\Units\Alien\Land\Mesh\", "ExportTime:11/14/2006 3:39:13 PM" },
                scm.Information.ToArray());
        }

        [TestMethod]
        [DataRow(Ual0101Lod0)]
        [DataRow(Xsl0001Lod1)]
        [DataRow(Blender)]
        public void ReadsTheNormalBeforeTheTangent(string path)
        {
            Scm scm = ScmParser.Parse(File.ReadAllBytes(path));

            // the normals point along the faces of the triangles, the tangents across them
            float normal = 0, tangent = 0;
            for (int i = 0; i < scm.Indices.Length; i += 3)
            {
                ScmVertex a = scm.Vertices[scm.Indices[i]];
                Vector3 face = Vector3.Cross(
                    scm.Vertices[scm.Indices[i + 1]].Position - a.Position,
                    scm.Vertices[scm.Indices[i + 2]].Position - a.Position);
                normal += MathF.Abs(Vector3.Dot(face, a.Normal));
                tangent += MathF.Abs(Vector3.Dot(face, a.Tangent));
            }
            Assert.IsTrue(normal > 2 * tangent, $"normal {normal}, tangent {tangent}");
        }

        [TestMethod]
        public void IgnoresTheNamesOfTheSections()
        {
            // the engine reads no section name; FAF's frigate, destroyer and a few more say VRTX for VTXL
            byte[] bytes = File.ReadAllBytes(FafVrtx);
            int vertexOffset = BitConverter.ToInt32(bytes, 0x10);
            Assert.AreEqual("VRTX", System.Text.Encoding.ASCII.GetString(bytes, vertexOffset - 4, 4));

            Scm scm = ScmParser.Parse(bytes);

            Assert.AreEqual(1608, scm.Vertices.Count);
            Assert.IsTrue(scm.Vertices.All(vertex => vertex.Bone0 < scm.Bones.Count));

            byte[] renamed = File.ReadAllBytes(Ual0101Lod1);
            renamed[BitConverter.ToInt32(renamed, 0x08) - 4] = (byte)'X';
            Assert.AreEqual(3, ScmParser.Parse(renamed).Bones.Count);
        }

        [TestMethod]
        [DataRow(Ual0101Lod0, 3)]
        [DataRow(Uel0401Lod1, 16)]
        [DataRow(Blender, 29)]
        [DataRow(Max, 36)]
        [DataRow(FafVrtx, 12)]
        public void KeepsTheSkinnedBoneCount(string path, int skinned)
        {
            // the game's exporter stores the bones the vertices use, others (3ds Max, FAF's VRTX meshes) all bones
            Assert.AreEqual(skinned, ScmParser.Parse(File.ReadAllBytes(path)).SkinnedBoneCount);
        }

        [TestMethod]
        [DataRow(Ual0101Lod0)]
        [DataRow(Ual0101Lod1)]
        [DataRow(Uel0401Lod1)]
        [DataRow(Xsl0001Lod1)]
        public void ReadsTheFirstVertexAndTriangleCountOfABone(string path)
        {
            // the game's exporter sorts the vertices and triangles by bone and stores where each bone's start
            Scm scm = ScmParser.Parse(File.ReadAllBytes(path));

            for (int bone = 0; bone < scm.Bones.Count; bone++)
            {
                int triangles = Enumerable.Range(0, scm.TriangleCount).Count(t => scm.Vertices[scm.Indices[t * 3]].Bone0 == bone);
                Assert.AreEqual(triangles, scm.Bones[bone].TriangleCount, $"bone {bone}");
                if (triangles > 0)
                {
                    int first = Enumerable.Range(0, scm.Vertices.Count).First(v => scm.Vertices[v].Bone0 == bone);
                    Assert.AreEqual(first, scm.Bones[bone].FirstVertex, $"bone {bone}");
                }
            }
        }

        [TestMethod]
        public void ReadsTheBoneNamesFromWhereTheEngineReadsThem()
        {
            // the engine reads the names one after the other from byte 0x40, whatever a bone's offset says
            byte[] bytes = File.ReadAllBytes(Ual0101Lod0);
            int boneOffset = BitConverter.ToInt32(bytes, 0x08);
            BitConverter.GetBytes(0).CopyTo(bytes, boneOffset + 108 + 0x5C);

            CollectionAssert.AreEqual(
                new[] { "UAL0101", "Turret", "Turret_Barrel" },
                ScmParser.Parse(bytes).Bones.Take(3).Select(bone => bone.Name).ToArray());
        }

        [TestMethod]
        public void ReadsAStream()
        {
            using FileStream stream = File.OpenRead(Ual0101Lod1);

            Assert.AreEqual(110, ScmParser.Parse(stream).Vertices.Count);
        }

        [TestMethod]
        public void RejectsWhatIsNotAMesh()
        {
            byte[] bytes = File.ReadAllBytes(Ual0101Lod1);

            byte[] magic = (byte[])bytes.Clone();
            magic[0] = (byte)'X';
            Assert.ThrowsException<ScmFormatException>(() => ScmParser.Parse(magic));

            byte[] version = (byte[])bytes.Clone();
            version[4] = 4;
            Assert.AreEqual(4, Assert.ThrowsException<ScmFormatException>(() => ScmParser.Parse(version)).Offset);

            Assert.ThrowsException<ScmFormatException>(() => ScmParser.Parse(bytes.AsSpan(0, 1000)));
            Assert.ThrowsException<ScmFormatException>(() => ScmParser.Parse(bytes.AsSpan(0, 20)));
        }
    }
}
