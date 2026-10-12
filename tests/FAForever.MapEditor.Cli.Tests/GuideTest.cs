using FAForever.MapEditor.Cli.Guide;
using ImageMagick;

namespace FAForever.MapEditor.Cli.Tests
{
    /// <summary>
    /// The pure parts of <c>faf-map guide</c>: the crop maths, the manifest, the pages, the check's
    /// comparison, reading a tour file and the pictures. The run against an editor is checked by hand
    /// (the guide's section of the App's guide): a test of it would need the App's window.
    /// </summary>
    [TestClass]
    public sealed class GuideTest
    {
        private static GuideManifest Manifest() => new GuideManifest(
            GuideManifest.CurrentFormat,
            new GuideEditor("1.0.0+abc", "2026-10-12T00:00:00Z", "2026-10-12"),
            "dark",
            [1280, 800],
            [
                new GuideTour("window", "A tour of the window", "The parts of the window.", 10, "images/window/00-overview.webp",
                [
                    new GuideStep(1, "welcome", "Welcome", "Hello.", null, null, null, null, null),
                    new GuideStep(2, "modes", "The <modes>", "Each mode is one kind of work.", "More about [modes].", null,
                        new GuideTarget("part \"rail\"", true, [0, 36, 52, 735], null), "images/window/02-modes.webp", null),
                    new GuideStep(3, "try-3d", "Try it", "Click 3D.", null, "That is the 3D view.",
                        new GuideTarget("label \"3D\"", true, [395, 80, 41, 28], null), "images/window/03-try-3d.webp", "images/window/03-try-3d-after.webp"),
                ]),
                new GuideTour("terrain", "Terrain", "Shape the map.", 20, null, []),
            ]);

        [TestMethod]
        public void ACropIsTheTargetAndItsMarginInsideTheWindow()
        {
            CollectionAssert.AreEqual(new[] { 60, 60, 120, 110 }, GuideGeometry.Crop([100, 100, 40, 30], 40, 1280, 800));
            CollectionAssert.AreEqual(new[] { 0, 0, 90, 90 }, GuideGeometry.Crop([10, 10, 40, 40], 40, 1280, 800), "kept inside the window");
            CollectionAssert.AreEqual(new[] { 1200, 720, 80, 80 }, GuideGeometry.Crop([1240, 760, 40, 40], 40, 1280, 800), "at the far corner");
        }

        [TestMethod]
        public void ATargetThatFillsTheWindowOrIsNoneIsTheWholeWindow()
        {
            Assert.IsNull(GuideGeometry.Crop([0, 36, 1280, 735], 40, 1280, 800));
            Assert.IsNull(GuideGeometry.Crop(null, 40, 1280, 800));
            Assert.IsNull(GuideGeometry.Crop([10, 10, 0, 0], 40, 1280, 800));
        }

        [TestMethod]
        public void RectanglesUniteIntersectAndGrow()
        {
            CollectionAssert.AreEqual(new[] { 0, 0, 100, 60 }, GuideGeometry.Union([0, 0, 50, 50], [60, 10, 40, 50]));
            CollectionAssert.AreEqual(new[] { 60, 10, 10, 40 }, GuideGeometry.Intersect([0, 0, 70, 50], [60, 10, 40, 50]));
            Assert.IsNull(GuideGeometry.Intersect([0, 0, 10, 10], [10, 0, 10, 10]), "touching is not meeting");
            CollectionAssert.AreEqual(new[] { 6, 6, 18, 18 }, GuideGeometry.Inflate([10, 10, 10, 10], 4));
        }

        [TestMethod]
        public void AWindowSizeIsTwoNumbers()
        {
            Assert.AreEqual((1280, 800), GuideGeometry.ParseSize("1280x800"));
            Assert.AreEqual((1024, 700), GuideGeometry.ParseSize("1024X700"));
            Assert.ThrowsException<GuideException>(() => GuideGeometry.ParseSize("1280"));
            Assert.ThrowsException<GuideException>(() => GuideGeometry.ParseSize("10x10"));
        }

