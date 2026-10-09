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
        /// The repository's folder, for the files the tests read and write in the source tree:
        /// from where this file was compiled, so it holds wherever the build output goes.
        /// </summary>
        public static string RepositoryFolder => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourceFile())!, "..", ".."));

        private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
    }
}
