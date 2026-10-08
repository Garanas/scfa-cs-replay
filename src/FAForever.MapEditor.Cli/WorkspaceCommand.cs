using System.Diagnostics;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Cli
{
    /// <summary>
    /// <c>faf-map workspace init|convert|status [folder]</c>: the editor's first-start setup without the editor.
    /// </summary>
    internal static class WorkspaceCommand
    {
        public static void Run(WorkspaceOptions o)
        {
            switch (o.Action)
            {
                case "init":
                    Init(o);
                    break;
                case "convert":
                    Convert(Open(o.Folder));
                    break;
                case "status":
                    Status(Open(o.Folder));
                    break;
                default:
                    throw new MapProjectException($"There is no workspace action '{o.Action}': init, convert or status");
            }
        }

        private static void Init(WorkspaceOptions o)
        {
            string folder = o.Folder ?? throw new MapProjectException("Name the folder of the workspace");
            FafInstallation? faf = FafInstallation.Find();
            string game = o.GameFolder ?? faf?.GameFolder
                ?? throw new MapProjectException("FAF's fa_path.lua names no game folder: pass --game");
            string vault = o.VaultFolder ?? faf?.VaultFolder
                ?? throw new MapProjectException("FAF's fa_path.lua names no vault: pass --vault");

            Workspace workspace = Workspace.Create(folder, game, o.FafDataFolder ?? faf?.DataFolder, vault);
            UserSettings.Default.SetWorkspace(workspace.Folder);
            Console.WriteLine($"Set up the workspace in {workspace.Folder}");
            Console.WriteLine($"  game  {workspace.GameFolder}");
            Console.WriteLine($"  FAF   {workspace.FafDataFolder ?? "(none)"}");
            Console.WriteLine($"  vault {workspace.VaultFolder}");
            Console.WriteLine("Convert the game's assets with: faf-map workspace convert");
        }

        private static Workspace Open(string? folder)
        {
            folder ??= UserSettings.Default.Workspace
                ?? throw new MapProjectException("No workspace is set up: faf-map workspace init <folder>");
            return Workspace.Open(folder);
        }

        private static void Status(Workspace workspace)
        {
            AssetCheck check = workspace.CheckAssets();
            Console.WriteLine($"Workspace {workspace.Folder}");
            Console.WriteLine($"Assets: {check.State}");
            foreach (string reason in check.Reasons)
            {
                Console.WriteLine($"  {reason}");
            }
            if (check.Manifest is { } manifest)
            {
                Console.WriteLine($"Converted {manifest.Converted:yyyy-MM-dd HH:mm} by editor {manifest.EditorVersion}: " +
                    $"{manifest.Textures} textures, {manifest.Meshes} meshes, {manifest.Props} props, {manifest.Units} units");
            }
        }

        private static void Convert(Workspace workspace)
        {
            using CancellationTokenSource stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                stop.Cancel();
            };

            Stopwatch clock = Stopwatch.StartNew();
            long shown = -1;
            Progress progress = new Progress(p =>
            {
                long second = clock.ElapsedMilliseconds / 1000;
                if (second != Interlocked.Exchange(ref shown, second))
                {
                    Console.WriteLine($"{clock.Elapsed:mm\\:ss} {p.Stage}: {p.Done} of {p.Total}");
                }
            });

            try
            {
                AssetConversion conversion = AssetConverter.Convert(workspace, progress, stop.Token);
                Console.WriteLine($"Converted in {clock.Elapsed:mm\\:ss}: {conversion.Manifest.Textures} textures, {conversion.Manifest.Meshes} meshes, " +
                    $"{conversion.Manifest.Props} props, {conversion.Manifest.Units} units; {conversion.Skipped.Count} files skipped (assets/skipped.txt)");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Stopped; the next conversion goes on from here.");
            }
        }

        /// <summary>
        /// Reports on the converting thread, unlike <see cref="Progress{T}"/>, which needs a synchronization context to be orderly.
        /// </summary>
        private sealed class Progress(Action<AssetProgress> report) : IProgress<AssetProgress>
        {
            public void Report(AssetProgress value) => report(value);
        }
    }
}
