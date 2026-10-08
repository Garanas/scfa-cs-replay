using System.Text.RegularExpressions;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// The name of a map's folder in the game and the vault: the map's name, then <c>.v</c> and its
    /// version in four digits (<c>theta_passage.v0004</c>). A map without a version has the bare name.
    /// </summary>
    /// <param name="Name">The name without the version, e.g. <c>theta_passage</c>.</param>
    /// <param name="Version">The version (<c>map_version</c> in the <c>_scenario.lua</c>), if any.</param>
    public sealed partial record MapFolderName(string Name, int? Version)
    {
        public override string ToString() => Version is int version ? $"{Name}.v{version:0000}" : Name;

        /// <summary>
        /// Splits a folder name into the name and the version.
        /// </summary>
        public static MapFolderName Parse(string folder)
        {
            Match match = VersionSuffix().Match(folder);
            return match.Success
                ? new MapFolderName(folder[..match.Index], int.Parse(match.Groups[1].Value))
                : new MapFolderName(folder, null);
        }

        /// <summary>
        /// Points the paths into this map's folder (<c>/maps/theta_passage.v0003/...</c>, or without a
        /// version) at this folder, with this version. Paths into other maps are left alone.
        /// </summary>
        public string RewritePaths(string text) =>
            Regex.Replace(text, $@"/maps/{Regex.Escape(Name)}(\.v\d+)?/", $"/maps/{this}/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        [GeneratedRegex(@"\.v(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex VersionSuffix();
    }
}
