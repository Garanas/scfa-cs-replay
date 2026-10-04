
namespace FAForever.FileFormats.Replay
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="Name">The name of the map.</param>
    /// <param name="Description">The description of the map.</param>
    /// <param name="SCMapReference">The (local) path to the map. Note that the game mounts folders and files that may have a different starting point then a regular local path.</param>
    /// <param name="PreviewReference">The (local) path to the preview. Is optional, if not provided then the baked-in preview of the binary scmap is used instead.</param>
    /// <param name="Repository">A URL that points to a repository.</param>
    /// <param name="SizeX">The size of the map over the in-game x axis. A value of 1 corresponds to the size of a wall. A value of 8 corresponds to the size of a factory.</param>
    /// <param name="SizeZ">The size of the map over the in-game z axis. Note that the y-axis is up/down. A value of 1 corresponds to the size of a wall. A value of 8 corresponds to the size of a factory.</param>
    public record ReplayScenarioMap(string? Name, string? Description, string? SCMapReference, string? PreviewReference, string? Repository, int? Version, int? SizeX, int? SizeZ, int? MassReclaim, int? EnergyReclaim)
    {
        /// <summary>
        /// The name with the game's localisation marker stripped, e.g.
        /// "&lt;LOC SCMP_026&gt;Vya-3 Protectorate" becomes "Vya-3 Protectorate".
        /// </summary>
        public string? DisplayName => StripLocalisationTag(Name);

        /// <summary>
        /// The description with the game's localisation marker stripped.
        /// </summary>
        public string? DisplayDescription => StripLocalisationTag(Description);

        /// <summary>
        /// Strips the game's localisation marker ("&lt;LOC key&gt;fallback text") and returns
        /// the trimmed fallback text.
        /// </summary>
        public static string? StripLocalisationTag(string? text)
        {
            if (text is null)
            {
                return null;
            }

            if (text.StartsWith("<LOC ", StringComparison.OrdinalIgnoreCase) && text.IndexOf('>') is int end and >= 0)
            {
                return text[(end + 1)..].Trim();
            }

            return text.Trim();
        }
    }
}
