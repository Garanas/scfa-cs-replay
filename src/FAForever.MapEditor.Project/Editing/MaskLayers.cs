using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Adds, renames and removes the masks for tools (<see cref="MapDocument.Masks"/>, user story
    /// K-1). A name is also a file name (<c>masks/&lt;name&gt;.png</c>): letters, digits, spaces,
    /// dashes and underscores, and no two names that differ only in case.
    /// </summary>
    public static class MaskLayers
    {
        /// <summary>
        /// The longest name a mask may have.
        /// </summary>
        public const int MaxNameLength = 64;

        /// <summary>
        /// Why a name cannot be a new mask's name in the document, or null when it can.
        /// </summary>
        /// <param name="except">A mask the name may equal (one being renamed).</param>
        public static string? Problem(MapDocument document, string name, string? except = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "A mask needs a name";
            }
            if (name.Length > MaxNameLength)
            {
                return $"A mask's name has at most {MaxNameLength} characters";
            }
            if (name != name.Trim())
            {
                return "A mask's name does not start or end with a space";
            }
            if (!name.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_'))
            {
                return $"'{name}' is no name for a mask: use letters, digits, spaces, dashes and underscores";
            }
            if (document.Masks.Keys.FirstOrDefault(other => other.Equals(name, StringComparison.OrdinalIgnoreCase) && other != except) is string taken)
            {
                return $"The map has a mask '{taken}' already";
            }
            return null;
        }

        /// <summary>
        /// A name no mask has: <paramref name="stem"/>, else with the first free number after it.
        /// </summary>
        public static string FreeName(MapDocument document, string stem = "Mask")
        {
            if (Problem(document, stem) is null)
            {
                return stem;
            }
            for (int number = 2; ; number++)
            {
                string name = $"{stem} {number}";
                if (Problem(document, name) is null)
                {
                    return name;
                }
            }
        }

        /// <summary>
        /// The document with a new, empty mask.
        /// </summary>
        /// <param name="half">Half the map's size (one pixel per two by two ogrids, as the stratum
        /// masks mostly are), else the map's size.</param>
        /// <exception cref="ArgumentException">The name cannot be a mask's (<see cref="Problem"/>).</exception>
        public static MapDocument Add(MapDocument document, string name, bool half = false)
        {
            if (Problem(document, name) is string problem)
            {
                throw new ArgumentException(problem, nameof(name));
            }
            int width = half ? Math.Max(1, document.Width / 2) : document.Width;
            int height = half ? Math.Max(1, document.Height / 2) : document.Height;
            return document with { Masks = document.Masks.Add(name, TiledBuffer<byte>.Create(width, height)) };
        }

        /// <summary>
        /// The document with a mask under another name, its rules (user story K-2) with it and the
        /// rules that read it reading the new name.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such mask, or the new name cannot be a mask's.</exception>
        public static MapDocument Rename(MapDocument document, string name, string newName)
        {
            if (!document.Masks.TryGetValue(name, out TiledBuffer<byte>? mask))
            {
                throw new ArgumentException($"The map has no mask '{name}'", nameof(name));
            }
            if (Problem(document, newName, except: name) is string problem)
            {
                throw new ArgumentException(problem, nameof(newName));
            }
            return RuleEdits.MaskRenamed(document with { Masks = document.Masks.Remove(name).Add(newName, mask) }, name, newName);
        }

        /// <summary>
        /// The document without a mask and its rules.
        /// </summary>
        /// <exception cref="ArgumentException">There is no such mask.</exception>
        public static MapDocument Remove(MapDocument document, string name) =>
            document.Masks.ContainsKey(name)
                ? RuleEdits.MaskRemoved(document with { Masks = document.Masks.Remove(name) }, name)
                : throw new ArgumentException($"The map has no mask '{name}'", nameof(name));

        /// <summary>
        /// Whether a mask of this size fits the map: its size or half of it.
        /// </summary>
        public static bool Fits(int mapWidth, int mapHeight, int width, int height) =>
            (width == mapWidth && height == mapHeight) || (width == Math.Max(1, mapWidth / 2) && height == Math.Max(1, mapHeight / 2));
    }
}
