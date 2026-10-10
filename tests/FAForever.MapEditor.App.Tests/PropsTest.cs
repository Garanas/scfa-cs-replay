using System.Numerics;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The Props mode (user stories P-1, P-2, P-3, P-5): placing props from the picker and the
    /// shelf, the commands on the selected props, painting and erasing with a palette, props that
    /// follow the sculpted terrain, and the reclaim.
    /// </summary>
    [TestClass]
    public class PropsTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";
        private const string Rock = "env/evergreen/props/rocks/rock01_prop.bp";
        private const string Pine = "env/evergreen/props/trees/pine06_prop.bp";

        private sealed class Dialogs : IEditorDialogs
        {
            public string? Pick { get; set; }

            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult<string?>(null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Task.FromResult(SaveChoice.Discard);

            public Task<string?> PickAssetAsync(AssetKind kind, string? title = null, string? current = null) => Task.FromResult(Pick);
        }

        private static async Task<MainViewModel> Open(string tool = "props.place", Dialogs? dialogs = null)
        {
            MainViewModel model = new MainViewModel(dialogs ?? new Dialogs());
            model.Assets.Use(TestAssets.Catalog());
            await model.OpenAsync(HardFfa);
            model.SelectTool(tool);
            model.Props.Random = new Random(5);
            return model;
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive: false);

        private static int[] SelectedIds(MainViewModel model) => [.. model.Selection.Items.Select(thing => thing.Id)];

        [TestMethod]
        public async Task TheModeHasItsToolsAndSections()
        {
            MainViewModel model = await Open("props.select");
            CollectionAssert.AreEqual(new[] { "props.select", "props.place", "props.paint", "props.erase" }, model.Tools.Select(tool => tool.Definition.Id).ToArray());
            Assert.IsTrue(model.Tools.All(tool => tool.Tool is not null), "every tool is built");
            Assert.AreEqual(ObjectKinds.Props, model.SelectableKinds);
            Assert.AreEqual(PropsViewModel.PaletteKind, model.Presets.Kind, "the panel lists the palettes");
        }

        [TestMethod]
        public async Task AClickPlacesTheChosenPropAsOneStepAndSelectsIt()
        {
            Dialogs dialogs = new Dialogs { Pick = Rock };
            MainViewModel model = await Open("props.select", dialogs);
            int before = model.ShownDocument!.Props.Length;

            // nothing chosen: the click says so and places nothing
            model.SelectTool("props.place");
            model.Stroke([(40, 40)]);
            Assert.AreEqual(before, model.ShownDocument.Props.Length);
            StringAssert.Contains(model.Status, "Choose a prop");

            Assert.AreEqual(Rock, (await model.Commands.RunAsync("props.choose", [], interactive: true)).Result, "the picker's answer");
            Assert.AreEqual("props.place", model.ActiveTool!.Definition.Id);
            model.Stroke([(40, 40)]);
            await Task.Yield();

            MapDocument document = model.ShownDocument!;
            Assert.AreEqual(before + 1, document.Props.Length);
            Prop placed = document.Props[^1];
            Assert.AreEqual("/" + Rock, placed.Blueprint);
            Assert.AreEqual(document.Terrain.Heightmap.HeightAt(40, 40), placed.Position.Y, 1e-3);
            Assert.AreEqual("Place a prop", model.Map!.Session.UndoDescription);
            CollectionAssert.AreEqual(new[] { ObjectRef.Prop(placed.Id) }, model.Selection.Items.ToArray(), "the new prop is selected");

            // Q turns it, as the select tool does
            float heading = PropOrientation.Heading(placed);
            Assert.IsTrue(model.ActiveTool.Tool!.Key("Q", ToolModifiers.None));
            await Task.Yield();
            Assert.AreEqual(heading - 15 * MathF.PI / 180, PropOrientation.Heading(model.ShownDocument!.Props[^1]), 1e-3);

            Assert.IsTrue(model.Undo() && model.Undo());
            Assert.AreEqual(before, model.ShownDocument!.Props.Length);
        }

        [TestMethod]
        public async Task ARockOnASlopeTiltsATreeStandsUpright()
        {
            MainViewModel model = await Open();
            (float x, float z) = Steepest(model.ShownDocument!.Terrain.Heightmap);
            model.Props.RandomTurn = false;
            await Run(model, "props.place", new JsonObject { ["key"] = Rock, ["x"] = x, ["z"] = z });
            await Run(model, "props.place", new JsonObject { ["key"] = Pine, ["x"] = x, ["z"] = z });
            Prop rock = model.ShownDocument!.Props[^2];
            Prop tree = model.ShownDocument.Props[^1];
            Vector3 normal = PropOrientation.TerrainNormal(model.ShownDocument.Terrain.Heightmap, x, z);
            Assert.AreEqual(0, Vector3.Distance(normal, rock.RotationY), 1e-4, "the rock stands along the normal");
            Assert.AreEqual(Vector3.UnitY, tree.RotationY, "the tree stands upright");

            model.Props.Alignment = PropAlignment.Terrain;
            await Run(model, "props.place", new JsonObject { ["key"] = Pine, ["x"] = x, ["z"] = z, ["heading"] = 90 });
            Prop tilted = model.ShownDocument!.Props[^1];
            Assert.AreEqual(0, Vector3.Distance(normal, tilted.RotationY), 1e-4, "aligned with the terrain, whatever it is");
            Assert.AreEqual(90, SelectionInspector.Degrees(PropOrientation.Heading(tilted)), 1e-3);
        }

        /// <summary>
        /// The place of the steepest slope among a coarse grid of the map.
        /// </summary>
        private static (float X, float Z) Steepest(Heightmap heights)
        {
            (float X, float Z) best = (8, 8);
            float lean = 1;
            for (int z = 8; z < heights.Height - 8; z += 4)
            {
                for (int x = 8; x < heights.Width - 8; x += 4)
                {
                    float y = PropOrientation.TerrainNormal(heights, x, z).Y;
                    if (y < lean)
                    {
                        (best, lean) = ((x, z), y);
                    }
                }
            }
            Assert.IsTrue(lean < 0.95f, "the map has a slope");
            return best;
        }

        [TestMethod]
        public async Task ChoosingOnTheShelfAndDroppingPlaceProps()
        {
            MainViewModel model = await Open();
            model.Assets.Select(Rock);
            Assert.AreEqual(Rock, model.Tool<PropPlaceTool>().Prop?.Key, "a prop chosen on the shelf in the Props mode is the one to place");

            int before = model.ShownDocument!.Props.Length;
            Assert.IsTrue(model.DropAsset(Pine, 60, 70));
            await Task.Yield();
            Assert.AreEqual(before + 1, model.ShownDocument!.Props.Length);
            Assert.AreEqual(new Vector3(60, 0, 70) with { Y = model.ShownDocument.Props[^1].Position.Y }, model.ShownDocument.Props[^1].Position);
            Assert.AreEqual(Pine, model.Tool<PropPlaceTool>().Prop?.Key, "the dropped prop is the one to place next");

            // in another mode a dropped prop is placed all the same
            model.SelectMode("terrain");
            Assert.IsTrue(model.DropAsset(Rock, 80, 70));
            await Task.Yield();
            Assert.AreEqual(before + 2, model.ShownDocument!.Props.Length);

            // with the paint tool, a prop chosen on the shelf goes into the palette
            model.SelectTool("props.paint");
            model.Assets.Select(Pine);
            CollectionAssert.AreEqual(new[] { Pine }, model.Props.Palette.Select(item => item.Key).ToArray());

            // another variant of the same tile, chosen in the inspector, takes its place
            model.Assets.SelectedMember = model.Assets.Selected!.Members.Single(entry => entry.Name == "pine06_v1");
            Assert.AreEqual("env/evergreen/props/trees/pine06_v1_prop.bp", string.Join(";", model.Props.Palette.Select(item => item.Key)));
        }

        [TestMethod]
        public async Task TheSelectedPropsAreTiltedStoodUpDroppedScaledAndReplaced()
        {
            MainViewModel model = await Open();
            Heightmap heights = model.ShownDocument!.Terrain.Heightmap;
            (float x, float z) = Steepest(heights);
            model.Props.Alignment = PropAlignment.Upright;
            await Run(model, "props.place", new JsonObject { ["key"] = Pine, ["x"] = x, ["z"] = z, ["heading"] = 30 });
            await Run(model, "props.place", new JsonObject { ["key"] = Pine, ["x"] = x + 4, ["z"] = z, ["heading"] = 60 });
            ObjectRef[] both = [.. model.ShownDocument!.Props.TakeLast(2).Select(prop => ObjectRef.Prop(prop.Id))];
            model.Selection.Set(both);
            Prop Find(int index) => (Prop)model.ShownDocument!.Find(both[index])!;

            await Run(model, "props.alignToTerrain");
            Assert.AreEqual(0, Vector3.Distance(PropOrientation.TerrainNormal(heights, x, z), Find(0).RotationY), 1e-4);
            Assert.AreEqual(30, SelectionInspector.Degrees(PropOrientation.Heading(Find(0))), 1e-2, "aligning keeps the heading");

            await Run(model, "props.upright");
            Assert.AreEqual(Vector3.UnitY, Find(1).RotationY);
            Assert.AreEqual(60, SelectionInspector.Degrees(PropOrientation.Heading(Find(1))), 1e-2);

            await Run(model, "props.tilt", new JsonObject { ["pitch"] = 10 });
            Assert.AreEqual(10, PropOrientation.Tilt(Find(1)).Pitch * 180 / MathF.PI, 1e-3);
            PropSelection section = model.Selection.Inspector.Sections.OfType<PropSelection>().Single();
            Assert.AreEqual("10", section.Pitch);
            Assert.AreEqual("0", section.Roll);

            // a value typed into the inspector goes to every selected prop
            section.Roll = "-5";
            await Task.Yield();
            Assert.AreEqual(-5, PropOrientation.Tilt(Find(0)).Roll * 180 / MathF.PI, 1e-3);
            section.Scale = "2.5";
            await Task.Yield();
            Assert.AreEqual(new Vector3(2.5f), Find(1).Scale);
            Assert.AreEqual("2.5", section.Scale);
            section.Scale = "big";
            Assert.AreEqual("2.5", section.Scale, "a typo changes nothing");

            await Run(model, "props.dropToGround", new JsonObject { ["sink"] = 1 });
            Assert.AreEqual(heights.HeightAt(x, z) - 1, Find(0).Position.Y, 1e-3);

            Vector3 up = Find(0).RotationY;
            await Run(model, "props.randomize", new JsonObject { ["scaleMin"] = 0.5, ["scaleMax"] = 0.75 });
            Assert.IsTrue(Find(0).Scale.X is >= 0.5f and <= 0.75f);
            Assert.AreEqual(0, Vector3.Distance(up, Find(0).RotationY), 1e-4, "turning at random keeps the lean");

            Assert.AreEqual(2, (await Run(model, "props.replace", new JsonObject { ["key"] = Rock })).Result);
            Assert.AreEqual("/" + Rock, Find(1).Blueprint);

            await Run(model, "props.replace", new JsonObject { ["key"] = Pine });
            await Run(model, "props.randomVariant");
            string[] family = ["/env/evergreen/props/trees/pine06_prop.bp", "/env/evergreen/props/trees/pine06_s2_prop.bp", "/env/evergreen/props/trees/pine06_v1_prop.bp"];
            Assert.IsTrue(family.Contains(Find(0).Blueprint) && family.Contains(Find(1).Blueprint), "a variant of pine06, never oak01");

            model.Selection.Set([ObjectRef.Marker(model.ShownDocument!.Markers.Spawns[0].Id)]);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Run(model, "props.upright"), "no props selected");
        }

        [TestMethod]
        public async Task PaintingIsOneStepThatKeepsItsSpacingAndErasingTakesThemAway()
        {
            MainViewModel model = await Open("props.paint");
            int before = model.ShownDocument!.Props.Length;

            model.Stroke([(100, 100)]);
            Assert.AreEqual(before, model.ShownDocument.Props.Length, "an empty palette paints nothing");
            StringAssert.Contains(model.Status, "palette is empty");

            await Run(model, "props.addToPalette", new JsonObject { ["key"] = Pine, ["weight"] = 2 });
            await Run(model, "props.addToPalette", new JsonObject { ["key"] = Rock });
            PropPaintTool paint = model.Tool<PropPaintTool>();
            paint.Radius = 10;
            paint.Density = 20;
            paint.Spacing = 3;
            model.Stroke([(100, 100), (110, 100), (120, 100), (100, 100)]);

            MapDocument painted = model.ShownDocument!;
            Prop[] added = [.. painted.Props.Skip(before)];
            Assert.IsTrue(added.Length > 10, $"{added.Length} props");
            Assert.AreEqual("Paint props", model.Map!.Session.UndoDescription);
            Assert.AreEqual(2, model.History.Count, "opened, and the stroke");
            Assert.IsTrue(added.All(prop => prop.Blueprint is "/" + Pine or "/" + Rock));
            Assert.IsTrue(added.All(prop => painted.Props.Where(other => other.Id != prop.Id)
                .All(other => Vector2.Distance(new(prop.Position.X, prop.Position.Z), new(other.Position.X, other.Position.Z)) >= 3)),
                "no painted prop closer than the spacing to any other");

            // painting over the same place again adds little: it is full up to the spacing
            model.Stroke([(100, 100), (120, 100)]);
            Assert.IsTrue(model.ShownDocument!.Props.Length - painted.Props.Length < added.Length / 2);

            // the eraser takes the palette's props away, only those when asked
            PropEraseTool erase = model.Tool<PropEraseTool>();
            model.SelectTool("props.erase");
            erase.Radius = 30;
            erase.OnlyPalette = true;
            await Run(model, "props.removeFromPalette", new JsonObject { ["key"] = Rock });
            model.Stroke([(110, 100)]);
            Assert.IsFalse(model.ShownDocument!.Props.Any(prop => prop.Blueprint == "/" + Pine && Vector2.Distance(new(prop.Position.X, prop.Position.Z), new(110, 100)) <= 30));
            Assert.IsTrue(model.ShownDocument.Props.Any(prop => prop.Blueprint == "/" + Rock), "the rocks are no longer in the palette");
            erase.OnlyPalette = false;
            model.Stroke([(110, 100)]);
            Assert.IsFalse(model.ShownDocument!.Props.Any(prop => Vector2.Distance(new(prop.Position.X, prop.Position.Z), new(110, 100)) <= 30));
            Assert.AreEqual("Erase props", model.Map.Session.UndoDescription);
        }

        [TestMethod]
        public async Task PaintingOnlyWhereALayerCovers()
        {
            MainViewModel model = await Open("props.paint");
            await Run(model, "props.addToPalette", new JsonObject { ["key"] = Pine });
            PropPaintTool paint = model.Tool<PropPaintTool>();
            Assert.IsTrue(paint.Layers.Count > 1, "the map's strata to choose from");
            Assert.AreSame(paint.Layers[1], paint.LayerNamed(paint.Layers[1].Name));

            // a mask of the left half of the map (a mask layer of K-1 is chosen the same way)
            int width = model.ShownDocument!.Width;
            TiledBuffer<byte>.Builder left = TiledBuffer<byte>.Create(width, model.ShownDocument.Height).ToBuilder();
            for (int z = 0; z < left.Height; z++)
            {
                for (int x = 0; x < width / 2; x++)
                {
                    left[x, z] = 255;
                }
            }
            TiledBuffer<byte> mask = left.ToBuffer();
            paint.OnlyOn = new PaintLayer("Left half", _ => mask);
            paint.Radius = 40;
            paint.Density = 10;
            paint.Spacing = 2;
            int before = model.ShownDocument.Props.Length;
            model.Stroke([(width / 2f, 100)]);
            Prop[] added = [.. model.ShownDocument!.Props.Skip(before)];
            Assert.IsTrue(added.Length > 5);
            Assert.IsTrue(added.All(prop => prop.Position.X < width / 2f + 1), "only where the layer covers");

            paint.OnlyOn = PaintLayer.None;
            paint.NotOn = new PaintLayer("Left half", _ => mask);
            before = model.ShownDocument.Props.Length;
            model.Stroke([(width / 2f, 160)]);
            added = [.. model.ShownDocument!.Props.Skip(before)];
            Assert.IsTrue(added.Length > 5);
            Assert.IsTrue(added.All(prop => prop.Position.X >= width / 2f - 1), "nowhere the layer covers");
        }

        [TestMethod]
        public async Task PropsFollowTheSculptedTerrainInTheSameStep()
        {
            MainViewModel model = await Open();
            await Run(model, "props.place", new JsonObject { ["key"] = Pine, ["x"] = 128, ["z"] = 128 });
            await Run(model, "props.place", new JsonObject { ["key"] = Pine, ["x"] = 20, ["z"] = 230 });
            Prop near = model.ShownDocument!.Props[^2];
            Prop far = model.ShownDocument.Props[^1];
            float ground = near.Position.Y - model.ShownDocument.Terrain.Heightmap.HeightAt(128, 128);

            model.SelectTool("terrain.sculpt");
            SculptTool sculpt = model.Tool<SculptTool>();
            sculpt.Mode = HeightBrushMode.Raise;
            sculpt.Radius = 10;
            sculpt.Strength = 4;
            int steps = model.History.Count;
            model.Stroke([(126, 128), (130, 128)]);

            MapDocument raised = model.ShownDocument!;
            Prop moved = raised.Props.Single(prop => prop.Id == near.Id);
            Assert.IsTrue(moved.Position.Y > near.Position.Y + 1, "it rose with the terrain");
            Assert.AreEqual(ground, moved.Position.Y - raised.Terrain.Heightmap.HeightAt(128, 128), 1e-3, "as high above the ground as before");
            Assert.AreEqual(far, raised.Props.Single(prop => prop.Id == far.Id), "far away, untouched");
            Assert.AreEqual(steps + 1, model.History.Count, "one step: the stroke and the props");
            Assert.IsTrue(model.Undo());
            Assert.AreEqual(near.Position, model.ShownDocument!.Props.Single(prop => prop.Id == near.Id).Position);

            Assert.AreEqual(false, (await Run(model, "props.followTerrain")).Result);
            model.Stroke([(126, 128), (130, 128)]);
            Assert.AreEqual(near.Position, model.ShownDocument!.Props.Single(prop => prop.Id == near.Id).Position, "turned off, the props stay");
        }

        [TestMethod]
        public async Task TheReclaimOfTheMapTheAreasAndTheSelection()
        {
            MainViewModel model = await Open();
            ReclaimTotal start = model.Props.Reclaim;
            Assert.AreEqual(model.ShownDocument!.Props.Length, start.Props);

            await Run(model, "props.place", new JsonObject { ["key"] = Rock, ["x"] = 30, ["z"] = 30 });
            await Run(model, "props.place", new JsonObject { ["key"] = Pine, ["x"] = 34, ["z"] = 30 });
            Assert.AreEqual(start.Mass + 20, model.Props.Reclaim.Mass, 1e-6, "the rock's 20 mass");
            Assert.AreEqual(start.Energy + 30, model.Props.Reclaim.Energy, 1e-6, "the pine's 30 energy");
            StringAssert.Contains(model.Props.ReclaimText, "mass");

            model.Selection.Set([.. model.ShownDocument!.Props.TakeLast(2).Select(prop => ObjectRef.Prop(prop.Id))]);
            Assert.AreEqual("20 mass, 30 energy in 2 props", model.Selection.Inspector.Sections.OfType<PropSelection>().Single().Reclaim);

            CommandRun run = await Run(model, "props.reclaim");
            JsonObject answer = (JsonObject)run.Result!;
            Assert.AreEqual(model.Props.Reclaim.Mass, answer["mass"]!.GetValue<double>());
            Assert.AreEqual(model.ShownDocument.Markers.Areas.Length, ((JsonArray)answer["areas"]!).Count);
            Assert.AreEqual("props", model.SelectedMode!.Definition.Id);
        }

        [TestMethod]
        public async Task APaletteIsSavedAndAppliedAsAPreset()
        {
            MainViewModel model = await Open("props.paint");
            model.Presets.Use(new PresetLibrary(Path.Combine(AppContext.BaseDirectory, "presets", Guid.NewGuid().ToString("N"))));
            await Run(model, "props.addToPalette", new JsonObject { ["key"] = Pine, ["weight"] = 3 });
            await Run(model, "props.addToPalette", new JsonObject { ["key"] = Rock, ["weight"] = 1 });
            model.Tool<PropPaintTool>().Density = 7;
            model.Props.ScaleMax = 2;
            await Run(model, "edit.saveAsPreset", new JsonObject { ["name"] = "Forest" });
            CollectionAssert.AreEqual(new[] { "Forest" }, model.Presets.Shown.Select(preset => preset.Name).ToArray());

            model.Props.Palette.Clear();
            model.Tool<PropPaintTool>().Density = 1;
            model.Props.ScaleMax = 1;
            model.SelectTool("props.select");
            await Run(model, "presets.apply", new JsonObject { ["kind"] = PropsViewModel.PaletteKind, ["name"] = "Forest" });
            CollectionAssert.AreEqual(new[] { (Pine, 3.0), (Rock, 1.0) }, model.Props.Palette.Select(item => (item.Key, item.Weight)).ToArray());
            Assert.AreEqual(7, model.Tool<PropPaintTool>().Density);
            Assert.AreEqual(2, model.Props.ScaleMax);
            Assert.AreEqual("props.paint", model.ActiveTool!.Definition.Id);
        }
    }
}
