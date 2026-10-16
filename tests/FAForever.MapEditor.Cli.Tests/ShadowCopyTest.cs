namespace FAForever.MapEditor.Cli.Tests
{
    [TestClass]
    public sealed class ShadowCopyTest
    {
        private static string NewFolder()
        {
            string folder = Path.Combine(Path.GetTempPath(), "shadow-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        [TestMethod]
        public void CopiesTheBuildWithThisSystemsNativeLibrariesOnly()
        {
            string bin = NewFolder();
            string shadow = NewFolder();
            try
            {
                string family = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
                string other = OperatingSystem.IsWindows() ? "linux-x64" : "win-x64";
                string arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
                Write(Path.Combine(bin, "faf-map-editor.exe"), "exe");
                Write(Path.Combine(bin, "editor.dll"), "one");
                Write(Path.Combine(bin, "Plugins", "a.lua"), "lua");
                Write(Path.Combine(bin, "runtimes", $"{family}-{arch}", "native", "skia.dll"), "native");
                Write(Path.Combine(bin, "runtimes", other, "native", "skia.so"), "elsewhere");

                (string exe, int copied) = ShadowCopy.Prepare(Path.Combine(bin, "faf-map-editor.exe"), shadow);
                Assert.AreEqual(Path.Combine(shadow, "faf-map-editor.exe"), exe);
                Assert.AreEqual(4, copied, "the files, the folder's and this system's native library");
                Assert.IsTrue(File.Exists(Path.Combine(shadow, "Plugins", "a.lua")));
                Assert.IsTrue(File.Exists(Path.Combine(shadow, "runtimes", $"{family}-{arch}", "native", "skia.dll")));
                Assert.IsFalse(Directory.Exists(Path.Combine(shadow, "runtimes", other)), "another system's libraries stay out");
                Assert.AreEqual(File.GetLastWriteTimeUtc(Path.Combine(bin, "editor.dll")), File.GetLastWriteTimeUtc(Path.Combine(shadow, "editor.dll")),
                    "the copy keeps the build's time, which state.build reports");

                // a build changed one file and removed another: only that is copied, the other goes
                Assert.AreEqual(0, ShadowCopy.Prepare(Path.Combine(bin, "faf-map-editor.exe"), shadow).Copied, "nothing changed");
                Write(Path.Combine(bin, "editor.dll"), "two, longer");
                File.Delete(Path.Combine(bin, "Plugins", "a.lua"));
                Assert.AreEqual(1, ShadowCopy.Prepare(Path.Combine(bin, "faf-map-editor.exe"), shadow).Copied);
                Assert.AreEqual("two, longer", File.ReadAllText(Path.Combine(shadow, "editor.dll")));
                Assert.IsFalse(File.Exists(Path.Combine(shadow, "Plugins", "a.lua")), "what the build no longer has is gone");

                Assert.ThrowsException<ArgumentException>(() => ShadowCopy.Prepare(Path.Combine(bin, "faf-map-editor.exe"), bin), "never into the build itself");
            }
            finally
            {
                Directory.Delete(bin, recursive: true);
                Directory.Delete(shadow, recursive: true);
            }
        }

        [TestMethod]
        public void TellsThisSystemsRuntimes()
        {
            string family = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
            Assert.IsTrue(ShadowCopy.IsThisSystem(family));
            Assert.IsTrue(ShadowCopy.IsThisSystem(System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier));
            Assert.IsFalse(ShadowCopy.IsThisSystem("linux-riscv64"));
        }
    }
}
