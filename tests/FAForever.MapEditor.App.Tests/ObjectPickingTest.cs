using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.App.Rendering;
using FAForever.MapEditor.App.Rendering.Gpu;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.Tests
{
    [TestClass]
    public sealed class ObjectPickingTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static async Task<MapDocument> HardFfaAsync()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            return model.ShownDocument!;
        }

        private static Prop PropAt(int id, float x, float z, float y = 0) =>
            new Prop { Id = id, Blueprint = "/env/evergreen/props/rocks/rock01_prop.bp", Position = new Vector3(x, y, z), Scale = Vector3.One };

        // a box two ogrids wide and four high on the ground, for every blueprint
        private static ObjectBounds Tall(string blueprint) => new ObjectBounds(new Vector3(-1, 0, -1), new Vector3(1, 4, 1));

        private static readonly Vector3 Down = new Vector3(0, -1, 0);

        [TestMethod]
        public void PropsOfTheMarkerMeshAreEffects()
        {
            Assert.IsTrue(EffectProps.IsEffectMesh(@"D:\ws\assets\env\common\props\marker01_lod0.glb"), "lavasteam02_prop, blowing sand, mist: the game shows an effect");
            Assert.IsFalse(EffectProps.IsEffectMesh(@"D:\ws\assets\env\evergreen\props\rocks\rock01_lod0.glb"));
            Assert.AreEqual(1, EffectProps.Bounds.Max.Y, "picked by the small diamond drawn in its place");
        }

        [TestMethod]
        public async Task FindsThePropUnderAPointAndNotTheOneBesideIt()
        {
            MapDocument document = (await HardFfaAsync()) with { Props = [PropAt(1, 100, 100), PropAt(2, 110, 100)] };
            PickOptions props = new PickOptions(Units: false, Markers: false, Decals: false);

            Assert.AreEqual(ObjectRef.Prop(1), ObjectPicking.Pick(document, new Vector3(100.5f, 200, 100), Down, props, Tall, terrain: 200));
            Assert.AreEqual(ObjectRef.Prop(2), ObjectPicking.Pick(document, new Vector3(109.5f, 200, 100.5f), Down, props, Tall, terrain: 200));
            Assert.IsNull(ObjectPicking.Pick(document, new Vector3(105, 200, 100), Down, props, Tall, terrain: 200), "between them");
            Assert.IsNull(ObjectPicking.Pick(document, new Vector3(100, 200, 100), Down, props with { Props = false }, Tall, terrain: 200), "props hidden");
        }

        [TestMethod]
        public async Task TheNearestObjectAlongTheRayWins()
        {
            MapDocument document = (await HardFfaAsync()) with { Props = [PropAt(1, 100, 100), PropAt(2, 120, 100)] };
            Vector3 eye = new Vector3(90, 2, 100);
            Vector3 east = Vector3.UnitX;
            PickOptions props = new PickOptions(Units: false, Markers: false, Decals: false);

            Assert.AreEqual(ObjectRef.Prop(1), ObjectPicking.Pick(document, eye, east, props, Tall, terrain: null));
            Assert.AreEqual(ObjectRef.Prop(2), ObjectPicking.Pick(document, new Vector3(130, 2, 100), -east, props, Tall, terrain: null));
            Assert.IsNull(ObjectPicking.Pick(document, eye, east, props, Tall, terrain: 5), "the terrain hides what lies behind it");
        }

        [TestMethod]
        public void APropTurnsWithItsAxesAndScales()
        {
            // turned a quarter about the vertical axis: its x axis points along z
            Prop turned = PropAt(1, 0, 0) with { RotationX = Vector3.UnitZ, RotationY = Vector3.UnitY, RotationZ = -Vector3.UnitX, Scale = new Vector3(2, 2, 2) };
            ObjectBounds long_ = new ObjectBounds(new Vector3(-5, 0, -0.5f), new Vector3(5, 1, 0.5f));
            Matrix4x4 placement = ObjectPlacement.Prop(turned);

            Assert.IsNotNull(ObjectPicking.HitBox(placement, long_, new Vector3(0, 10, 9), Down), "ten ogrids along z, scaled twice");
            Assert.IsNull(ObjectPicking.HitBox(placement, long_, new Vector3(9, 10, 0), Down), "not along x");
            Assert.AreEqual(8, ObjectPicking.HitBox(placement, long_, new Vector3(0, 10, 0), Down)!.Value, 1e-4, "its top at two ogrids");
        }

        [TestMethod]
        public async Task FindsUnitsByNameAndMarkersAsSpheres()
        {
            MapDocument document = await HardFfaAsync();
            Unit unit = new Unit { Name = "UNIT_7", Blueprint = "uel0101", Position = new Vector3(50, 0, 60) };
            Army first = document.Armies[0];
            document = document with { Armies = document.Armies.SetItem(0, first with { Units = first.Units with { Units = first.Units.Units.Add(unit) } }) };
            PickOptions units = new PickOptions(Props: false, Markers: false, Decals: false);
            Assert.AreEqual(ObjectRef.Unit("UNIT_7"), ObjectPicking.Pick(document, new Vector3(50, 300, 60), Down, units, Tall, terrain: null));

            Spawn spawn = document.Markers.Spawns[0];
            document = document with { Markers = new MarkerSet { Spawns = [spawn] } };
            Vector3 place = ObjectPlacement.MarkerPlace(spawn, document.Terrain.Heightmap);
            Vector3 eye = place + new Vector3(0, 100, 0);
            PickOptions markers = new PickOptions(Props: false, Units: false, Decals: false);
            Assert.AreEqual(ObjectRef.Marker(spawn.Id), ObjectPicking.Pick(document, eye, Down, markers, Tall, terrain: null));
            float height = ObjectPlacement.MarkerHeight(spawn, place, eye);
            Assert.IsTrue(height > ObjectPlacement.MarkerBase(spawn), "from afar a marker grows to keep its size on screen");
            Assert.IsNull(ObjectPicking.Pick(document, eye + new Vector3(height, 0, 0), Down, markers, Tall, terrain: null), "beside it");
        }

        [TestMethod]
        public async Task ADecalIsWhatLiesUnderThePointWhenNoObjectDoes()
        {
            MapDocument document = await HardFfaAsync();
            Decal under = new Decal { Id = 41, Type = DecalType.Albedo, Textures = ["a.dds"], Position = new Vector3(10, 0, 10), Scale = new Vector3(20, 1, 20) };
            Decal top = under with { Id = 42, Position = new Vector3(20, 0, 20) };
            document = document with { Decals = [under, top], Props = [PropAt(1, 25, 25)] };
            PickOptions decals = new PickOptions(Props: false, Units: false, Markers: false);

            Assert.AreEqual(ObjectRef.Decal(41), ObjectPicking.Pick(document, new Vector3(15, 100, 15), Down, decals, Tall, terrain: 100));
            Assert.AreEqual(ObjectRef.Decal(42), ObjectPicking.Pick(document, new Vector3(25, 100, 25), Down, decals, Tall, terrain: 100), "the last lies on top");
            Assert.AreEqual(ObjectRef.Prop(1), ObjectPicking.Pick(document, new Vector3(25, 100, 25), Down, new PickOptions(), Tall, terrain: 100), "an object first");
            Assert.IsNull(ObjectPicking.Pick(document, new Vector3(5, 100, 5), Down, decals, Tall, terrain: 100));
            Assert.AreEqual(ObjectRef.Decal(41), ObjectPicking.Pick(document, new Vector3(25, 100, 25), Down, decals with { HiddenDecals = new HashSet<int> { 42 } }, Tall, terrain: 100),
                "a hidden decal (of a hidden group) is not picked: the one under it is");
        }

        [TestMethod]
        public void TheCameraRayGoesThroughThePlaceUnderThePoint()
        {
            EditorCamera camera = new EditorCamera((_, _, _) => 12, (_, _) => 12);
            camera.Reset(512, 512);
            camera.Aspect = 1.5f;
            camera.Look(pitch: 40, yaw: 30);

            foreach (Vector2 view in new[] { Vector2.Zero, new Vector2(0.5f, -0.3f), new Vector2(-0.8f, 0.2f) })
            {
                (Vector3 origin, Vector3 direction) = camera.RayAt(view);
                Vector3 ground = camera.PointAt(view)!.Value;
                Vector3 along = origin + direction * ((origin.Y - 12) / -direction.Y);
                Assert.AreEqual(ground.X, along.X, 0.5, $"{view}");
                Assert.AreEqual(ground.Z, along.Z, 0.5, $"{view}");
            }
        }
    }
}
