using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A picture of the map maker's own projected onto the map in the top view (user story N-2):
    /// a drawing to trace, a screenshot of another map. It is a guide: kept in the project
    /// (<c>sketch/images.json</c> and the picture as <c>sketch/images/&lt;name&gt;.png</c>) and never
    /// exported; brushes, masks and generators never read it.
    /// </summary>
    public sealed record ProjectedImage
    {
        /// <summary>
        /// What the image is called, and the name of its file in <c>sketch/images/</c>: letters, digits,
        /// spaces, dashes and underscores, which no other image of the map has (in any case).
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// The picture as a PNG, copied into the project: the longest side at most
        /// <see cref="Editing.ProjectedImages.MaxPixels"/> pixels, whatever format it came in.
        /// </summary>
        public required ImmutableArray<byte> Picture { get; init; }

        /// <summary>
        /// The picture's size in pixels.
        /// </summary>
        public required int PixelWidth { get; init; }

        public required int PixelHeight { get; init; }

        /// <summary>
        /// Where the picture's middle lies, in ogrids.
        /// </summary>
        public double CentreX { get; init; }

        public double CentreZ { get; init; }

        /// <summary>
        /// How wide and high the picture lies on the map before it is turned, in ogrids.
        /// </summary>
        public double Width { get; init; } = 1;

        public double Height { get; init; } = 1;

        /// <summary>
        /// How far the picture is turned about its middle, in degrees, clockwise as the top view shows it.
        /// </summary>
        public double Turn { get; init; }

        /// <summary>
        /// How much of the map shows through, 0 to 1 (1 is opaque).
        /// </summary>
        public double Opacity { get; init; } = 1;

        /// <summary>
        /// Whether the top view draws it without colour.
        /// </summary>
        public bool Greyscale { get; init; }

        /// <summary>
        /// Whether it must not be moved, scaled or turned by the mouse (the inspector's fields still change it).
        /// </summary>
        public bool Locked { get; init; }

        /// <summary>
        /// Whether the top view draws it.
        /// </summary>
        public bool Shown { get; init; } = true;

        /// <summary>
        /// Whether it lies over the sketch's strokes (true) or under them, over the terrain and under
        /// everything the editor draws on it (false).
        /// </summary>
        public bool AboveSketch { get; init; }

        /// <summary>
        /// Whether scaling a corner keeps the picture's proportions, and a fit to the map or its
        /// playable area keeps them too (else it stretches to the rectangle).
        /// </summary>
        public bool KeepProportions { get; init; } = true;
    }
}
