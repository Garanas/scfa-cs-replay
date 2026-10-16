using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// The stacks of coverage of a project (<see cref="MapDocument.CoverageStacks"/>): per stratum
    /// mask, mask for tools or water map with a stack, <c>stacks/&lt;target&gt;.json</c> and its
    /// layers' PNGs (<c>stacks/&lt;target&gt;/&lt;layer&gt;.png</c>, a painted layer's alpha as
    /// <c>&lt;layer&gt;.coverage.png</c>), beside the composite in its usual file. A grid without a
    /// stack file is its own painted layer, so an older project opens unchanged. The rules of masks of
    /// an older project (<c>masks/&lt;name&gt;.rules.json</c>) become a rules layer of the mask's
    /// stack holding the mask's PNG; the first save writes the stack and removes the rules file.
    /// </summary>
    public static partial class MapProjectStore
    {
        /// <summary>
        /// Writes every stack of coverage and removes the files of stacks the document no longer has.
        /// </summary>
        private static void SaveCoverageStacks(MapDocument document, string folder, MapDocument? previous)
        {
            foreach ((string key, LayerStack stack) in document.CoverageStacks)
            {
                SaveStack(document, stack, folder, previous?.CoverageStacks.GetValueOrDefault(key), null);
            }
            string stacks = Path.Combine(folder, MapProjectLayout.Stacks);
            if (!Directory.Exists(stacks))
            {
                return;
            }
            foreach (string index in Directory.EnumerateFiles(stacks, "*.json").ToList())
            {
                string key = Path.GetFileNameWithoutExtension(index);
                if (key != LayerTarget.Heightmap.Key && !document.CoverageStacks.ContainsKey(key) && LayerTarget.FromKey(key) is LayerTarget target)
                {
                    SaveStack(document, LayerStack.Empty(target), folder, null, null);
                }
            }
        }

        /// <summary>
        /// The document with the project's stacks of coverage (and the rules of masks of an older
        /// project as rules layers), each grid what its stack makes.
        /// </summary>
        /// <exception cref="MapProjectException">A layer's file is missing or does not fit.</exception>
        private static MapDocument LoadCoverageStacks(MapDocument document, string folder)
        {
            string stacks = Path.Combine(folder, MapProjectLayout.Stacks);
            if (Directory.Exists(stacks))
            {
                foreach (string index in Directory.EnumerateFiles(stacks, "*.json"))
                {
                    string key = Path.GetFileNameWithoutExtension(index);
                    if (key == LayerTarget.Heightmap.Key || LayerTarget.FromKey(key) is not LayerTarget target || !CoverageLayers.Exists(document, target))
                    {
                        continue;
                    }
                    LayerStack stack = ReadStack(document, target, folder);
                    if (!stack.IsEmpty)
                    {
                        document = document with { CoverageStacks = document.CoverageStacks.SetItem(key, stack) };
                    }
                }
            }
            document = WithLegacyMaskRules(document, folder);
            return CoverageLayers.Recomposite(document);
        }

        /// <summary>
        /// The document with the rules of masks of an older project (<c>masks/&lt;name&gt;.rules.json</c>)
        /// as a rules layer of each mask that has no stack file: the mask's PNG is what they made last.
        /// </summary>
        private static MapDocument WithLegacyMaskRules(MapDocument document, string folder)
        {
            string masks = Path.Combine(folder, MapProjectLayout.Masks);
            if (!Directory.Exists(masks))
            {
                return document;
            }
            foreach (string file in Directory.EnumerateFiles(masks, "*" + MapProjectLayout.MaskRulesSuffix))
            {
                string name = Path.GetFileName(file)[..^MapProjectLayout.MaskRulesSuffix.Length];
                if (!document.CoverageStacks.ContainsKey(LayerTarget.Mask(name).Key))
                {
                    document = WithMaskRules(document, name, MapProjectMapping.ToModel(ProjectJson.Read<MaskRulesDto>(file)));
                }
            }
            return document;
        }

        /// <summary>
        /// Whether a changed file of the project belongs to a stack of coverage, and which.
        /// </summary>
        private static bool IsCoverageStackFile(string changed, out string key)
        {
            key = "";
            string prefix = MapProjectLayout.Stacks + "/";
            if (!changed.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }
            string rest = changed[prefix.Length..];
            int slash = rest.IndexOf('/');
            key = slash < 0 ? (rest.EndsWith(".json", StringComparison.Ordinal) ? rest[..^".json".Length] : "") : rest[..slash];
            return key.Length > 0 && key != LayerTarget.Heightmap.Key && LayerTarget.FromKey(key) is not null;
        }

        /// <summary>
        /// The document with a stack of coverage read again after a file of it changed; without its
        /// index the stack ends and the grid stays as it is.
        /// </summary>
        private static MapDocument ReloadCoverageStack(MapDocument document, string folder, string key)
        {
            if (LayerTarget.FromKey(key) is not LayerTarget target || !CoverageLayers.Exists(document, target))
            {
                return document;
            }
            LayerStack stack = ReadStack(document, target, folder);
            if (stack.IsEmpty)
            {
                return document.CoverageStacks.ContainsKey(key) ? document with { CoverageStacks = document.CoverageStacks.Remove(key) } : document;
            }
            return document.CoverageStacks.TryGetValue(key, out LayerStack? held) && held.Equals(stack)
                ? document
                : document with { CoverageStacks = document.CoverageStacks.SetItem(key, stack) };
        }
    }
}
