using System.Numerics;
using System.Text.Json;
using FAForever.MapEditor.App.Rendering;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The levels of detail of the 3D view's props and units (<c>ObjectModels</c>, <see cref="ObjectLods"/>),
    /// chosen as the engine chooses them (faf-re, Draiget/faf-re commit 2ba55b2: <c>Mesh::ComputeLOD</c>
    /// in <c>src/sdk/moho/mesh/Mesh.cpp</c>, the default cutoff in <c>RMeshBlueprint.cpp</c>).
    /// </summary>
    [TestClass]
    public sealed class ObjectModelsTest
    {
        [TestMethod]
        [DataRow(0f, 0)]
        [DataRow(130f, 0)]
        [DataRow(130.5f, 1)]
        [DataRow(260f, 1)]
        [DataRow(261f, 2)]
        [DataRow(5000f, 2)]
        public void ShowsTheFirstLevelWhoseCutoffReachesTheDepth(float depth, int level)
        {
            // the last level stays in sight beyond its cutoff (the game fades it out)
            Assert.AreEqual(level, ObjectLods.LevelAt([130, 260, 520], depth));
        }

        [TestMethod]
        [DataRow(0f)]
        [DataRow(-5f)]
        public void ShowsALevelWithACutoffOfZeroOrLessAtEveryDepth(float cutoff)
        {
            float[] cutoffs = [cutoff, 260, 520];

            Assert.AreEqual(0, ObjectLods.LevelAt(cutoffs, 10));
            Assert.AreEqual(0, ObjectLods.LevelAt(cutoffs, 400));
            Assert.AreEqual(0, ObjectLods.LevelAt(cutoffs, 10_000));
            Assert.AreEqual(1, ObjectLods.LevelAt([100, cutoff, 520], 10_000), "after a level that ends");
        }

        [TestMethod]
        public void ShowsTheOnlyLevelEverywhere()
        {
            Assert.AreEqual(0, ObjectLods.LevelAt([ObjectLods.DefaultCutoff], 50_000));
        }

        [TestMethod]
        [DataRow("""{"mesh": "a_lod0.glb"}""", 1000f)]
        [DataRow("""{"mesh": "a_lod0.glb", "cutoff": null}""", 1000f)]
        [DataRow("""{"mesh": "a_lod0.glb", "cutoff": 215}""", 215f)]
        [DataRow("""{"mesh": "a_lod0.glb", "cutoff": 0}""", 0f)]
        public void ALevelWithoutACutoffShowsUpTo1000(string lod, float cutoff)
        {
            using JsonDocument json = JsonDocument.Parse(lod);

            Assert.AreEqual(cutoff, ObjectLods.CutoffOf(json.RootElement));
        }

        [TestMethod]
        public void ChoosesTheLevelByTheDepthInTheView()
        {
            // a prop off to the side is as deep in the view as one straight ahead at the same depth
            Vector3 eye = new Vector3(100, 80, 100);
            Vector3 target = new Vector3(100, 0, 300);
            Matrix4x4 viewProjection = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY)
                * Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, 16 / 9f, 1, 4000);

            Vector3 forward = ObjectLods.ForwardOf(viewProjection);

            Assert.IsTrue(Vector3.Distance(Vector3.Normalize(target - eye), forward) < 1e-5f, $"{forward}");
            Vector3 ahead = eye + 200 * forward;
            Vector3 aside = ahead + 150 * Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
            Assert.AreEqual(200, ObjectLods.ViewDepth(forward, eye, ahead), 1e-3);
            Assert.AreEqual(200, ObjectLods.ViewDepth(forward, eye, aside), 1e-3);
            Assert.AreEqual(0, ObjectLods.LevelAt([220, 1000], ObjectLods.ViewDepth(forward, eye, aside)), "250 ogrids away, 200 deep");
        }
    }
}
