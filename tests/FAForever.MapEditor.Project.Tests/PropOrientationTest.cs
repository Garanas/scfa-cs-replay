using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// How props stand (user stories P-1, P-2): their tilt, aligning them with the terrain, standing
    /// them up, setting them on the ground, painting them with a palette, and their reclaim (P-5).
    /// </summary>
    [TestClass]
    public class PropOrientationTest
    {
        private const float Tolerance = 1e-4f;

        private static MapDocument original = null!;

        [ClassInitialize]
        public static void Initialize(TestContext _) => original = MapImporter.ImportFolder(ProjectFolders.HardFfa);

        private static Prop Turned(float heading) =>
            PropOrientation.Oriented(new Prop { Id = 1, Blueprint = "/env/evergreen/props/rocks/rock01_prop.bp", Scale = Vector3.One }, heading, Vector3.UnitY);

        private static void AssertRotation(Prop prop)
        {
            Assert.AreEqual(1, prop.RotationX.Length(), Tolerance);
            Assert.AreEqual(1, prop.RotationY.Length(), Tolerance);
            Assert.AreEqual(1, prop.RotationZ.Length(), Tolerance);
            Assert.AreEqual(0, Vector3.Dot(prop.RotationX, prop.RotationY), Tolerance);
            Assert.AreEqual(0, Vector3.Dot(prop.RotationY, prop.RotationZ), Tolerance);
            Assert.AreEqual(1, Vector3.Dot(Vector3.Cross(prop.RotationX, prop.RotationY), prop.RotationZ), Tolerance, "a rotation, not a mirror");
        }

        [TestMethod]
        public void AnUprightPropTurnedAsTheSelectToolTurnsIt()
        {
            Prop prop = Turned(0.7f);
            AssertRotation(prop);
            Assert.AreEqual(0.7f, MapObjects.PropHeading(prop), Tolerance);
            Assert.AreEqual(Vector3.UnitY, prop.RotationY);

            // the select tool's turn adds to the heading, and so does Frame
            Prop again = original.AddProp(prop).RotateObjects([ObjectRef.Prop(original.NextPropId())], 0.5f).Props[^1];
            Assert.AreEqual(1.2f, MapObjects.PropHeading(again), Tolerance);
            Assert.AreEqual(Vector3.Distance(Turned(1.2f).RotationX, again.RotationX), 0, Tolerance);
        }

        [TestMethod]
        public void AligningToANormalMakesItTheUpAndKeepsTheHeading()
        {
            Vector3 normal = Vector3.Normalize(new Vector3(0.4f, 1, -0.3f));
            foreach (float heading in new[] { 0f, 1f, -2.5f, 3f })
            {
                Prop aligned = PropOrientation.AlignedTo(Turned(heading), normal);
                AssertRotation(aligned);
                Assert.AreEqual(0, Vector3.Distance(normal, aligned.RotationY), Tolerance);
                Assert.AreEqual(heading, PropOrientation.Heading(aligned), Tolerance, "aligning never turns it");
                Assert.AreEqual(MathF.Acos(normal.Y), PropOrientation.Lean(aligned), Tolerance);

                Prop upright = PropOrientation.Upright(aligned);
                AssertRotation(upright);
                Assert.AreEqual(Vector3.UnitY, upright.RotationY);
                Assert.AreEqual(heading, PropOrientation.Heading(upright), Tolerance, "standing it up never turns it");
                Assert.AreEqual(0, Vector3.Distance(Turned(heading).RotationX, upright.RotationX), Tolerance);
            }
        }

        [TestMethod]
        public void PitchAndRollAreReadAsTheyAreGiven()
        {
            Prop prop = PropOrientation.WithTilt(Turned(2), 0.2f, -0.3f);
            AssertRotation(prop);
            (float pitch, float roll) = PropOrientation.Tilt(prop);
            Assert.AreEqual(0.2f, pitch, Tolerance);
            Assert.AreEqual(-0.3f, roll, Tolerance);
            Assert.AreEqual(2, PropOrientation.Heading(prop), Tolerance);

            // a pitch leans the prop towards its own z, a roll towards its own -x
            Prop pitched = PropOrientation.WithTilt(Turned(0), 0.5f, 0);
            Assert.IsTrue(pitched.RotationY.Z > 0.4f && MathF.Abs(pitched.RotationY.X) < Tolerance);
            Prop rolled = PropOrientation.WithTilt(Turned(0), 0, 0.5f);
            Assert.IsTrue(rolled.RotationY.X < -0.4f && MathF.Abs(rolled.RotationY.Z) < Tolerance);

            Assert.AreEqual((0f, 0f), PropOrientation.Tilt(new Prop { Id = 1, Blueprint = "x" }), "a matrix of zeros stands upright");
        }

        [TestMethod]
        public void TheTerrainNormalLeansAwayFromTheSlope()
        {
            // a slope rising towards +x: the normal leans to -x
            TiledBuffer<ushort> samples = TiledBuffer<ushort>.Create(17, 17);
            TiledBuffer<ushort>.Builder builder = samples.ToBuilder();
            for (int z = 0; z < 17; z++)
            {
                for (int x = 0; x < 17; x++)
                {
                    builder[x, z] = (ushort)(x * 128);
                }
            }
            Heightmap heights = new Heightmap { Scale = 1 / 128f, Samples = builder.ToBuffer() };
            Vector3 normal = PropOrientation.TerrainNormal(heights, 8, 8);
            Assert.AreEqual(0, Vector3.Distance(Vector3.Normalize(new Vector3(-1, 1, 0)), normal), Tolerance);
        }

        [TestMethod]
        public void TreesStandUprightRocksTilt()
        {
            Assert.AreEqual("rocks", PropOrientation.CategoryOf("/env/evergreen/props/rocks/rock01_prop.bp"));
            Assert.AreEqual("trees", PropOrientation.CategoryOf("/ENV/Evergreen/Props/Trees/pine06_prop.bp"));
            Assert.AreEqual("other", PropOrientation.CategoryOf("/env/aeon/props/aeon_bus_prop.bp"));
            Assert.IsTrue(PropOrientation.TiltsWithTerrain("rocks"));
            Assert.IsTrue(PropOrientation.TiltsWithTerrain("rocks_dark"));
            Assert.IsTrue(PropOrientation.TiltsWithTerrain("crystals-red"));
            Assert.IsTrue(PropOrientation.TiltsWithTerrain("logs"));
            Assert.IsFalse(PropOrientation.TiltsWithTerrain("trees"));
            Assert.IsFalse(PropOrientation.TiltsWithTerrain("groups"));
            Assert.IsFalse(PropOrientation.TiltsWithTerrain("other"));
        }

        [TestMethod]
        public void PlacingFollowsTheCategoryRule()
        {
            Heightmap heights = Slope();
            PropPlacement placement = new PropPlacement { RandomTurn = false, Sink = 0.5f };
            Random random = new Random(1);
            Prop rock = placement.Make("/env/evergreen/props/rocks/rock01_prop.bp", 8, 8, heights, random);
            Prop tree = placement.Make("/env/evergreen/props/trees/pine06_prop.bp", 8, 8, heights, random);
            Assert.IsTrue(PropOrientation.Lean(rock) > 0.7f, "a rock tilts with the slope");
            Assert.AreEqual(0, PropOrientation.Lean(tree), Tolerance, "a tree stands upright");
            Assert.AreEqual(heights.HeightAt(8, 8) - 0.5f, tree.Position.Y, Tolerance, "sunk");
            Assert.AreEqual(0, PropOrientation.Heading(rock), Tolerance);

            Prop upright = (placement with { Alignment = PropAlignment.Upright }).Make("/env/evergreen/props/rocks/rock01_prop.bp", 8, 8, heights, random);
            Assert.AreEqual(0, PropOrientation.Lean(upright), Tolerance);
            Prop scaled = (placement with { ScaleMin = 2, ScaleMax = 3, RandomTilt = 10 }).Make("/env/evergreen/props/trees/pine06_prop.bp", 8, 8, heights, random);
            Assert.IsTrue(scaled.Scale.X is >= 2 and <= 3 && scaled.Scale.X == scaled.Scale.Y);
            Assert.IsTrue(PropOrientation.Lean(scaled) <= 10 * MathF.PI / 180 + Tolerance);
            AssertRotation(scaled);
        }

        private static Heightmap Slope()
        {
            TiledBuffer<ushort>.Builder builder = TiledBuffer<ushort>.Create(17, 17).ToBuilder();
            for (int z = 0; z < 17; z++)
            {
                for (int x = 0; x < 17; x++)
                {
                    builder[x, z] = (ushort)(x * 128);
                }
            }
            return new Heightmap { Scale = 1 / 128f, Samples = builder.ToBuffer() };
        }

        [TestMethod]
        public void EditsOfTheSelectedPropsKeepTheRest()
        {
            MapDocument document = original.AddProps([Turned(1) with { Position = new Vector3(50, 0, 60) }, Turned(2) with { Position = new Vector3(70, 0, 80) }], out ImmutableArray<int> ids);
            Assert.AreEqual(original.Props.Length + 2, document.Props.Length);
            Assert.AreEqual(2, ids.Distinct().Count());

            MapDocument dropped = document.DropPropsToGround([ids[0]], sink: 1);
            Heightmap heights = original.Terrain.Heightmap;
            Assert.AreEqual(heights.HeightAt(50, 60) - 1, dropped.Props[^2].Position.Y, Tolerance);
            Assert.AreEqual(0, dropped.Props[^1].Position.Y, "only the one named");

            MapDocument tilted = document.SetPropTilt(ids, 0.3f, null).SetPropScale([ids[1]], 2);
            Assert.AreEqual(0.3f, PropOrientation.Tilt(tilted.Props[^1]).Pitch, Tolerance);
            Assert.AreEqual(2f, tilted.Props[^1].Heading(), Tolerance);
            Assert.AreEqual(new Vector3(2), tilted.Props[^1].Scale);
            Assert.AreEqual(0, PropOrientation.Lean(tilted.StandPropsUpright(ids).Props[^1]), Tolerance);

            MapDocument aligned = document.AlignPropsToTerrain(ids);
            Assert.AreEqual(0, Vector3.Distance(PropOrientation.TerrainNormal(heights, 70, 80), aligned.Props[^1].RotationY), Tolerance);

            MapDocument replaced = document.ReplacePropBlueprints(ids, _ => "/env/evergreen/props/rocks/rock02_prop.bp");
            Assert.AreEqual("/env/evergreen/props/rocks/rock02_prop.bp", replaced.Props[^1].Blueprint);
            Assert.AreEqual(original.Props.Length, document.RemoveProps(ids.ToHashSet()).Props.Length);
            Assert.AreSame(document, document.ChangeProps([], prop => prop), "nothing named, nothing changed");
        }

        [TestMethod]
        public void PropsFollowTheTerrainOnlyWhereItChanged()
        {
            Heightmap heights = original.Terrain.Heightmap;
            MapDocument document = (original with { Props = [] }).AddProps(
            [
                Turned(0) with { Position = new Vector3(20, heights.HeightAt(20, 20) - 0.5f, 20) },
                Turned(0) with { Position = new Vector3(200, heights.HeightAt(200, 200), 200) },
            ], out _);

            // raise the terrain around (20, 20)
            TiledBuffer<ushort>.Builder builder = heights.Samples.ToBuilder();
            for (int z = 10; z <= 30; z++)
            {
                for (int x = 10; x <= 30; x++)
                {
                    builder[x, z] = (ushort)(builder[x, z] + 640);
                }
            }
            MapDocument raised = document with { Terrain = document.Terrain with { Heightmap = heights with { Samples = builder.ToBuffer() } } };
            MapDocument followed = raised.PropsFollowTerrain(heights, out int moved);
            Assert.AreEqual(1, moved);
            Assert.AreEqual(raised.Terrain.Heightmap.HeightAt(20, 20) - 0.5f, followed.Props[^2].Position.Y, 1e-3, "as deep in the ground as it was");
            Assert.AreEqual(document.Props[^1], followed.Props[^1], "far away, untouched");
            Assert.AreSame(document, document.PropsFollowTerrain(heights, out _), "the same heights move nothing");
        }

        [TestMethod]
        public void PaintingKeepsTheSpacingAndTheConditions()
        {
            Heightmap heights = original.Terrain.Heightmap;
            PaletteEntry[] palette = [new PaletteEntry("/env/evergreen/props/trees/pine06_prop.bp", 3), new PaletteEntry("/env/evergreen/props/rocks/rock01_prop.bp", 1), new PaletteEntry("/never.bp", 0)];
            PropScatter scatter = new PropScatter([], palette, new PropPlacement(), new Random(7)) { Density = 50, Spacing = 2 };
            List<Prop> placed = [];
            for (int i = 0; i < 5; i++)
            {
                placed.AddRange(scatter.Dab(heights, 100, 100, 10, scatter.Tries(MathF.PI * 100)));
            }
            Assert.IsTrue(placed.Count > 20, $"{placed.Count} props");
            for (int i = 0; i < placed.Count; i++)
            {
                for (int j = i + 1; j < placed.Count; j++)
                {
                    Assert.IsTrue(Vector2.Distance(new Vector2(placed[i].Position.X, placed[i].Position.Z), new Vector2(placed[j].Position.X, placed[j].Position.Z)) >= 2);
                }
                Assert.IsTrue(Vector2.Distance(new Vector2(placed[i].Position.X, placed[i].Position.Z), new Vector2(100, 100)) <= 10.001f);
            }
            Assert.IsFalse(placed.Any(prop => prop.Blueprint == "/never.bp"), "a weight of 0 is never chosen");
            Assert.IsTrue(placed.Count(prop => prop.Blueprint.Contains("pine06")) > placed.Count(prop => prop.Blueprint.Contains("rock01")));

            // only where a mask covers: the left half of the map
            TiledBuffer<byte>.Builder mask = TiledBuffer<byte>.Create(128, 128).ToBuilder();
            for (int z = 0; z < 128; z++)
            {
                for (int x = 0; x < 64; x++)
                {
                    mask[x, z] = 255;
                }
            }
            MaskCondition left = new MaskCondition(mask.ToBuffer(), heights.Width, heights.Height, 0.5f);
            PropScatter masked = new PropScatter([], palette, new PropPlacement(), new Random(3)) { Density = 50, Spacing = 1, Conditions = [left] };
            List<Prop> half = masked.Dab(heights, heights.Width / 2f, 100, 30, 400);
            Assert.IsTrue(half.Count > 10);
            Assert.IsTrue(half.All(prop => prop.Position.X <= heights.Width / 2f + 2));
            Assert.IsTrue((left with { Inverted = true }).Allows(heights.Width - 1, 5));

            Assert.ThrowsException<ArgumentException>(() => new PropScatter([], [new PaletteEntry("/a.bp", 0)], new PropPlacement(), new Random()));
        }

        [TestMethod]
        public void ThePropGridFindsWhatIsNear()
        {
            PropGrid grid = new PropGrid([Turned(0) with { Id = 4, Position = new Vector3(10, 0, 10) }, Turned(0) with { Id = 5, Position = new Vector3(30, 0, 10) }]);
            CollectionAssert.AreEqual(new[] { 4 }, grid.Near(12, 10, 3).Select(entry => entry.Id).ToArray());
            Assert.IsFalse(grid.AnyWithin(20, 10, 5));
            grid.Remove(4, 10, 10);
            Assert.AreEqual(1, grid.Count);
            Assert.IsFalse(grid.AnyWithin(12, 10, 3));
        }

        [TestMethod]
        public void ReclaimAddsUpTheBlueprints()
        {
            string assets = Path.Combine(Path.GetTempPath(), "reclaim-" + Guid.NewGuid().ToString("N"));
            try
            {
                string rocks = Path.Combine(assets, "env", "evergreen", "props", "rocks");
                Directory.CreateDirectory(rocks);
                File.WriteAllText(Path.Combine(rocks, "rock01_prop.json"), """{ "id": "x", "reclaimMass": 20, "reclaimEnergy": 5 }""");
                AssetCatalog catalog = AssetCatalog.Load(assets);
                Prop rock = Turned(0) with { Position = new Vector3(15, 0, 15) };
                Prop[] props = [rock, rock with { Position = new Vector3(100, 0, 100) }, rock with { Blueprint = "/env/unknown_prop.bp" }];
                Assert.AreEqual(new ReclaimTotal(40, 10, 3, 1), PropReclaim.Of(props, catalog));

                MapDocument document = original with
                {
                    Props = [.. props.Select((prop, i) => prop with { Id = i + 1 })],
                    Markers = original.Markers with { Areas = [new Area { Name = "AREA_1", X0 = 0, Z0 = 0, X1 = 50, Z1 = 50 }] },
                };
                AreaReclaim area = PropReclaim.PerArea(document, catalog).Single();
                Assert.AreEqual("AREA_1", area.Area);
                Assert.AreEqual(20, area.Total.Mass);
                Assert.AreEqual(2, area.Total.Props, "the unknown one at 15, 15 counts, as 0");
            }
            finally
            {
                Directory.Delete(assets, recursive: true);
            }
        }
    }

    internal static class PropTestExtensions
    {
        public static float Heading(this Prop prop) => PropOrientation.Heading(prop);
    }
}
