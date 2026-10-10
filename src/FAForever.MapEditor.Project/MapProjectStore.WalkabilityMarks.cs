using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// The walkability marked by hand (user story T-7) on disk: <c>walkability-marks.png</c>, an
    /// ogrid a pixel (black none, grey blocked, white open), written only for a map with marks.
    /// </summary>
    public static partial class MapProjectStore
    {
        private static void SaveWalkabilityMarks(MapDocument document, string folder, MapDocument? previous)
        {
            string file = Path.Combine(folder, MapProjectLayout.WalkabilityMarks);
            if (document.WalkabilityMarks is not TiledBuffer<byte> marks)
            {
                File.Delete(file);
                return;
            }
            if (previous is not null && ReferenceEquals(marks, previous.WalkabilityMarks) && File.Exists(file))
            {
                return;
            }
            ProjectImages.WriteGray8(file, marks.Width, marks.Height, [.. marks.ToArray().Select(WalkabilityMarks.ToPixel)]);
        }

        private static MapDocument WithWalkabilityMarks(MapDocument document, string folder)
        {
            string file = Path.Combine(folder, MapProjectLayout.WalkabilityMarks);
            return File.Exists(file) ? document with { WalkabilityMarks = ReadWalkabilityMarks(file, document.Width, document.Height) } : document;
        }

        private static MapDocument ReloadWalkabilityMarks(MapDocument document, string folder)
        {
            string file = Path.Combine(folder, MapProjectLayout.WalkabilityMarks);
            if (!File.Exists(file))
            {
                return document.WalkabilityMarks is null ? document : document with { WalkabilityMarks = null };
            }
            TiledBuffer<byte> read = ReadWalkabilityMarks(file, document.Width, document.Height);
            return document.WalkabilityMarks is TiledBuffer<byte> held && read.ToArray().AsSpan().SequenceEqual(held.ToArray())
                ? document
                : document with { WalkabilityMarks = read };
        }

        private static TiledBuffer<byte> ReadWalkabilityMarks(string file, int width, int height) =>
            TiledBuffer<byte>.FromArray(width, height, [.. ProjectImages.ReadGray8(file, width, height).Select(WalkabilityMarks.FromPixel)]);
    }
}
