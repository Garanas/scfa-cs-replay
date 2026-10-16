using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project
{
    // The rules of masks (user story K-2) and props (P-4): optional files of the project.
    public static partial class MapProjectStore
    {
        /// <summary>
        /// Writes the rules of the props (<c>prop-rules.json</c>) and removes the rules files of masks
        /// of an older project: the rules of masks live in their stacks
        /// (<see cref="SaveCoverageStacks"/>). A map without prop rules gets no such file.
        /// </summary>
        private static void SaveRules(MapDocument document, string folder)
        {
            string masks = Path.Combine(folder, MapProjectLayout.Masks);
            if (Directory.Exists(masks))
            {
                foreach (string file in Directory.EnumerateFiles(masks, "*" + MapProjectLayout.MaskRulesSuffix).ToList())
                {
                    File.Delete(file);
                }
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
        /// The document with the rules of props of the project's files (the rules of masks are read
        /// with the stacks, <see cref="LoadCoverageStacks"/>).
        /// </summary>
        private static MapDocument WithRules(MapDocument document, string folder)
        {
            string props = Path.Combine(folder, MapProjectLayout.PropRules);
            if (File.Exists(props))
            {
                document = document with { PropRules = [.. ProjectJson.Read<List<PropRuleDto>>(props).Select(MapProjectMapping.ToModel)] };
            }
            return document;
        }

        /// <summary>
        /// The document with a mask's rules (of an older project's file) as its rules layer. Rules of
        /// a mask without its PNG make an empty mask of the map's size: it has not been generated yet.
        /// </summary>
        private static MapDocument WithMaskRules(MapDocument document, string name, MaskRuleSet rules)
        {
            if (!document.Masks.ContainsKey(name))
            {
                document = document with { Masks = document.Masks.Add(name, TiledBuffer<byte>.Create(document.Width, document.Height)) };
            }
            return document.SetMaskRules(name, rules.Rules);
        }

        /// <summary>
        /// The document with a mask's rules read again from an older project's file another tool
        /// wrote; a removed file changes nothing (the rules live in the mask's stack).
        /// </summary>
        private static MapDocument ReloadMaskRules(MapDocument document, string file, string name)
        {
            if (!File.Exists(file))
            {
                return document;
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
