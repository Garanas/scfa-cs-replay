using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace FAForever.MapEditor.Cli
{
    /// <summary>
    /// <c>faf-map editor start</c> and <c>stop</c>: a test editor with automation on a port, started
    /// with its own settings file and waited for until it is ready to be driven, and stopped until its
    /// process has exited (so its build output is no longer locked).
    /// </summary>
    public static class EditorProcess
    {
        /// <returns>0 when the editor is ready, 1 when it failed or did not get ready in time.</returns>
        public static int Start(EditorOptions o)
        {
            if (State(o.Port) is not null)
            {
                Console.Error.WriteLine($"An editor already answers on port {o.Port}: stop it first (faf-map editor stop --port {o.Port})");
                return 1;
            }
            string? exe = FindEditor(o.Exe);
            if (exe is null)
            {
                Console.Error.WriteLine("Cannot find faf-map-editor: pass --exe or set FAF_MAP_EDITOR_EXE");
                return 1;
            }

            if (!o.NoShadow)
            {
                // run from a copy, so a build can write the output while this editor runs
                string shadow = Path.GetFullPath(o.Shadow ?? ShadowCopy.DefaultFolder(o.Port));
                try
                {
                    (string copy, int copied) = ShadowCopy.Prepare(exe, shadow);
                    Console.Error.WriteLine($"Runs from a copy of the build in {shadow} ({copied} files copied); stop it with faf-map editor stop --port {o.Port}");
                    exe = copy;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"Cannot copy the build into {shadow}: {exception.Message} (an editor still runs from it? --no-shadow runs the build itself)");
                    return 1;
                }
            }

            // what restart starts again with: the build itself (not the copy), the settings, the map
            Remember(o, FindEditor(o.Exe)!);

            ProcessStartInfo start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            if (o.Map is string map)
            {
                start.ArgumentList.Add(Path.GetFullPath(map));
            }
            start.ArgumentList.Add("--automation");
            start.ArgumentList.Add(o.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (o.Settings is string settings)
            {
                start.Environment["FAF_MAP_EDITOR_SETTINGS"] = Path.GetFullPath(settings);
            }
            // detached: its output goes to the log, so a caller reading this tool's output to the end
            // is not kept waiting by the editor holding the same pipe
            string log = LogFile(o);
            using Process process = DetachedProcess.Start(start, log);
            Console.Error.WriteLine($"Started {exe} (process {process.Id}) on port {o.Port}; its output goes to {log}");
            if (o.NoWait)
            {
                return 0;
            }

            Stopwatch clock = Stopwatch.StartNew();
            string waiting = "the editor to answer";
            while (clock.Elapsed < TimeSpan.FromSeconds(o.Timeout))
            {
                if (process.HasExited)
                {
                    Console.Error.WriteLine($"The editor exited with code {process.ExitCode} before it was ready");
                    return 1;
                }
                JsonObject? state = State(o.Port);
                waiting = state is null ? "the editor to answer" : Waiting(state);
                if (waiting.Length == 0)
                {
                    Console.WriteLine(new JsonObject
                    {
                        ["pid"] = process.Id,
                        ["port"] = o.Port,
                        ["map"] = state!["map"]?["name"]?.DeepClone(),
                        ["projectFolder"] = state["projectFolder"]?.DeepClone(),
                        ["workspace"] = state["workspace"]?.DeepClone(),
                        ["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 1),
                        ["log"] = log,
                        ["exe"] = exe,
                    }.ToJsonString());
                    return 0;
                }
                Thread.Sleep(250);
            }
            Console.Error.WriteLine($"The editor was not ready after {o.Timeout} s: waiting for {waiting}");
            return 1;
        }

        /// <summary>
        /// <c>faf-map editor restart</c>: stops the editor on the port and starts it again with the
        /// arguments it was started with (<see cref="StartFile"/>), each option given now taking the
        /// place of the remembered one. The copy it runs from is brought up to date first, so the
        /// restarted editor runs the newest build.
        /// </summary>
        /// <returns>As <see cref="Start"/>; 1 when the editor would not stop.</returns>
        public static int Restart(EditorOptions o)
        {
            EditorOptions again = WithRemembered(o);
            if (Stop(again) != 0)
            {
                return 1;
            }
            return Start(again);
        }

        /// <summary>
        /// The file that remembers what the editor on a port was started with: in the temporary
        /// folder, beside the shadow copies.
        /// </summary>
        public static string StartFile(int port) =>
            Path.Combine(Path.GetTempPath(), "faf-map-editor-shadow", $"start-{port.ToString(System.Globalization.CultureInfo.InvariantCulture)}.json");

        /// <summary>
        /// The options of <paramref name="o"/>, with what the last start on its port remembered for
        /// each one left out.
        /// </summary>
        public static EditorOptions WithRemembered(EditorOptions o)
        {
            JsonObject? remembered = null;
            try
            {
                string file = StartFile(o.Port);
                remembered = File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file)) as JsonObject : null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                Console.Error.WriteLine($"Cannot read what the editor on port {o.Port} was started with: {exception.Message}");
            }
            if (remembered is null)
            {
                Console.Error.WriteLine($"No start on port {o.Port} is remembered: the options given are used");
                return o;
            }
            string? Text(string key) => remembered[key]?.GetValue<string>();
            return new EditorOptions
            {
                Command = "start",
                Arguments = o.Arguments,
                Port = o.Port,
                Settings = o.Settings ?? Text("settings"),
                Map = o.Map ?? Text("map"),
                Exe = o.Exe ?? Text("exe"),
                NoWait = o.NoWait,
                Timeout = o.Timeout,
                Force = o.Force,
                Shadow = o.Shadow ?? Text("shadow"),
                NoShadow = o.NoShadow || remembered["noShadow"]?.GetValue<bool>() == true,
                Log = o.Log ?? Text("log"),
            };
        }

        private static void Remember(EditorOptions o, string exe)
        {
            JsonObject start = new JsonObject
            {
                ["exe"] = exe,
                ["settings"] = o.Settings is string settings ? Path.GetFullPath(settings) : null,
                ["map"] = o.Map is string map ? Path.GetFullPath(map) : null,
                ["shadow"] = o.Shadow is string shadow ? Path.GetFullPath(shadow) : null,
                ["noShadow"] = o.NoShadow,
                ["log"] = o.Log is string log ? Path.GetFullPath(log) : null,
            };
            try
            {
                string file = StartFile(o.Port);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, start.ToJsonString());
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Cannot remember the start for restart: {exception.Message}");
            }
        }

        /// <returns>0 when no editor runs on the port any more, 1 when it is still running.</returns>
        public static int Stop(EditorOptions o)
        {
            JsonObject? state = State(o.Port);
            if (state is null)
            {
                Console.Error.WriteLine($"No editor answers on port {o.Port}");
                return 0;
            }
            Process? process = null;
            if (state["pid"]?.GetValue<int>() is int pid)
            {
                try
                {
                    process = Process.GetProcessById(pid);
                }
                catch (ArgumentException)
                {
                    // gone already
                }
            }
            Send(o.Port, "close");
            using (process)
            {
                if (process is null)
                {
                    return 0;
                }
                if (!WaitForExit(process, TimeSpan.FromSeconds(o.Timeout)))
                {
                    if (!o.Force)
                    {
                        Console.Error.WriteLine($"The editor (process {process.Id}) did not exit within {o.Timeout} s; --force ends it");
                        return 1;
                    }
                    process.Kill(entireProcessTree: true);
                    bool ended = WaitForExit(process, TimeSpan.FromSeconds(10));
                    Console.Error.WriteLine($"Ended the editor (process {process.Id})");
                    return ended ? 0 : 1;
                }
                Console.Error.WriteLine($"The editor (process {process.Id}) exited");
                return 0;
            }
        }

        /// <summary>
        /// Waits until the process has exited, or the time is up; whether it exited.
        /// </summary>
        private static bool WaitForExit(Process process, TimeSpan timeout)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (!process.WaitForExit(TimeSpan.FromMilliseconds(250)))
            {
                if (IsZombie(process.Id))
                {
                    return true;
                }
                if (clock.Elapsed >= timeout)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Whether a process on Linux has exited and waits for its parent to collect it (state Z in
        /// <c>/proc/&lt;pid&gt;/stat</c>). This tool is not its parent, so it looks alive to .NET; in a
        /// container whose first process collects nothing (<c>docker run</c> without <c>--init</c>) it
        /// stays so forever.
        /// </summary>
        private static bool IsZombie(int pid)
        {
            if (!OperatingSystem.IsLinux())
            {
                return false;
            }
            try
            {
                string stat = File.ReadAllText($"/proc/{pid}/stat");
                // "pid (name) state ...": the name may hold spaces and brackets
                return stat[(stat.LastIndexOf(')') + 1)..].TrimStart().StartsWith('Z');
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>
        /// What a starting editor still waits for, empty when it is ready: the workspace, the map
        /// open, the assets read, nothing busy.
        /// </summary>
        public static string Waiting(JsonObject state)
        {
            string stage = state["stage"]?.GetValue<string>() ?? "";
            if (stage != "Ready")
            {
                return stage == "SetUp" ? "the workspace's setup (the settings file names no workspace)" : $"the stage Ready (now {stage})";
            }
            if (state["busy"]?.GetValue<bool>() == true)
            {
                return "a busy operation";
            }
            if (state["map"] is null)
            {
                return "a map to open";
            }
            if (state["loading"]?["assetsLoaded"]?.GetValue<bool>() != true)
            {
                return "the assets";
            }
            return "";
        }

        /// <summary>
        /// Where a started editor's own output goes: <c>--log</c>, else <c>editor-port.log</c> beside
        /// the settings file, else in the temporary folder.
        /// </summary>
        public static string LogFile(EditorOptions o) => Path.GetFullPath(o.Log ?? Path.Combine(
            o.Settings is string settings ? Path.GetDirectoryName(Path.GetFullPath(settings))! : Path.GetTempPath(),
            $"editor-{o.Port}.log"));

        /// <summary>
        /// The editor's state, or null when none answers on the port.
        /// </summary>
        private static JsonObject? State(int port) => Send(port, "state");

        private static JsonObject? Send(int port, string command)
        {
            using HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            try
            {
                using StringContent content = new StringContent("{}", Encoding.UTF8, "application/json");
                using HttpResponseMessage response = client.PostAsync($"http://localhost:{port}/{command}", content).GetAwaiter().GetResult();
                return JsonNode.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult()) as JsonObject;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// The editor's executable: the one given, the one <c>FAF_MAP_EDITOR_EXE</c> names, else the
        /// one built next to this tool (the App's output beside the CLI's, in either build layout).
        /// </summary>
        public static string? FindEditor(string? given)
        {
            string name = OperatingSystem.IsWindows() ? "faf-map-editor.exe" : "faf-map-editor";
            foreach (string? candidate in Candidates())
            {
                if (candidate is not null && File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            return null;

            IEnumerable<string?> Candidates()
            {
                yield return given;
                yield return Environment.GetEnvironmentVariable("FAF_MAP_EDITOR_EXE");
                string here = AppContext.BaseDirectory;
                yield return Path.Combine(here, name);
                // bin/FAForever.MapEditor.Cli/debug/ beside bin/FAForever.MapEditor.App/debug/, or
                // src/FAForever.MapEditor.Cli/bin/Debug/net10.0/ beside src/FAForever.MapEditor.App/bin/...
                const string cli = "FAForever.MapEditor.Cli";
                int at = here.LastIndexOf(cli, StringComparison.Ordinal);
                if (at >= 0)
                {
                    yield return Path.Combine(here[..at] + "FAForever.MapEditor.App" + here[(at + cli.Length)..], name);
                }
            }
        }
    }
}
