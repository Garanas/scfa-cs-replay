using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A node of the graph the AI finds its way with on one layer.
    /// </summary>
    public sealed record PathNode : Marker
    {
        public PathLayer Layer { get; init; }

        /// <summary>
        /// The ids of the nodes this one links to (<see cref="Marker.Id"/>).
        /// </summary>
        public ImmutableArray<int> AdjacentTo { get; init; } = [];

        public override Color32 Color => Color32.FromArgb(Layer switch
        {
            PathLayer.Land => 0xff00ff00,
            PathLayer.Water => 0xff0000ff,
            PathLayer.Amphibious => 0xff00ffff,
            _ => 0xffffffff,
        });
    }
}
