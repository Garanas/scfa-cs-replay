namespace FAForever.MapEditor.Cli.Tests
{
    [TestClass]
    public sealed class EditorProcessTest
    {
        [TestMethod]
        public void ARestartTakesTheArgumentsOfTheLastStartUnlessGivenAgain()
        {
            // a port no test editor uses, so the file of a real one is never touched
            const int port = 59871;
            string file = EditorProcess.StartFile(port);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, """{"exe":"D:/build/faf-map-editor.exe","settings":"D:/test/settings.json","map":"D:/maps/one","shadow":null,"noShadow":false,"log":null}""");
            try
            {
                EditorOptions again = EditorProcess.WithRemembered(new EditorOptions { Command = "restart", Port = port, Map = "D:/maps/two" });

                Assert.AreEqual("D:/build/faf-map-editor.exe", again.Exe);
                Assert.AreEqual("D:/test/settings.json", again.Settings);
                Assert.AreEqual("D:/maps/two", again.Map, "an option given now wins");
                Assert.IsFalse(again.NoShadow);
                Assert.AreEqual(port, again.Port);
            }
            finally
            {
                File.Delete(file);
            }

            EditorOptions none = new EditorOptions { Command = "restart", Port = port, Settings = "D:/other.json" };
            Assert.AreSame(none, EditorProcess.WithRemembered(none), "without a remembered start, the options given");
        }
    }
}
