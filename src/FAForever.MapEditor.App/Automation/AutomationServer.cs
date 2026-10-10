using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Input;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Automation;

/// <summary>
/// Lets a program drive the running editor, as Playwright drives a browser: commands over HTTP on
/// <c>http://localhost:port/</c>, each run on the user interface's thread as if chosen from the
/// menu, each answered with the editor's state as JSON. <c>faf-map editor</c> is its client.
/// </summary>
/// <remarks>
/// <para>
/// The operations are the commands of the registry: <c>run {id, ...arguments}</c>, or the
/// command's id as the path; they run without a person, so they ask nothing. Automation adds reads
/// of the window and the map (<c>state</c>, <c>ui</c>, <c>height</c>, ...), the mouse
/// (<c>stroke</c>), screenshots and <c>close</c>, and keeps its earlier names as aliases of
/// commands (<see cref="Aliases"/>). <c>GET /help</c> lists them all. The method does not matter:
/// a command without a body can be a GET (<c>curl localhost:port/undo</c>), and query parameters
/// count as arguments; Windows refuses a POST without a body.
/// </para>
/// <para>
/// It listens on the loopback address only and is off unless the editor is started with
/// <c>--automation port</c>.
/// </para>
/// </remarks>
public sealed partial class AutomationServer(MainWindow window, MainViewModel model, int port) : IDisposable
{
    private readonly HttpListener _listener = new HttpListener();
    private readonly CancellationTokenSource _stop = new CancellationTokenSource();

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public void Start()
    {
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();
        _ = Task.Run(ListenAsync);
        model.Status = $"Automation on http://localhost:{port}/";
        model.Logger("Automation").LogInformation("Automation on http://localhost:{Port}/", port);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Close();
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }
            _ = Task.Run(() => AnswerAsync(context));
        }
    }

    private async Task AnswerAsync(HttpListenerContext context)
    {
        int status = 200;
        JsonNode response;
        byte[] bytes;
        try
        {
            string body = context.Request.HasEntityBody
                ? await new StreamReader(context.Request.InputStream, Encoding.UTF8).ReadToEndAsync()
                : "";
            JsonObject arguments = body.Length > 0 ? JsonNode.Parse(body) as JsonObject ?? [] : [];
            foreach (string key in context.Request.QueryString.AllKeys.OfType<string>())
            {
                arguments[key] = Value(context.Request.QueryString[key] ?? "");
            }
            string command = context.Request.Url!.AbsolutePath.Trim('/');
            response = await Dispatcher.UIThread.InvokeAsync(() => RunAsync(command, arguments));
            // inside the try: an answer that cannot be written (a number that is not finite) is an
            // error the client gets, never a request left without an answer
            bytes = Encoding.UTF8.GetBytes(response.ToJsonString(Json));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            status = 400;
            // the caller gets the error in the answer: logged as information, so the Console's count stays the editor's own warnings
            Dispatcher.UIThread.Post(() => model.Logger("Automation").LogInformation("An automation command failed: {Reason}", exception.Message));
            response = new JsonObject { ["ok"] = false, ["error"] = exception.Message };
            bytes = Encoding.UTF8.GetBytes(response.ToJsonString(Json));
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    /// <summary>
    /// The views of the map by their name in automation (<c>tab</c>, the state's <c>tab</c>), with
    /// the command that shows each.
    /// </summary>
    private static readonly (string Name, MapViewTab View, string Command)[] MapViews =
    [
        ("top", MapViewTab.Top, "view.top"),
        ("3d", MapViewTab.ThreeD, "view.3d"),
        ("side", MapViewTab.SideBySide, "view.sideBySide"),
    ];

    /// <summary>
    /// The names automation had before its operations became commands of the registry, with the
    /// command each runs, how its arguments are named there, and where the answer goes in the reply.
    /// </summary>
    private static readonly Dictionary<string, (string Runs, (string From, string To)[] Renamed, string? Answer)> Aliases = new()
    {
        ["setup"] = ("workspace.setUp", [], "setUp"),
        ["update-assets"] = ("workspace.convert", [], "converted"),
        ["open"] = ("file.open", [("path", "folder")], null),
        ["import"] = ("file.import", [("path", "folder")], null),
        ["save"] = ("file.save", [("path", "folder")], null),
        ["save-as"] = ("file.saveAs", [("path", "folder")], null),
        ["commit"] = ("file.commit", [], "commit"),
        ["export"] = ("file.export", [("output", "folder")], "folder"),
        ["undo"] = ("edit.undo", [], "done"),
        ["redo"] = ("edit.redo", [], "done"),
        ["goto"] = ("edit.goTo", [], "done"),
        ["brush"] = ("brush.set", [], null),
        ["fit"] = ("view.fit", [], null),
        ["camera"] = ("camera.look", [], null),
        ["focus"] = ("camera.focus", [], null),
        ["view"] = ("view.zoomTo", [], null),
        ["gpu-debug"] = ("view.gpuDebug", [], null),
    };

    /// <summary>
    /// What automation itself answers: reads of the window and the map, and input as the mouse
    /// gives it. Every operation is a command of the registry (<c>commands</c>, <c>run</c>).
    /// </summary>
    private static readonly string[] Own =
    [
        "state", "commands", "run {id, interactive?, wait?, ...its arguments}", "mode {id}", "tool {id}", "tab {name: top|3d|side}", "ui {hidden?, under?, type?: Button, text?: part of a name, label or text}", "perf {reset?, gpu?}", "logs {problems?, count?}",
        "diagnostics {record?, send?, endpoint?, flush?, upload?}", "version-control {open?, enable?, name?, email?, lfs?, remote?}",
        "picker {open?, query?, group?, category?, tech?, move?: [across, down], choose?, cancel?}",
        "height {x, z}", "mask {layer, x, z}", "stroke {points: [[x, z], ...]}", "hit {x, y}", "pick {x, y, highlight?}",
        "show {markers?, pathNodes?, camera?, props?, units?, areas?}", "screenshot {path, map?, gpu?, brush?, settle?}", "close",
    ];

    /// <summary>
    /// Runs a command on the user interface's thread.
    /// </summary>
    private async Task<JsonNode> RunAsync(string command, JsonObject arguments)
    {
        JsonObject result = new JsonObject();
        // how much of the state the answer carries (AutomationServer.Answers)
        StateFilter filter = TakeStateFilter(command, arguments);
        switch (command)
        {
            case "" or "help":
                return new JsonObject
                {
                    ["automation"] = new JsonArray([.. Own.Concat(InputCommands).Concat(ToolingCommands).Concat(DialogCommands).Concat(LayoutCommands).Concat(ShellCommands).Concat(AnswerCommands).Select(line => (JsonNode)JsonValue.Create(line))]),
                    ["commands"] = new JsonArray([.. model.Commands.All.Where(c => !c.IsIncomplete).Select(c => (JsonNode)JsonValue.Create(c.Usage))]),
                    ["aliases"] = new JsonObject([.. Aliases.Select(alias => KeyValuePair.Create(alias.Key, (JsonNode?)JsonValue.Create(alias.Value.Runs)))]),
                };
            case "state":
                break;
            case "commands":
                // every command of the registry: those not built yet with their user story
                return new JsonObject
                {
                    ["commands"] = new JsonArray([.. model.Commands.All.Select(command => (JsonNode)new JsonObject
                    {
                        ["id"] = command.Id,
                        ["title"] = command.FullTitle,
                        ["arguments"] = new JsonArray([.. command.Parameters.Select(parameter => (JsonNode)JsonValue.Create(parameter.ToString()))]),
                        ["gesture"] = command.Gesture,
                        ["story"] = command.Story,
                        ["notBuilt"] = command.Incomplete?.Missing,
                        ["canRun"] = model.Commands.CanRun(command),
                    })]),
                };
            case "run":
            {
                // a command of the registry with its arguments; it asks nothing, a missing argument fails
                string id = Take(arguments, "id");
                if (!await StartCommandAsync(id, arguments, result))
                {
                    await RunCommandAsync(id, arguments, result, "result");
                }
                break;
            }
            case "mode":
                await RunCommandAsync($"{Take(arguments, "id")}.mode", arguments, result, null);
                break;
            case "tool":
                await RunCommandAsync($"tool.{Take(arguments, "id")}", arguments, result, null);
                break;
            case "tab":
            {
                string name = Take(arguments, "name").ToLowerInvariant();
                string shows = MapViews.FirstOrDefault(view => view.Name == name).Command
                    ?? throw new ArgumentException($"There is no view '{name}': {string.Join(" or ", MapViews.Select(view => view.Name))}");
                await RunCommandAsync(shows, arguments, result, null);
                break;
            }
            case "ui":
                // the window's controls: what an agent can read and use, with their places for hit and the mouse
                return new JsonObject { ["ui"] = DescribeUi(arguments) };
            case "perf":
                // where the time goes: per measured span, and the recent stalls of the user interface
                return Perf(arguments, filter);
            case "logs":
                // what the Console shows, newest last
                {
                    int count = (int)(Number(arguments, "count") ?? 50);
                    bool problems = arguments["problems"]?.GetValue<bool>() == true;
                    return new JsonObject
                    {
                        ["logs"] = new JsonArray([.. (problems ? model.Diagnostics.ConsoleProblems : model.Diagnostics.Console).TakeLast(count).Select(entry => (JsonNode)new JsonObject
                        {
                            ["time"] = entry.Time.ToString("O"),
                            ["level"] = entry.Level.ToString(),
                            ["category"] = entry.Category,
                            ["message"] = entry.Message,
                        })]),
                    };
                }
            case "diagnostics":
            {
                // the choices about diagnostics, and the session's files; what changes runs the commands
                bool flush = arguments["flush"]?.GetValue<bool>() == true;
                bool upload = arguments["upload"]?.GetValue<bool>() == true;
                arguments.Remove("flush");
                arguments.Remove("upload");
                if (arguments.Count > 0)
                {
                    await RunCommandAsync("diagnostics.set", arguments, result, null);
                }
                if (flush)
                {
                    await RunCommandAsync("diagnostics.flush", [], result, null);
                }
                if (upload)
                {
                    await RunCommandAsync("diagnostics.upload", [], result, "uploaded");
                }
                return new JsonObject
                {
                    ["record"] = model.Diagnostics.Record,
                    ["send"] = model.Diagnostics.Send,
                    ["endpoint"] = model.Telemetry.Settings.Endpoint,
                    ["folder"] = model.Telemetry.Folder,
                    ["session"] = model.Telemetry.SessionId,
                    ["logsFile"] = model.Telemetry.LogsFile,
                    ["metricsFile"] = model.Telemetry.MetricsFile,
                    ["warnings"] = model.Telemetry.Warnings,
                    ["errors"] = model.Telemetry.Errors,
                    ["uploaded"] = result["uploaded"]?.DeepClone(),
                    ["uploadError"] = model.Telemetry.LastUploadError,
                };
            }
            case "version-control":
            {
                // how the open project stands in git; open shows the page (file.commit without a
                // message), enable turns version control on with the choices given
                bool open = arguments["open"]?.GetValue<bool>() == true;
                bool enable = arguments["enable"]?.GetValue<bool>() == true;
                arguments.Remove("open");
                arguments.Remove("enable");
                if (arguments.Count > 0 && !enable)
                {
                    throw new ArgumentException("The choices (name, email, lfs, remote) go with enable: true");
                }
                if (open || model.VersionControl.Repository is null)
                {
                    await RunCommandAsync("file.commit", [], result, null);
                }
                if (enable)
                {
                    await RunCommandAsync("file.enableVersionControl", arguments, result, null);
                }
                result["versionControl"] = new JsonObject
                {
                    ["git"] = model.VersionControl.Repository?.GitInstalled,
                    ["lfsInstalled"] = model.VersionControl.Repository?.LfsInstalled,
                    ["repository"] = model.VersionControl.Repository?.IsRepository,
                    ["usesLfs"] = model.VersionControl.Repository?.UsesLfs,
                    ["changes"] = model.VersionControl.Repository?.Changes,
                    ["branch"] = model.VersionControl.Repository?.Branch,
                    ["lastCommit"] = model.VersionControl.Repository?.LastCommit,
                    ["remote"] = model.VersionControl.Repository?.Remote,
                    ["ahead"] = model.VersionControl.Repository?.Ahead,
                    ["behind"] = model.VersionControl.Repository?.Behind,
                    ["history"] = new JsonArray([.. model.VersionControl.History.Take(20).Select(commit => (JsonNode)JsonValue.Create($"{commit.ShortHash} {commit.Date:yyyy-MM-dd HH:mm} {commit.Author}: {commit.Subject}"))]),
                    ["chosen"] = model.VersionControl.ChosenCommit?.ShortHash,
                    ["files"] = new JsonArray([.. model.VersionControl.ChosenFiles.Take(50).Select(file => (JsonNode)JsonValue.Create($"{file.Change} {file.Path}"))]),
                    ["pageOpen"] = model.VersionControl.IsOpen,
                    ["error"] = model.VersionControl.Error,
                };
                break;
            }
            case "picker":
            {
                // input to the picker as a person gives it: open runs a command that picks
                // (assets.pick, units.pick) as the menu does, without waiting for the answer;
                // the rest types, filters, moves and chooses
                if (arguments["open"]?.GetValue<string>() is string open)
                {
                    arguments.Remove("open");
                    _ = model.RunAsync(open);
                }
                AssetPickerViewModel picker = model.Picker;
                if (arguments.Count > 0 && !picker.IsOpen)
                {
                    throw new ArgumentException("The picker is not open: give open: assets.pick or units.pick");
                }
                string? Filter(string name) => arguments[name]?.GetValue<string>() is { Length: > 0 } value ? value : null;
                if (arguments.ContainsKey("query"))
                {
                    picker.Query = arguments["query"]?.GetValue<string>() ?? "";
                }
                if (arguments.ContainsKey("group"))
                {
                    picker.Group = Filter("group");
                }
                if (arguments.ContainsKey("category"))
                {
                    picker.Category = Filter("category");
                }
                if (arguments.ContainsKey("tech"))
                {
                    picker.Tech = Filter("tech");
                }
                if (arguments["move"] is JsonArray move)
                {
                    picker.Move(move[0]!.GetValue<int>(), move[1]!.GetValue<int>());
                }
                if (arguments["choose"]?.GetValue<bool>() == true)
                {
                    picker.Choose();
                }
                if (arguments["cancel"]?.GetValue<bool>() == true)
                {
                    picker.Cancel();
                }
                // the state has the picker's filters; its first tiles come with this answer
                result["pickerTiles"] = new JsonArray([.. picker.Tiles.Take(60).Select(tile => (JsonNode)new JsonObject
                {
                    ["title"] = tile.Title,
                    ["detail"] = tile.Detail,
                    ["key"] = tile.Entry.Key,
                })]);
                break;
            }
            case "stroke":
                // input: the left mouse down, dragged along the points and up again
                model.Stroke([.. (arguments["points"] as JsonArray ?? throw new ArgumentException("points: [[x, z], ...] is missing"))
                    .Select(point => (point![0]!.GetValue<float>(), point[1]!.GetValue<float>()))]);
                break;
            case "height":
            {
                MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
                int x = (int)(Number(arguments, "x") ?? throw new ArgumentException("x is missing"));
                int z = (int)(Number(arguments, "z") ?? throw new ArgumentException("z is missing"));
                result["height"] = document.Terrain.Heightmap.GetHeight(x, z);
                break;
            }
            case "mask":
            {
                MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
                int maskLayer = (int)(Number(arguments, "layer") ?? throw new ArgumentException("layer is missing"));
                double x = Number(arguments, "x") ?? throw new ArgumentException("x is missing");
                double z = Number(arguments, "z") ?? throw new ArgumentException("z is missing");
                TiledBuffer<byte> mask = document.Terrain.StratumMasks.Layers[maskLayer - 1];
                result["value"] = mask[
                    Math.Clamp((int)(x * mask.Width / document.Width), 0, mask.Width - 1),
                    Math.Clamp((int)(z * mask.Height / document.Height), 0, mask.Height - 1)];
                break;
            }
            case "hit":
                // what the mouse would reach at a point of the window: the control that gets its events
                result["hit"] = HitAt(Number(arguments, "x") ?? 0, Number(arguments, "y") ?? 0);
                break;
            case "pick":
            {
                // the object under a point of the 3D view (its pixels, as the mouse's); highlight: draw it highlighted
                ObjectRef? picked = window.View3D.PickAt(new Point(Number(arguments, "x") ?? 0, Number(arguments, "y") ?? 0));
                result["picked"] = picked?.ToString();
                if (arguments["highlight"]?.GetValue<bool>() == true)
                {
                    // as a click with the select tool would: the selection, which the 3D view highlights
                    model.Selection.Set(picked is ObjectRef thing ? [thing] : []);
                }
                break;
            }
            case "show":
                // the overlays' commands, each with whether to show it
                foreach ((string key, JsonNode? on) in arguments)
                {
                    await RunCommandAsync($"view.overlay.{key}", new JsonObject { ["on"] = on?.DeepClone() }, result, null);
                }
                break;
            case "screenshot":
                if (arguments["part"] is not null && await RunToolingAsync(command, arguments, result))
                {
                    // one part of the window (AutomationServer.Tooling)
                    break;
                }
                if (arguments["settle"]?.GetValue<bool>() == true)
                {
                    result["settled"] = await SettleAsync(arguments["gpu"]?.GetValue<bool>() == true);
                }
                result["file"] = arguments["gpu"]?.GetValue<bool>() == true
                    ? model.Shows3DView
                        ? await window.View3D.CaptureAsync(Text(arguments, "path"), arguments["brush"] is JsonArray brush
                            ? new System.Numerics.Vector2(brush[0]!.GetValue<float>(), brush[1]!.GetValue<float>())
                            : null).WaitAsync(TimeSpan.FromSeconds(10))
                        : throw new ArgumentException("The 3D view draws only while it shows: tab name=3d first")
                    : Screenshot(Text(arguments, "path"), arguments["map"]?.GetValue<bool>() == true);
                break;
            case "close":
                // after the answer has gone out
                Dispatcher.UIThread.Post(window.CloseWithoutAsking, DispatcherPriority.Background);
                break;
            default:
                if (await RunInputAsync(command, arguments, result))
                {
                    break;
                }
                if (Aliases.TryGetValue(command, out var alias))
                {
                    foreach ((string from, string to) in alias.Renamed)
                    {
                        if (arguments[from] is JsonNode value)
                        {
                            arguments.Remove(from);
                            arguments[to] = value.DeepClone();
                        }
                    }
                    await RunCommandAsync(alias.Runs, arguments, result, alias.Answer);
                }
                else if (model.Commands.Find(command) is not null)
                {
                    await RunCommandAsync(command, arguments, result, "result");
                }
                else
                {
                    throw new ArgumentException($"Unknown command '{command}'; GET /help lists them");
                }
                break;
        }

        AddState(result, command, filter);
        result["ok"] = true;
        return result;
    }

    /// <summary>
    /// The picker as automation reads it: what it asks, its filters with their choices.
    /// </summary>
    private static JsonObject DescribePicker(AssetPickerViewModel picker)
    {
        static JsonObject Choices(IReadOnlyList<AssetChoice> choices) =>
            new JsonObject([.. choices.Select(choice => KeyValuePair.Create(choice.Value!, (JsonNode?)choice.Count))]);
        return new JsonObject
        {
            ["open"] = picker.IsOpen,
            ["kind"] = picker.Kind.ToString(),
            ["title"] = picker.Title,
            ["query"] = picker.Query,
            ["group"] = picker.Group,
            ["category"] = picker.Category,
            ["tech"] = picker.Tech,
            ["summary"] = picker.Summary,
            ["selected"] = picker.Selected?.Entry.Key,
            ["groups"] = Choices(picker.Groups),
            ["categories"] = Choices(picker.Categories),
            ["techs"] = Choices(picker.Techs),
        };
    }

    private JsonObject State()
    {
        MapDocument? document = model.Map?.Document;
        SculptTool sculpt = model.Tool<SculptTool>();
        StrataTool strata = model.Tool<StrataTool>();
        return new JsonObject
        {
            ["title"] = model.Title,
            ["build"] = BuildState(),
            ["stage"] = model.Setup.Stage.ToString(),
            ["mode"] = model.SelectedMode?.Definition.Id,
            ["tool"] = model.ActiveTool?.Definition.Id,
            ["toolNotBuilt"] = model.ActiveTool?.Definition.Missing,
            ["palette"] = model.Palette.IsOpen,
            ["workspace"] = model.Setup.Workspace?.Folder,
            ["setupError"] = model.Setup.Error,
            ["assetReasons"] = new JsonArray([.. model.Setup.AssetReasons.Select(reason => (JsonNode)JsonValue.Create(reason))]),
            ["conversion"] = model.Setup.IsConverting ? model.Setup.ConversionText : null,
            ["status"] = model.Status,
            ["projectFolder"] = model.Map?.ProjectFolder,
            ["map"] = document is null ? null : new JsonObject
            {
                ["name"] = document.Scenario.Name,
                ["folder"] = document.Folder.ToString(),
                ["width"] = document.Width,
                ["height"] = document.Height,
                ["scmapVersion"] = document.Terrain.ScmapVersion,
            },
            ["modified"] = model.Map?.Session.IsModified ?? false,
            ["busy"] = model.IsBusy,
            ["undo"] = model.Map?.Session.UndoDescription,
            ["redo"] = model.Map?.Session.RedoDescription,
            ["history"] = new JsonArray([.. (model.Map?.Session.History ?? []).Select(step => (JsonNode)JsonValue.Create(step.IsCurrent ? $"> {step.Description}" : step.Description))]),
            ["brush"] = new JsonObject
            {
                ["sculpt"] = new JsonObject
                {
                    ["mode"] = sculpt.Mode.ToString(),
                    ["radius"] = sculpt.Radius,
                    ["strength"] = sculpt.Strength,
                    ["target"] = sculpt.Target,
                    ["shape"] = sculpt.Shape.Chosen.Key ?? "round",
                    ["rotation"] = sculpt.Shape.Rotation,
                    ["randomRotation"] = sculpt.Shape.RandomRotation,
                },
                ["strata"] = new JsonObject
                {
                    ["layer"] = strata.Painted?.Name,
                    ["radius"] = strata.Radius,
                    ["strength"] = strata.Strength,
                    ["erase"] = strata.Erase,
                    ["shape"] = strata.Shape.Chosen.Key ?? "round",
                    ["rotation"] = strata.Shape.Rotation,
                    ["randomRotation"] = strata.Shape.RandomRotation,
                },
            },
            ["painting"] = PaintingState.Describe(model),
            ["zoom"] = window.MapView.Zoom,
            ["tab"] = MapViews.First(view => view.View == model.View).Name,
            ["camera"] = new JsonObject
            {
                ["x"] = model.Camera.Now.X,
                ["z"] = model.Camera.Now.Z,
                ["distance"] = model.Camera.Now.Distance,
                ["yaw"] = model.Camera.Now.Yaw,
                ["pitch"] = model.Camera.Now.Pitch,
            },
            ["show"] = new JsonObject([.. model.Overlays.Select(overlay => KeyValuePair.Create(overlay.Id, (JsonNode?)JsonValue.Create(overlay.IsOn)))]),
            ["selection"] = new JsonObject
            {
                ["count"] = model.Selection.Count,
                ["kinds"] = model.Selection.Summary,
                ["items"] = new JsonArray([.. model.Selection.Items.Take(200).Select(thing => (JsonNode)JsonValue.Create(thing.ToString()))]),
                ["selectable"] = model.SelectableKinds.ToString(),
                ["copied"] = model.Selection.Clipboard?.Items.Length ?? 0,
                ["x"] = model.Selection.Inspector.X,
                ["z"] = model.Selection.Inspector.Z,
                ["heading"] = model.Selection.Inspector.Heading,
            },
            ["markers"] = DescribeMarkers(),
            ["dock"] = model.Dock.ToString(),
            ["assets"] = new JsonObject
            {
                ["kind"] = model.Assets.Kind.ToString(),
                ["scope"] = model.Assets.Scope.ToString(),
                ["query"] = model.Assets.Query,
                ["group"] = model.Assets.Group,
                ["category"] = model.Assets.Category,
                ["summary"] = model.Assets.Summary,
                ["selected"] = model.Assets.Shown?.Key,
            },
            ["picker"] = DescribePicker(model.Picker),
            ["symmetry"] = ChooseSymmetryHandler.Describe(model.Symmetry),
            ["selectionTools"] = new JsonObject { ["locked"] = SelectionKinds.Describe(model.LockedKinds), ["isolated"] = model.Isolated?.Count, ["decalsShown"] = model.ShowDecals, ["boxKinds"] = model.BoxKinds.ToString() },
            ["decals"] = new JsonObject
            {
                ["count"] = model.ShownDocument?.Decals.Length ?? 0,
                ["placing"] = model.DecalPlaceTool.Decal,
                ["width"] = model.DecalPlaceTool.Width,
                ["heading"] = model.DecalPlaceTool.Heading,
                ["cutOff"] = model.DecalPlaceTool.CutOffLod,
                ["dragMode"] = model.Tool<DecalSelectTool>().DragMode.ToString(),
                ["hidden"] = model.Decals.HiddenDecals.Count,
                ["groups"] = new JsonArray([.. model.Decals.Groups.Select(group => (JsonNode)new JsonObject
                {
                    ["id"] = group.Id,
                    ["name"] = group.Name,
                    ["count"] = group.Count,
                    ["shown"] = group.IsShown,
                })]),
            },
            ["wideDecals"] = model.WideDecals.Describe(),
            ["mapPanels"] = model.DescribeMapPanels(),
            ["mapSize"] = model.DescribeMapSize(),
            ["heightLayers"] = HeightLayersState.Describe(model),
            ["presets"] = new JsonObject
            {
                ["folder"] = model.Presets.Library?.Folder,
                ["kind"] = model.Presets.Kind,
                ["shown"] = new JsonArray([.. model.Presets.Shown.Select(preset => (JsonNode)JsonValue.Create(preset.Name))]),
            },
            ["units"] = new JsonObject
            {
                ["place"] = model.Tool<UnitPlaceTool>().Describe(),
                ["tree"] = model.Armies.DescribeGroups(),
            },
            ["armies"] = model.Armies.DescribeArmies(),
            ["generated"] = new JsonArray([.. model.Generation.States.Select(state => (JsonNode)new JsonObject
            {
                ["id"] = state.Generator.Id,
                ["status"] = state.Status.ToString(),
                ["changed"] = new JsonArray([.. state.ChangedInputs.Select(input => (JsonNode)JsonValue.Create(input))]),
            })]),
            ["walkability"] = GenerateWalkabilityHandler.Describe(model.Walkability),
            ["procedural"] = ProceduralState(),
            ["problems"] = CheckMapHandler.Describe(model.Problems),
            ["props"] = model.Props.Describe(),
            ["rules"] = new JsonObject { ["masks"] = model.MaskRules.Describe(model.ShownDocument), ["props"] = model.PropRules.Describe() },
            ["layout"] = model.Layout.Describe(),
            ["upload"] = model.Upload.Describe(),
            ["gpu"] = new JsonObject
            {
                ["renderer"] = window.View3D.Renderer,
                ["error"] = window.View3D.Error,
                ["textures"] = window.View3D.TextureState.Textures,
                ["textureError"] = window.View3D.TextureState.Error,
                ["decals"] = window.View3D.DecalState.Decals,
                ["decalDraws"] = window.View3D.DecalState.Draws,
                ["decalTextures"] = window.View3D.DecalState.Textures,
                ["objects"] = ObjectState(),
            },
            ["pid"] = Environment.ProcessId,
            ["views"] = ViewsState(),
            ["loading"] = LoadingState(),
            ["handles"] = DescribeHandles(),
            ["hover"] = model.Selection.Hovered?.ToString(),
            ["plugins"] = model.Plugins.Describe(),
            ["dialog"] = model.Question.Describe(),
            ["overlays"] = OverlaysState(),
            ["paletteResults"] = PaletteState(),
            ["looks"] = LooksState(),
            ["selectionPreset"] = model.Tool<SelectionPresetTool>().Describe(),
            ["stamp"] = model.Tool<StampTool>().Describe(),
            ["favourites"] = model.Assets.DescribeFavourites(),
            ["walkabilityMarks"] = model.Tool<WalkabilityMarkTool>().Describe(),
            ["adaptiveFiles"] = new JsonObject { ["writes"] = model.Markers.WritesAdaptiveFiles, ["text"] = model.Markers.AdaptiveFilesText },
            ["floatingWindows"] = FloatingWindowsState(),
            ["waves"] = WavesState(),
            ["panelParts"] = model.DescribePanelParts(),
            ["shortcuts"] = model.Shortcuts.Describe(),
            ["workspaceSettings"] = model.WorkspaceSettings.Describe(),
        };
    }

    /// <summary>
    /// Where the handles show in a view's pixels, to aim the mouse at: the move square, the arrows'
    /// tips, the ring at +x and the scale squares by their side.
    /// </summary>
    private static JsonObject HandlePoints(FAForever.MapEditor.Project.Editing.SelectionFrame frame, ToolView view)
    {
        FAForever.MapEditor.Project.Editing.HandleLayout layout = FAForever.MapEditor.Project.Editing.HandleLayout.Of(frame, view.OgridsPerPixelAt(frame.Centre));
        JsonNode? At(System.Numerics.Vector3 place) => view.ToScreen(place) is System.Numerics.Vector2 p ? new JsonArray(Math.Round(p.X, 1), Math.Round(p.Y, 1)) : null;
        JsonObject points = new JsonObject
        {
            ["move"] = At(layout.Centre),
            ["moveX"] = At(System.Numerics.Vector3.Lerp(layout.ArrowX.From, layout.ArrowX.To, 0.6f)),
            ["moveZ"] = At(System.Numerics.Vector3.Lerp(layout.ArrowZ.From, layout.ArrowZ.To, 0.6f)),
            ["turn"] = frame.Turns ? At(layout.Centre + new System.Numerics.Vector3(layout.RingRadius, 0, 0)) : null,
        };
        foreach ((FAForever.MapEditor.Project.Editing.HandlePart part, System.Numerics.Vector3 place) in layout.Scales)
        {
            points[part.ToString()] = At(place);
        }
        return points;
    }

    /// <summary>
    /// The handles of the selection (user story F-11): the settings, the box they sit on, the handle
    /// under the mouse and the one dragged, and the sun's head in 3D.
    /// </summary>
    private JsonObject DescribeHandles()
    {
        JsonObject state = SetHandlesHandler.Describe(model.Handles);
        SelectTool? select = model.ActiveTool?.Tool switch
        {
            SelectTool tool => tool,
            DecalSelectTool decals => decals.Select,
            _ => null,
        };
        if (select?.Frame is FAForever.MapEditor.Project.Editing.SelectionFrame frame)
        {
            state["centre"] = new JsonArray(Math.Round(frame.Centre.X, 2), Math.Round(frame.Centre.Y, 2), Math.Round(frame.Centre.Z, 2));
            state["heading"] = Math.Round(frame.Heading * 180 / Math.PI, 2);
            state["size"] = new JsonArray(Math.Round(2 * frame.HalfSize.X, 2), Math.Round(2 * frame.HalfSize.Y, 2));
            state["turns"] = frame.Turns;
            state["scales"] = frame.ScalesSides ? "sides" : frame.Scales ? "corners" : "none";
            state["hovered"] = select.Hovered.ToString();
            state["dragged"] = select.Dragged.ToString();
            state["points"] = new JsonObject
            {
                ["top"] = window.MapView.IsEffectivelyVisible ? HandlePoints(frame, window.MapView.ToolViewOf(null)) : null,
                ["3d"] = window.View3D.IsEffectivelyVisible ? HandlePoints(frame, window.View3D.ToolViewOf()) : null,
            };
        }
        state["status"] = model.SelectionStatus;
        if (model.ActiveTool?.Tool is SunTool sun)
        {
            state["sunHead"] = sun.HeadHovered;
        }
        return state;
    }

    /// <summary>
    /// The Markers mode (user stories R-1 to R-6): the kinds shown, the chains and the one chosen,
    /// the adaptive map, the last deposit's warning, and the settings of the active marker tool.
    /// </summary>
    private JsonObject DescribeMarkers() => new JsonObject
    {
        ["shownKinds"] = new JsonArray([.. MarkersViewModel.Choices.Where(choice => model.Markers.Shows(choice.Kind)).Select(choice => (JsonNode)JsonValue.Create(choice.Id))]),
        ["pickable"] = model.ShownMarkerKinds.ToString(),
        ["chains"] = new JsonArray([.. model.Markers.Chains.Select(chain => (JsonNode)new JsonObject
        {
            ["name"] = chain.Name,
            ["markers"] = new JsonArray([.. chain.Entries.Select(entry => (JsonNode)JsonValue.Create(entry.Name))]),
        })]),
        ["chain"] = model.Markers.SelectedChain?.Name,
        ["adaptive"] = model.Markers.IsAdaptive,
        ["warning"] = model.Markers.PlacementWarning,
        ["paths"] = new JsonObject
        {
            ["layers"] = new JsonArray([.. model.Markers.Paths.Layers.Select(layer => (JsonNode)JsonValue.Create(layer.ToString()))]),
            ["spacing"] = model.Markers.Paths.SpacingValue,
            ["replace"] = model.Markers.Paths.Replace,
            ["status"] = model.Markers.Paths.Status,
            ["result"] = model.Markers.Paths.Result,
        },
        ["optionalGroups"] = new JsonArray([.. model.Markers.OptionalGroups.Select(item => (JsonNode)JsonValue.Create(item.Line))]),
        ["tool"] = model.ActiveTool?.Tool switch
        {
            MarkerPresetTool preset => new JsonObject { ["preset"] = preset.Preset },
            PlaceMarkerTool place => new JsonObject { ["places"] = place.KindName, ["snap"] = place.Snap.Snap.ToString(), ["aiType"] = place.IsAi ? place.AiType : null },
            PathTool path => new JsonObject { ["layer"] = path.Layer.ToString(), ["snap"] = path.Snap.Snap.ToString() },
            _ => null,
        },
    };

    /// <summary>
    /// The props, units and markers the 3D view drew in its last frame, with their draws, the
    /// models on the card, read and being read, and the blueprints drawn as a box for want of a model.
    /// </summary>
    private JsonObject ObjectState()
    {
        var state = window.View3D.ObjectState;
        return new JsonObject
        {
            ["props"] = state.Props,
            ["units"] = state.Units,
            ["markers"] = state.Markers,
            ["draws"] = state.Draws,
            ["meshes"] = state.Meshes,
            ["modelsRead"] = state.ModelsRead,
            ["modelsLoading"] = state.ModelsLoading,
            ["missingMeshes"] = state.Missing,
            ["triangles"] = state.Triangles,
        };
    }

    /// <summary>
    /// Renders the window, or the map alone, into a PNG file.
    /// </summary>
    private string Screenshot(string path, bool mapOnly)
    {
        Visual visual = mapOnly ? window.MapView : window;
        Size size = visual.Bounds.Size;
        double scale = window.RenderScaling;
        using RenderTargetBitmap bitmap = new RenderTargetBitmap(
            new PixelSize(Math.Max(1, (int)(size.Width * scale)), Math.Max(1, (int)(size.Height * scale))),
            new Vector(96 * scale, 96 * scale));
        bitmap.Render(visual);
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        bitmap.Save(full, new PngBitmapEncoderOptions());
        return full;
    }

    private string? HitAt(double x, double y)
    {
        Avalonia.Input.IInputElement? element = window.InputHitTest(new Point(x, y));
        List<string> chain = [];
        for (Visual? visual = element as Visual; visual is not null && chain.Count < 4; visual = visual.GetVisualParent())
        {
            chain.Add(visual is Avalonia.Controls.Control { Name: { Length: > 0 } name } ? $"{visual.GetType().Name} {name}" : visual.GetType().Name);
        }
        return chain.Count == 0 ? null : string.Join(" < ", chain);
    }

    /// <summary>
    /// Runs a command of the registry as automation does: without a person, so it asks nothing.
    /// </summary>
    /// <param name="answer">Where its answer goes in the reply; null to leave it out.</param>
    private async Task RunCommandAsync(string id, JsonObject arguments, JsonObject result, string? answer)
    {
        // a command that refuses answers ran: false with why, as one that cannot run now does
        CommandRun run = await model.Commands.TryRunAsync(id, arguments, interactive: false);
        result["ran"] = run.Ran;
        if (run.Error is Exception error)
        {
            model.Logger("Automation").LogInformation("The command {Command} refused: {Reason}", id, error.Message);
        }
        if (run.Why is string why)
        {
            // a command that did not run says why, never silently
            result["why"] = why;
        }
        if (answer is not null)
        {
            result[answer] = run.Result is null ? null : JsonSerializer.SerializeToNode(run.Result, run.Result.GetType());
        }
    }

    /// <summary>
    /// Takes an argument out of the arguments, which then go to the command without it.
    /// </summary>
    private static string Take(JsonObject arguments, string name)
    {
        string value = Text(arguments, name);
        arguments.Remove(name);
        return value;
    }

    /// <summary>
    /// A query parameter that reads as JSON (a number, true) as such, any other as text, as the CLI sends them.
    /// </summary>
    private static JsonNode? Value(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return JsonValue.Create(text);
        }
    }

    private static string Text(JsonObject arguments, string name) =>
        arguments[name]?.GetValue<string>() ?? throw new ArgumentException($"{name} is missing");

    private static double? Number(JsonObject arguments, string name) => arguments[name] switch
    {
        null => null,
        JsonValue value when value.GetValueKind() == JsonValueKind.Number => double.Parse(value.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture),
        JsonValue value when value.TryGetValue(out string? text) && double.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out double parsed) => parsed,
        _ => throw new ArgumentException($"{name} is not a number"),
    };
}
