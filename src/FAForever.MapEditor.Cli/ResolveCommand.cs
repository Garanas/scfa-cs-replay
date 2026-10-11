using System.Text.Json;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Cli
{
    /// <summary>
    /// <c>faf-map dev resolve &lt;map folders...&gt; --workspace &lt;folder&gt;</c>: imports each map
    /// and resolves every texture, decal, cube map, prop and unit it names through the workspace's
    /// assets (<see cref="AssetResolution"/>), printing per kind how many resolve and each that does
    /// not. Only reads; the workspace needs only its <c>assets</c> folder (read-only is fine), which is
    /// how a Linux container checks the lookups on a file system that minds case.
    /// </summary>
    public static class ResolveCommand
    {
        public static int Run(IReadOnlyList<string> maps, string? workspaceFolder, string? output)
        {
            if (workspaceFolder is null)
            {
                Console.Error.WriteLine("Give --workspace: a folder with the converted assets in its assets folder");
                return 2;
            }
            Workspace workspace = Workspace.Exists(workspaceFolder)
                ? Workspace.Open(workspaceFolder)
                : new Workspace { Folder = Path.GetFullPath(workspaceFolder), GameFolder = "", VaultFolder = "" };
            if (!Directory.Exists(workspace.AssetsFolder))
            {
                Console.Error.WriteLine($"{workspace.AssetsFolder} does not exist");
                return 2;
            }
            AssetCatalog catalog = AssetCatalog.Load(workspace.AssetsFolder);
            Console.WriteLine($"{catalog.Entries.Count} assets in {workspace.AssetsFolder}");

            int unresolved = 0;
            List<object> report = [];
            foreach (string folder in maps)
            {
                MapDocument document;
                try
                {
                    document = MapImporter.ImportFolder(folder);
                }
                catch (MapProjectException exception)
                {
                    Console.Error.WriteLine($"{folder}: {exception.Message}");
                    unresolved++;
                    continue;
                }
                IReadOnlyList<AssetLookup> lookups = AssetResolution.Resolve(document, workspace, catalog);
                Console.WriteLine($"{Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))}: {lookups.Count(l => l.Resolved)} of {lookups.Count} assets resolve");
                foreach (IGrouping<string, AssetLookup> kind in lookups.GroupBy(l => l.Kind))
                {
                    Console.WriteLine($"  {kind.Key}: {kind.Count(l => l.Resolved)} of {kind.Count()} ({kind.Sum(l => l.Uses)} uses)");
                }
                foreach (AssetLookup lookup in lookups.Where(l => !l.Resolved))
                {
                    Console.WriteLine($"  unresolved {lookup.Kind} {lookup.Path} ({lookup.Uses}x): {lookup.Reason}");
                }
                unresolved += lookups.Count(l => !l.Resolved);
                report.Add(new { map = folder, lookups });
            }
            if (output is not null)
            {
                File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerOptions.Web) { WriteIndented = true }));
                Console.WriteLine($"Wrote {output}");
            }
            return unresolved == 0 ? 0 : 1;
        }
    }
}
