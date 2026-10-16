using System.Text.Json;

namespace FAForever.MapEditor.Project.Generation
{
    /// <summary>
    /// Which results kept outside the map (walkability) were current when a project was saved, in
    /// <see cref="MapProjectLayout.KeptResults"/>: the editor generates them again when it opens the
    /// project, so they are there as before and "what changed" compares with them. The file's name
    /// starts with a point, so the project's watcher leaves it out.
    /// </summary>
    public static class KeptResults
    {
        /// <summary>
        /// Writes the ids of the current results; none removes the file.
        /// </summary>
        /// <exception cref="IOException">It cannot be written.</exception>
        public static void Write(string projectFolder, IReadOnlyCollection<string> ids)
        {
            string file = Path.Combine(projectFolder, MapProjectLayout.KeptResults);
            if (ids.Count == 0)
            {
                File.Delete(file);
                return;
            }
            File.WriteAllText(file, JsonSerializer.Serialize(new Kept([.. ids.Order(StringComparer.Ordinal)])));
        }

        /// <summary>
        /// The ids written last; none when the file is missing or cannot be read.
        /// </summary>
        public static IReadOnlyList<string> Read(string projectFolder)
        {
            string file = Path.Combine(projectFolder, MapProjectLayout.KeptResults);
            try
            {
                return File.Exists(file) ? JsonSerializer.Deserialize<Kept>(File.ReadAllText(file))?.Generated ?? [] : [];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                return [];
            }
        }

        private sealed record Kept(List<string> Generated);
    }
}