        [TestMethod]
        public void TheManifestSurvivesAWriteAndRead()
        {
            GuideManifest again = GuideManifest.FromJson(Manifest().ToJson());

            Assert.AreEqual("1.0.0+abc", again.Editor.Version);
            Assert.AreEqual(2, again.Tours.Count);
            GuideStep step = again.Tours[0].Steps[2];
            Assert.AreEqual("images/window/03-try-3d-after.webp", step.After);
            CollectionAssert.AreEqual(new[] { 395, 80, 41, 28 }, step.Target!.Bounds);
            Assert.IsNull(again.Tours[0].Steps[0].Target);
            StringAssert.Contains(Manifest().ToJson(), "The <modes>", "readable, not escaped");
        }

        [TestMethod]
        public void PagesHaveAltTextOnEveryPictureAndEscapeTheirText()
        {
            GuideManifest manifest = Manifest();
            GuideTour tour = manifest.Tours[0];

            string html = GuidePages.TourHtml(manifest, tour);
            string markdown = GuidePages.TourMarkdown(manifest, tour);

            // the welcome has no picture; the overview, the modes' crop, the 3D crop and its after picture have
            Assert.AreEqual(4, System.Text.RegularExpressions.Regex.Matches(html, "<img ").Count);
            Assert.AreEqual(4, System.Text.RegularExpressions.Regex.Matches(markdown, @"!\[[^\]]+\]\(").Count);
            foreach (System.Text.RegularExpressions.Match image in System.Text.RegularExpressions.Regex.Matches(html, "<img [^>]*>"))
            {
                StringAssert.Contains(image.Value, "alt=\"");
                Assert.IsFalse(image.Value.Contains("alt=\"\""), "alt text is never empty");
            }
            StringAssert.Contains(html, "The &lt;modes&gt;");
            StringAssert.Contains(html, "alt=\"Try it, after the step\"");
            StringAssert.Contains(markdown, "![Try it, after the step](images/window/03-try-3d-after.webp)");
            StringAssert.Contains(markdown, "> That is the 3D view.");
            Assert.IsFalse(html.Contains("<script"), "no scripts");
            StringAssert.Contains(html, "<style>");
        }

