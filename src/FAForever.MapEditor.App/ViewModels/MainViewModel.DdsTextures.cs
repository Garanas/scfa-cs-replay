using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Files.InMemory;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The export step that converts the map's own textures in other formats (PNG, JPEG, TGA, ...) to
/// DDS and points every reference at them (<see cref="TextureExport"/>), in the version the export
/// writes only: the project keeps its pictures.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The id of the export step that converts textures to DDS.
    /// </summary>
    public const string DdsTexturesExportStep = "ddsTextures";

    private void InitializeDdsTextures()
    {
        Export.Register(new ExportStep(DdsTexturesExportStep, "Convert the map's own textures to DDS", ExportStep.TexturesOrder)
        {
            Applies = run =>
            {
                int pending = TextureExport.Pending(run.Document).Count;
                run.Note = pending == 0 ? "every texture is DDS" : null;
                return pending > 0;
            },
            Run = async run =>
            {
                MapDocument document = run.Document;
                (MapDocument exported, IReadOnlyList<ConvertedTexture> converted) = await Task.Run(() =>
                {
                    using Performance.Span measure = Performance.Measure("model.dds-textures");
                    MapDocument made = TextureExport.Apply(document, out IReadOnlyList<ConvertedTexture> list);
                    return (made, list);
                });
                run.Document = exported;
                run.Note = DescribeConverted(converted);
                Logger("Files").LogInformation("Converted the map's own textures to DDS: {Textures}",
                    string.Join("; ", converted.Select(texture => $"{texture.Source} to {texture.Target} ({texture.Format}{(texture.Cached ? ", as before" : "")})")));
            },
        });
    }

    /// <summary>
    /// The note of the step: how many textures, how many came from the cache, and their names.
    /// </summary>
    private static string DescribeConverted(IReadOnlyList<ConvertedTexture> converted)
    {
        int cached = converted.Count(texture => texture.Cached);
        string names = string.Join(", ", converted.Take(4).Select(texture => Path.GetFileName(texture.Source))) + (converted.Count > 4 ? ", ..." : "");
        return $"{Units(converted.Count, "texture")} converted{(cached > 0 ? $" ({cached} as before)" : "")}: {names}";
    }
}
