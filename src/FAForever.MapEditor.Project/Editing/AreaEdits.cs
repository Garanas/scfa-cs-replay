using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Which parts of an area's rectangle a handle moves (user story M-5): a corner moves two edges,
    /// an edge one. <see cref="Move"/> moves all four, the whole area.
    /// </summary>
    [Flags]
    public enum AreaHandle
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8,
        TopLeft = Top | Left,
        TopRight = Top | Right,
        BottomLeft = Bottom | Left,
        BottomRight = Bottom | Right,
        Move = Left | Top | Right | Bottom,
    }

    /// <summary>
    /// Areas drawn and resized by hand (user story M-5): their rectangles in ogrids, x to the right
    /// and z down in the top view, <c>X0, Z0</c> the top left corner.
    /// </summary>
    public static class AreaEdits
    {
        /// <summary>
        /// The area with its corners in order: <c>X0 &lt;= X1</c>, <c>Z0 &lt;= Z1</c>.
        /// </summary>
        public static Area Normalized(Area area) => area with
        {
            X0 = Math.Min(area.X0, area.X1),
            X1 = Math.Max(area.X0, area.X1),
            Z0 = Math.Min(area.Z0, area.Z1),
            Z1 = Math.Max(area.Z0, area.Z1),
        };

        /// <summary>
        /// The places of an area's eight handles: the corners and the middles of the edges.
        /// </summary>
        public static IEnumerable<(AreaHandle Handle, double X, double Z)> Handles(Area area)
        {
            Area a = Normalized(area);
            double mx = (a.X0 + a.X1) / 2;
            double mz = (a.Z0 + a.Z1) / 2;
            yield return (AreaHandle.TopLeft, a.X0, a.Z0);
            yield return (AreaHandle.Top, mx, a.Z0);
            yield return (AreaHandle.TopRight, a.X1, a.Z0);
            yield return (AreaHandle.Right, a.X1, mz);
            yield return (AreaHandle.BottomRight, a.X1, a.Z1);
            yield return (AreaHandle.Bottom, mx, a.Z1);
            yield return (AreaHandle.BottomLeft, a.X0, a.Z1);
            yield return (AreaHandle.Left, a.X0, mz);
        }

        /// <summary>
        /// The handle within <paramref name="reach"/> ogrids of a place (the nearest), else
        /// <see cref="AreaHandle.Move"/> on an edge or inside, else <see cref="AreaHandle.None"/>.
        /// </summary>
        public static AreaHandle HandleAt(Area area, double x, double z, double reach)
        {
            (AreaHandle handle, double distance) = Handles(area)
                .Select(h => (h.Handle, Distance: Math.Max(Math.Abs(h.X - x), Math.Abs(h.Z - z))))
                .MinBy(h => h.Distance);
            if (distance <= reach)
            {
                return handle;
            }
            Area a = Normalized(area);
            return x >= a.X0 - reach && x <= a.X1 + reach && z >= a.Z0 - reach && z <= a.Z1 + reach ? AreaHandle.Move : AreaHandle.None;
        }

        /// <summary>
        /// The area with what a handle moves moved by (<paramref name="dx"/>, <paramref name="dz"/>)
        /// ogrids; an edge dragged past the other one makes the rectangle turn over, so it stays in order.
        /// </summary>
        public static Area Drag(Area area, AreaHandle handle, double dx, double dz) => Normalized(area with
        {
            X0 = area.X0 + (handle.HasFlag(AreaHandle.Left) ? dx : 0),
            X1 = area.X1 + (handle.HasFlag(AreaHandle.Right) ? dx : 0),
            Z0 = area.Z0 + (handle.HasFlag(AreaHandle.Top) ? dz : 0),
            Z1 = area.Z1 + (handle.HasFlag(AreaHandle.Bottom) ? dz : 0),
        });
    }
}
