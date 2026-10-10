using System.Numerics;
using FAForever.FileFormats.Map;

namespace FAForever.FileFormats.Map.Tests
{
    [TestClass]
    public class MapSymmetryTest
    {
        private static Vector3 P(float x, float z) => new Vector3(x, 0, z);

        [TestMethod]
        public void FindsTheSymmetryOfThetaPassage()
        {
            MapSave save = MapSaveParser.Parse(File.ReadAllText(ScmapParserTest.ThetaPassage + "_save.lua"));
            List<Vector3> positions = save.Markers
                .Where(marker => marker.Type is "Mass" or "Hydrocarbon" || marker.Name.StartsWith("ARMY_", StringComparison.Ordinal))
                .Where(marker => marker.Name != "ARMY_17")
                .Select(marker => marker.Position)
                .ToList();

            Assert.IsNotNull(MapSymmetry.Find(256, 256, positions));
        }

        [TestMethod]
        public void TellsTheKindsApart()
        {
            Assert.AreEqual(MapSymmetryKind.Rotational, MapSymmetry.Find(100, 100, [P(10, 20), P(90, 80)]));
            Assert.AreEqual(MapSymmetryKind.MirroredLeftRight, MapSymmetry.Find(100, 100, [P(10, 20), P(90, 20)]));
            Assert.AreEqual(MapSymmetryKind.MirroredTopBottom, MapSymmetry.Find(100, 100, [P(10, 20), P(10, 80)]));
            Assert.AreEqual(MapSymmetryKind.MirroredDiagonal, MapSymmetry.Find(100, 100, [P(10, 30), P(30, 10)]));
            Assert.IsNull(MapSymmetry.Find(100, 100, [P(10, 20), P(70, 40)]));
        }

        [TestMethod]
        public void AllowsSmallDifferences()
        {
            Assert.AreEqual(MapSymmetryKind.Rotational, MapSymmetry.Find(100, 100, [P(10, 20), P(91, 79)]));
            Assert.IsNull(MapSymmetry.Find(100, 100, [P(10, 20), P(95, 75)]));
        }

        private static void AreClose(Vector2 expected, Vector2 actual, string message = "")
        {
            Assert.AreEqual(expected.X, actual.X, 1e-3f, $"x {message}");
            Assert.AreEqual(expected.Y, actual.Y, 1e-3f, $"z {message}");
        }

        [TestMethod]
        public void MirrorsAPointByEachKind()
        {
            Vector2 point = new Vector2(10, 30);
            AreClose(new Vector2(90, 70), MapSymmetry.Images(MapSymmetryKind.Rotational, 2, 100, 100, point)[1]);
            AreClose(new Vector2(90, 30), MapSymmetry.Images(MapSymmetryKind.MirroredLeftRight, 2, 100, 100, point)[1]);
            AreClose(new Vector2(10, 70), MapSymmetry.Images(MapSymmetryKind.MirroredTopBottom, 2, 100, 100, point)[1]);
            AreClose(new Vector2(30, 10), MapSymmetry.Images(MapSymmetryKind.MirroredDiagonal, 2, 100, 100, point)[1]);
            AreClose(new Vector2(70, 90), MapSymmetry.Images(MapSymmetryKind.MirroredOtherDiagonal, 2, 100, 100, point)[1]);
            AreClose(point, MapSymmetry.Images(MapSymmetryKind.MirroredLeftRight, 7, 100, 100, point)[0], "the point itself comes first");
            Assert.AreEqual(2, MapSymmetry.Images(MapSymmetryKind.MirroredTopBottom, 6, 100, 100, point).Length, "a mirror has two parts");
        }

