using System.Collections.Immutable;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project
{
    // The rules of masks (user story K-2) and props (P-4): optional files of the project.
    public static partial class MapProjectStore
    {
        /// <summary>
        /// Writes the rules of the masks (<c>masks/&lt;name&gt;.rules.json</c>) and of the props
        /// (<c>prop-rules.json</c>), and removes the files of rules the document no longer has. A map
        /// without rules gets no such files.
        /// </summary>
        private static void SaveRules(MapDocument document, string folder)
        {
            string masks = Path.Combine(folder, MapProjectLayout.Masks);
            if (Directory.Exists(masks))
            {
                foreach (string file in Directory.EnumerateFiles(masks, "*" + MapProjectLayout.MaskRulesSuffix).ToList())
                {
                    string name = Path.GetFileName(file)[..^MapProjectLayout.MaskRulesSuffix.Length];
                    if (!document.MaskRules.ContainsKey(name))
                    {
                        File.Delete(file);
                    }
                }
            }
            foreach ((string name, MaskRuleSet rules) in document.MaskRules)
            {
                Directory.CreateDirectory(masks);
                ProjectJson.Write(Path.Combine(folder, MapProjectLayout.MaskRules(name)), MapProjectMapping.ToDto(rules));
            }

            string props = Path.Combine(folder, MapProjectLayout.PropRules);
            if (document.PropRules.IsEmpty)
            {
                File.Delete(props);
            }
            else
            {
                ProjectJson.Write(props, document.PropRules.Select(MapProjectMapping.ToDto).ToList());
            }
        }

        /// <summary>
        /// The document with the rules of the project's files. Rules of a mask without its PNG make
        /// an empty mask of the map's size: it has not been generated yet.
        /// </summary>
        private static MapDocument WithRules(MapDocument document, string folder)
        {
            string masks = Path.Combine(folder, MapProjectLayout.Masks);
            if (Directory.Exists(masks))
            {
                foreach (string file in Directory.EnumerateFiles(masks, "*" + MapProjectLayout.MaskRulesSuffix))
                {
                    string name = Path.GetFileName(file)[..^MapProjectLayout.MaskRulesSuffix.Length];
                    document = WithMaskRules(document, name, MapProjectMapping.ToModel(ProjectJson.Read<MaskRulesDto>(file)));
                }
            }
            string props = Path.Combine(folder, MapProjectLayout.PropRules);
            if (File.Exists(props))
            {
                document = document with { PropRules = [.. ProjectJson.Read<List<PropRuleDto>>(props).Select(MapProjectMapping.ToModel)] };
            }
            return document;
        }

        private static MapDocument WithMaskRules(MapDocument document, string name, MaskRuleSet rules)
        {
            ImmutableSortedDictionary<string, TiledBuffer<byte>> masks = document.Masks;
            if (!masks.ContainsKey(name))
            {
                masks = masks.Add(name, TiledBuffer<byte>.Create(document.Width, document.Height));
            }
            return document with { Masks = masks, MaskRules = document.MaskRules.SetItem(name, rules) };
        }

        /// <summary>
        /// The document with a mask's rules read again, or (the file removed) without them.
        /// </summary>
        private static MapDocument ReloadMaskRules(MapDocument document, string file, string name)
        {
            if (!File.Exists(file))
            {
                return document.MaskRules.ContainsKey(name) ? document with { MaskRules = document.MaskRules.Remove(name) } : document;
            }
            if (document.MaskRules.TryGetValue(name, out MaskRuleSet? held))
            {
                return ReloadJson(document, file, MapProjectMapping.ToDto(held), (d, rules) => WithMaskRules(d, name, MapProjectMapping.ToModel(rules)));
            }
            return WithMaskRules(document, name, MapProjectMapping.ToModel(ProjectJson.Read<MaskRulesDto>(file)));
        }

        /// <summary>
        /// The document with the rules of props read again, or (the file removed) without any.
        /// </summary>
        private static MapDocument ReloadPropRules(MapDocument document, string file)
        {
            if (!File.Exists(file))
            {
                return document.PropRules.IsEmpty ? document : document with { PropRules = [] };
            }
            return ReloadJson(document, file, document.PropRules.Select(MapProjectMapping.ToDto).ToList(),
                (d, rules) => d with { PropRules = [.. rules.Select(MapProjectMapping.ToModel)] });
        }
    }
}
