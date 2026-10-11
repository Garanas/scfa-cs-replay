using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// World Machine and Gaea work through files on disk: the editor writes the draft heightmap and
    /// the masks for tools a map maker ticks into the project's <c>procedural/temp/</c>
    /// (<see cref="WriteDraft"/>); the tools read them as inputs and write their outputs beside them;
    /// an output chosen as the final's base (<see cref="Editing.HeightStacks.UseAsFinalBase"/>) is an
    /// image layer, read again whenever the file changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The layout of <c>procedural/temp/</c>, left out of version control (the inputs follow from
    /// the project, the outputs from the tools' own files in <c>procedural/sources/</c>, and the
    /// final's base keeps its last reading in <c>stacks/heightmap/</c>):
    /// </para>
    /// <list type="bullet">
    /// <item><c>draft-heightmap.gen.png</c>: the draft, 16-bit grey, (width + 1) by (height + 1)
    /// samples, one step 1/128 of a game unit as the map's heightmap.</item>
    /// <item><c>draft-heightmap.gen.r16</c>: the same samples as raw little-endian 16-bit values, row by
    /// row from the north-west corner (what World Machine and Gaea call R16 or RAW16).</item>
    /// <item><c>draft-mask-&lt;name&gt;.gen.png</c>: a ticked mask for tools, 8-bit grey at its own size
    /// (the map's or half of it).</item>
    /// <item>anything else: the tools' outputs (<see cref="Outputs"/>).</item>
    /// </list>
    /// <para>
    /// The editor's files are named so (<c>.gen</c>, <see cref="MapProjectLayout.Generated"/>): it
    /// writes them again whenever the draft or a ticked mask changes. A tool writes its outputs under
    /// names of its own, which the editor never writes. A project of format 6 named the editor's files
    /// without <c>.gen</c>; writing the draft removes those.
    /// </para>
    /// </remarks>
    public static class ProceduralFiles
    {
        /// <summary>
        /// The draft heightmap as a 16-bit PNG, relative to the project folder.
        /// </summary>
        public const string DraftHeightmap = MapProjectLayout.ProceduralTemp + "/draft-heightmap.gen.png";

        /// <summary>
        /// The draft heightmap as raw 16-bit samples, relative to the project folder.
        /// </summary>
        public const string DraftHeightmapRaw = MapProjectLayout.ProceduralTemp + "/draft-heightmap.gen.r16";

        /// <summary>
        /// The draft's files as a project of format 6 named them, removed when the draft is written.
        /// </summary>
        private static readonly string[] Earlier = [MapProjectLayout.ProceduralTemp + "/draft-heightmap.png", MapProjectLayout.ProceduralTemp + "/draft-heightmap.r16"];

        private const string MaskPrefix = "draft-mask-";

        /// <summary>
        /// The extensions of files a tool's output can be: what <see cref="HeightmapFiles"/> reads.
        /// </summary>
        public static IReadOnlyList<string> OutputExtensions { get; } = [".png", ".r16", ".raw", ".tif", ".tiff"];

        /// <summary>
        /// A mask's file beside the draft, relative to the project folder.
        /// </summary>
        public static string DraftMask(string name) => $"{MapProjectLayout.ProceduralTemp}/{MaskPrefix}{name}{MapProjectLayout.GeneratedMark}.png";

        /// <summary>
        /// Whether a file of the project folder is one the editor writes for the tools (an input): one
        /// named <c>.gen</c>, or the draft's files as a project of format 6 named them.
        /// </summary>
        public static bool IsInput(string relative)
        {
            relative = relative.Replace('\\', '/');
            string name = Path.GetFileName(relative);
            return MapProjectLayout.IsGenerated(relative)
                || Earlier.Contains(relative, StringComparer.OrdinalIgnoreCase)
                || (relative.StartsWith(MapProjectLayout.ProceduralTemp + "/", StringComparison.OrdinalIgnoreCase) && name.StartsWith(MaskPrefix, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Writes the draft heightmap (PNG and raw) and the masks named (those the map has) into
        /// <c>procedural/temp/</c>, and removes the files of masks written before that are no longer
        /// asked for.
        /// </summary>
        /// <returns>The files written, relative to the project folder.</returns>
        public static IReadOnlyList<string> WriteDraft(MapDocument document, string folder, IEnumerable<string> masks)
        {
            string temp = Path.Combine(folder, MapProjectLayout.ProceduralTemp);
            Directory.CreateDirectory(temp);
            Heightmap draft = document.DraftHeightmap;
            HeightmapFiles.Write(Path.Combine(folder, DraftHeightmap), draft);
            HeightmapFiles.Write(Path.Combine(folder, DraftHeightmapRaw), draft);
            List<string> written = [DraftHeightmap, DraftHeightmapRaw];
            HashSet<string> wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in masks)
            {
                if (document.Masks.TryGetValue(name, out TiledBuffer<byte>? mask))
                {
                    string file = DraftMask(name);
                    ProjectImages.WriteGray8(Path.Combine(folder, file), mask.Width, mask.Height, mask.ToArray());
                    written.Add(file);
                    wanted.Add(Path.GetFileName(file));
                }
            }
            foreach (string old in Directory.EnumerateFiles(temp, MaskPrefix + "*.png"))
            {
                if (!wanted.Contains(Path.GetFileName(old)))
                {
                    File.Delete(old);
                }
            }
            foreach (string old in Earlier.Select(file => Path.Combine(folder, file)).Where(File.Exists))
            {
                File.Delete(old);
            }
            return written;
        }

        /// <summary>
        /// The tools' outputs in <c>procedural/temp/</c> (and its folders): the image files the
        /// editor does not write itself, newest first, relative to the project folder.
        /// </summary>
        public static IReadOnlyList<string> Outputs(string folder)
        {
            string temp = Path.Combine(folder, MapProjectLayout.ProceduralTemp);
            if (!Directory.Exists(temp))
            {
                return [];
            }
            return [.. Directory.EnumerateFiles(temp, "*", SearchOption.AllDirectories)
                .Where(file => OutputExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                .Select(file => (File: file, Relative: Path.GetRelativePath(folder, file).Replace('\\', '/')))
                .Where(entry => !IsInput(entry.Relative))
                .OrderByDescending(entry => File.GetLastWriteTimeUtc(entry.File))
                .Select(entry => entry.Relative)];
        }
    }
}
