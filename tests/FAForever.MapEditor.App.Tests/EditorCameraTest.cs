using System.Numerics;
using FAForever.MapEditor.App.Rendering.Gpu;

namespace FAForever.MapEditor.App.Tests
{
    [TestClass]
    public sealed class EditorCameraTest
    {
        private static EditorCamera Flat(float height = 0)
        {
            EditorCamera camera = new EditorCamera((_, _, _) => height, (_, _) => height);
            camera.Reset(512, 512);
            return camera;
        }

        private static void Settle(EditorCamera camera)
        {
            for (int i = 0; i < 200 && camera.Update(0.05f); i++)
            {
            }
        }

        [TestMethod]
        public void StartsStraightDownOverTheMiddle()
        {
            EditorCamera camera = Flat();

            (float x, float z, float distance, float yaw, float pitch) = camera.Now;

            Assert.AreEqual((256f, 256f, 0f, 90f), (x, z, yaw, pitch));
            Assert.IsTrue(distance > 512, "the whole map in view");
            Assert.AreEqual(256, camera.Eye.X, 0.01);
            Assert.AreEqual(256, camera.Eye.Z, 0.01);
        }

        [TestMethod]
        public void ZoomsTowardsWhatTheMousePointsAt()
        {
            EditorCamera camera = Flat();
            float before = camera.Now.Distance;

            camera.Zoom(new Vector2(0.8f, 0), 1, 1);
            Settle(camera);

            Assert.IsTrue(camera.Now.Distance < before * 0.85f, "three steps of 8.6 % closer");
            Assert.IsTrue(camera.Now.X > 256 + 10, "the mouse is to the right, east at yaw 0");
            Assert.AreEqual(256, camera.Now.Z, 0.5);
        }

        [TestMethod]
        public void ZoomsInNoCloserThanItsMinimum()
        {
            EditorCamera camera = Flat(30);

            camera.Zoom(Vector2.Zero, 1, 200);
            Settle(camera);

            Assert.AreEqual(4.5, camera.Now.Distance, 0.1);
            Assert.AreEqual(34.5, camera.Eye.Y, 0.1, "above the ground of the hill, not of the plain");
        }

        [TestMethod]
        public void ZoomsOutAlongTheView()
        {
            EditorCamera camera = Flat();
            float before = camera.Now.Distance;

            camera.Zoom(new Vector2(0.8f, 0.5f), 1, -1);
            Settle(camera);

            Assert.IsTrue(camera.Now.Distance > before);
            Assert.AreEqual(256, camera.Now.X, 0.5, "out goes straight back, wherever the mouse is");
        }

        [TestMethod]
        public void PanningMovesTheGroundWithTheMouse()
        {
            EditorCamera camera = Flat();

            camera.Pan(new Vector2(100, 0), 800);
            Settle(camera);

            Assert.IsTrue(camera.Now.X < 256, "the mouse to the right drags the map right: the camera looks further west");
            Assert.AreEqual(256, camera.Now.Z, 0.5);
        }

        [TestMethod]
        public void TurnsAndTiltsWithinItsLimits()
        {
            EditorCamera camera = Flat();

            camera.Rotate(new Vector2(40, -1000));
            Settle(camera);

            Assert.AreEqual(-10, camera.Now.Yaw, 0.01, "to the right turns clockwise");
            Assert.AreEqual(10, camera.Now.Pitch, 0.01, "up tilts towards the horizon, no further than 10 degrees");

            camera.Rotate(new Vector2(0, 1000));
            Settle(camera);
            Assert.AreEqual(90, camera.Now.Pitch, 0.01, "straight down at most");
        }

        [TestMethod]
        public void GlidesToItsTarget()
        {
            EditorCamera camera = Flat();
            float before = camera.Now.Distance;

            camera.Zoom(Vector2.Zero, 1, 1);
            Assert.IsTrue(camera.Update(0.01f), "still on its way after a hundredth of a second");
            float halfway = camera.Now.Distance;
            Settle(camera);

            Assert.IsTrue(halfway < before && halfway > camera.Now.Distance);
            Assert.IsFalse(camera.Update(0.01f));
        }

        [TestMethod]
        public void FocusesOnAPlaceAndWhatIsAroundIt()
        {
            EditorCamera camera = Flat();

            camera.Focus(new Vector2(100, 400), 30);
            Settle(camera);

            Assert.AreEqual((100f, 400f), (camera.Now.X, camera.Now.Z));
            Assert.AreEqual(30 / MathF.Tan(EditorCamera.FieldOfView / 2) * 1.1f, camera.Now.Distance, 0.01);
            Assert.AreEqual(90, camera.Now.Pitch, "the angles stay");
        }

        [TestMethod]
        public void MovesToAPlaceAtOnce()
        {
            EditorCamera camera = Flat();
            float distance = camera.Now.Distance;

            camera.MoveTo(new Vector2(10, 20));

            Assert.AreEqual((10f, 20f, distance), (camera.Now.X, camera.Now.Z, camera.Now.Distance));
            Assert.IsFalse(camera.Update(0.01f), "no gliding: the 3D view may be hidden");
        }

        [TestMethod]
        public void FindsThePlaceUnderTheMouse()
        {
            EditorCamera camera = Flat(12);
            camera.Aspect = 1;

            Vector3 middle = camera.PointAt(Vector2.Zero)!.Value;
            Vector3 right = camera.PointAt(new Vector2(1, 0))!.Value;
            Vector3 top = camera.PointAt(new Vector2(0, 1))!.Value;

            Assert.AreEqual(256, middle.X, 0.1);
            Assert.AreEqual(256, middle.Z, 0.1);
            Assert.AreEqual(12, middle.Y, 0.1);
            Assert.IsTrue(right.X > 400, "the right of the view is east at yaw 0");
            Assert.IsTrue(top.Z < 100, "the top of the view is north");
        }

        [TestMethod]
        public void ItsFootprintIsWhatItSees()
        {
            EditorCamera camera = Flat();
            camera.Aspect = 2;

            Vector2[] straightDown = camera.Footprint();
            float halfHeight = camera.Now.Distance * MathF.Tan(EditorCamera.FieldOfView / 2);
            Assert.AreEqual(256 - halfHeight * 2, straightDown[0].X, 0.5, "top left, twice as wide as high");
            Assert.AreEqual(256 - halfHeight, straightDown[0].Y, 0.5);
            Assert.AreEqual(256 + halfHeight * 2, straightDown[2].X, 0.5, "bottom right");

            camera.Look(pitch: 20);
            Vector2[] tilted = camera.Footprint();
            Assert.IsTrue(tilted[1].X - tilted[0].X > tilted[2].X - tilted[3].X, "a trapezoid: wider far away, at the top");
            Assert.IsTrue(tilted[0].Y < 256 && tilted[3].Y > tilted[0].Y);
        }
    }
}
