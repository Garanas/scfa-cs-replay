using System.Numerics;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// A start position: a marker named after its army, e.g. <c>ARMY_1</c>.
    /// </summary>
    public sealed record Spawn : Marker
    {
        /// <summary>
        /// The army that starts here: the marker's name.
        /// </summary>
        public string Army => Name;

        /// <summary>
        /// Where the no-rush circle of the army lies relative to the start position, in game units;
        /// null for a circle around the start position.
        /// </summary>
        public Vector2? NoRushOffset { get; init; }

        public override Color32 Color => Color32.FromArgb(0xff800080);
    }
}
