namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// A map or project that cannot be read or written, with a message for the map maker: which
    /// file, and what is wrong with it.
    /// </summary>
    public sealed class MapProjectException(string message, Exception? inner = null) : Exception(message, inner);
}
