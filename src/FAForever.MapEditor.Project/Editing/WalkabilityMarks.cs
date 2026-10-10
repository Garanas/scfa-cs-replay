using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Walkability marked by hand (user story T-7): per ogrid none, blocked or open
    /// (<see cref="MapDocument.WalkabilityMarks"/>), painted with the Terrain mode's Walkability tool
    /// (<see cref="PaintTarget.WalkabilityMarks"/>). Generating walkability takes them over the
    /// heights and terrain types; the export writes what the game can take of them into the terrain
    /// types.
    /// </summary>
    /// <remarks>
    /// The game decides where units go by the heights, the water and the terrain types: an ogrid
    /// whose terrain type blocks (<see cref="TerrainType.Blocking"/>) is closed to every layer of
    /// units, and the game reads an ogrid's type at its far corner (<see cref="NavGenerator"/>).
    /// So a blocked mark becomes the blocking type <see cref="BlockingType"/> there on export, and
    /// walkability is generated from the types as the export writes them: what the editor shows is
    /// what the game does. An open mark takes a blocking type away (<see cref="OpenType"/>), but no
    /// file of a map makes steep ground walkable for the game; walkability counts such an ogrid as
    /// open (the map maker's decision, e.g. for path networks), and the check <c>walkability-marks</c>
    /// warns where the game will not.
    /// </remarks>
    public static class WalkabilityMarks
    {
        /// <summary>
        /// No mark: walkability is what the heights and types make it.
        /// </summary>
        public const byte None = 0;

        /// <summary>
        /// Land units must not pass.
        /// </summary>
        public const byte Blocked = 1;

        /// <summary>
        /// Land units may pass.
        /// </summary>
        public const byte Open = 2;

        /// <summary>
        /// The terrain type a blocked mark becomes on export: <c>Dirt09</c>, the game's blocking dirt.
        /// </summary>
        public const byte BlockingType = 9;

        /// <summary>
        /// The terrain type an open mark puts in place of a blocking one: <c>Default</c>.
        /// </summary>
        public const byte OpenType = 1;

        /// <summary>
        /// The marks' value of a project's pixel (<c>walkability-marks.png</c>: black none, grey
        /// blocked, white open).
        /// </summary>
        public static byte FromPixel(byte pixel) => pixel < 64 ? None : pixel < 192 ? Blocked : Open;

        /// <summary>
        /// The project's pixel of a mark.
        /// </summary>
        public static byte ToPixel(byte mark) => mark switch { Blocked => 128, Open => 255, _ => 0 };

        /// <summary>
        /// How many ogrids are marked blocked and open.
        /// </summary>
        public static (int Blocked, int Open) Count(TiledBuffer<byte>? marks)
        {
            if (marks is null)
            {
                return (0, 0);
            }
            int blocked = 0;
            int open = 0;
            foreach (byte mark in marks.ToArray())
            {
                blocked += mark == Blocked ? 1 : 0;
                open += mark == Open ? 1 : 0;
            }
            return (blocked, open);
        }

        /// <summary>
        /// The terrain types as the export writes them, row by row: a blocked mark makes the type the
        /// game reads for that ogrid (at its far corner) <see cref="BlockingType"/>, an open mark makes
        /// a blocking type there <see cref="OpenType"/>.
        /// </summary>
        public static byte[] TerrainTypes(MapDocument document)
        {
            byte[] types = document.Terrain.Types.ToArray();
            if (document.WalkabilityMarks is not TiledBuffer<byte> marks || marks.Width != document.Terrain.Types.Width || marks.Height != document.Terrain.Types.Height)
            {
                return types;
            }
            int width = marks.Width;
            int height = marks.Height;
            byte[] marked = marks.ToArray();
            for (int z = 0; z + 1 < height; z++)
            {
                for (int x = 0; x + 1 < width; x++)
                {
                    byte mark = marked[z * width + x];
                    if (mark == None)
                    {
                        continue;
                    }
                    int read = (z + 1) * width + x + 1;
                    bool blocking = FileFormats.Map.TerrainTypes.Get(types[read]).Blocking;
                    if (mark == Blocked && !blocking)
                    {
                        types[read] = BlockingType;
                    }
                    else if (mark == Open && blocking)
                    {
                        types[read] = OpenType;
                    }
                }
            }
            return types;
        }

        /// <summary>
        /// The terrain types as the export writes them (<see cref="TerrainTypes(MapDocument)"/>).
        /// </summary>
        public static TiledBuffer<byte> ExportedTypes(MapDocument document) => document.WalkabilityMarks is null
            ? document.Terrain.Types
            : TiledBuffer<byte>.FromArray(document.Terrain.Types.Width, document.Terrain.Types.Height, TerrainTypes(document));

        /// <summary>
        /// The ogrids marked open, row by row, for <see cref="NavGenerator"/>; null without any.
        /// </summary>
        public static bool[]? ForcedOpen(MapDocument document)
        {
            if (document.WalkabilityMarks is not TiledBuffer<byte> marks || marks.Width != document.Width || marks.Height != document.Height)
            {
                return null;
            }
            byte[] marked = marks.ToArray();
            bool[] open = new bool[marked.Length];
            bool any = false;
            for (int i = 0; i < marked.Length; i++)
            {
                open[i] = marked[i] == Open;
                any |= open[i];
            }
            return any ? open : null;
        }

        /// <summary>
        /// The ogrids marked open where the game will not let land units pass whatever the map's
        /// files say: too steep (a side rising <see cref="NavGenerator.MaxHeightDifference"/> or
        /// more) or under water; their count and the first, row by row.
        /// </summary>
        public static (int Count, (int X, int Z)? First) OpenWhereTheGameWillNot(MapDocument document)
        {
            if (document.WalkabilityMarks is not TiledBuffer<byte> marks || marks.Width != document.Width || marks.Height != document.Height)
            {
                return (0, null);
            }
            Heightmap heights = document.Terrain.Heightmap;
            float? water = document.Water.HasWater ? document.Water.Elevation : null;
            int count = 0;
            (int, int)? first = null;
            byte[] marked = marks.ToArray();
            for (int z = 0; z < marks.Height; z++)
            {
                for (int x = 0; x < marks.Width; x++)
                {
                    if (marked[z * marks.Width + x] != Open)
                    {
                        continue;
                    }
                    float a = heights.GetHeight(x, z);
                    float b = heights.GetHeight(x + 1, z);
                    float c = heights.GetHeight(x, z + 1);
                    float d = heights.GetHeight(x + 1, z + 1);
                    bool steep = MathF.Abs(a - b) >= NavGenerator.MaxHeightDifference || MathF.Abs(c - d) >= NavGenerator.MaxHeightDifference
                        || MathF.Abs(a - c) >= NavGenerator.MaxHeightDifference || MathF.Abs(b - d) >= NavGenerator.MaxHeightDifference;
                    bool wet = water is float level && (a + b + c + d) / 4 < level;
                    if (steep || wet)
                    {
                        count++;
                        first ??= (x, z);
                    }
                }
            }
            return (count, first);
        }
    }
}
