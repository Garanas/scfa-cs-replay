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
            Assert.AreEqual(3, model.History.Count, "opened, the symmetry chosen, and one stroke");

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
            model.SelectTool("masks.paint");
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
        public async Task PartnersTurnAndScaleAboutTheSelectionAndCopiesAreMirrored()
        {
            MainViewModel model = await Open("leftRight");
            await Run(model, "props.place", new JsonObject { ["key"] = Rock, ["x"] = 80, ["z"] = 30, ["heading"] = 0 });
            ObjectRef[] first = [.. model.Selection.Items];
            await Run(model, "props.place", new JsonObject { ["key"] = Rock, ["x"] = 90, ["z"] = 30, ["heading"] = 0 });
            ObjectRef[] second = [.. model.Selection.Items];
            model.Selection.Set([first[0], second[0]]);

            await Run(model, "edit.rotate", new JsonObject { ["degrees"] = 90, ["about"] = "selection" });
            Assert.AreEqual(new Vector2(85, 25), Flat(model.ShownDocument!.PlaceOf(first[0])!.Value));
            Assert.AreEqual(new Vector2(171, 25), Flat(model.ShownDocument!.PlaceOf(first[1])!.Value), "about the image of the centre");
            Assert.AreEqual(new Vector2(171, 35), Flat(model.ShownDocument!.PlaceOf(second[1])!.Value));

            await Run(model, "edit.scale", new JsonObject { ["factor"] = 2, ["about"] = "selection" });
            Assert.AreEqual(new Vector2(171, 20), Flat(model.ShownDocument!.PlaceOf(first[1])!.Value), "spread from the image of the centre");
            Assert.AreEqual(2, ((Prop)model.ShownDocument!.Find(first[1])!).Scale.X, 1e-3f);

            await Run(model, "edit.scale", new JsonObject { ["factor"] = 0.5f, ["about"] = "each" });
            Assert.AreEqual(new Vector2(171, 20), Flat(model.ShownDocument!.PlaceOf(first[1])!.Value), "each about its own centre");
            Assert.AreEqual(1, ((Prop)model.ShownDocument!.Find(first[1])!).Scale.X, 1e-3f);

            // copies: pasted and duplicated, with their images
            model.Selection.Set([first[0]]);
            await Run(model, "edit.copy");
            await Run(model, "edit.paste", new JsonObject { ["x"] = 60, ["z"] = 100 });
            Assert.AreEqual(2, model.Selection.Count, "the copy and its image");
            Assert.IsTrue(model.Selection.Items.Any(thing => Flat(model.ShownDocument!.PlaceOf(thing)!.Value) == new Vector2(196, 100)));

            model.Selection.Set([second[0], second[1]]);
            int props = model.ShownDocument!.Props.Length;
            await Run(model, "edit.duplicate", new JsonObject { ["dx"] = 2, ["dz"] = 2 });
            Assert.AreEqual(props + 2, model.ShownDocument!.Props.Length, "the thing's copy and its image, not a copy of the partner as well");
            Vector2 copy = Flat(model.ShownDocument!.PlaceOf(model.Selection.Items[0])!.Value);
            Vector2 image = Flat(model.ShownDocument!.PlaceOf(model.Selection.Items[1])!.Value);
            Assert.AreEqual(256 - copy.X, image.X, 1e-3f);
            Assert.AreEqual(copy.Y, image.Y, 1e-3f);
        }

        [TestMethod]
        [DataRow("rotational", 2)]
        [DataRow("rotational", 4)]
        [DataRow("leftRight", 2)]
        [DataRow("diagonal", 2)]
        public async Task ASelectionHoldingThingsAndTheirPartnersStaysSymmetric(string kind, int count)
        {
            MainViewModel model = await Open(kind, count, tool: "markers.select");
            await Run(model, "markers.place", new JsonObject { ["kind"] = "mass", ["x"] = 90, ["z"] = 70 });
            ObjectRef[] all = [.. model.Selection.Items];
            Assert.AreEqual(count, all.Length, "placed with its images, all selected");
            IReadOnlyList<MapSymmetryTransform> transforms = model.Symmetry.Transforms(model.ShownDocument!);

            void AssertSymmetric(string what)
            {
                MapDocument document = model.ShownDocument!;
                Vector3 first = document.PlaceOf(all[0])!.Value;
                foreach (MapSymmetryTransform transform in transforms.Skip(1))
                {
                    Vector3 image = transform.Apply(first);
                    Assert.IsTrue(all.Skip(1).Any(thing => Vector2.Distance(Flat(document.PlaceOf(thing)!.Value), new Vector2(image.X, image.Z)) < 0.01f),
                        $"{what}: a deposit at the image ({image.X}, {image.Z}) of ({first.X}, {first.Z})");
                }
                Assert.IsFalse(SymmetryChecks.NotSymmetric(document, transforms).Any(problem => problem.Thing is ObjectRef thing && all.Contains(thing)), $"{what}: no deposit without a partner");
            }

            await Run(model, "edit.nudge", new JsonObject { ["dx"] = 3, ["dz"] = 1 });
            AssertSymmetric("nudged");
            await Run(model, "edit.rotate", new JsonObject { ["degrees"] = 90, ["about"] = "each" });
            AssertSymmetric("turned each");
            await Run(model, "edit.rotate", new JsonObject { ["degrees"] = 180, ["about"] = "selection" });
            AssertSymmetric("turned about the centre");
            await Run(model, "edit.scale", new JsonObject { ["factor"] = 1.2f, ["about"] = "selection" });
            AssertSymmetric("scaled");

            // dragging one of the images: it follows the mouse, the others its images
            SelectTool tool = (SelectTool)model.ActiveTool!.Tool!;
            Vector3 grabbed = model.ShownDocument!.PlaceOf(all[^1])!.Value;
            Drag(tool, grabbed, new Vector2(grabbed.X + 6, grabbed.Z + 4));
            Assert.AreEqual(new Vector2(MathF.Round(grabbed.X + 6 - 0.5f) + 0.5f, MathF.Round(grabbed.Z + 4 - 0.5f) + 0.5f), Flat(model.ShownDocument!.PlaceOf(all[^1])!.Value), "the grabbed deposit goes with the mouse, onto an ogrid's centre");
            AssertSymmetric("dragged");

            await Run(model, "edit.delete");
            Assert.IsFalse(all.Any(model.ShownDocument!.Exists), "deleted, all of them");
        }

        [TestMethod]
        public async Task ACurveDrawnWithTheSymmetryIsMirrored()
        {
            MainViewModel model = await Open("leftRight", tool: "terrain.curve");
            CommandRun run = await Run(model, "terrain.addCurve", new JsonObject { ["points"] = new JsonArray(new JsonArray(60, 60), new JsonArray(70, 140)), ["profile"] = "ridge", ["height"] = 12 });
            string name = (string)run.Result!;
            HeightCurve curve = HeightLayers.Find(model.ShownDocument!, name)!.Curve!;
            Assert.AreEqual(new SymmetrySetting(MapSymmetryKind.MirroredLeftRight, 2), curve.Symmetry, "drawn while the symmetry mirrors edits");
            Layer layer = HeightLayers.Find(model.ShownDocument!, name)!;
            Assert.IsTrue(layer.Values![65, 100] > 1, "the ridge rises");
            Assert.AreEqual(layer.Values![65, 100], layer.Values![256 - 65, 100], 0.05, "the image rises as much");
            CurveTool tool = (CurveTool)model.ActiveTool!.Tool!;
            Assert.AreEqual(1, tool.Marks!.Images!.Count, "the views draw the image");
            Assert.IsTrue(tool.Marks.Images[0].All(point => point.X > 128), "on the other side");

            await Run(model, "terrain.setCurve", new JsonObject { ["mirrored"] = false });
            Assert.IsNull(HeightLayers.Find(model.ShownDocument!, name)!.Curve!.Symmetry);
            Assert.AreEqual(0, HeightLayers.Find(model.ShownDocument!, name)!.Values![256 - 65, 100], "the curve alone");
        }

        [TestMethod]
        public async Task EachMapKeepsItsOwnSymmetry()
        {
            MainViewModel model = await Open("diagonal");
            Assert.AreEqual(new SymmetrySetting(MapSymmetryKind.MirroredDiagonal, 2), model.Map!.Document.Symmetry, "the map keeps the choice");
            Assert.AreEqual("Choose the symmetry mirrored diagonally: top right and bottom left", model.Map.Session.UndoDescription);
            await Run(model, "select.symmetry", new JsonObject { ["kind"] = "rotational", ["count"] = 3 });
            Assert.AreEqual(1, model.Map.Session.History.Count(step => step.Description.Contains("symmetry")), "choices that follow each other are one step");
            await Run(model, "edit.undo");
            Assert.IsFalse(model.Symmetry.IsOn, "undone: the map had none");
            await Run(model, "edit.redo");
            Assert.AreEqual(3, model.Symmetry.Chosen.Count);

            string folder = Path.Combine(Path.GetTempPath(), "faf-symmetry-" + Guid.NewGuid().ToString("N"));
            try
            {
                model.Map.SaveAs(folder);
                await model.OpenAsync(HardFfa);
                Assert.IsFalse(model.Symmetry.IsOn, "another map: its own symmetry, none");
                await Run(model, "select.symmetry");
                Assert.AreEqual(SymmetryViewModel.Detect(model.Map.Document) ?? MapSymmetryKind.Rotational, model.Symmetry.Chosen.Kind, "turned on: the map's own, not the last map's");

                await model.OpenAsync(folder);
                Assert.AreEqual(new SymmetryChoice(MapSymmetryKind.Rotational, 3, "Rotational, 3 players"), model.Symmetry.Chosen, "opened with the symmetry it was saved with");
                Assert.IsFalse(model.Map.Session.IsModified);

                await Run(model, "file.new", new JsonObject { ["name"] = "Fresh", ["size"] = 256, ["players"] = 2 });
                Assert.AreEqual(new SymmetryChoice(MapSymmetryKind.Rotational, 2, "Point symmetry (rotational, 2 players)"), model.Symmetry.Chosen, "a new map: the symmetry of its spawns");
                Assert.IsFalse(model.Problems.Run().Any(problem => problem.Check == SymmetryChecks.Id), "a new map is exactly symmetric");
            }
            finally
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
        }

        [TestMethod]
        public async Task TheHandlesTurnAndScaleThePartnersToo()
        {
            MainViewModel model = await Open("leftRight", tool: "props.select");
            await Run(model, "props.place", new JsonObject { ["key"] = Rock, ["x"] = 80, ["z"] = 30, ["heading"] = 0 });
            ObjectRef[] first = [.. model.Selection.Items];
            await Run(model, "props.place", new JsonObject { ["key"] = Rock, ["x"] = 90, ["z"] = 30, ["heading"] = 0 });
            ObjectRef[] second = [.. model.Selection.Items];
            await Run(model, "select.handles", new JsonObject { ["pivot"] = "selection" });
            model.SelectTool("props.select");
            model.Selection.Set([first[0], second[0]]);
            SelectTool tool = (SelectTool)model.ActiveTool!.Tool!;
            HandleLayout layout = HandleLayout.Of(tool.Frame!, ToolInput.Default.OgridsPerPixel);
            Vector2 middle = tool.Frame!.Middle;
            (_, Vector3 corner) = layout.Scales[2];

            Drag(tool, corner, middle + (new Vector2(corner.X, corner.Z) - middle) * 2);

            Vector3 a = model.ShownDocument!.PlaceOf(first[0])!.Value;
            Vector3 partner = model.ShownDocument!.PlaceOf(first[1])!.Value;
            Assert.AreEqual(256 - a.X, partner.X, 1e-2f, "the partner stays the image");
            Assert.AreEqual(a.Z, partner.Z, 1e-2f);
            Assert.AreEqual(((Prop)model.ShownDocument!.Find(first[0])!).Scale.X, ((Prop)model.ShownDocument!.Find(first[1])!).Scale.X, 1e-4f);

            layout = HandleLayout.Of(tool.Frame!, ToolInput.Default.OgridsPerPixel);
            Vector3 ring = layout.Centre + new Vector3(layout.RingRadius, 0, 0);
            Assert.AreEqual(new HandlePart(HandleKind.Turn), tool.HandleAt(ring.X, ring.Z, ToolInput.Default));
            Drag(tool, ring, tool.Frame!.Middle + new Vector2(0, layout.RingRadius));
            Assert.AreEqual("Turn the selection", model.Map!.Session.UndoDescription);
            a = model.ShownDocument!.PlaceOf(first[0])!.Value;
            partner = model.ShownDocument!.PlaceOf(first[1])!.Value;
            Assert.AreEqual(256 - a.X, partner.X, 1e-2f, "turned about the image of the centre");
            Assert.AreEqual(a.Z, partner.Z, 1e-2f);
        }

        private static void Drag(IMapTool tool, Vector3 from, Vector2 to)
        {
            tool.Press(from.X, from.Z, ToolInput.Default);
            Vector2 start = new Vector2(from.X, from.Z);
            for (int i = 1; i <= 4; i++)
            {
                Vector2 at = Vector2.Lerp(start, to, i / 4f);
                tool.Drag(at.X, at.Y, ToolInput.Default);
            }
            tool.Release();
        }

        [TestMethod]
        public async Task TheBrushUnderTheMouseShowsEveryImage()
        {
            MainViewModel model = await Open(tool: "terrain.sculpt");
            BrushPreview brush = BrushPreview.Of(model.ActiveTool!.Tool, 60, 40)!;
            IReadOnlyList<BrushPreview> images = brush.Images(model.EditTransforms(model.ShownDocument!));
            Assert.AreEqual(4, images.Count);
            Assert.AreEqual(brush, images[0], "the brush itself first");
            Assert.AreEqual(Quarter(60, 40), new Vector2(images[1].X, images[1].Z));

            await Run(model, "select.symmetry", new JsonObject { ["kind"] = "leftRight" });
            images = brush.Images(model.EditTransforms(model.ShownDocument!));
            Assert.IsTrue(images[1].Mirrored, "flipped in the mirrored part");
            Assert.AreEqual(196, images[1].X, 1e-3f);

            await Run(model, "select.mirrorEdits", new JsonObject { ["on"] = false });
            Assert.AreEqual(1, brush.Images(model.EditTransforms(model.ShownDocument!)).Count, "guides only: the brush alone");
        }

        [TestMethod]
        public async Task DecalPartnersScaleAndTakeTheirSize()
        {
            MainViewModel model = await Open("leftRight");
            await Run(model, "decals.place", new JsonObject { ["key"] = "env/common/decals/mud.dds", ["x"] = 60, ["z"] = 40, ["width"] = 10, ["heading"] = 30 });
            ObjectRef[] pair = [.. model.Selection.Items];
            model.Selection.Set([pair[0]]);

            await Run(model, "decals.scale", new JsonObject { ["x"] = 2 });
            Assert.AreEqual(20, ((Decal)model.ShownDocument!.Find(pair[1])!).Scale.X, 1e-3f);
            await Run(model, "decals.size", new JsonObject { ["length"] = 7 });
            Assert.AreEqual(7, ((Decal)model.ShownDocument!.Find(pair[1])!).Scale.Z, 1e-3f);
            Vector3 centre = MapObjects.DecalCentre((Decal)model.ShownDocument!.Find(pair[1])!);
            Assert.AreEqual(196, centre.X, 1e-2f, "about its own centre");
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

        [TestMethod]
        [DataRow("point", 0, 2, "Point")]
        [DataRow("diagonal", 0, 2, "Diagonal \\")]
        [DataRow("otherDiagonal", 0, 2, "Diagonal /")]
        [DataRow("bothWays", 0, 4, "Both ways")]
        [DataRow("bothDiagonals", 0, 4, "Both diagonals")]
        [DataRow("eightWay", 0, 8, "8-way mirrored")]
        [DataRow("kaleidoscope", 3, 6, "6-way mirrored")]
        [DataRow("kaleidoscope", 8, 16, "16-way mirrored")]
        public async Task EveryKindPlacesAPropInEachOfItsParts(string kind, int count, int parts, string shortName)
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            model.Assets.Use(UnitsTest.Catalog());
            await model.OpenAsync(HardFfa);
            JsonObject arguments = new JsonObject { ["kind"] = kind };
            if (count > 0)
            {
                arguments["count"] = count;
            }
            await Run(model, "select.symmetry", arguments);
            Assert.AreEqual(parts, model.Symmetry.Chosen.Parts, kind);
            Assert.AreEqual(shortName, model.Symmetry.Chosen.Short);
            Assert.AreEqual(model.Symmetry.Setting, model.Map!.Document.Symmetry, "the map keeps it");

            await Run(model, "props.place", new JsonObject { ["key"] = Rock, ["x"] = 100, ["z"] = 37, ["heading"] = 20 });
            Prop[] rocks = [.. model.Selection.Items.Where(thing => thing.Kind == ObjectKind.Prop).Select(thing => (Prop)model.ShownDocument!.Find(thing)!)];
            Assert.AreEqual(parts, rocks.Length, $"{kind}: a rock in every part");
            foreach (Prop rock in rocks)
            {
                Assert.AreEqual(Vector2.Distance(new Vector2(128, 128), new Vector2(100, 37)), Vector2.Distance(new Vector2(128, 128), Flat(rock.Position)), 1e-2f, "as far from the middle");
            }
            int lines = model.Symmetry.Guides(model.ShownDocument!).Count;
            Assert.IsTrue(lines <= 8, "the guides fit the 3D view's eight");
            Assert.AreEqual(kind == "point", model.Symmetry.GuidePoint(model.ShownDocument!) is not null, "the point symmetry's guide is a point");
            Assert.IsTrue(lines > 0 || kind == "point", "the other kinds' guides are lines");
        }

        [TestMethod]
        public async Task TurningOnFindsTheSymmetryWithTheMostParts()
        {
            MainViewModel model = new MainViewModel(new MainViewModelTest.Answers());
            model.Assets.Use(UnitsTest.Catalog());
            await Run(model, "file.new", new JsonObject { ["name"] = "Corners", ["size"] = 256, ["players"] = 2 });
            foreach (string markers in new[] { "spawns", "mass", "hydrocarbon" })
            {
                await Run(model, "markers.clear", new JsonObject { ["kind"] = markers, ["confirm"] = true });
            }
            await Run(model, "select.symmetry", new JsonObject { ["kind"] = "off" });
            await Run(model, "select.symmetry", new JsonObject { ["kind"] = "bothWays" });
            await Run(model, "markers.place", new JsonObject { ["kind"] = "mass", ["x"] = 40.5f, ["z"] = 60.5f });
            await Run(model, "markers.place", new JsonObject { ["kind"] = "spawn", ["x"] = 70, ["z"] = 30 });
            await Run(model, "select.symmetry", new JsonObject { ["kind"] = "off" });
            model.Symmetry.Forget();

            await Run(model, "select.symmetry");
            Assert.AreEqual(MapSymmetryKind.MirroredBothWays, model.Symmetry.Chosen.Kind, "both mirrors, which also hold the half turn");
        }

        [TestMethod]
        public async Task TheSymmetryGoesAroundThePlayableAreaAndKeepsItsPlace()
        {
            MainViewModel model = await Open("leftRight");
            Assert.IsFalse(model.Symmetry.OffersAround, "the whole map is playable: no chip");
            await Run(model, "map.addArea", new JsonObject { ["name"] = "PLAY", ["x0"] = 20, ["z0"] = 30, ["x1"] = 120, ["z1"] = 130 });
            await Run(model, "map.setPlayableArea", new JsonObject { ["name"] = "PLAY" });
            Assert.AreEqual(new MapSymmetryArea(20, 30, 120, 130), model.Symmetry.PlayableArea);
            Assert.IsTrue(model.Symmetry.OffersAround, "the chip shows");
            Assert.IsFalse(model.Symmetry.IsAroundPlayableArea);

            CommandRun run = await Run(model, "select.symmetry", new JsonObject { ["around"] = "playableArea" });
            Assert.AreEqual(new MapSymmetryArea(20, 30, 120, 130), model.Map!.Document.Symmetry!.Around);
            Assert.AreEqual(MapSymmetryKind.MirroredLeftRight, model.Map.Document.Symmetry.Kind, "the kind stays");
            StringAssert.Contains(model.Map.Session.UndoDescription, "around the playable area");
            StringAssert.Contains(((JsonObject)run.Result!).ToJsonString(), "\"around\":[20,30,120,130]");

            await Run(model, "props.place", new JsonObject { ["key"] = Rock, ["x"] = 30, ["z"] = 50 });
            Prop[] rocks = [.. model.Selection.Items.Select(thing => (Prop)model.ShownDocument!.Find(thing)!)];
            Assert.IsTrue(rocks.Any(rock => MathF.Abs(rock.Position.X - 110) < 1e-3f && MathF.Abs(rock.Position.Z - 50) < 1e-3f), "mirrored about the area's middle, x 70");
            (Vector2 From, Vector2 To) guide = model.Symmetry.Guides(model.ShownDocument!).Single();
            Assert.AreEqual(new Vector2(70, 30), guide.From);

            // the area moves; the symmetry stays where it was put until it is put around the area again
            await Run(model, "map.setArea", new JsonObject { ["name"] = "PLAY", ["x0"] = 40 });
            Assert.AreEqual(new MapSymmetryArea(20, 30, 120, 130), model.Symmetry.Around);
            StringAssert.Contains(model.Symmetry.AroundText, "no longer");
            model.Symmetry.IsAroundPlayableArea = false;
            model.Symmetry.IsAroundPlayableArea = true;
            Assert.AreEqual(new MapSymmetryArea(40, 30, 120, 130), model.Map.Document.Symmetry!.Around, "the chip off and on follows the area");

            string folder = Path.Combine(Path.GetTempPath(), "faf-symmetry-" + Guid.NewGuid().ToString("N"));
            try
            {
                model.Map.SaveAs(folder);
                await model.OpenAsync(HardFfa);
                Assert.IsNull(model.Symmetry.Around, "another map: around its whole map");
                await model.OpenAsync(folder);
                Assert.AreEqual(new MapSymmetryArea(40, 30, 120, 130), model.Symmetry.Around, "saved with the project");
                Assert.IsTrue(model.Symmetry.IsAroundPlayableArea);

                await Run(model, "select.symmetry", new JsonObject { ["around"] = "map" });
                Assert.IsNull(model.Map!.Document.Symmetry!.Around);
            }
            finally
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
        }

        [TestMethod]
        public async Task ASymmetryAroundTheWholeMapCannotGoAroundAPlayableAreaThatIsTheWholeMap()
        {
            MainViewModel model = await Open("point");
            ArgumentException refused = await Assert.ThrowsExceptionAsync<ArgumentException>(() => model.Commands.RunAsync("select.symmetry", new JsonObject { ["around"] = "playableArea" }, interactive: false));
            StringAssert.Contains(refused.Message, "whole map");
            Assert.IsNull(model.Symmetry.Around);
        }

        private static Vector2 Flat(Vector3 place) => new Vector2(MathF.Round(place.X, 3), MathF.Round(place.Z, 3));
    }
}
