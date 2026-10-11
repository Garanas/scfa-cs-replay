using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace FAForever.MapEditor.App.Plugins;

/// <summary>
/// The example plugins that ship with the editor (<c>Plugins/Examples</c>, embedded): written into
/// a workspace's <c>plugins</c> folder once, so a new user (and the sandbox workspace of an agent)
/// has plugins to try and read. An example the user deletes is not written again: the hidden file
/// <c>.examples</c> names the ones written. A copy whose files are all an earlier version of the
/// example (<see cref="Earlier"/>), untouched by the user, is brought up to date.
/// </summary>
public static class ExamplePlugins
{
    private const string Prefix = "Examples/";
    private const string Written = ".examples";

    /// <summary>
    /// The SHA-256 of earlier versions of the examples' files (lines ending in a line feed), as
    /// <c>folder/file:hash</c>: a copy still equal to one was not changed by the user and is
    /// replaced by the current version. A file changed for a release adds its old hash here.
    /// </summary>
    private static readonly HashSet<string> Earlier = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Reclaim per army 1.0: warned of a few rocks more or less
        "reclaim-per-army/plugin.lua:c2ea11c2f32ed706b47746183b6892a709eb1b17b4d527b257586ff82240a42d",
        "reclaim-per-army/plugin.json:6df515540bd07a85769c25e23fec332965b6f44baf00786426eb0eed26d11aa8",
        // the manifests before they named their category and kind (wave 14C)
        "plateau-brush/plugin.json:e1f4874b08620267c9ce099d16e555ba6f43a3a7b33e6c89e84bf501ca5f783a",
        "reclaim-per-army/plugin.json:4831774f6230efaa4ccf8e0bb0e84f9e3b777eccabb17de2d237501050344bb5",
        "scatter-rocks/plugin.json:816e06ad53053c77fbd4066e89e67cfa71fc475ae0178c37c186450d89899997",
    };

    /// <summary>
    /// The examples' folder names.
    /// </summary>
    public static IReadOnlyList<string> Names => [.. Resources().Select(resource => resource.Folder).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>
    /// Writes the examples the folder never had, and brings untouched copies of earlier versions up to date.
    /// </summary>
    /// <returns>The examples written or brought up to date now.</returns>
    public static IReadOnlyList<string> Ensure(string pluginsFolder)
    {
        Directory.CreateDirectory(pluginsFolder);
        string written = Path.Combine(pluginsFolder, Written);
        HashSet<string> had = new HashSet<string>(File.Exists(written) ? File.ReadAllLines(written) : [], StringComparer.OrdinalIgnoreCase);
        List<string> now = [];
        Assembly assembly = typeof(ExamplePlugins).Assembly;
        foreach (IGrouping<string, (string Folder, string File, string Resource)> example in Resources().GroupBy(resource => resource.Folder))
        {
            if (Directory.Exists(Path.Combine(pluginsFolder, example.Key)))
            {
                if (Upgrade(pluginsFolder, example.Key, [.. example]))
                {
                    now.Add(example.Key);
                }
                continue;
            }
            if (had.Contains(example.Key))
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
    /// Replaces a copy of an example whose files are each the current version or an earlier one,
    /// at least one earlier: the user never changed it.
    /// </summary>
    /// <returns>Whether it was brought up to date.</returns>
    private static bool Upgrade(string pluginsFolder, string folder, IReadOnlyList<(string Folder, string File, string Resource)> files)
    {
        Assembly assembly = typeof(ExamplePlugins).Assembly;
        List<(string Path, string Text)> newer = [];
        foreach ((_, string file, string resource) in files)
        {
            string path = Path.Combine(pluginsFolder, folder, file);
            using StreamReader reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            string shipped = reader.ReadToEnd();
            string? onDisk = File.Exists(path) ? File.ReadAllText(path) : null;
            if (onDisk is not null && Normal(onDisk) == Normal(shipped))
            {
                continue;
            }
            if (onDisk is null || !Earlier.Contains($"{folder}/{file}:{Hash(onDisk)}"))
            {
                return false;
            }
            newer.Add((path, shipped));
        }
        foreach ((string path, string text) in newer)
        {
            File.WriteAllText(path, text);
        }
        return newer.Count > 0;
    }

    private static string Normal(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Normal(text))));

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
