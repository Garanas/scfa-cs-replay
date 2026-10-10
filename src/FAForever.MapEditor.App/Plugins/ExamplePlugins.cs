using System.Reflection;

namespace FAForever.MapEditor.App.Plugins;

/// <summary>
/// The example plugins that ship with the editor (<c>Plugins/Examples</c>, embedded): written into
/// a workspace's <c>plugins</c> folder once, so a new user (and the sandbox workspace of an agent)
/// has plugins to try and read. An example the user deletes is not written again: the hidden file
/// <c>.examples</c> names the ones written.
/// </summary>
public static class ExamplePlugins
{
    private const string Prefix = "Examples/";
    private const string Written = ".examples";

    /// <summary>
    /// The examples' folder names.
    /// </summary>
    public static IReadOnlyList<string> Names => [.. Resources().Select(resource => resource.Folder).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>
    /// Writes the examples the folder never had.
    /// </summary>
    /// <returns>The examples written now.</returns>
    public static IReadOnlyList<string> Ensure(string pluginsFolder)
    {
        Directory.CreateDirectory(pluginsFolder);
        string written = Path.Combine(pluginsFolder, Written);
        HashSet<string> had = new HashSet<string>(File.Exists(written) ? File.ReadAllLines(written) : [], StringComparer.OrdinalIgnoreCase);
        List<string> now = [];
        Assembly assembly = typeof(ExamplePlugins).Assembly;
        foreach (IGrouping<string, (string Folder, string File, string Resource)> example in Resources().GroupBy(resource => resource.Folder))
        {
            if (had.Contains(example.Key) || Directory.Exists(Path.Combine(pluginsFolder, example.Key)))
            {
                continue;
            }
            foreach ((string folder, string file, string resource) in example)
            {
                string path = Path.Combine(pluginsFolder, folder, file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using Stream source = assembly.GetManifestResourceStream(resource)!;
                using FileStream target = File.Create(path);
                source.CopyTo(target);
            }
            now.Add(example.Key);
        }
        if (now.Count > 0)
        {
            File.WriteAllLines(written, [.. had.Concat(now).Order(StringComparer.OrdinalIgnoreCase)]);
        }
        return now;
    }

    /// <summary>
    /// The embedded files: <c>Examples/&lt;folder&gt;/&lt;file&gt;</c>.
    /// </summary>
    private static IEnumerable<(string Folder, string File, string Resource)> Resources() =>
        typeof(ExamplePlugins).Assembly.GetManifestResourceNames()
            .Select(resource => (Resource: resource, Path: resource.Replace('\\', '/')))
            .Where(item => item.Path.StartsWith(Prefix, StringComparison.Ordinal))
            .Select(item =>
            {
                string[] parts = item.Path[Prefix.Length..].Split('/', 2);
                return (parts[0], parts[1], item.Resource);
            });
}
