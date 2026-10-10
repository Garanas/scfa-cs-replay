using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// Selection presets (user story F-10): things of several kinds saved at once and placed again.
    /// </summary>
    [TestClass]
    public class SelectionPresetsTest
    {
        private static Prop Rock(Heightmap heights, float x, float z) => new Prop
        {
            Id = 0,
            Blueprint = "/env/evergreen/props/rocks/rock01_prop.bp",
            Position = new Vector3(x, heights.HeightAt(x, z) + 0.5f, z),
            RotationX = Vector3.UnitX,
            RotationY = Vector3.UnitY,
            RotationZ = Vector3.UnitZ,
            Scale = Vector3.One,
        };

        /// <summary>
        /// HardFFA with a small civilian base at (60, 40): two units in a group, a rock, a decal, a
        /// mass deposit and two linked path nodes in a chain.
        /// </summary>
        private static (MapDocument Document, ObjectRef[] Base) Base()
        {
            MapDocument document = MapImporter.ImportFolder(ProjectFolders.HardFfa) with { Props = [], Decals = [], DecalGroups = [] };
            Heightmap heights = document.Terrain.Heightmap;
            document = document
                .AddGroup("NEUTRAL_CIVILIAN", ["INITIAL"], "Farm", out ImmutableArray<string> farm)
                .AddUnit("NEUTRAL_CIVILIAN", farm, new Unit { Name = "UNIT_1", Blueprint = "uec1201", Position = new Vector3(58.5f, heights.HeightAt(58.5f, 40), 40) })
                .AddUnit("NEUTRAL_CIVILIAN", farm, new Unit { Name = "UNIT_2", Blueprint = "uec1101", Position = new Vector3(62, heights.HeightAt(62, 42), 42) })
                .AddProp(Rock(heights, 60, 36))
                .AddDecal(DecalEdits.NewDecal(heights, "/env/common/decals/dirt001_albedo.dds", 60, 40, 8, 8, 0))
                .AddMarker(new ResourceMarker { Id = 0, Name = "Mass 01", Kind = ResourceKind.Mass, Position = new Vector3(64.5f, heights.HeightAt(64.5f, 38.5f), 38.5f) }, out Marker mass)
                .AddMarker(new PathNode { Id = 0, Name = "LandPN0", Layer = PathLayer.Land, Position = new Vector3(56, heights.HeightAt(56, 44), 44) }, out Marker a)
                .AddMarker(new PathNode { Id = 0, Name = "LandPN1", Layer = PathLayer.Land, Position = new Vector3(64, heights.HeightAt(64, 44), 44) }, out Marker b)
                .LinkPathNodes(a.Id, b.Id)
                .AddChain(new Chain { Name = "Patrol", MarkerIds = [a.Id, b.Id] }, out _);
            ObjectRef[] things =
            [
                ObjectRef.Unit("UNIT_1"), ObjectRef.Unit("UNIT_2"), ObjectRef.Prop(document.Props[^1].Id), ObjectRef.Decal(document.Decals[^1].Id),
                ObjectRef.Marker(mass.Id), ObjectRef.Marker(a.Id), ObjectRef.Marker(b.Id), ObjectRef.Area("AREA_1"),
            ];
            return (document, things);
        }

        [TestMethod]
        public void SavesThingsOfEveryKindAroundTheirMiddleAndPlacesThemElsewhere()
        {
            (MapDocument document, ObjectRef[] things) = Base();
            SelectionPresetSettings captured = SelectionPresets.Capture(document, things, "Farm");
            Assert.AreEqual(7, captured.Count, "areas are left out");
            Assert.AreEqual("2 units, 1 prop, 1 decal, 3 markers", captured.Summary());

            // through the preset's JSON, as the workspace keeps it
            SelectionPresetSettings settings = Preset.Of(SelectionPresets.Kind, "Farm", captured).Read<SelectionPresetSettings>();
            Heightmap heights = document.Terrain.Heightmap;
            MapDocument placed = SelectionPresets.Place(document, settings, "Farm", "ARMY_2", ["INITIAL"], 150, 120, 0, out ImmutableArray<ObjectRef> added);

            Assert.AreEqual(7, added.Length);
            Assert.AreEqual(2, placed.Props.Length);
            Prop rock = placed.Props[^1];
            // the middle of the seven places, rounded to a whole ogrid
            Vector2 middle = new Vector2(61, 41);
            Assert.AreEqual(150 + 60 - middle.X, rock.Position.X, 1e-3);
            Assert.AreEqual(120 + 36 - middle.Y, rock.Position.Z, 1e-3);
            Assert.AreEqual(heights.HeightAt(rock.Position.X, rock.Position.Z) + 0.5f, rock.Position.Y, 1e-3, "as high above the terrain as saved");
            Assert.AreEqual(2, placed.Decals.Length);

            PlacedUnit[] units = [.. placed.AllUnits().Where(unit => unit.Army == "ARMY_2" && unit.Group.Contains("Farm"))];
            Assert.AreEqual(2, units.Length, "for the army given");
            CollectionAssert.AreEqual(new[] { "INITIAL", "Farm" }, units[0].Group.ToArray(), "a new group named after the preset");
            Assert.AreEqual((150 + 58.5f - middle.X, 120 + 40 - middle.Y), (units[0].Unit.Position.X, units[0].Unit.Position.Z), "the farm stays on its half ogrid");

            PathNode[] nodes = [.. added.Select(placed.Find).OfType<PathNode>()];
            Assert.AreEqual(2, nodes.Length);
            Assert.IsTrue(nodes[0].AdjacentTo.Contains(nodes[1].Id), "the nodes are linked among each other");
            Assert.IsTrue(placed.Markers.Chains.Any(chain => chain.MarkerIds.SequenceEqual([nodes[0].Id, nodes[1].Id])), "their chain is made again");
            ResourceMarker mass = added.Select(placed.Find).OfType<ResourceMarker>().Single();
            Assert.AreNotEqual("Mass 01", mass.Name, "a free name");
        }

        [TestMethod]
        public void TurnsWhatItPlacesAboutThePlaceAndLeavesOutWhatFallsOffTheMap()
        {
            (MapDocument document, ObjectRef[] things) = Base();
            SelectionPresetSettings settings = SelectionPresets.Capture(document, things, "Farm");

            MapDocument turned = SelectionPresets.Place(document, settings, "Farm", "ARMY_2", ["INITIAL"], 100, 100, MathF.PI / 2, out ImmutableArray<ObjectRef> added);
            Prop rock = turned.Props[^1];
            // (-1, -5) from the middle turned a quarter clockwise in the top view is (5, -1)
            Assert.AreEqual(105, rock.Position.X, 1e-3);
            Assert.AreEqual(99, rock.Position.Z, 1e-3);
            Assert.AreEqual(1, MathF.Abs(rock.RotationX.Z), 1e-4, "the prop itself turned");
            Assert.AreEqual(7, added.Length);

            MapDocument edge = SelectionPresets.Place(document, settings, "Farm", "ARMY_2", ["INITIAL"], 1, 1, 0, out ImmutableArray<ObjectRef> atEdge);
            Assert.AreEqual(1, edge.Props.Length, "the rock would lie outside the map");
            Assert.IsTrue(atEdge.Length < 7);
        }

        [TestMethod]
        public void RefusesNothingAndUnitsWithoutAnArmy()
        {
            (MapDocument document, ObjectRef[] things) = Base();
            Assert.ThrowsException<ArgumentException>(() => SelectionPresets.Capture(document, [ObjectRef.Area("AREA_1"), ObjectRef.Prop(9999)], "x"));
            SelectionPresetSettings settings = SelectionPresets.Capture(document, things, "Farm");
            Assert.ThrowsException<ArgumentException>(() => SelectionPresets.Place(document, settings, "Farm", null, ["INITIAL"], 100, 100, 0, out _));
            Assert.ThrowsException<ArgumentException>(() => SelectionPresets.Place(document, settings, "Farm", "ARMY_99", ["INITIAL"], 100, 100, 0, out _));
        }
    }
}
