using System.Security.Cryptography;
using System.Text;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// The example unit presets that ship with the editor: civilian settlements of the four building
    /// styles (UEF, Aeon, Cybran and Seraphim civilians) from hamlets to cities, farms, an industrial
    /// yard and the like, and small wreck fields, taken from the campaign, coop and vault maps (<c>docs/map-editor/unit-template-picks.json</c>, made with
    /// <c>faf-map dev unit-templates</c>, see <see cref="UnitTemplates"/>). They hold blueprint ids and
    /// places only, with an army role (<see cref="UnitPresetSettings.Role"/>) that picks the army of
    /// the map they are placed on.
    /// </summary>
    /// <remarks>
    /// <see cref="Ensure"/> writes them into a workspace's <c>presets/units</c> once, so every new
    /// workspace starts with them. One the user deletes is not written again: the hidden file
    /// <c>.examples</c> there names the ones written. A copy still equal to an earlier version of its
    /// example (<see cref="Earlier"/>), untouched by the user, is brought up to date; one the user
    /// changed is left alone. An untouched copy of an example that no longer ships (<see cref="Removed"/>)
    /// is deleted.
    /// </remarks>
    public static class ExampleUnitPresets
    {
        private const string Prefix = "ExampleUnitPresets/";
        private const string Written = ".examples";

        /// <summary>
        /// The SHA-256 of earlier versions of the examples' files (lines ending in a line feed), as
        /// <c>file:hash</c>: a copy still equal to one was not changed by the user and is replaced by
        /// the current version. A file changed for a release adds its old hash here.
        /// </summary>
        private static readonly HashSet<string> Earlier = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Wreck field, mixed.json:4c55b6993851a6f60b510d437aaa00ee950b3e378b0716148ea6dfab6f944fc8",
        };

        /// <summary>
        /// The SHA-256 of examples that no longer ship (as <see cref="Earlier"/>): the enemy bases,
        /// defence points, wall rings and players' starts, the walled towns (now without their walls,
        /// under other names) and the wreck fields with walls. A copy still equal to one was not changed
        /// by the user and is deleted; one the user changed stays.
        /// </summary>
        private static readonly HashSet<string> Removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Aeon base, large.json:694ac54d28714d88a552a1007432c94b7d95f7ae0eff57927a08e1e2fea76b7e",
            "Aeon base, medium.json:f28b241834f42181f2a169126c382c32415388623e1048bab1818708b387aa83",
            "Aeon base, small.json:321468bff8b2dec2cd3e80ddac0e1808be253997bf1035d80f035294e47768e8",
            "Aeon bridge defences, walled.json:a5556a37e0aa89eadcf2688e09077e9e196558d605a0a65f52af936ae29c3a0a",
            "Aeon civilian town, walled.json:3f407fd99560c201ae6c2458cc4eba9ca84b49cd772f68acd542d4ea27affb46",
            "Aeon wall ring.json:612b60fe6d93588c2c5f2d869b89a0b7a9cab589d86e74b6f83080cbd17865fe",
            "Civilian wall ring.json:8a0bc3e8ac1c882b4f203852053047c7777b2a79726e463f9e494c47b28f51f2",
            "Cybran base, large.json:2aeb76076c95d502d0507954f90038a2d9f28b9f4826ec1b9e1cefc4fa1d50e4",
            "Cybran base, medium.json:5090f7bd0834bde1a3451ec585c75991713b92c278679ebcb78f0d3f5d25d5c9",
            "Cybran base, small.json:e9f90a78c0c52118b24d0c8334673788c0cc94ca52f13a4d343f40393906f808",
            "Cybran defence camp.json:39b43b5095e5f67e9624d778caca30ed1d45a0198037a97ed1260cc1a688e9bf",
            "Cybran defence point, walled.json:f230cbfb6310a71e4afef1bc7535f2662f278b9e28b5ceb7d22beb395c56884b",
            "Cybran wall ring.json:1cc693ff0e3b815e50418f7deb6e0bfd9a2228263148448c6ec9f6f4aa112a26",
            "Player start, Cybran economy.json:c9fd329920ec63fe855e3717d3ef9f7e5cfe8a9e18f80dd92c23e24f0aef7baf",
            "Player start, UEF outpost.json:6e189fd2ea7d54060f73ce337dad344f5838446b062deff6d345fe2c6b2fcabf",
            "Seraphim base, large.json:b6feed12988a84b3364c313a45f7c8c9d7f4233ba066af71ced2fd18d1820bfc",
            "Seraphim base, medium.json:52ba111990c3bb79f14de2cb9714da7d4c8d1885b4b7c2ca9b463073b13f577a",
            "Seraphim base, small.json:cd92f1c8b7a6349c063e852be0a75aea546646693a63a5ad0b611b04f28a9f43",
            "Seraphim civilian outpost.json:117c3b8a1dfb83904b8c7098e07f9316118ed14aa85b0a7d998bcc7203fb4cde",
            "Seraphim defence point.json:b25fe414ff61ea287566df8cab9aa3ca76b882f0f6c537190baf6d8544d99142",
            "UEF base, large.json:24a4d4910e9a29ca931e92dde4466d9a1e9097f0e0d23c090ac4f7deb40106db",
            "UEF base, medium.json:205eeca8471ba56cfd92d2c0dea1e81998a74ce1d15d04c7cc3bc1db2262c7b8",
            "UEF base, small.json:a764333050ef85449b9d2a17b725beed51a9e343bef3b5cdf3d9d12aa64e2ffd",
            "UEF civilian town, walled.json:ac8028b115dd3bd17db0ac9994bb947150800a36398213db62cb250824d455b9",
            "UEF outpost, walled.json:256f6cdcbe7648026fa10ebd10652d9f71d5c5b6f51a61a801344284a2a696a4",
            "UEF turret camp.json:0327b4321d3542837ad2c0f702ae3b583bf91697b14a0cb604b2e8523489f78a",
            "UEF wall corners.json:40a01c9fab2d9757e9604ebf10a189a30c1f357db729faa5e9ae37a06022f534",
            "UEF wall line.json:49ffdc2d7b697c213e27735ff83debd37406836c90817353401c7cbfa92a1c5b",
            "UEF wall ring.json:d2def4cc617aeeb20137d91b236b90ecc08a3babaa4a38a879874a0de5346db4",
            "Wreck field, Aeon.json:efd7bdc9bfd432c02f5a4345f0b9effcfe540f07b6d900c3c92f594e21b98d48",
            "Wrecked Cybran town.json:5c0508ee51ecaea098afcd7acbd8a1d0e73855bc4abb3cf94c462d62ed43445c",
        };

        /// <summary>
        /// The examples' file names, e.g. <c>Aeon base, small.json</c>.
        /// </summary>
        public static IReadOnlyList<string> Names => [.. Resources().Select(resource => resource.File).Order(StringComparer.Ordinal)];

        /// <summary>
        /// The shipped text of an example's file.
        /// </summary>
        public static string Text(string file)
        {
            string resource = Resources().First(r => r.File == file).Resource;
            using StreamReader reader = new StreamReader(typeof(ExampleUnitPresets).Assembly.GetManifestResourceStream(resource)!);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Writes the examples the workspace never had into its <c>presets/units</c>, brings untouched
        /// copies of earlier versions up to date and deletes untouched copies of examples that no longer ship.
        /// </summary>
        /// <returns>The files written or brought up to date now.</returns>
        public static IReadOnlyList<string> Ensure(Workspace workspace) => Ensure(Path.Combine(workspace.PresetsFolder, PresetKinds.Units));

        /// <summary>
        /// Writes the examples into a folder of units presets (see <see cref="Ensure(Workspace)"/>).
        /// </summary>
        public static IReadOnlyList<string> Ensure(string unitsFolder)
        {
            string written = Path.Combine(unitsFolder, Written);
            HashSet<string> had = new HashSet<string>(File.Exists(written) ? File.ReadAllLines(written) : [], StringComparer.OrdinalIgnoreCase);
            List<string> now = [];
            List<string> recorded = [];
            RemoveRetired(unitsFolder);
            foreach ((string file, _) in Resources())
            {
                string path = Path.Combine(unitsFolder, file);
                string shipped = Text(file);
                if (File.Exists(path))
                {
                    string onDisk = File.ReadAllText(path);
                    if (Normal(onDisk) != Normal(shipped) && Earlier.Contains($"{file}:{Hash(onDisk)}"))
                    {
                        File.WriteAllText(path, shipped);
                        now.Add(file);
                    }
                    if (!had.Contains(file))
                    {
                        recorded.Add(file);
                    }
                    continue;
                }
                if (had.Contains(file))
                {
                    continue;
                }
                Directory.CreateDirectory(unitsFolder);
                File.WriteAllText(path, shipped);
                now.Add(file);
                recorded.Add(file);
            }
            if (recorded.Count > 0)
            {
                File.WriteAllLines(written, [.. had.Concat(recorded).Order(StringComparer.OrdinalIgnoreCase)]);
            }
            return now;
        }

        /// <summary>
        /// Deletes the untouched copies of examples that no longer ship (<see cref="Removed"/>).
        /// </summary>
        /// <returns>The files deleted.</returns>
        public static IReadOnlyList<string> RemoveRetired(string unitsFolder)
        {
            List<string> deleted = [];
            if (!Directory.Exists(unitsFolder))
            {
                return deleted;
            }
            foreach (string file in Removed.Select(entry => entry[..entry.LastIndexOf(':')]).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string path = Path.Combine(unitsFolder, file);
                if (File.Exists(path) && Removed.Contains($"{file}:{Hash(File.ReadAllText(path))}"))
                {
                    File.Delete(path);
                    deleted.Add(file);
                }
            }
            return deleted;
        }

        private static string Normal(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

        private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Normal(text))));

        private static IEnumerable<(string File, string Resource)> Resources() =>
            typeof(ExampleUnitPresets).Assembly.GetManifestResourceNames()
                .Where(resource => resource.StartsWith(Prefix, StringComparison.Ordinal))
                .Select(resource => (resource[Prefix.Length..], resource));
    }
}
