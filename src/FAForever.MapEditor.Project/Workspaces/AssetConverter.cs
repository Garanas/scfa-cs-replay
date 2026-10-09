using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using FAForever.FileFormats.Blueprints;
using FAForever.FileFormats.Lua;
using FAForever.FileFormats.Mesh;
using FAForever.MapEditor.Project.Workspaces.Serialized;
using ImageMagick;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// How far a conversion is.
    /// </summary>
    /// <param name="Done">Files done, of <paramref name="Total"/>.</param>
    /// <param name="Stage">What it is doing, e.g. <c>Reading the blueprints</c>.</param>
    public sealed record AssetProgress(int Done, int Total, string Stage)
    {
        public double Fraction => Total == 0 ? 0 : (double)Done / Total;
    }

    /// <summary>
    /// The result of a conversion: its manifest, and the files it could not convert.
    /// </summary>
    public sealed record AssetConversion(AssetManifestDto Manifest, IReadOnlyList<string> Skipped);

    /// <summary>
    /// Converts the game's archives (<see cref="GameArchives"/>) into a workspace's assets:
    /// textures (<c>.dds</c>) to PNG with their alpha channel, prop and unit blueprints (<c>.bp</c>)
    /// to the editor's JSON (<see cref="AssetBlueprints"/>), meshes (<c>.scm</c>) to glTF
    /// (<c>.glb</c>, <see cref="GlbWriter"/>) with their skeleton. The
    /// rest (scripts, animations, sounds) is not needed for making maps and left out.
    /// </summary>
    /// <remarks>
    /// The conversion writes into <see cref="WorkspaceLayout.AssetsInProgress"/> and replaces the
    /// assets only when it is done, so the assets are always whole. A conversion that was
    /// stopped goes on where it was: files already there are kept (each is written under another
    /// name first and renamed when complete).
    /// </remarks>
    public static class AssetConverter
    {
        /// <summary>
        /// The version of what the conversion writes. Raise it whenever that changes, so that
        /// every workspace converts again.
        /// </summary>
        public const int FormatVersion = 2;

        /// <summary>
        /// The version of the editor, without the commit it was built from, e.g. <c>1.2.0</c>.
        /// Assets converted by another version are converted again.
        /// </summary>
        public static string EditorVersion { get; } = (typeof(AssetConverter).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0")
            .Split('+')[0];

        /// <summary>
        /// Converts the archives of <paramref name="workspace"/> into its assets.
        /// </summary>
        /// <exception cref="MapProjectException">An archive is missing or unreadable.</exception>
        /// <exception cref="OperationCanceledException">Stopped; the next conversion goes on from here.</exception>
        public static AssetConversion Convert(Workspace workspace, IProgress<AssetProgress>? progress = null, CancellationToken cancellation = default)
        {
            IReadOnlyList<string> paths = workspace.FindArchives();
            string staging = Path.Combine(workspace.Folder, WorkspaceLayout.AssetsInProgress);
            Directory.CreateDirectory(staging);

            progress?.Report(new AssetProgress(0, 0, "Checking the archives"));
            List<ArchiveDto> archives = [.. paths.Select(path =>
            {
                FileInfo file = new FileInfo(path);
                return new ArchiveDto
                {
                    Path = path,
                    Size = file.Length,
                    LastWriteUtc = file.LastWriteTimeUtc,
                    Sha256 = GameArchives.IsFafArchive(path) ? AssetCheck.Sha256(path) : null,
                };
            })];

            ConcurrentBag<string> skipped = [];
            using (GameArchives game = GameArchives.Open(paths))
            {
                List<string> blueprints = [.. game.Files.Where(f => f.EndsWith(".bp", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
                List<string> files = [.. game.Files.Where(f => f.EndsWith(".dds", StringComparison.Ordinal) || f.EndsWith(".scm", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
                int total = blueprints.Count + files.Count;

                progress?.Report(new AssetProgress(0, total, "Reading the blueprints"));
                ConvertBlueprints(game, blueprints, staging, skipped, cancellation);

                int done = blueprints.Count;
                progress?.Report(new AssetProgress(done, total, "Converting the textures and meshes"));
                Parallel.ForEach(files, new ParallelOptions { CancellationToken = cancellation }, file =>
                {
                    string output = Path.Combine(staging, WorkspaceLayout.AssetOf(file).Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(output))
                    {
                        try
                        {
                            byte[] bytes = game.Read(file);
                            if (file.EndsWith(".dds", StringComparison.Ordinal))
                            {
                                WriteComplete(output, part => WritePng(bytes, part));
                            }
                            else
                            {
                                WriteComplete(output, part => File.WriteAllBytes(part, GlbWriter.Write(ScmParser.Parse(bytes))));
                            }
                        }
                        catch (Exception exception) when (exception is MagickException or InvalidDataException or ScmFormatException or ArgumentException)
                        {
                            skipped.Add($"{file}: {exception.Message}");
                        }
                    }
                    progress?.Report(new AssetProgress(Interlocked.Increment(ref done), total, "Converting the textures and meshes"));
                });
            }

            AssetManifestDto manifest = new AssetManifestDto
            {
                FormatVersion = FormatVersion,
                EditorVersion = EditorVersion,
                Converted = DateTimeOffset.Now,
                Archives = archives,
                Textures = Count(staging, "*.png"),
                Meshes = Count(staging, "*.glb"),
                Props = Count(staging, "*_prop.json"),
                Units = Count(staging, "*_unit.json"),
            };
            ProjectJson.Write(Path.Combine(staging, WorkspaceLayout.Manifest), manifest);

            List<string> skippedList = [.. skipped.Order(StringComparer.Ordinal)];
            File.WriteAllLines(Path.Combine(staging, "skipped.txt"), skippedList);

            progress?.Report(new AssetProgress(1, 1, "Replacing the assets"));
            if (Directory.Exists(workspace.AssetsFolder))
            {
                Directory.Delete(workspace.AssetsFolder, recursive: true);
            }
            Directory.Move(staging, workspace.AssetsFolder);
            return new AssetConversion(manifest, skippedList);
        }

        public static Task<AssetConversion> ConvertAsync(Workspace workspace, IProgress<AssetProgress>? progress = null, CancellationToken cancellation = default) =>
            Task.Run(() => Convert(workspace, progress, cancellation), cancellation);

        /// <summary>
        /// Reads every blueprint first, since props and units can name a mesh blueprint of another
        /// file, then writes the props and units.
        /// </summary>
        private static void ConvertBlueprints(GameArchives game, List<string> files, string staging, ConcurrentBag<string> skipped, CancellationToken cancellation)
        {
            ConcurrentBag<(string File, Blueprint Blueprint)> parsed = [];
            Parallel.ForEach(files, new ParallelOptions { CancellationToken = cancellation }, file =>
            {
                try
                {
                    foreach (Blueprint blueprint in BlueprintParser.Parse(Encoding.UTF8.GetString(game.Read(file)), "/" + file))
                    {
                        parsed.Add((file, blueprint));
                    }
                }
                catch (Exception exception) when (exception is LuaSyntaxException or FormatException or InvalidCastException)
                {
                    skipped.Add($"{file}: {exception.Message}");
                }
            });

            Dictionary<string, BlueprintMesh> meshes = [];
            foreach ((_, Blueprint blueprint) in parsed)
            {
                if (blueprint is BlueprintMesh mesh)
                {
                    meshes[mesh.BlueprintId] = mesh;
                }
            }

            foreach (IGrouping<string, (string File, Blueprint Blueprint)> file in parsed.GroupBy(p => p.File))
            {
                cancellation.ThrowIfCancellationRequested();
                object? dto = file.Select(p => p.Blueprint).FirstOrDefault(b => b is BlueprintProp or BlueprintUnit) switch
                {
                    BlueprintProp prop => AssetBlueprints.ToDto(prop, game.Contains, meshes),
                    BlueprintUnit unit => AssetBlueprints.ToDto(unit, game.Contains, meshes),
                    _ => null,
                };
                if (dto is not null)
                {
                    string output = Path.Combine(staging, WorkspaceLayout.AssetOf(file.Key).Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    File.WriteAllText(output, dto is PropBlueprintDto p ? ProjectJson.Serialize(p) : ProjectJson.Serialize((UnitBlueprintDto)dto));
                }
            }
        }

        /// <summary>
        /// A DDS texture as an 8-bit PNG; with an alpha channel when the texture has one.
        /// </summary>
        private static void WritePng(byte[] dds, string path)
        {
            MagickReadSettings settings = new MagickReadSettings { Format = MagickFormat.Dds };
            settings.SetDefine(MagickFormat.Dds, "skip-mipmaps", "true");
            using MagickImage image = new MagickImage(dds, settings);
            image.Depth = 8;
            image.Settings.SetDefine(MagickFormat.Png, "compression-level", "4");
            // .NET writes the file: ImageMagick's own writing fails on paths longer than Windows' old limit
            File.WriteAllBytes(path, image.ToByteArray(MagickFormat.Png));
        }

        /// <summary>
        /// Writes a file under another name, then renames it: a stopped conversion leaves no half files.
        /// </summary>
        private static void WriteComplete(string path, Action<string> write)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string part = path + ".part";
            write(part);
            File.Move(part, path, overwrite: true);
        }

        private static int Count(string folder, string pattern) => Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories).Count();
    }
}