        [TestMethod]
        public void TurnsAPointClockwiseForEachPlayer()
        {
            // straight up from the middle of a 100 by 100 map, clockwise in the top view (z down)
            Vector2[] four = MapSymmetry.Images(MapSymmetryKind.Rotational, 4, 100, 100, new Vector2(50, 10));
            AreClose(new Vector2(50, 10), four[0]);
            AreClose(new Vector2(90, 50), four[1]);
            AreClose(new Vector2(50, 90), four[2]);
            AreClose(new Vector2(10, 50), four[3]);

            for (int count = 2; count <= MapSymmetry.MaximumCount; count++)
            {
                Vector2[] images = MapSymmetry.Images(MapSymmetryKind.Rotational, count, 256, 256, new Vector2(40, 100));
                Assert.AreEqual(count, images.Length);
                foreach (Vector2 image in images)
                {
                    Assert.AreEqual(Vector2.Distance(new Vector2(128, 128), images[0]), Vector2.Distance(new Vector2(128, 128), image), 1e-3f, "every image as far from the middle");
                }
                Assert.AreEqual(Vector2.Distance(images[0], images[1]), Vector2.Distance(images[count - 1], images[0]), 1e-3f, "evenly spaced");
            }
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => MapSymmetry.Transforms(MapSymmetryKind.Rotational, 1, 100, 100));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => MapSymmetry.Transforms(MapSymmetryKind.Rotational, 9, 100, 100));
        }

        [TestMethod]
        public void TheImageOfAnImageIsTheNextImage()
        {
            foreach (MapSymmetryKind kind in Enum.GetValues<MapSymmetryKind>())
            {
                IReadOnlyList<MapSymmetryTransform> transforms = MapSymmetry.Transforms(kind, 5, 200, 200);
                Assert.AreEqual(MapSymmetryTransform.Identity, transforms[0]);
                Assert.AreEqual(kind != MapSymmetryKind.Rotational, transforms[1].IsMirror, kind.ToString());
                // the second transform once more steps on to the next image; a mirror goes back
                Vector2 point = new Vector2(33, 71);
                AreClose(transforms[2 % transforms.Count].Apply(point), transforms[1].Apply(transforms[1].Apply(point)), kind.ToString());
            }
        }

        [TestMethod]
        public void TheInverseTakesAnImageBackAndTheTurnAngleRebuildsTheTransform()
        {
            foreach (MapSymmetryKind kind in Enum.GetValues<MapSymmetryKind>())
            {
                foreach (MapSymmetryTransform transform in MapSymmetry.Transforms(kind, 3, 200, 200))
                {
                    Vector2 point = new Vector2(33, 71);
                    AreClose(point, transform.Inverse().Apply(transform.Apply(point)), kind.ToString());
                    // the linear part is the turn by the angle, after a flip of x for a mirror
                    (float sin, float cos) = MathF.SinCos(transform.TurnAngle);
                    float flip = transform.IsMirror ? -1 : 1;
                    Assert.AreEqual(cos * flip, transform.Xx, 1e-5f, kind.ToString());
                    Assert.AreEqual(-sin, transform.Xz, 1e-5f, kind.ToString());
                    Assert.AreEqual(sin * flip, transform.Zx, 1e-5f, kind.ToString());
                    Assert.AreEqual(cos, transform.Zz, 1e-5f, kind.ToString());
                }
            }
        }

        [TestMethod]
        public void MirrorsAYawAsItMirrorsWhereItFaces()
        {
            float yaw = 0.3f;
            Assert.AreEqual(-yaw, Transform(MapSymmetryKind.MirroredLeftRight).ApplyYaw(yaw), 1e-5f);
            Assert.AreEqual(MathF.PI - yaw, Transform(MapSymmetryKind.MirroredTopBottom).ApplyYaw(yaw), 1e-5f);
            Assert.AreEqual(MathF.PI / 2 - yaw, Transform(MapSymmetryKind.MirroredDiagonal).ApplyYaw(yaw), 1e-5f);
            Assert.AreEqual(-MathF.PI / 2 - yaw, Transform(MapSymmetryKind.MirroredOtherDiagonal).ApplyYaw(yaw), 1e-5f);
            Assert.AreEqual(yaw - MathF.PI, Transform(MapSymmetryKind.Rotational).ApplyYaw(yaw), 1e-5f, "half a turn, from -pi to pi");
            Assert.AreEqual(yaw - MathF.PI / 2, MapSymmetry.Transforms(MapSymmetryKind.Rotational, 4, 100, 100)[1].ApplyYaw(yaw), 1e-5f);
        }

        [TestMethod]
        public void MirrorsARotationAsItsYawAndKeepsItARotation()
        {
            float yaw = 0.7f;
            Matrix4x4 rotation = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(5, 6, 7);
            foreach (MapSymmetryKind kind in Enum.GetValues<MapSymmetryKind>())
            {
                foreach (MapSymmetryTransform transform in MapSymmetry.Transforms(kind, 3, 100, 100))
                {
                    Matrix4x4 image = transform.Apply(rotation);
                    Matrix4x4 expected = Matrix4x4.CreateRotationY(transform.ApplyYaw(yaw)) * Matrix4x4.CreateTranslation(5, 6, 7);
                    Assert.AreEqual(1, image.GetDeterminant(), 1e-4f, $"{kind}: a rotation");
                    foreach ((float actual, float wanted) in new[]
                    {
                        (image.M11, expected.M11), (image.M13, expected.M13), (image.M31, expected.M31), (image.M33, expected.M33),
                        (image.M22, expected.M22), (image.M41, expected.M41), (image.M43, expected.M43),
                    })
                    {
                        Assert.AreEqual(wanted, actual, 1e-4f, kind.ToString());
                    }
                }
            }

            // a prop tilted on a slope keeps its tilt, mirrored
            Matrix4x4 tilted = Matrix4x4.CreateRotationX(0.2f) * Matrix4x4.CreateRotationY(yaw);
            Matrix4x4 mirrored = Transform(MapSymmetryKind.MirroredLeftRight).Apply(tilted);
            Assert.AreEqual(tilted.M32, mirrored.M32, 1e-5f, "it faces as far up as before");
            Assert.AreEqual(-tilted.M31, mirrored.M31, 1e-5f, "and to the other side");
        }

        [TestMethod]
        public void GuidesRunBetweenTheParts()
        {
            (Vector2 From, Vector2 To) axis = MapSymmetry.Guides(MapSymmetryKind.MirroredLeftRight, 2, 100, 50).Single();
            AreClose(new Vector2(50, 0), axis.From);
            AreClose(new Vector2(50, 50), axis.To);
            AreClose(new Vector2(100, 0), MapSymmetry.Guides(MapSymmetryKind.MirroredOtherDiagonal, 2, 100, 100).Single().From);

            (Vector2 From, Vector2 To)[] rays = MapSymmetry.Guides(MapSymmetryKind.Rotational, 4, 100, 100);
            Assert.AreEqual(4, rays.Length);
            AreClose(new Vector2(50, 0), rays[0].To, "the first straight up");
            AreClose(new Vector2(100, 50), rays[1].To);
            (Vector2 From, Vector2 To)[] three = MapSymmetry.Guides(MapSymmetryKind.Rotational, 3, 100, 100);
            Assert.IsTrue(three.All(ray => MathF.Abs(ray.To.X) < 1e-3f || MathF.Abs(ray.To.X - 100) < 1e-3f || MathF.Abs(ray.To.Y) < 1e-3f || MathF.Abs(ray.To.Y - 100) < 1e-3f), "the rays end at the edge");
        }

        private static MapSymmetryTransform Transform(MapSymmetryKind kind) => MapSymmetry.Transforms(kind, 2, 100, 100)[1];
    }
}
