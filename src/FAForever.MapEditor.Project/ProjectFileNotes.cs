using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// Who writes a layer's file and the file of the grid a stack makes, in words for the inspector
    /// (<see cref="MapProjectLayout"/>, "who writes a file shows in its name"): the editor's own
    /// (<c>.gen</c>, made again from other things, edits to it lost), the map's own (painted in the
    /// editor, saved by another tool and read back), or a tool's (read by an image layer, never written).
    /// </summary>
    public static class ProjectFileNotes
    {
        /// <summary>
        /// Where a layer's values are kept and who writes them, e.g.
        /// <c>Its file: stacks/stratum-2/Rocks.gen.png, the editor's: made again from its rules ...</c>.
        /// </summary>
        /// <param name="target">The stack's target.</param>
        public static string OfLayer(LayerTarget target, Layer layer)
        {
            string file = MapProjectLayout.StackLayerFile(target.Key, layer);
            return layer.Source switch
            {
                RulesSource => $"Its file: {file}, the editor's: made again from its rules whenever they or what they read change; what another tool saves into it is lost",
                DecalMasksSource => $"Its file: {file}, the editor's: made again from the placed decal sets' masks whenever they change; what another tool saves into it is lost",
                ImageSource { Path: string path } when Path.IsPathRooted(path) =>
                    $"Reads {path}, outside the project: the editor never writes it and reads it again when the project opens; its last reading is kept in {file}",
                ImageSource { Path: string path } =>
                    $"Reads {path}, the tool's file: the editor never writes it, and reads it again whenever it changes",
                MaskRefSource reference => $"No file: it reads {LayerStacks.MaskTitle(reference.Key)}",
                CurveSource => "No file: made from its curve, kept with the stack's settings",
                _ => $"Its file: {file}, the map's own: the editor writes what is painted, and reads back what another tool saves into it",
            };
        }

        /// <summary>
        /// The file of a target's grid and who writes it: the editor's while a stack makes it, else the
        /// map's own (one painted layer, no stack yet).
        /// </summary>
        public static string OfResult(MapDocument document, LayerTarget target)
        {
            string file = MapProjectLayout.GridFile(document, target);
            return MapProjectLayout.IsMadeByStack(document, target)
                ? $"Makes {file}, the editor's: written again whenever a layer changes; what another tool saves into it is lost, so change a layer's file instead"
                : $"Saved as {file}, the map's own: painted here, or saved by another tool and read back";
        }
    }
}
