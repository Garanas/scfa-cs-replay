using System.IO.Compression;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Workspaces;
using ImageMagick;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Pictures of the editor's screens, drawn without a screen and compared with the ones in
    /// <c>snapshots/</c>: a change of look shows as a failing test, to look at and accept. Run with
    /// <c>UPDATE_SNAPSHOTS=1</c> to accept the new pictures (they are written into the source
    /// tree, for the commit). A failure writes the picture it got next to the tests' output.
    /// </summary>
    [TestClass]
    public sealed class SnapshotTest
    {
        // a pixel counts as different when a channel differs by more than this; the share of such pixels allowed
        private const int ChannelTolerance = 24;
        private const double PixelTolerance = 0.002;

        private static string Snapshots => Path.Combine(HeadlessApp.RepositoryFolder, "tests", "FAForever.MapEditor.App.Tests", "snapshots");

        private sealed class NoDialogs : IEditorDialogs
        {
            public Task<string?> PickFolderAsync(string title, string? startIn = null) => Task.FromResult<string?>(null);

            public Task<SaveChoice> AskToSaveChangesAsync(string mapName) => Task.FromResult(SaveChoice.Discard);
        }

        private static MainViewModel NewModel()
        {
            MainViewModel model = new MainViewModel(new NoDialogs());
            model.Setup.FindFaf = () => null;
            return model;
        }

        private static string NewFolder(string name) => Path.Combine(AppContext.BaseDirectory, "snapshot-runs", $"{name}-{Guid.NewGuid():N}");

        [TestMethod]
        public Task TheSetupPage() => HeadlessApp.Run(async () =>
        {
            MainViewModel model = NewModel();
            await model.StartAsync(new UserSettings(Path.Combine(NewFolder("settings"), "settings.json")));
            model.Setup.Folder = @"D:\FAF Map Editor";
            model.Setup.GameFolder = @"D:\Games\Supreme Commander Forged Alliance";
            model.Setup.FafDataFolder = @"C:\ProgramData\FAForever\gamedata";
            model.Setup.VaultFolder = @"D:\faf-vault";

            Compare("setup", model);
        });

        [TestMethod]
        public Task TheAssetsPage() => HeadlessApp.Run(async () =>
        {
            // a workspace of a game with empty archives, never converted
            string root = NewFolder("assets");
            string game = Path.Combine(root, "game");
            Directory.CreateDirectory(Path.Combine(game, "gamedata"));
            foreach (string name in GameArchives.Names)
            {
                using ZipArchive empty = ZipFile.Open(Path.Combine(game, "gamedata", name + ".scd"), ZipArchiveMode.Create);
            }
            Directory.CreateDirectory(Path.Combine(root, "vault"));
            Workspace workspace = Workspace.Create(Path.Combine(root, "workspace"), game, null, Path.Combine(root, "vault"));
            UserSettings settings = new UserSettings(Path.Combine(root, "settings.json"));
            settings.SetWorkspace(workspace.Folder);

            MainViewModel model = NewModel();
            await model.StartAsync(settings);
            Assert.AreEqual(EditorStage.UpdateAssets, model.Setup.Stage);

            Compare("assets", model);
        });

        [TestMethod]
        public Task TheEditor() => HeadlessApp.Run(async () =>
        {
            MainViewModel model = NewModel();
            await model.OpenAsync("assets/maps/HardFFA.v0001");

            Compare("editor", model);
        });

        /// <summary>
        /// Draws the window for the model at 1280 by 800 and compares it with its snapshot.
        /// </summary>
        private static void Compare(string name, MainViewModel model)
        {
            MainWindow window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            byte[] png = Png(window.CaptureRenderedFrame());
            window.CloseWithoutAsking();

            string expected = Path.Combine(Snapshots, name + ".png");
            if (Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1")
            {
                Directory.CreateDirectory(Snapshots);
                File.WriteAllBytes(expected, png);
                return;
            }

            string actual = Path.Combine(AppContext.BaseDirectory, "snapshot-results", name + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(actual)!);
            File.WriteAllBytes(actual, png);
            Assert.IsTrue(File.Exists(expected), $"There is no snapshot {expected} yet: look at {actual} and run the tests with UPDATE_SNAPSHOTS=1 to keep it");

            (int width, int height, byte[] got) = Pixels(png);
            (int expectedWidth, int expectedHeight, byte[] want) = Pixels(File.ReadAllBytes(expected));
            Assert.AreEqual((expectedWidth, expectedHeight), (width, height), $"the size of {name} changed; see {actual}");
            int different = 0;
            for (int i = 0; i < got.Length; i += 4)
            {
                if (Math.Abs(got[i] - want[i]) > ChannelTolerance || Math.Abs(got[i + 1] - want[i + 1]) > ChannelTolerance
                    || Math.Abs(got[i + 2] - want[i + 2]) > ChannelTolerance)
                {
                    different++;
                }
            }
            double share = (double)different / (width * height);
            Assert.IsTrue(share <= PixelTolerance,
                $"{name} looks different in {share:P2} of its pixels; compare {actual} with {expected}, and run with UPDATE_SNAPSHOTS=1 if that is the new look");
        }

        private static byte[] Png(Bitmap? frame)
        {
            Assert.IsNotNull(frame, "the window drew a frame");
            using MemoryStream stream = new MemoryStream();
            frame.Save(stream, new PngBitmapEncoderOptions());
            return stream.ToArray();
        }

        private static (int Width, int Height, byte[] Rgba) Pixels(byte[] png)
        {
            using MagickImage image = new MagickImage(png);
            using IPixelCollection<ushort> pixels = image.GetPixels();
            return ((int)image.Width, (int)image.Height, pixels.ToByteArray(PixelMapping.RGBA)!);
        }
    }
}
