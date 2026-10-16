using System.Collections.Immutable;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using ImageMagick;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The projected images of the Sketch mode (user story N-2): the commands that project, change,
    /// fit and remove them, the list and the inspector's fields that follow the document, the overlay,
    /// and the Image tool's mouse.
    /// </summary>
    [TestClass]
    public sealed class ProjectedImagesTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        [ClassCleanup]
        public static void Cleanup()
        {
            if (Directory.Exists(PicturesFolder))
            {
                Directory.Delete(PicturesFolder, recursive: true);
            }
        }

        private static string PicturesFolder => Path.Combine(AppContext.BaseDirectory, "pictures");

        /// <summary>
        /// A picture of our own: red on the left half, blue on the right; never a file of the person's.
        /// </summary>
        internal static string Picture(int width = 200, int height = 100, string name = "Draft.png")
        {
            string file = Path.Combine(PicturesFolder, $"{width}x{height}", name);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            using MagickImage image = new MagickImage(MagickColors.Red, (uint)width, (uint)height);
            using MagickImage right = new MagickImage(MagickColors.Blue, (uint)(width / 2), (uint)height);
            image.Composite(right, width / 2, 0, CompositeOperator.Over);
            File.WriteAllBytes(file, image.ToByteArray(MagickFormat.Png));
            return file;
        }

        /// <summary>
        /// Answers the picker with the file a test names.
        /// </summary>
        private sealed class Pictures(string? file) : IEditorDialogs
        {
            public List<string> Questions { get; } = [];

            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult<string?>(null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Task.FromResult(SaveChoice.Discard);

            public Task<string?> PickFileAsync(string title, IReadOnlyList<string> extensions, string? startIn = null)
            {
                Questions.Add(title);
                return Task.FromResult(file);
            }
        }

        private static async Task<MainViewModel> Open(IEditorDialogs? dialogs = null, bool withImage = false)
        {
            MainViewModel model = new MainViewModel(dialogs ?? new MainViewModelTest.Answers());
            await model.OpenAsync(HardFfa);
            model.SelectMode("sketch");
            if (withImage)
            {
                await Run(model, "images.project", new JsonObject { ["path"] = Picture() });
            }
            return model;
        }

        private static Task<CommandRun> Run(MainViewModel model, string id, JsonObject? arguments = null, bool interactive = false) =>
            model.Commands.RunAsync(id, arguments ?? [], interactive);

        private static ProjectedImage Image(MainViewModel model, string name = "Draft") => model.ShownDocument!.FindImage(name)!;

        [TestMethod]
        public async Task ProjectingAnImageFitsItToTheMapChoosesItAndTakesTheImageTool()
        {
            MainViewModel model = await Open();

            CommandRun run = await Run(model, "images.project", new JsonObject { ["path"] = Picture(200, 100) });

            Assert.AreEqual("Draft", run.Result);
            ProjectedImage image = Image(model);
            Assert.AreEqual((128.0, 128.0, 256.0, 128.0), (image.CentreX, image.CentreZ, image.Width, image.Height), "in the middle, as large as the 256 by 256 map lets it be with its proportions");
            Assert.AreEqual((200, 100), (image.PixelWidth, image.PixelHeight));
            Assert.IsTrue(image.Shown && !image.Locked && !image.AboveSketch && image.Opacity == 1);
            Assert.AreEqual("Draft", model.Images.ChosenName);
            Assert.AreEqual("sketch.image", model.ActiveTool!.Definition.Id);
            Assert.AreEqual("Project the image Draft", model.Map!.Session.History.Last().Description);

            Assert.IsTrue((await Run(model, "edit.undo")).Ran);
            Assert.IsTrue(model.ShownDocument!.Images.IsEmpty);
            Assert.IsNull(model.Images.ChosenName, "an image that went away is no longer chosen");
        }

        [TestMethod]
        public async Task ProjectingAsksForTheFileOnlyOfAPerson()
        {
            string file = Picture(80, 80, "Seton.png");
            Pictures dialogs = new Pictures(file);
            MainViewModel model = await Open(dialogs);

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Run(model, "images.project"));
            Assert.AreEqual(0, dialogs.Questions.Count, "automation never opens a dialog");

            Assert.AreEqual("Seton", (await Run(model, "images.project", interactive: true)).Result);
            Assert.AreEqual(1, dialogs.Questions.Count);

            Pictures cancelled = new Pictures(null);
            MainViewModel other = await Open(cancelled);
            Assert.IsNull((await Run(other, "images.project", interactive: true)).Result, "no file chosen, no image");
            Assert.IsTrue(other.ShownDocument!.Images.IsEmpty);
        }

        [TestMethod]
        public async Task ProjectingNamesTheImageAfterItsFileOrAsToldAndKeepsNamesFree()
        {
            MainViewModel model = await Open();
            string file = Picture();

            await Run(model, "images.project", new JsonObject { ["path"] = file });
            Assert.AreEqual("Draft 2", (await Run(model, "images.project", new JsonObject { ["path"] = file })).Result);
            Assert.AreEqual("Layout B", (await Run(model, "images.project", new JsonObject { ["path"] = file, ["name"] = "Layout B" })).Result);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "images.project", new JsonObject { ["path"] = file, ["name"] = "layout b" }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "images.project", new JsonObject { ["path"] = file, ["name"] = "a/b" }));
            await Assert.ThrowsExceptionAsync<MapProjectException>(() => Run(model, "images.project", new JsonObject { ["path"] = Path.Combine(PicturesFolder, "missing.png") }));
            Assert.AreEqual(3, model.ShownDocument!.Images.Length);
        }

        [TestMethod]
        public async Task EachPropertyIsSetByACommandAsOneStepOfUndo()
        {
            MainViewModel model = await Open(withImage: true);

            await Run(model, "images.set", new JsonObject { ["opacity"] = 40, ["greyscale"] = true, ["locked"] = true, ["aboveSketch"] = true, ["shown"] = false });
            ProjectedImage image = Image(model);
            Assert.AreEqual((0.4, true, true, true, false), (image.Opacity, image.Greyscale, image.Locked, image.AboveSketch, image.Shown));
            Assert.AreEqual("Change the image Draft", model.Map!.Session.History.Last().Description);

            await Run(model, "images.set", new JsonObject { ["centreX"] = 100, ["centreZ"] = 90, ["width"] = 64, ["height"] = 32, ["turn"] = 190, ["keepProportions"] = false });
            image = Image(model);
            Assert.AreEqual((100.0, 90.0, 64.0, 32.0, -170.0, false), (image.CentreX, image.CentreZ, image.Width, image.Height, image.Turn, image.KeepProportions), "a turn is brought into -180 to 180");

            await Run(model, "images.set", new JsonObject { ["rename"] = "Napkin" });
            Assert.IsNull(model.ShownDocument!.FindImage("Draft"));
            Assert.AreEqual("Napkin", model.Images.ChosenName, "the chosen image keeps being chosen under its new name");

            await Run(model, "images.set", new JsonObject { ["name"] = "napkin", ["opacity"] = 100 });
            Assert.AreEqual(1.0, Image(model, "Napkin").Opacity, "a name in any case");

            await Run(model, "edit.undo");
            Assert.AreEqual(0.4, Image(model, "Napkin").Opacity, "the opacity step is undone alone");
            await Run(model, "edit.undo");
            Assert.AreEqual("Draft", Image(model).Name, "the rename is a step of its own");
            Assert.AreEqual("Draft", model.Images.ChosenName, "and the chosen image follows the name");
            await Run(model, "edit.redo");
            Assert.AreEqual("Napkin", model.ShownDocument!.Images[0].Name);
        }

        [TestMethod]
        public async Task KeepingProportionsAWidthGivenAloneMakesTheHeightFollow()
        {
            MainViewModel model = await Open(withImage: true);
            await Run(model, "images.set", new JsonObject { ["width"] = 100 });
            Assert.AreEqual(50, Image(model).Height, 1e-9, "the proportions it has now: 2 to 1");

            await Run(model, "images.set", new JsonObject { ["height"] = 20 });
            Assert.AreEqual(40, Image(model).Width, 1e-9);

            await Run(model, "images.set", new JsonObject { ["keepProportions"] = false });
            await Run(model, "images.set", new JsonObject { ["width"] = 90 });
            Assert.AreEqual((90.0, 20.0), (Image(model).Width, Image(model).Height), "off, each side is its own");
        }

        [TestMethod]
        public async Task ACommandRefusesWhatIsNoImageOrNoValue()
        {
            MainViewModel model = await Open(withImage: true);
            await Run(model, "images.project", new JsonObject { ["path"] = Picture(), ["name"] = "Other" });

            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "images.set", new JsonObject { ["name"] = "Nothing", ["opacity"] = 10 }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "images.set", new JsonObject { ["name"] = "Draft", ["rename"] = "other" }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "images.set", new JsonObject { ["name"] = "Draft", ["rename"] = "a.b" }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "images.set", new JsonObject { ["name"] = "Draft", ["opacity"] = 120 }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "images.set", new JsonObject { ["name"] = "Draft", ["width"] = 0.2 }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "images.set", new JsonObject { ["name"] = "Draft", ["turn"] = double.NaN }));
            int steps = model.Map!.Session.History.Count;

            model.Images.Choose(null);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "images.remove"), "none named, none chosen");
            Assert.AreEqual(steps, model.Map.Session.History.Count, "a refused command is no step");
            Assert.IsTrue((await Run(model, "images.set", new JsonObject { ["name"] = "Draft" })).Ran, "nothing to change changes nothing");
            Assert.AreEqual(steps, model.Map.Session.History.Count);
        }

        [TestMethod]
        public async Task ChangesAfterEachOtherToOneFieldAreOneStep()
        {
            MainViewModel model = await Open(withImage: true);
            int steps = model.Map!.Session.History.Count;

            // a slider dragged
            foreach (int percent in new[] { 90, 80, 70, 60 })
            {
                await Run(model, "images.set", new JsonObject { ["opacity"] = percent });
            }

            Assert.AreEqual(steps + 1, model.Map.Session.History.Count);
            Assert.AreEqual(0.6, Image(model).Opacity, 1e-9);
            await Run(model, "edit.undo");
            Assert.AreEqual(1.0, Image(model).Opacity);
        }

        [TestMethod]
        public async Task FitsToTheMapAndToThePlayableArea()
        {
            MainViewModel model = await Open(withImage: true);
            await Run(model, "images.set", new JsonObject { ["centreX"] = 5, ["turn"] = 30, ["width"] = 10 });

            await Run(model, "images.fitToMap");
            ProjectedImage image = Image(model);
            Assert.AreEqual((128.0, 128.0, 256.0, 128.0, 0.0), (image.CentreX, image.CentreZ, image.Width, image.Height, image.Turn));

            await Run(model, "map.addArea", new JsonObject { ["x0"] = 32, ["z0"] = 32, ["x1"] = 96, ["z1"] = 224 });
            await Run(model, "images.fitToPlayableArea");
            image = Image(model);
            Assert.AreEqual((64.0, 128.0, 64.0, 32.0), (image.CentreX, image.CentreZ, image.Width, image.Height), "the playable area is the first one: 64 wide, the picture twice as wide as high");
        }

        [TestMethod]
        public async Task ReplacesThePictureAndRemovesTheImage()
        {
            MainViewModel model = await Open(withImage: true);
            await Run(model, "images.set", new JsonObject { ["centreX"] = 70, ["width"] = 100, ["turn"] = 12 });
            ImmutableArray<byte> before = Image(model).Picture;

            Assert.AreEqual("100 by 100", (await Run(model, "images.replace", new JsonObject { ["path"] = Picture(100, 100, "Square.png") })).Result);

            ProjectedImage image = Image(model);
            Assert.AreNotEqual(before, image.Picture);
            Assert.AreEqual((100, 100), (image.PixelWidth, image.PixelHeight));
            Assert.AreEqual((70.0, 12.0, 100.0, 100.0), (image.CentreX, image.Turn, image.Width, image.Height), "the place and turn stay, the height follows the picture");

            await Run(model, "images.remove");
            Assert.IsTrue(model.ShownDocument!.Images.IsEmpty);
            await Run(model, "edit.undo");
            Assert.AreEqual(1, model.ShownDocument.Images.Length);
        }

        [TestMethod]
        public async Task TheListAndTheChoiceFollowTheDocument()
        {
            MainViewModel model = await Open();
            Assert.IsTrue(model.Images.IsEmpty);

            string file = Picture();
            await Run(model, "images.project", new JsonObject { ["path"] = file });
            await Run(model, "images.project", new JsonObject { ["path"] = file });
            CollectionAssert.AreEqual(new[] { "Draft", "Draft 2" }, model.Images.Items.Select(item => item.Name).ToArray());
            Assert.AreEqual("Draft 2", model.Images.ChosenName);
            Assert.AreSame(model.Images.Items[1], model.Images.ChosenItem);

            await Run(model, "images.choose", new JsonObject { ["name"] = "draft" });
            Assert.AreEqual("Draft", model.Images.ChosenName);
            model.Images.ChosenItem = model.Images.Items[1];
            Assert.AreEqual("Draft 2", model.Images.ChosenName, "choosing in the list chooses the image");

            // the eye is a command too
            model.Images.Items[0].Shown = false;
            Assert.IsFalse(Image(model).Shown);
            await Run(model, "images.choose");
            Assert.IsNull(model.Images.ChosenName);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Run(model, "images.choose", new JsonObject { ["name"] = "Nothing" }));
        }

        [TestMethod]
        public async Task TheInspectorFieldsShowTheChosenImageAndRunTheCommand()
        {
            MainViewModel model = await Open(withImage: true);

            model.Images.Opacity.Text = "35";
            Assert.AreEqual(0.35, Image(model).Opacity, 1e-9);
            model.Images.CentreX.Text = "20,5";
            Assert.AreEqual(20.5, Image(model).CentreX, "a comma or a point");
            model.Images.Width.Text = "64";
            Assert.AreEqual((64.0, 32.0), (Image(model).Width, Image(model).Height));
            model.Images.Turn.Text = "-45";
            Assert.AreEqual(-45.0, Image(model).Turn);
            model.Images.Locked.IsOn = true;
            model.Images.Greyscale.IsOn = true;
            model.Images.Order.Chosen = "Above the sketch";
            Assert.IsTrue(Image(model).Locked && Image(model).Greyscale && Image(model).AboveSketch);
            model.Images.NameField.Text = "Napkin 2";
            Assert.AreEqual("Napkin 2", model.Images.ChosenName);

            // an edit made another way shows in the fields
            await Run(model, "images.set", new JsonObject { ["opacity"] = 10, ["turn"] = 5 });
            Assert.AreEqual("10", model.Images.Opacity.Text);
            Assert.AreEqual("5", model.Images.Turn.Text);
            StringAssert.Contains(model.Images.PictureText, "200 × 100 pixels");
            StringAssert.Contains(model.Images.PictureText, "sketch/images/Napkin 2.png");
            StringAssert.Contains(model.Images.PictureText, "Never exported");
        }

        [TestMethod]
        public async Task TheOverlayHasItsKeyAndShowsOrHidesThemAll()
        {
            MainViewModel model = await Open(withImage: true);
            EditorCommand overlay = model.Commands.All.Single(command => command.Id == "view.overlay.images");
            Assert.AreEqual("Ctrl+Shift+J", overlay.Gesture);
            Assert.IsTrue(model.ShowImages);

            await Run(model, "view.overlay.images");
            Assert.IsFalse(model.ShowImages);
            Assert.IsFalse(model.Overlays.Single(item => item.Id == "images").IsOn);
            await Run(model, "view.overlay.images", new JsonObject { ["on"] = true });
            Assert.IsTrue(model.ShowImages);
            Assert.IsTrue(model.ShownDocument!.Images[0].Shown, "the overlay never changes the images themselves");
        }

        [TestMethod]
        public async Task TheImageToolMovesScalesAndTurnsInOneStepEach()
        {
            MainViewModel model = await Open(withImage: true);
            ImageTool tool = model.Tool<ImageTool>();
            ToolInput input = ToolInput.Default;
            int steps = model.Map!.Session.History.Count;

            // inside: move
            tool.Press(128, 128, input);
            tool.Drag(138, 133, input);
            tool.Drag(148, 138, input);
            tool.Release();
            Assert.AreEqual((148.0, 138.0), (Image(model).CentreX, Image(model).CentreZ));
            Assert.AreEqual(steps + 1, model.Map.Session.History.Count, "a drag is a step of undo");
            await Run(model, "edit.undo");
            Assert.AreEqual(128.0, Image(model).CentreX);

            // the bottom right corner, keeping the proportions (256 by 128): the top left stays at (0, 64)
            tool.Press(256, 192, input);
            tool.Drag(200, 150, input);
            tool.Release();
            ProjectedImage scaled = Image(model);
            Assert.AreEqual(scaled.Width / 2, scaled.Height, 0.05);
            (double x, double z) = ProjectedImages.Corners(scaled)[0];
            Assert.AreEqual((0.0, 64.0), (Math.Round(x, 1), Math.Round(z, 1)));
            await Run(model, "edit.undo");

            // the knob lies 28 pixels (14 ogrids at 0.5 an ogrid a pixel... 28 * 0.5) above the top edge: a quarter turn, with Shift in steps of 15
            float knob = ImageTool.KnobPixels * input.OgridsPerPixel;
            tool.Press(128, 64 - knob, input with { Modifiers = ToolModifiers.Shift });
            tool.Drag(260, 128, input with { Modifiers = ToolModifiers.Shift });
            tool.Release();
            Assert.AreEqual(90.0, Image(model).Turn, "the mouse went from straight above the middle to straight right of it");
            Assert.AreEqual("Turn the image Draft", model.Map.Session.History.Last().Description);
        }

        [TestMethod]
        public async Task AClickChoosesTheTopImageUnderTheMouseAndEmptyGroundNone()
        {
            MainViewModel model = await Open(withImage: true);
            string file = Picture();
            await Run(model, "images.project", new JsonObject { ["path"] = file });
            await Run(model, "images.project", new JsonObject { ["path"] = file });
            await Run(model, "images.set", new JsonObject { ["name"] = "Draft 3", ["width"] = 32 });
            await Run(model, "images.set", new JsonObject { ["name"] = "Draft", ["aboveSketch"] = true });
            ImageTool tool = model.Tool<ImageTool>();
            int steps = model.Map!.Session.History.Count;

            tool.Press(128, 128, ToolInput.Default);
            tool.Release();
            Assert.AreEqual("Draft", model.Images.ChosenName, "over the sketch beats the later ones under it");
            await Run(model, "images.set", new JsonObject { ["name"] = "Draft", ["shown"] = false });
            tool.Press(128, 128, ToolInput.Default);
            tool.Release();
            Assert.AreEqual("Draft 3", model.Images.ChosenName, "a hidden image is not under the mouse; the last drawn of the others is");

            await Run(model, "images.set", new JsonObject { ["name"] = "Draft 3", ["locked"] = true });
            tool.Press(128, 128, ToolInput.Default);
            tool.Drag(200, 200, ToolInput.Default);
            tool.Release();
            Assert.AreEqual("Draft 2", model.Images.ChosenName, "an unlocked image before a locked one");

            tool.Press(5, 5, ToolInput.Default);
            tool.Release();
            Assert.IsNull(model.Images.ChosenName, "a click on nothing");
            Assert.AreEqual(steps + 3, model.Map.Session.History.Count, "clicks and drags that move nothing are no steps");
        }

        [TestMethod]
        public async Task ALockedImageDoesNotMoveAndShowsNoHandlesAndTheThreeDViewIsLeftAlone()
        {
            MainViewModel model = await Open(withImage: true);
            ImageTool tool = model.Tool<ImageTool>();
            ToolView top = ToolView.Flat(0.5f);

            Assert.IsTrue(tool.Shapes(top).OfType<ToolSquare>().Count() == 4, "a square at each corner");
            Assert.IsTrue(tool.Shapes(top).OfType<ToolDisc>().Any(), "and the knob");
            ToolView threeD = new ToolView(false, null, place => new System.Numerics.Vector2(place.X, place.Z), _ => 0.5f);
            Assert.AreEqual(0, tool.Shapes(threeD).Count, "the 3D view never shows an image");
            tool.Press(128, 128, ToolInput.Default with { View = threeD });
            tool.Drag(150, 150, ToolInput.Default with { View = threeD });
            tool.Release();
            Assert.AreEqual(128.0, Image(model).CentreX, "a drag in the 3D view moves nothing");

            await Run(model, "images.set", new JsonObject { ["locked"] = true });
            Assert.IsTrue(tool.Shapes(top).Count == 2 && tool.Shapes(top).All(shape => shape is ToolLine), "a locked image has its outline alone, over its dark rim");
            tool.Press(128, 128, ToolInput.Default);
            tool.Drag(150, 150, ToolInput.Default);
            tool.Release();
            Assert.AreEqual(128.0, Image(model).CentreX);
        }

        [TestMethod]
        public async Task TheKeysDeleteAndEscapeAndTheHiddenOverlayLeaveNothingToHandle()
        {
            MainViewModel model = await Open(withImage: true);
            ImageTool tool = model.Tool<ImageTool>();

            Assert.IsTrue(tool.Key("Escape", ToolModifiers.None));
            Assert.IsNull(model.Images.ChosenName);
            Assert.IsFalse(tool.Key("Delete", ToolModifiers.None), "nothing chosen, nothing removed");

            await Run(model, "view.overlay.images", new JsonObject { ["on"] = false });
            tool.Press(128, 128, ToolInput.Default);
            tool.Release();
            Assert.IsNull(model.Images.ChosenName, "with the images hidden a click chooses none");

            await Run(model, "view.overlay.images", new JsonObject { ["on"] = true });
            tool.Press(128, 128, ToolInput.Default);
            tool.Release();
            Assert.AreEqual("Draft", model.Images.ChosenName);
            Assert.IsTrue(tool.Key("Delete", ToolModifiers.None));
            await Task.Delay(50);
            Assert.IsTrue(model.ShownDocument!.Images.IsEmpty);
        }

        [TestMethod]
        public async Task TheImagesAreNeverPartOfWhatIsExported()
        {
            MainViewModel model = await Open();
            IReadOnlyDictionary<string, byte[]> before = MapExporter.ToBytes(model.ShownDocument!);
            await Run(model, "images.project", new JsonObject { ["path"] = Picture() });
            IReadOnlyDictionary<string, byte[]> after = MapExporter.ToBytes(model.ShownDocument!);

            CollectionAssert.AreEquivalent(before.Keys.ToList(), after.Keys.ToList());
            foreach ((string path, byte[] bytes) in before)
            {
                CollectionAssert.AreEqual(bytes, after[path], path);
            }
        }

        [TestMethod]
        public async Task TheStateSaysWhatTheEditorHolds()
        {
            MainViewModel model = await Open(withImage: true);
            JsonObject state = model.Images.Describe();

            Assert.AreEqual(1, state["count"]!.GetValue<int>());
            Assert.AreEqual("Draft", state["chosen"]!.GetValue<string>());
            JsonObject item = (JsonObject)state["items"]!.AsArray()[0]!;
            Assert.AreEqual(256, item["size"]!.AsArray()[0]!.GetValue<double>());
            Assert.AreEqual(200, item["pixels"]!.AsArray()[0]!.GetValue<int>());
            Assert.IsTrue(item["shown"]!.GetValue<bool>());
        }
    }
}
