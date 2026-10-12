using System.Collections.Immutable;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using ImageMagick;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public class ProjectedImagesTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        /// <summary>
        /// A picture of our own: a left half of one colour and a right half of another, in a format of the caller's choice.
        /// </summary>
        internal static string Picture(int width, int height, MagickFormat format = MagickFormat.Png, string extension = ".png")
        {
            string file = Path.Combine(ProjectFolders.New("pictures"), "Draft" + extension);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            using MagickImage image = new MagickImage(MagickColors.Red, (uint)width, (uint)height);
            using MagickImage right = new MagickImage(MagickColors.Blue, (uint)(width / 2), (uint)height);
            image.Composite(right, width / 2, 0, CompositeOperator.Over);
            File.WriteAllBytes(file, image.ToByteArray(format));
            return file;
        }

        internal static ProjectedImage Image(string file, string? name = null)
        {
            (ImmutableArray<byte> png, int width, int height) = ProjectedImages.Read(file);
            return new ProjectedImage { Name = name ?? ProjectedImages.NameOfFile(file), Picture = png, PixelWidth = width, PixelHeight = height };
        }

        private static MapDocument Map() => MapImporter.ImportFolder(ProjectFolders.HardFfa);

        [TestMethod]
        public void ReadsPicturesOfOtherFormatsAsPng()
        {
            foreach ((MagickFormat format, string extension) in new[] { (MagickFormat.Jpeg, ".jpg"), (MagickFormat.Bmp, ".bmp"), (MagickFormat.WebP, ".webp") })
            {
                (ImmutableArray<byte> png, int width, int height) = ProjectedImages.Read(Picture(60, 40, format, extension));
                Assert.AreEqual((60, 40), (width, height), extension);
                CollectionAssert.AreEqual(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, png.AsSpan()[..4].ToArray(), extension);
            }
        }

        [TestMethod]
        public void ScalesALargePictureDownToTheLimit()
        {
            (ImmutableArray<byte> png, int width, int height) = ProjectedImages.Read(Picture(8192, 2048));

            Assert.AreEqual((ProjectedImages.MaxPixels, ProjectedImages.MaxPixels / 4), (width, height), "the longest side at the limit, the proportions kept");
            Assert.AreEqual((width, height), ProjectedImages.SizeOf(png.AsSpan()));
        }

        [TestMethod]
        public void RefusesAFileThatIsNoPicture()
        {
            string file = Path.Combine(ProjectFolders.New("notes"), "notes.png");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "not a picture");

            MapProjectException exception = Assert.ThrowsException<MapProjectException>(() => ProjectedImages.Read(file));
            StringAssert.Contains(exception.Message, "notes.png");
            Assert.ThrowsException<MapProjectException>(() => ProjectedImages.Read(Path.Combine(Path.GetDirectoryName(file)!, "missing.png")));
        }

        [TestMethod]
        public void NamesAnImageAfterItsFileAndKeepsNamesFree()
        {
            Assert.AreEqual("napkin-draft", ProjectedImages.NameOfFile(@"C:\x\napkin-draft.jpg"));
            Assert.AreEqual("my-picture", ProjectedImages.NameOfFile("my.picture.png"));
            Assert.AreEqual("Image", ProjectedImages.NameOfFile("...png"));

            MapDocument map = Map().AddImage(Image(Picture(10, 10), "Draft"), out ProjectedImage first);
            Assert.AreEqual("Draft", first.Name);
            map = map.AddImage(Image(Picture(10, 10), "Draft"), out ProjectedImage second);
            Assert.AreEqual("Draft 2", second.Name, "no two images share a name");
            StringAssert.Contains(ProjectedImages.Problem(map, "draft")!, "already", "Windows compares file names without case");
            Assert.IsNull(ProjectedImages.Problem(map, "Draft", except: "Draft"), "an image may keep its own name");
            Assert.IsNotNull(ProjectedImages.Problem(map, "a/b"));
            Assert.IsNotNull(ProjectedImages.Problem(map, " a"));
            Assert.IsNotNull(ProjectedImages.Problem(map, ""));
        }

        [TestMethod]
        public void AddsAnImageFittedToTheMapAndEditsItByName()
        {
            MapDocument map = Map();
            map = map.AddImage(Image(Picture(200, 100)), out ProjectedImage added);

            Assert.AreEqual(map.Width / 2.0, added.CentreX);
            Assert.AreEqual(map.Height / 2.0, added.CentreZ);
            Assert.AreEqual(map.Width, added.Width, 1e-9, "as wide as the map: the picture is wider than high");
            Assert.AreEqual(map.Width / 2.0, added.Height, 1e-9, "its proportions");

            map = map.UpdateImage(added.Name, image => image with { Opacity = 0.4, Locked = true });
            Assert.AreEqual(0.4, map.FindImage("DRAFT")!.Opacity, "found in any case");
            map = map.RenameImage(added.Name, "Napkin 1");
            Assert.AreEqual("Napkin 1", map.Images[0].Name);
            Assert.ThrowsException<KeyNotFoundException>(() => map.UpdateImage("Draft", image => image));
            map = map.RemoveImage("Napkin 1");
            Assert.IsTrue(map.Images.IsEmpty);
        }

        [TestMethod]
        public void FitsToTheMapAndThePlayableArea()
        {
            MapDocument map = Map();
            map = map.AddImage(Image(Picture(100, 200)), out ProjectedImage added);
            map = map.UpdateImage(added.Name, image => image with { CentreX = 5, Turn = 30, Width = 3, Height = 3 });

            ProjectedImage onMap = map.FitImageToMap(added.Name).Images[0];
            Assert.AreEqual(0, onMap.Turn, "a fit is not turned");
            Assert.AreEqual(map.Height, onMap.Height, 1e-9, "higher than wide: the map's height sets the size");
            Assert.AreEqual(map.Height / 2.0, onMap.Width, 1e-9, "the picture's proportions, not those of the scaling before");

            ProjectedImage stretched = map.UpdateImage(added.Name, image => image with { KeepProportions = false }).FitImageToMap(added.Name).Images[0];
            Assert.AreEqual((double)map.Width, stretched.Width, 1e-9);
            Assert.AreEqual((double)map.Height, stretched.Height, 1e-9);

            Area area = new Area { Name = "AREA_1", X0 = 100, Z0 = 200, X1 = 300, Z1 = 600 };
            MapDocument playable = map with { Markers = map.Markers with { Areas = [area] } };
            ProjectedImage inArea = playable.FitImageToPlayableArea(added.Name).Images[0];
            Assert.AreEqual((200.0, 400.0), (inArea.CentreX, inArea.CentreZ));
            Assert.AreEqual(400, inArea.Height, 1e-9);
            Assert.AreEqual(200, inArea.Width, 1e-9);
        }

        [TestMethod]
        public void ReplacingThePictureKeepsThePlaceAndFollowsTheProportions()
        {
            MapDocument map = Map().AddImage(Image(Picture(100, 100)), out ProjectedImage added);
            map = map.UpdateImage(added.Name, image => image with { CentreX = 70, CentreZ = 80, Width = 40, Height = 40, Turn = 12 });

            ProjectedImage replaced = map.ReplacePicture(added.Name, Image(Picture(200, 100))).Images[0];

            Assert.AreEqual((70.0, 80.0, 12.0, 40.0), (replaced.CentreX, replaced.CentreZ, replaced.Turn, replaced.Width));
            Assert.AreEqual(20, replaced.Height, 1e-9);
            Assert.AreEqual((200, 100), (replaced.PixelWidth, replaced.PixelHeight));
        }

        [TestMethod]
        public void KnowsWhereAPointLiesInATurnedImage()
        {
            ProjectedImage image = new ProjectedImage { Name = "A", Picture = [], PixelWidth = 10, PixelHeight = 10, CentreX = 100, CentreZ = 100, Width = 40, Height = 20, Turn = 90 };

            Assert.IsTrue(ProjectedImages.Contains(image, 100, 115), "turned a quarter, the long side runs up and down");
            Assert.IsFalse(ProjectedImages.Contains(image, 115, 100));
            (double x, double z) = ProjectedImages.Corners(image)[0];
            Assert.AreEqual((110.0, 80.0), (Math.Round(x, 9), Math.Round(z, 9)), "the top left corner turned clockwise a quarter");
            (double kx, double kz) = ProjectedImages.Knob(image, 5);
            Assert.AreEqual((115.0, 100.0), (Math.Round(kx, 9), Math.Round(kz, 9)), "beyond the top edge, which now faces right");
        }

        [TestMethod]
        public void FindsTheHandleUnderTheMouse()
        {
            ProjectedImage image = new ProjectedImage { Name = "A", Picture = [], PixelWidth = 10, PixelHeight = 10, CentreX = 100, CentreZ = 100, Width = 40, Height = 20 };

            Assert.AreEqual(ImageHandle.TopLeft, ProjectedImages.HandleAt(image, 81, 91, 3, 12));
            Assert.AreEqual(ImageHandle.BottomRight, ProjectedImages.HandleAt(image, 119, 109, 3, 12));
            Assert.AreEqual(ImageHandle.Turn, ProjectedImages.HandleAt(image, 100, 78, 3, 12), "the knob, 12 ogrids beyond the top edge");
            Assert.AreEqual(ImageHandle.Move, ProjectedImages.HandleAt(image, 100, 100, 3, 12));
            Assert.AreEqual(ImageHandle.None, ProjectedImages.HandleAt(image, 200, 100, 3, 12));
        }

        [TestMethod]
        public void ScalesFromTheOppositeCorner()
        {
            ProjectedImage image = new ProjectedImage { Name = "A", Picture = [], PixelWidth = 10, PixelHeight = 10, CentreX = 100, CentreZ = 100, Width = 40, Height = 20 };

            // the bottom right corner (120, 110) to (140, 120): the top left (80, 90) stays
            ProjectedImage free = ProjectedImages.Scaled(image, ImageHandle.BottomRight, 140, 120, keepProportions: false);
            Assert.AreEqual((80.0, 90.0), ProjectedImages.Corners(free)[0]);
            Assert.AreEqual((60.0, 30.0), (free.Width, free.Height));

            // keeping the proportions the corner slides along the diagonal: (140, 110) projects onto it
            ProjectedImage kept = ProjectedImages.Scaled(image, ImageHandle.BottomRight, 140, 110, keepProportions: true);
            Assert.AreEqual(kept.Width / 2, kept.Height, 1e-9);
            (double x, double z) = ProjectedImages.Corners(kept)[0];
            Assert.AreEqual((80.0, 90.0), (Math.Round(x, 9), Math.Round(z, 9)));

            // the top left corner dragged past the opposite one stops at the smallest size
            ProjectedImage tiny = ProjectedImages.Scaled(image, ImageHandle.TopLeft, 500, 500, keepProportions: false);
            Assert.AreEqual((ProjectedImages.MinimumSize, ProjectedImages.MinimumSize), (tiny.Width, tiny.Height));
            Assert.ThrowsException<ArgumentException>(() => ProjectedImages.Scaled(image, ImageHandle.Move, 0, 0, true));
        }

        [TestMethod]
        public void ScalesATurnedImageAboutTheFixedCorner()
        {
            ProjectedImage image = new ProjectedImage { Name = "A", Picture = [], PixelWidth = 10, PixelHeight = 10, CentreX = 100, CentreZ = 100, Width = 40, Height = 20, Turn = 37 };
            (double fixedX, double fixedZ) = ProjectedImages.Corners(image)[0];
            (double dragX, double dragZ) = ProjectedImages.Corners(image)[2];

            ProjectedImage bigger = ProjectedImages.Scaled(image, ImageHandle.BottomRight, dragX + 10, dragZ + 10, keepProportions: true);

            (double x, double z) = ProjectedImages.Corners(bigger)[0];
            Assert.AreEqual((Math.Round(fixedX, 6), Math.Round(fixedZ, 6)), (Math.Round(x, 6), Math.Round(z, 6)), "the opposite corner does not move");
            Assert.AreEqual(37, bigger.Turn);
            Assert.IsTrue(bigger.Width > 40);
        }

        [TestMethod]
        public void TurnsByTheAngleTheMouseWentAndSnapsWithShift()
        {
            ProjectedImage image = new ProjectedImage { Name = "A", Picture = [], PixelWidth = 10, PixelHeight = 10, CentreX = 100, CentreZ = 100, Width = 40, Height = 20, Turn = 10 };

            // from straight above the middle to straight right of it: a quarter turn clockwise
            ProjectedImage quarter = ProjectedImages.Turned(image, 100, 50, 150, 100, snap: false);
            Assert.AreEqual(100, quarter.Turn, 1e-9);
            // 100 + 7 degrees, rounded to a step of 15 degrees
            double angle = 7 * Math.PI / 180;
            ProjectedImage snapped = ProjectedImages.Turned(quarter, 150, 100, 100 + 50 * Math.Cos(angle), 100 + 50 * Math.Sin(angle), snap: true);
            Assert.AreEqual(105, snapped.Turn, 1e-9);
            Assert.AreEqual(-170, ProjectedImages.Normalized(190), 1e-9);
            Assert.AreEqual(180, ProjectedImages.Normalized(-180), 1e-9);
        }

        [TestMethod]
        public void SavesAndLoadsTheImagesWithTheirPictures()
        {
            MapDocument map = Map();
            map = map.AddImage(Image(Picture(80, 40), "Napkin"), out _);
            map = map.AddImage(Image(Picture(30, 60), "Layout B"), out _);
            map = map.UpdateImage("Napkin", image => image with { CentreX = 123.5, Turn = -6, Opacity = 0.55, Greyscale = true, Locked = true, AboveSketch = true, Shown = false, KeepProportions = false, Width = 300, Height = 111 });
            string project = ProjectFolders.New("project");

            MapProjectStore.Save(map, project);

            Assert.IsTrue(File.Exists(Path.Combine(project, "sketch", "images.json")));
            Assert.IsTrue(File.Exists(Path.Combine(project, "sketch", "images", "Napkin.png")));
            Assert.IsTrue(File.Exists(Path.Combine(project, "sketch", "images", "Layout B.png")));
            MapDocument loaded = MapProjectStore.Load(project);
            Assert.AreEqual(2, loaded.Images.Length);
            for (int i = 0; i < 2; i++)
            {
                ProjectedImage expected = map.Images[i];
                ProjectedImage actual = loaded.Images[i];
                Assert.AreEqual(expected with { Picture = [] }, actual with { Picture = [] }, "the list's order is kept, every property too");
                CollectionAssert.AreEqual(expected.Picture.ToArray(), actual.Picture.ToArray());
            }
        }

        [TestMethod]
        public void WritesNothingWithoutImagesAndRemovesWhatWentAway()
        {
            MapDocument map = Map().AddImage(Image(Picture(20, 20), "A"), out _).AddImage(Image(Picture(20, 20), "B"), out _);
            string project = ProjectFolders.New("project");
            MapProjectStore.Save(map, project);

            MapDocument one = map.RemoveImage("A");
            MapProjectStore.Save(one, project, map);
            Assert.IsFalse(File.Exists(Path.Combine(project, "sketch", "images", "A.png")));
            Assert.IsTrue(File.Exists(Path.Combine(project, "sketch", "images", "B.png")));

            MapProjectStore.Save(one.RemoveImage("B"), project, one);
            Assert.IsFalse(File.Exists(Path.Combine(project, "sketch", "images.json")));
            Assert.IsFalse(Directory.Exists(Path.Combine(project, "sketch", "images")));
            Assert.IsTrue(MapProjectStore.Load(project).Images.IsEmpty);

            string plain = ProjectFolders.New("plain");
            MapProjectStore.Save(Map(), plain);
            Assert.IsFalse(Directory.Exists(Path.Combine(plain, "sketch")), "a project without images has no sketch folder of ours");
        }

        [TestMethod]
        public void ARenamedImageKeepsOneFileUnderItsNewName()
        {
            MapDocument map = Map().AddImage(Image(Picture(20, 20), "Old"), out _);
            string project = ProjectFolders.New("project");
            MapProjectStore.Save(map, project);

            MapDocument renamed = map.RenameImage("Old", "New");
            MapProjectStore.Save(renamed, project, map);

            string[] files = [.. Directory.GetFiles(Path.Combine(project, "sketch", "images")).Select(Path.GetFileName)!];
            CollectionAssert.AreEqual(new[] { "New.png" }, files);
        }

        [TestMethod]
        public void NeverExportsTheImages()
        {
            MapDocument map = Map();
            MapDocument with = map.AddImage(Image(Picture(20, 20)), out _);

            IReadOnlyDictionary<string, byte[]> expected = MapExporter.ToBytes(map);
            IReadOnlyDictionary<string, byte[]> actual = MapExporter.ToBytes(with);

            CollectionAssert.AreEquivalent(expected.Keys.ToList(), actual.Keys.ToList());
            foreach ((string path, byte[] bytes) in expected)
            {
                CollectionAssert.AreEqual(bytes, actual[path], path);
            }
        }

        [TestMethod]
        public void ReloadsImagesChangedOnDisk()
        {
            MapDocument map = Map().AddImage(Image(Picture(20, 20), "A"), out _);
            string project = ProjectFolders.New("project");
            MapProjectStore.Save(map, project);

            Assert.AreSame(map, MapProjectStore.Reload(map, project, ["sketch/images.json", "sketch/images/A.png"]), "what the editor saved changes nothing");

            // another tool saves the picture, and the list is edited by hand
            File.WriteAllBytes(Path.Combine(project, "sketch", "images", "A.png"), File.ReadAllBytes(ProjectedImagesTest.Picture(50, 25)));
            string list = Path.Combine(project, "sketch", "images.json");
            File.WriteAllText(list, File.ReadAllText(list).Replace("\"locked\": false", "\"locked\": true").Replace("\"opacity\": 1", "\"opacity\": 0.25"));
            MapDocument reloaded = MapProjectStore.Reload(map, project, ["sketch/images/A.png", "sketch/images.json"]);

            Assert.AreEqual((50, 25), (reloaded.Images[0].PixelWidth, reloaded.Images[0].PixelHeight));
            Assert.IsTrue(reloaded.Images[0].Locked);
            Assert.AreEqual(0.25, reloaded.Images[0].Opacity);

            File.Delete(list);
            Directory.Delete(Path.Combine(project, "sketch", "images"), true);
            Assert.IsTrue(MapProjectStore.Reload(reloaded, project, ["sketch/images.json"]).Images.IsEmpty, "images removed on disk go from the map");
        }

        [TestMethod]
        public void RefusesAListWhosePictureIsMissing()
        {
            MapDocument map = Map().AddImage(Image(Picture(20, 20), "A"), out _);
            string project = ProjectFolders.New("project");
            MapProjectStore.Save(map, project);
            File.Delete(Path.Combine(project, "sketch", "images", "A.png"));

            MapProjectException exception = Assert.ThrowsException<MapProjectException>(() => MapProjectStore.Load(project));
            StringAssert.Contains(exception.Message, "A.png");
        }
    }
}
