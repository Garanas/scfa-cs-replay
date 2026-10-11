using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// The normal map as the <c>.scmap</c> stores it: tiles of at most 2048 by 2048 ogrids, row by row
    /// (<see cref="Scmap.NormalMaps"/>), and as the editor holds it: one image. The engine skips the
    /// stored tiles and makes the normal map from the heightmap in tiles of its own
    /// (<c>CWldTerrainRes::InitNormalMap</c> in Draiget/faf-re, <c>src/sdk/moho/sim/CWldMap.cpp</c>), so
    /// only other tools read them.
    /// </summary>
    internal static class NormalMapTiles
    {
        /// <summary>
        /// The size of a tile in ogrids the engine uses for a map: its size, at most 2048.
        /// </summary>
        public static (int Width, int Height) DefaultTileSize(int mapWidth, int mapHeight) =>
            (Math.Min(mapWidth, Scmap.MaxNormalMapTile), Math.Min(mapHeight, Scmap.MaxNormalMapTile));

        /// <summary>
        /// How many tiles cover the map along x and along z.
        /// </summary>
        public static (int Columns, int Rows) Grid(int mapWidth, int mapHeight, int tileWidth, int tileHeight) =>
            ((mapWidth + tileWidth - 1) / tileWidth, (mapHeight + tileHeight - 1) / tileHeight);

        /// <summary>
        /// The tiles of a map as one image: each tile decoded, all of the same size, put in place.
        /// </summary>
        /// <exception cref="MapProjectException">The map has no tiles, their number does not cover
        /// the map, or they differ in size.</exception>
        public static (int Width, int Height, TextureFormat Format, byte[] Rgba) Stitch(Scmap scmap)
        {
            IReadOnlyList<byte[]> tiles = scmap.NormalMaps;
            if (tiles.Count == 0)
            {
                throw new MapProjectException("The map has no normal map");
            }
            if (tiles.Count == 1)
            {
                return DdsTexture.Read(tiles[0], "normal map");
            }

            (int columns, int rows) = scmap.NormalMapTileWidth > 0 && scmap.NormalMapTileHeight > 0
                ? Grid(scmap.Width, scmap.Height, scmap.NormalMapTileWidth, scmap.NormalMapTileHeight)
                : (0, 0);
            if (columns * rows != tiles.Count)
            {
                throw new MapProjectException(
                    $"The map has {tiles.Count} normal map tiles of {scmap.NormalMapTileWidth} by {scmap.NormalMapTileHeight}, which do not cover a map of {scmap.Width} by {scmap.Height}");
            }

            (int tileWidth, int tileHeight, TextureFormat format, byte[] first) = DdsTexture.Read(tiles[0], "normal map tile 1");
            int width = tileWidth * columns;
            int height = tileHeight * rows;
            byte[] rgba = new byte[checked(width * height * 4)];
            for (int index = 0; index < tiles.Count; index++)
            {
                (int w, int h, _, byte[] tile) = index == 0 ? (tileWidth, tileHeight, format, first) : DdsTexture.Read(tiles[index], $"normal map tile {index + 1}");
                if (w != tileWidth || h != tileHeight)
                {
                    throw new MapProjectException($"The normal map's tile {index + 1} is {w} by {h}, the first {tileWidth} by {tileHeight}");
                }
                int left = index % columns * tileWidth;
                int top = index / columns * tileHeight;
                for (int y = 0; y < tileHeight; y++)
                {
                    tile.AsSpan(y * tileWidth * 4, tileWidth * 4).CopyTo(rgba.AsSpan(((top + y) * width + left) * 4));
                }
            }
            return (width, height, format, rgba);
        }

        /// <summary>
        /// The image as the tiles of a map: <paramref name="tileWidth"/> by <paramref name="tileHeight"/>
        /// ogrids when given and the image divides into their grid, else the engine's tiles
        /// (<see cref="DefaultTileSize"/>).
        /// </summary>
        /// <returns>The size of a tile in ogrids, as the <c>.scmap</c> stores it, and the tiles as DDS files.</returns>
        public static (int TileWidth, int TileHeight, IReadOnlyList<byte[]> Tiles) Split(
            int mapWidth, int mapHeight, int imageWidth, int imageHeight, byte[] rgba, TextureFormat format, int? tileWidth, int? tileHeight)
        {
            (int tw, int th) = tileWidth is int w && tileHeight is int h && w > 0 && h > 0 && Fits(w, h)
                ? (w, h)
                : DefaultTileSize(mapWidth, mapHeight);
            (int columns, int rows) = Grid(mapWidth, mapHeight, tw, th);
            if (columns * rows == 1 || imageWidth % columns != 0 || imageHeight % rows != 0)
            {
                // one tile, or an image that does not divide into the grid, which is written whole
                return (tw, th, [DdsTexture.Write(imageWidth, imageHeight, format, rgba)]);
            }

            int pieceWidth = imageWidth / columns;
            int pieceHeight = imageHeight / rows;
            List<byte[]> tiles = new List<byte[]>();
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    byte[] piece = new byte[pieceWidth * pieceHeight * 4];
                    for (int y = 0; y < pieceHeight; y++)
                    {
                        rgba.AsSpan(((row * pieceHeight + y) * imageWidth + column * pieceWidth) * 4, pieceWidth * 4).CopyTo(piece.AsSpan(y * pieceWidth * 4));
                    }
                    tiles.Add(DdsTexture.Write(pieceWidth, pieceHeight, format, piece));
                }
            }
            return (tw, th, tiles);

            bool Fits(int w, int h)
            {
                (int c, int r) = Grid(mapWidth, mapHeight, w, h);
                return imageWidth % c == 0 && imageHeight % r == 0;
            }
        }
    }
}
