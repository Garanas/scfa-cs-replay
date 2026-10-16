using Avalonia;
using Avalonia.Headless;
using FAForever.MapEditor.App;

[assembly: AvaloniaTestApplication(typeof(FAForever.MapEditor.App.Tests.HeadlessApp))]

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// The editor's application without a screen, for the tests of its window: Skia draws the
    /// frames, with the editor's own font, so pictures are the same on every computer.
    /// </summary>
    public static class HeadlessApp
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
            .UseSkia()
            .WithEditorFonts()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

        /// <summary>
        /// The one session of this test assembly: Avalonia starts once per process.
        /// </summary>
        public static HeadlessUnitTestSession Session => HeadlessUnitTestSession.GetOrStartForAssembly(typeof(HeadlessApp).Assembly);

        /// <summary>
        /// Runs a test on the user interface's thread of the headless application.
        /// </summary>
        public static Task Run(Func<Task> test) => Session.Dispatch(async () =>
        {
            await test();
            return true;
        }, CancellationToken.None);

        /// <summary>
        /// One control's picture as drawn now, as a PNG (not the whole window's): its own size at
        /// 96 dots per inch. Lay the window out first (<c>Dispatcher.UIThread.RunJobs()</c>).
        /// </summary>
        public static byte[] Capture(Avalonia.Controls.Control control)
        {
            Avalonia.Size size = control.Bounds.Size;
            using Avalonia.Media.Imaging.RenderTargetBitmap bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(
                new Avalonia.PixelSize(Math.Max(1, (int)size.Width), Math.Max(1, (int)size.Height)));
            bitmap.Render(control);
            using MemoryStream png = new MemoryStream();
            bitmap.Save(png);
            return png.ToArray();
        }

        /// <summary>
        /// Saves a picture into the tests' output (<c>captures/</c>) to look at, and returns its path.
        /// </summary>
        public static string SaveCapture(byte[] png, string name)
        {
            string file = Path.Combine(AppContext.BaseDirectory, "captures", name + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, png);
            return file;
        }

        /// <summary>
        /// The repository's folder, for the files the tests read and write in the source tree:
        /// from where this file was compiled, so it holds wherever the build output goes.
        /// </summary>
        public static string RepositoryFolder => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourceFile())!, "..", ".."));

        private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
    }
}
