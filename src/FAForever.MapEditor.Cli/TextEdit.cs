using System.Text;

namespace FAForever.MapEditor.Cli
{
    /// <summary>
    /// <c>faf-map dev replace</c>: replaces text in a source file the way an editor would, keeping
    /// what shell tools get wrong: the file's line endings (CRLF or LF, whatever the old and new
    /// text use), its UTF-8 and its byte order mark. The old text must occur as often as expected
    /// (once by default), so an edit never lands in the wrong place.
    /// </summary>
    public static class TextEdit
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        /// <summary>
        /// The text with <paramref name="old"/> replaced by <paramref name="replacement"/>, both
        /// written with the text's line endings.
        /// </summary>
        /// <param name="count">How many times the old text must occur; null: at least once, every one replaced.</param>
        /// <exception cref="ArgumentException">The old text is empty or occurs another number of times.</exception>
        public static string Replace(string text, string old, string replacement, int? count = 1)
        {
            string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            string from = WithNewlines(old, newline);
            string to = WithNewlines(replacement, newline);
            if (from.Length == 0)
            {
                throw new ArgumentException("The old text is empty");
            }
            int found = Occurrences(text, from);
            if (count is int expected ? found != expected : found == 0)
            {
                throw new ArgumentException(count is int n
                    ? $"The old text occurs {found} times, not {n}: {Preview(old)}"
                    : $"The old text does not occur: {Preview(old)}");
            }
            return text.Replace(from, to, StringComparison.Ordinal);
        }

        /// <summary>
        /// Replaces in a file, keeping its byte order mark; answers how many places changed.
        /// </summary>
        public static int ReplaceInFile(string path, string old, string replacement, int? count = 1)
        {
            byte[] bytes = File.ReadAllBytes(path);
            bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            string text = Utf8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
            string changed = Replace(text, old, replacement, count);
            int places = Occurrences(text, WithNewlines(old, text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n"));
            byte[] written = Utf8.GetBytes(changed);
            using FileStream stream = File.Create(path);
            if (bom)
            {
                stream.Write([0xEF, 0xBB, 0xBF]);
            }
            stream.Write(written);
            return places;
        }

        /// <summary>
        /// A text file's content as UTF-8, without a byte order mark: the old or new text of an edit.
        /// </summary>
        public static string ReadText(string path) => File.ReadAllText(path, Utf8);

        private static string WithNewlines(string text, string newline) =>
            text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", newline, StringComparison.Ordinal);

        private static int Occurrences(string text, string part)
        {
            int count = 0;
            for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
            {
                count++;
            }
            return count;
        }

        private static string Preview(string text)
        {
            string line = text.Replace("\r", "", StringComparison.Ordinal).Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? text;
            return line.Length > 80 ? line[..80] + "..." : line;
        }
    }
}
