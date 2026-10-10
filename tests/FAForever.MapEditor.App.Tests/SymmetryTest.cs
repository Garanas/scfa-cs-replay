using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Nodes;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Checks;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Symmetry applied to edits (user stories F-12, S-1): strokes of every brush tool, placing of
    /// every kind, moving, turning and deleting with the mirror partners, the commands, and the check.
    /// HardFFA is 256 ogrids square, so a quarter turn takes (x, z) to (256 - z, x).
    /// </summary>
    [TestClass]
    public class SymmetryTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";
        private const string Rock = "env/evergreen/props/rocks/rock01_prop.bp";

        private static async Task<MainViewModel> Open(string kind = "rotational", int count = 4, string? tool = null)
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            model.Assets.Use(UnitsTest.Catalog());
            await model.OpenAsync(HardFfa);
            model.Props.Random = new Random(5);
            await Run(model, "select.symmetry", new JsonObject { ["kind"] = kind, ["count"] = count });
            if (tool is not null)
            {
                model.SelectTool(tool);
            }
            return model;
        }

        private static async Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null)
        {
            CommandRun run = await model.Commands.RunAsync(id, arguments ?? [], interactive: false);
            Assert.IsTrue(run.Ran, id);
            return run;
        }

        private static Vector2 Quarter(float x, float z) => new Vector2(256 - z, x);

        private static float Height(MainViewModel model, float x, float z) => model.ShownDocument!.Terrain.Heightmap.HeightAt(x, z);

        [TestMethod]
        public async Task ASculptStrokeRaisesEveryPartInOneStep()
        {
            MainViewModel model = await Open(tool: "terrain.sculpt");
            MapDocument opened = model.ShownDocument!;
            await Run(model, "brush.set", new JsonObject { ["layer"] = 0, ["mode"] = "raise", ["radius"] = 6, ["strength"] = 4 });

            model.Stroke([(60, 40), (66, 40)]);

            foreach (Vector2 place in new[] { new Vector2(62, 40), Quarter(62, 40), Quarter(Quarter(62, 40).X, Quarter(62, 40).Y) })
            {
                float before = opened.Terrain.Heightmap.HeightAt(place.X, place.Y);
                Assert.IsTrue(Height(model, place.X, place.Y) > before + 2, $"raised at {place}");
            }
            Assert.AreEqual(2, model.History.Count, "opened, and one stroke");

            // guides only: the stroke paints where it is
            model.Undo();
            await Run(model, "select.mirrorEdits", new JsonObject { ["on"] = false });
            model.Stroke([(60, 40), (66, 40)]);
            Vector2 image = Quarter(62, 40);
            Assert.AreEqual(opened.Terrain.Heightmap.HeightAt(image.X, image.Y), Height(model, image.X, image.Y), 1e-3f);
            Assert.IsTrue(model.Symmetry.IsOn, "the guides stay");
        }

        [TestMethod]
        public async Task AShapedStrokeOfAMaskIsMirrored()
        {
            MainViewModel model = await Open("diagonal");
            await Run(model, "paint.addMask", new JsonObject { ["name"] = "Mirror" });
            model.SelectTool("paint.masks");
            MaskPaintTool tool = model.Tool<MaskPaintTool>();
            tool.Strength = 1;
            tool.Radius = 6;

            model.Stroke([(100, 40)]);

            TiledBuffer<byte> mask = model.ShownDocument!.Masks["Mirror"];
            float scale = mask.Width / 256f;
            int px = (int)(102 * scale);
            int pz = (int)(41 * scale);
            Assert.IsTrue(mask[px, pz] > 0, "painted");
            Assert.AreEqual(mask[px, pz], mask[pz, px], "and its mirror image");
        }

        [TestMethod]
        public async Task PaintedPropsAreMirroredAndErasedInEveryPart()
        {
            MainViewModel model = await Open(tool: "props.paint");
            await Run(model, "props.addToPalette", new JsonObject { ["key"] = Rock });
            int before = model.ShownDocument!.Props.Length;
            model.Tool<PropPaintTool>().Density = 30;

            model.Stroke([(60, 60), (64, 60)]);

            Prop[] painted = [.. model.ShownDocument!.Props.Skip(before)];
            Assert.IsTrue(painted.Length >= 8 && painted.Length % 4 == 0, $"in fours: {painted.Length}");
            Assert.AreEqual(0, SymmetryChecks.NotSymmetric(model.ShownDocument, model.Symmetry.Transforms(model.ShownDocument))
                .Count(problem => problem.Thing is { Kind: ObjectKind.Prop } thing && painted.Any(prop => prop.Id == thing.Id)));

            model.SelectTool("props.erase");
            model.Tool<PropEraseTool>().Radius = 20;
            model.Stroke([(62, 60)]);
            Assert.AreEqual(0, model.ShownDocument!.Props.Count(prop => painted.Any(p => p.Id == prop.Id)), "erased in every part");
        }

        [TestMethod]
        public async Task PlacingPlacesEveryImageAsOneStepAndSelectsThem()
        {
            MainViewModel model = await Open();
            int steps = model.History.Count;
            int spawns = model.ShownDocument!.Markers.Spawns.Length;

            await Run(model, "markers.place", new JsonObject { ["kind"] = "mass", ["x"] = 100.2f, ["z"] = 40.7f });
            Assert.AreEqual(4, model.Selection.Count);
            Assert.AreEqual(steps + 1, model.History.Count, "one step");
            ResourceMarker[] masses = [.. model.Selection.Items.Select(thing => (ResourceMarker)model.ShownDocument!.Find(thing)!)];
            Assert.IsTrue(masses.Any(mass => mass.Position.X == 215.5f && mass.Position.Z == 100.5f), "the quarter turn of 100.5, 40.5");

            await Run(model, "markers.place", new JsonObject { ["kind"] = "spawn", ["x"] = 30, ["z"] = 30 });
            Assert.AreEqual(spawns + 4, model.ShownDocument!.Markers.Spawns.Length, "each part its own army");

            await Run(model, "props.place", new JsonObject { ["key"] = Rock, ["x"] = 80, ["z"] = 30, ["heading"] = 20 });
            Assert.AreEqual(4, model.Selection.Items.Count(thing => thing.Kind == ObjectKind.Prop));

            await Run(model, "units.add", new JsonObject { ["blueprint"] = "uec1201", ["x"] = 70.3f, ["z"] = 50.2f, ["heading"] = 0 });
            Unit[] farms = [.. model.Selection.Items.Select(thing => model.ShownDocument!.FindUnit(thing.Name!)!.Unit)];
            Assert.AreEqual(4, farms.Length);
            foreach (Unit farm in farms)
            {
                // a structure of 3 by 2 turned a quarter is 2 by 3: on the build grid either way
                bool turned = MathF.Abs(MathF.Sin(farm.Orientation.Y)) > 0.5f;
                float halfX = turned ? 0 : 0.5f;
                float halfZ = turned ? 0.5f : 0;
                Assert.AreEqual(halfX, farm.Position.X - MathF.Floor(farm.Position.X), 1e-3f, $"{farm.Name} at {farm.Position}");
                Assert.AreEqual(halfZ, farm.Position.Z - MathF.Floor(farm.Position.Z), 1e-3f, $"{farm.Name} at {farm.Position}");
            }

            model.Undo();
            Assert.AreEqual(0, model.ShownDocument!.AllUnits().Count(unit => unit.Unit.Blueprint == "uec1201"), "the images go with one undo");
        }

        [TestMethod]
        public async Task ADecalAndAPathNodeAreMirroredOnTheDiagonal()
        {
            MainViewModel model = await Open("diagonal");
            await Run(model, "decals.place", new JsonObject { ["key"] = "env/common/decals/mud.dds", ["x"] = 120, ["z"] = 40, ["width"] = 10, ["heading"] = 30 });
            Decal[] decals = [.. model.Selection.Items.Select(thing => (Decal)model.ShownDocument!.Find(thing)!)];
            Assert.AreEqual(2, decals.Length);
            Vector3 centre = MapObjects.DecalCentre(decals[1]);
            Assert.AreEqual(40, centre.X, 1e-3f);
            Assert.AreEqual(120, centre.Z, 1e-3f);

            CommandRun first = await Run(model, "markers.place", new JsonObject { ["kind"] = "path", ["x"] = 150, ["z"] = 20 });
            int a = ObjectRef.Parse((string)((JsonObject)first.Result!)["ref"]!).Id;
            await Run(model, "markers.place", new JsonObject { ["kind"] = "path", ["x"] = 170, ["z"] = 20, ["linkTo"] = a });
            PathNode[] nodes = [.. model.ShownDocument!.Markers.PathNodes.Where(node => node.Position.X is 20 && node.Position.Z is 150 or 170)];
            Assert.AreEqual(2, nodes.Length, "the images of both nodes");
            Assert.IsTrue(model.ShownDocument!.AreLinked(nodes[0].Id, nodes[1].Id), "linked as the originals");
        }

        [TestMethod]
        public async Task PartnersMoveTurnAndAreDeletedWithTheSelection()
        {
            MainViewModel model = await Open("leftRight");
            await Run(model, "props.place", new JsonObject { ["key"] = Rock, ["x"] = 80, ["z"] = 30, ["heading"] = 0 });
            ObjectRef[] pair = [.. model.Selection.Items];
            model.Selection.Set([pair[0]]);

            await Run(model, "edit.nudge", new JsonObject { ["dx"] = 4, ["dz"] = 2 });
            Assert.AreEqual(new Vector2(84, 32), Flat(model.ShownDocument!.PlaceOf(pair[0])!.Value));
            Assert.AreEqual(new Vector2(172, 32), Flat(model.ShownDocument!.PlaceOf(pair[1])!.Value), "mirrored left to right");

            float before = model.ShownDocument!.HeadingOf(pair[1])!.Value;
            await Run(model, "edit.rotate", new JsonObject { ["degrees"] = 30 });
            Assert.AreEqual(-30, (model.ShownDocument!.HeadingOf(pair[1])!.Value - before) * 180 / MathF.PI, 1e-2f, "the other way");

            await Run(model, "edit.place", new JsonObject { ["x"] = 90 });
            Assert.AreEqual(166, model.ShownDocument!.PlaceOf(pair[1])!.Value.X, 1e-3f);

            await Run(model, "select.none");
            model.Selection.Set([pair[0]]);
            await Run(model, "select.symmetryPartners");
            CollectionAssert.AreEquivalent(pair, model.Selection.Items.ToArray());

            model.Selection.Set([pair[0]]);
            await Run(model, "edit.delete");
            Assert.IsFalse(model.ShownDocument!.Exists(pair[1]), "the partner goes too");
        }

        [TestMethod]
        public async Task TheSelectionIsMirroredOnceAndTheCheckFindsWhatLacksAPartner()
        {
            MainViewModel model = await Open("topBottom");
            await Run(model, "select.mirrorEdits", new JsonObject { ["on"] = false });
            await Run(model, "props.place", new JsonObject { ["key"] = Rock, ["x"] = 80, ["z"] = 30 });
            ObjectRef rock = model.Selection.Items.Single();
            Assert.IsTrue(model.Problems.Run().Any(problem => problem.Check == SymmetryChecks.Id && problem.Thing == rock), "a prop without its partner");

            CommandRun run = await Run(model, "edit.mirror");
            Assert.AreEqual(1, (int)((JsonObject)run.Result!)["added"]!);
            Assert.AreEqual(2, model.Selection.Count);
            run = await Run(model, "edit.mirror");
            Assert.AreEqual(0, (int)((JsonObject)run.Result!)["added"]!, "it has its partner now");
            Assert.IsFalse(model.Problems.Run().Any(problem => problem.Check == SymmetryChecks.Id && problem.Thing == rock));

            await Run(model, "select.symmetry", new JsonObject { ["kind"] = "off" });
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => model.Commands.RunAsync("edit.mirror", [], interactive: false), "no symmetry, nothing to mirror into");
        }

        private static Vector2 Flat(Vector3 place) => new Vector2(MathF.Round(place.X, 3), MathF.Round(place.Z, 3));
    }
}