        [TestMethod]
        public void TheIndexListsTheToursInOrderAndWriteMakesEveryPage()
        {
            GuideManifest manifest = Manifest();
            string index = GuidePages.IndexMarkdown(manifest);

            Assert.IsTrue(index.IndexOf("(window.md)", StringComparison.Ordinal) < index.IndexOf("(terrain.md)", StringComparison.Ordinal));
            string folder = Path.Combine(Path.GetTempPath(), "guide-pages-" + Guid.NewGuid().ToString("N"));
            try
            {
                GuidePages.Write(manifest, folder);
                foreach (string name in new[] { "index.md", "index.html", "window.md", "window.html", "terrain.md", "terrain.html" })
                {
                    Assert.IsTrue(File.Exists(Path.Combine(folder, name)), name);
                }
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [TestMethod]
        public void TheCheckReportsMissingMovedAddedAndRemoved()
        {
            Dictionary<string, List<LiveStep>> live = new Dictionary<string, List<LiveStep>>
            {
                ["window"] =
                [
                    new LiveStep("welcome", true, null, null),
                    new LiveStep("modes", true, [0, 36, 52, 760], null),   // 25 px taller
                    new LiveStep("extra", false, null, "no control named Extra"),
                ],
            };

            List<GuideFinding> findings = GuideCheck.Compare(Manifest(), live);

            Assert.IsTrue(findings.Any(finding => finding.Kind == GuideFindingKind.Moved && finding.Step == "modes"));
            Assert.IsTrue(findings.Any(finding => finding.Kind == GuideFindingKind.Removed && finding.Step == "try-3d"));
            Assert.IsTrue(findings.Any(finding => finding.Kind == GuideFindingKind.Added && finding.Step == "extra"));
            Assert.IsTrue(findings.Any(finding => finding.IsError && finding.Step == "extra"), "a new step whose target is missing fails");
            Assert.IsTrue(findings.Any(finding => finding.Kind == GuideFindingKind.RemovedTour && finding.Tour == "terrain"));
        }

        [TestMethod]
        public void AMissingTargetFailsAndASmallMoveDoesNot()
        {
            Dictionary<string, List<LiveStep>> live = new Dictionary<string, List<LiveStep>>
            {
                ["window"] =
                [
                    new LiveStep("welcome", true, null, null),
                    new LiveStep("modes", true, [4, 40, 56, 731], null),
                    new LiveStep("try-3d", false, null, "no control"),
                ],
                ["terrain"] = [],
            };

            List<GuideFinding> findings = GuideCheck.Compare(Manifest(), live);

            Assert.AreEqual(1, findings.Count);
            Assert.IsTrue(findings[0].IsError);
            StringAssert.Contains(findings[0].Message, "window/try-3d");
            Assert.IsFalse(GuideCheck.Moved([0, 0, 10, 10], [8, -8, 18, 2]), "eight pixels is within the tolerance");
            Assert.IsTrue(GuideCheck.Moved([0, 0, 10, 10], [9, 0, 10, 10]));
        }

        [TestMethod]
        public void ATourFileGivesItsStepsAndGuideFields()
        {
            TourFile tour = GuideTours.Parse("""
                {
                  // comments and trailing commas are allowed, as in the editor
                  "id": "demo", "title": "Demo", "summary": "A demo.", "order": 30,
                  "steps": [
                    { "id": "a", "title": "A", "text": "Text A." },
                    { "id": "b", "title": "B", "text": "Text B.", "guide": { "skip": true } },
                    { "id": "c", "title": "C", "text": "Text C.", "until": { "path": "tab", "value": "3d" }, "doItForMe": { "run": "view.3d" }, "done": "Done.",
                      "guide": { "more": "More.", "margin": 12 }, },
                  ],
                }
                """);

            Assert.AreEqual(30, tour.Order);
            Assert.AreEqual(3, tour.Steps.Count);
            Assert.IsTrue(tour.Steps[1].Skip);
            Assert.AreEqual(3, tour.Steps[2].Number);
            Assert.IsTrue(tour.Steps[2].DoItForMe);
            Assert.AreEqual("tab", tour.Steps[2].UntilPath);
            Assert.AreEqual("3d", tour.Steps[2].UntilValue!.GetValue<string>());
            Assert.AreEqual("More.", tour.Steps[2].More);
            Assert.AreEqual(12, tour.Steps[2].Margin);
            Assert.IsFalse(tour.Steps[0].DoItForMe);
        }

        private static double Red(IMagickImage<ushort> image, int x, int y)
        {
            using IPixelCollection<ushort> pixels = image.GetPixels();
            return pixels.GetPixel(x, y).ToColor()!.R / (double)Quantum.Max;
        }

        [TestMethod]
        public void ThePicturesAreCroppedPutTogetherAndNumbered()
        {
            using MagickImage window = new MagickImage(MagickColors.Black, 400, 300);
            using MagickImage frame = new MagickImage(MagickColors.White, 100, 80);

            // under the scrim the 3D frame is dimmed, but for the hole round the target
            GuideImages.PutIn3D(window, frame, [100, 100, 100, 80], scrimmed: true, target: [150, 120, 20, 20], scale: 1);
            double dimmed = Red(window, 110, 170);
            double bright = Red(window, 160, 130);
            Assert.IsTrue(dimmed > 0.2 && dimmed < 0.4, $"dimmed {dimmed}");
            Assert.IsTrue(bright > 0.95, $"the hole {bright}");

            using IMagickImage<ushort> crop = GuideImages.Crop(window, [90, 90, 120, 100], 1);
            Assert.AreEqual(120u, crop.Width);
            Assert.AreEqual(100u, crop.Height);

            GuideImages.DrawOverview(window, [(1, [10, 10, 80, 60]), (2, [10, 10, 40, 30])], 1);
            Assert.IsTrue(Red(window, 10, 40) > 0.1 || Red(window, 11, 40) > 0.1, "an outline lies on the picture");

            string folder = Path.Combine(Path.GetTempPath(), "guide-images-" + Guid.NewGuid().ToString("N"));
            try
            {
                string path = Path.Combine(folder, "a" + GuideImages.Extension);
                GuideImages.Save(window, path);
                Assert.IsTrue(new FileInfo(path).Length > 0);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
