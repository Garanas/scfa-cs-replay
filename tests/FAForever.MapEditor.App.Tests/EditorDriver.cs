using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using FAForever.MapEditor.App.Automation;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// A typed client of automation's commands, for tests and for scripts that drive a running
    /// editor: the same calls go to an <see cref="AutomationServer"/> in this process (headless
    /// tests: a frame is drawn before each, since the mouse hits what the last frame drew) or to an
    /// editor's port over HTTP. Each answers the editor's reply; a command that fails throws.
    /// </summary>
    public sealed class EditorDriver
    {
        private readonly Func<string, JsonObject, Task<JsonObject>> _send;

        private EditorDriver(Func<string, JsonObject, Task<JsonObject>> send) => _send = send;

        /// <summary>
        /// Drives a window in this process (headless tests).
        /// </summary>
        public static EditorDriver InProcess(AutomationServer automation) => new EditorDriver(async (command, arguments) =>
        {
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            JsonObject answer = (JsonObject)await automation.HandleAsync(command, arguments);
            Dispatcher.UIThread.RunJobs();
            return answer;
        });

        /// <summary>
        /// Drives a running editor started with <c>--automation port</c>.
        /// </summary>
        public static EditorDriver Http(int port)
        {
            HttpClient client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}/"), Timeout = TimeSpan.FromMinutes(2) };
            return new EditorDriver(async (command, arguments) =>
            {
                using StringContent content = new StringContent(arguments.ToJsonString(), Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await client.PostAsync(command, content);
                JsonObject answer = JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonObject ?? [];
                return answer["ok"]?.GetValue<bool>() == false
                    ? throw new InvalidOperationException($"{command}: {answer["error"]}")
                    : answer;
            });
        }

        /// <summary>
        /// Any command with its arguments.
        /// </summary>
        public Task<JsonObject> Send(string command, JsonObject? arguments = null) => _send(command, arguments ?? []);

        public Task<JsonObject> State() => Send("state");

        /// <summary>
        /// Runs a command of the registry; throws when it did not run, with why.
        /// </summary>
        public async Task<JsonNode?> Run(string id, JsonObject? arguments = null)
        {
            JsonObject sent = arguments?.DeepClone() as JsonObject ?? [];
            sent["id"] = id;
            JsonObject answer = await Send("run", sent);
            return answer["ran"]?.GetValue<bool>() == true
                ? answer["result"]
                : throw new InvalidOperationException($"{id} did not run: {answer["why"]}");
        }

        public Task<JsonObject> Tool(string id) => Send("tool", new JsonObject { ["id"] = id });

        public Task<JsonObject> Tab(string name) => Send("tab", new JsonObject { ["name"] = name });

        /// <summary>
        /// Sets a control by its label as a person would (<c>set</c>); answers what it reads now.
        /// </summary>
        public async Task<JsonNode?> Set(string label, JsonNode value, string? under = null)
        {
            JsonObject arguments = new JsonObject { ["label"] = label, ["value"] = value };
            if (under is not null)
            {
                arguments["under"] = under;
            }
            return (await Send("set", arguments))["set"]?["value"];
        }

        public Task<JsonObject> Click(string view, double x, double y, params string[] modifiers) =>
            Send("mouse", new JsonObject { ["view"] = view, ["x"] = x, ["y"] = y, ["modifiers"] = new JsonArray([.. modifiers.Select(m => (JsonNode)JsonValue.Create(m))]) });

        public Task<JsonObject> Key(string key) => Send("key", new JsonObject { ["key"] = key });

        /// <summary>
        /// Where a thing (<c>prop:5</c>) shows in a view, in its pixels: null when it lies outside.
        /// </summary>
        public async Task<(double X, double Y)?> PointOf(string thing, string view = "top")
        {
            JsonNode? point = (await Send("view-point", new JsonObject { ["ref"] = thing }))["point"]?[view];
            return point is null || point["inside"]?.GetValue<bool>() != true ? null : (point["x"]!.GetValue<double>(), point["y"]!.GetValue<double>());
        }

        /// <summary>
        /// One value of a grid of the map (<c>heightmap</c>, <c>stratum:1</c>, <c>mask:Forest</c>, ...).
        /// </summary>
        public async Task<double> Grid(string target, double x, double z) =>
            (await Send("grid", new JsonObject { ["target"] = target, ["x"] = x, ["z"] = z }))["grid"]!["value"]!.GetValue<double>();

        /// <summary>
        /// Frames things in both views (<c>view.frame</c>); none: the selection.
        /// </summary>
        public Task<JsonObject> Focus(params string[] things) => Send("focus", things.Length == 0
            ? new JsonObject { ["selection"] = true }
            : new JsonObject { ["refs"] = new JsonArray([.. things.Select(t => (JsonNode)JsonValue.Create(t))]) });

        public Task<JsonObject> Screenshot(string path, string? part = null, bool settle = true)
        {
            JsonObject arguments = new JsonObject { ["path"] = path, ["settle"] = settle };
            if (part is not null)
            {
                arguments["part"] = part;
            }
            return Send("screenshot", arguments);
        }
    }
}
