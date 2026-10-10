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
                case "scratch":
                    Scratch(o);
                    break;
                case "remove-scratch":
                    ScratchWorkspace.Delete(o.Folder ?? throw new MapProjectException("Name the folder of the scratch workspace"));
                    Console.WriteLine($"Removed {o.Folder}; the assets it linked to stay");
                    break;
                default:
                    throw new MapProjectException($"There is no workspace action '{o.Action}': init, convert, status, scratch or remove-scratch");
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
            // --settings: another settings file remembers it, and the user's own stays as it is
            UserSettings settings = o.Settings is string file ? new UserSettings(Path.GetFullPath(file)) : UserSettings.Default;
            settings.SetWorkspace(workspace.Folder);
            Console.WriteLine($"Set up the workspace in {workspace.Folder}");
            Console.WriteLine($"  remembered in {settings.File}");
            Console.WriteLine($"  game  {workspace.GameFolder}");
            Console.WriteLine($"  FAF   {workspace.FafDataFolder ?? "(none)"}");
            Console.WriteLine($"  vault {workspace.VaultFolder}");
            Console.WriteLine("Convert the game's assets with: faf-map workspace convert");
        }

        /// <summary>
        /// A workspace for a test editor that may write (projects, presets, brushes, exports), with
        /// the assets of another one linked in.
        /// </summary>
        private static void Scratch(WorkspaceOptions o)
        {
            string folder = o.Folder ?? throw new MapProjectException("Name the folder of the scratch workspace");
            Workspace source = Open(o.From);
            (Workspace workspace, ScratchWorkspace.AssetLink link) = ScratchWorkspace.Create(folder, source, o.ConvertInto);
            Console.WriteLine($"Made the scratch workspace {workspace.Folder}");
            Console.WriteLine($"  assets  {link switch
            {
                ScratchWorkspace.AssetLink.Junction => "a junction to",
                ScratchWorkspace.AssetLink.SymbolicLink => "a link to",
                ScratchWorkspace.AssetLink.Copy => "copied from",
                _ => "none: there are none in",
            }} {(o.ConvertInto is string into ? Path.GetFullPath(into) : source.AssetsFolder)}");
            if (o.ConvertInto is not null)
            {
                // the assets of its own, converted from the game unless they are current already
                AssetCheck check = workspace.CheckAssets();
                if (check.State == AssetState.Current)
                {
                    Console.WriteLine("  the assets there are current: nothing to convert");
                }
                else
                {
                    Console.WriteLine($"  converting the assets ({string.Join("; ", check.Reasons)})");
                    Convert(workspace);
                    SandboxMap.Ensure(workspace);
                }
            }
            Console.WriteLine($"  vault   {workspace.VaultFolder} (exports go here)");
            Console.WriteLine($"  sandbox {SandboxMap.FolderOf(workspace)}");
            if (o.Settings is string settings)
            {
                new UserSettings(Path.GetFullPath(settings)).SetWorkspace(workspace.Folder);
                Console.WriteLine($"  settings {Path.GetFullPath(settings)}: FAF_MAP_EDITOR_SETTINGS={Path.GetFullPath(settings)}");
            }
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
