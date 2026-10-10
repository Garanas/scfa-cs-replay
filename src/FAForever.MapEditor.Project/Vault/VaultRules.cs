using System.Text.RegularExpressions;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Vault
{
    /// <summary>
    /// What FAForever's API checks before it takes a map into the vault (user story M-8), checked
    /// here first so a map maker reads the reason before an upload rather than after it. The rules
    /// are those of <c>MapService</c> in <see href="https://github.com/FAForever/faf-java-api/blob/main/src/main/java/com/faforever/api/map/MapService.java">faf-java-api</see>.
    /// </summary>
    public static partial class VaultRules
    {
        /// <summary>The longest upload the API takes (<c>spring.servlet.multipart.max-file-size</c>).</summary>
        public const long MaxUploadBytes = 350L * 1024 * 1024;

        public const int MinNameLength = 4;

        public const int MaxNameLength = 50;

        public const int MaxMinusSigns = 3;

        public const int MaxVersion = 9999;

        /// <summary>
        /// The vault's folder name of a map without its version: its display name in lower case with
        /// spaces as underscores (<c>Theta Passage</c> becomes <c>theta_passage</c>). The API renames
        /// the uploaded folder to it and wants the map's files named after it.
        /// </summary>
        public static string FolderName(string displayName) => displayName.ToLowerInvariant().Replace(' ', '_').Trim();

        /// <summary>
        /// Why the vault would refuse <paramref name="document"/> uploaded as version
        /// <paramref name="version"/>; empty when it would take it as far as the map can tell (the
        /// server checks also that the name is the uploader's and the version is new).
        /// </summary>
        public static IReadOnlyList<string> Check(MapDocument document, int version)
        {
            List<string> reasons = [];
            string name = document.Scenario.Name;
            if (!NameCharacters().IsMatch(name))
            {
                reasons.Add($"The map's name \"{name}\" may hold only letters a to z, digits, spaces and minus signs");
            }
            if (name.Length < MinNameLength || name.Length > MaxNameLength)
            {
                reasons.Add($"The map's name must be {MinNameLength} to {MaxNameLength} characters long; it has {name.Length}");
            }
            if (name.Count(c => c == '-') > MaxMinusSigns)
            {
                reasons.Add($"The map's name may hold at most {MaxMinusSigns} minus signs");
            }
            if (name.Length > 0 && !char.IsAsciiLetter(name[0]))
            {
                reasons.Add("The map's name must start with a letter");
            }
            if (string.IsNullOrWhiteSpace(document.Scenario.Description))
            {
                reasons.Add("The map needs a description (Map, Information)");
            }
            if (version < 1 || version > MaxVersion)
            {
                reasons.Add($"The version must be 1 to {MaxVersion}; it would be {version}");
            }
            string folder = FolderName(name);
            if (!string.Equals(document.Name, folder, StringComparison.OrdinalIgnoreCase) || !string.Equals(document.FileName, folder, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add($"The vault names the map's folder and files after its name: \"{folder}\", but they are \"{document.Name}\" and \"{document.FileName}\". Give the map the name of its folder, or save it under a folder of its name");
            }
            return reasons;
        }

        [GeneratedRegex("^[a-zA-Z0-9\\- ]+$")]
        private static partial Regex NameCharacters();
    }
}
