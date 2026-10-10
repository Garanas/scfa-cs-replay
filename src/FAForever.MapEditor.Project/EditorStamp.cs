using System.Globalization;
using System.Reflection;
using FAForever.FileFormats.Lua;
using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project
{
    /// <summary>
    /// Which build of the editor this is: the version and the commit it was built from. The SDK
    /// appends <c>SourceRevisionId</c> to the informational version (<c>1.0.0+&lt;sha&gt;</c>): from
    /// git in a local build or CI checkout; a build without <c>.git</c> knows no commit.
    /// </summary>
    public static class EditorBuild
    {
        /// <summary>
        /// The version without the commit, e.g. <c>1.0.0</c>.
        /// </summary>
        public static string Version { get; } = Split().Version;

        /// <summary>
        /// The full commit hash, or null when the build did not know it.
        /// </summary>
        public static string? Commit { get; } = Split().Commit;

        /// <summary>
        /// The first seven characters of <see cref="Commit"/>, as git shows a commit.
        /// </summary>
        public static string? ShortCommit => Commit?[..Math.Min(7, Commit.Length)];

        private static (string Version, string? Commit) Split()
        {
            Assembly assembly = typeof(EditorBuild).Assembly;
            string informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString(3)
                ?? "0.0.0";
            int plus = informational.IndexOf('+');
            return plus < 0
                ? (informational, null)
                : (informational[..plus], plus == informational.Length - 1 ? null : informational[(plus + 1)..]);
        }
    }

    /// <summary>
    /// The editor named in an exported map's <c>_scenario.lua</c>: a comment line at the top and a
    /// table <c>map_editor</c> in <c>ScenarioInfo</c>. The game (<c>lua/ui/maputil.lua</c>,
    /// <c>LoadScenarioInfoFile</c>), the FAF client (<c>MapService.readMap</c>) and the vault's upload
    /// check (<c>MapService.validateScenarioLua</c> of faf-java-api) run the file in Lua and read
    /// only the keys they know, so the extra key and the comment change nothing for them.
    /// </summary>
    /// <param name="Exported">When the map was exported, to the second, in UTC.</param>
    public sealed record EditorStamp(string Name, string Version, string? Commit, int ProjectFormat, DateTimeOffset Exported)
    {
        public const string EditorName = "FAF Map Editor";

        /// <summary>
        /// The key of <c>ScenarioInfo</c> that holds it.
        /// </summary>
        public const string ScenarioKey = "map_editor";

        /// <summary>
        /// This build of the editor, exporting now.
        /// </summary>
        public static EditorStamp Now(TimeProvider? clock = null)
        {
            DateTimeOffset now = (clock ?? TimeProvider.System).GetUtcNow();
            return new EditorStamp(EditorName, EditorBuild.Version, EditorBuild.ShortCommit, MapProjectDto.CurrentFormatVersion,
                new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero));
        }

        /// <summary>
        /// The first line of <c>_scenario.lua</c>, e.g.
        /// <c>-- Made with the FAF Map Editor 1.0.0 (0cb76c2) on 2026-10-10</c>.
        /// </summary>
        public string Comment => $"-- Made with the {Name} {Version} ({Commit ?? "commit unknown"}) on {Exported.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

        /// <summary>
        /// The table <c>map_editor</c>: name, version, commit (left out when unknown), project format
        /// and the time of the export (ISO 8601, UTC).
        /// </summary>
        public LuaData.Table ToLua()
        {
            Dictionary<string, LuaData> fields = new Dictionary<string, LuaData>
            {
                ["name"] = new LuaData.String(Name),
                ["version"] = new LuaData.String(Version),
            };
            if (Commit is not null)
            {
                fields["commit"] = new LuaData.String(Commit);
            }
            fields["project_format"] = new LuaData.Number(ProjectFormat);
            fields["exported"] = new LuaData.String(Exported.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            return new LuaData.Table(fields);
        }
    }
}
