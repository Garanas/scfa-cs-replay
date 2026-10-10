using System.Text;

namespace FAForever.MapEditor.Cli.Tests
{
    [TestClass]
    public sealed class TextEditTest
    {
        [TestMethod]
        public void KeepsTheFilesLineEndings()
        {
            Assert.AreEqual("a\r\nX\r\nY\r\nc\r\n", TextEdit.Replace("a\r\nb\r\nc\r\n", "b", "X\nY"), "new lines become CRLF in a CRLF file");
            Assert.AreEqual("a\nX\nY\nc\n", TextEdit.Replace("a\nb\nc\n", "b", "X\r\nY"), "and LF in an LF file");
            Assert.AreEqual("one\r\nthree\r\n", TextEdit.Replace("one\r\ntwo\r\nthree\r\n", "one\ntwo\n", "one\n"), "the old text matches whatever its line endings");
        }

        [TestMethod]
        public void TheOldTextMustOccurAsOftenAsExpected()
        {
            Assert.ThrowsException<ArgumentException>(() => TextEdit.Replace("x x", "x", "y"), "twice where once was expected");
            Assert.AreEqual("y y", TextEdit.Replace("x x", "x", "y", count: 2));
            Assert.AreEqual("y y", TextEdit.Replace("x x", "x", "y", count: null));
            Assert.ThrowsException<ArgumentException>(() => TextEdit.Replace("x", "z", "y", count: null), "not at all");
            Assert.ThrowsException<ArgumentException>(() => TextEdit.Replace("x", "", "y"));
        }

        [TestMethod]
        public void KeepsUtf8AndTheByteOrderMark()
        {
            string file = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(file, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("// it's 25 °C\r\nint x;\r\n")]);

                Assert.AreEqual(1, TextEdit.ReplaceInFile(file, "int x;", "int x; // ‘quoted’"));

                byte[] bytes = File.ReadAllBytes(file);
                CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3], "the mark stays");
                Assert.AreEqual("// it's 25 °C\r\nint x; // ‘quoted’\r\n", Encoding.UTF8.GetString(bytes[3..]));

                File.WriteAllBytes(file, [0x63, 0x61, 0x66, 0xE9]); // "café" in cp1252
                Assert.ThrowsException<DecoderFallbackException>(() => TextEdit.ReplaceInFile(file, "caf", "x"), "a file that is not UTF-8 is left alone");
            }
            finally
            {
                File.Delete(file);
            }
        }
    }
}
