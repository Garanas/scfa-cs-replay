using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Input;
using FAForever.MapEditor.App.Diagnostics;
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
/// The commands: <c>GET /state</c>, <c>GET /height?x=&amp;z=</c>, <c>GET /mask?layer=&amp;x=&amp;z=</c> (0 to 255), <c>GET /help</c>, and with a JSON
/// body <c>POST /setup {folder, game?, faf?, vault?}</c> (the setup page of the first start, which converts the
/// game's assets before it answers), <c>/update-assets</c>, <c>/open {path}</c>, <c>/import {path}</c>, <c>/save</c>, <c>/save-as {path}</c>,
/// <c>/export {output, version?}</c>, <c>/undo</c>, <c>/redo</c>, <c>/goto {step}</c> (a step of the history, -1 for the map as opened),
/// <c>/brush {layer?, mode?, radius?, strength?, target?, erase?}</c> (layer 0 paints the heights, 1 to 8 a layer's mask), <c>/stroke {points: [[x, z], ...]}</c> (in
/// ogrids), <c>/fit</c>, <c>/tab {name}</c> (<c>top</c> or <c>3d</c>), <c>/camera {x?, z?, distance?, yaw?, pitch?}</c> (the 3D view's, in ogrids and degrees), <c>/show {markers?, pathNodes?}</c> (what the view shows), <c>/view {zoom, x, z}</c> (centre the view on a place of the map),
/// <c>/screenshot {path, map?, gpu?}</c> (the window, or with <c>map: true</c> the map alone, or with
/// <c>gpu: true</c> a frame of the 3D view, read back from the graphics card, as PNG) and
/// <c>/close</c>. The method does not matter: a command without a body can be a GET
/// (<c>curl localhost:port/undo</c>), and query parameters count as arguments; Windows refuses a
/// POST without a body.
/// </para>
/// <para>
/// It listens on the loopback address only and is off unless the editor is started with
/// <c>--automation port</c>.
/// </para>
/// </remarks>
public sealed class AutomationServer(MainWindow window, MainViewModel model, int port) : IDisposable
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
        try
        {
            string body = context.Request.HasEntityBody
                ? await new StreamReader(context.Request.InputStream, Encoding.UTF8).ReadToEndAsync()
                : "";
            JsonObject arguments = body.Length > 0 ? JsonNode.Parse(body) as JsonObject ?? [] : [];
            foreach (string key in context.Request.QueryString.AllKeys.OfType<string>())
            {
                arguments[key] = context.Request.QueryString[key];
            }
            string command = context.Request.Url!.AbsolutePath.Trim('/');
            response = await Dispatcher.UIThread.InvokeAsync(() => RunAsync(command, arguments));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            status = 400;
            response = new JsonObject { ["ok"] = false, ["error"] = exception.Message };
        }

        byte[] bytes = Encoding.UTF8.GetBytes(response.ToJsonString(Json));
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    /// <summary>
    /// Runs a command on the user interface's thread.
    /// </summary>
    private async Task<JsonNode> RunAsync(string command, JsonObject arguments)
    {
        JsonObject result = new JsonObject();
        switch (command)
        {
            case "" or "help":
                return new JsonObject { ["commands"] = new JsonArray("state", "commands", "run {id}", "mode {id}", "tool {id}", "ui {hidden?}", "perf {reset?}", "logs {problems?, count?}", "diagnostics {record?, send?, endpoint?, flush?, upload?}", "setup {folder, game?, faf?, vault?}", "update-assets", "height?x=&z=", "mask?layer=&x=&z=", "open {path}", "import {path}", "save", "save-as {path}", "version-control {open?, enable?, name?, email?, lfs?, remote?}", "commit {message}", "export {output, version?}", "undo", "redo", "goto {step}", "brush {layer?, mode?, radius?, strength?, target?, erase?}", "stroke {points: [[x, z], ...]}", "hit {x, y}", "fit", "tab {name: top|3d}", "camera {x?, z?, distance?, yaw?, pitch?}", "focus {x, z, radius?}", "gpu-debug {mode: 0 picture|1 albedo|2 light|3 normals|4 heights|5 lower layer|6 masks|7 masks as stored}", "show {markers?, pathNodes?, camera?}", "view {zoom, x, z}", "screenshot {path, map?, gpu?}", "close") };
            case "state":
                break;
            case "commands":
                // the controller's commands: those not built yet with their user story
                return new JsonObject
                {
                    ["commands"] = new JsonArray([.. model.Commands.All.Select(command => (JsonNode)new JsonObject
                    {
                        ["id"] = command.Id,
                        ["title"] = command.FullTitle,
                        ["gesture"] = command.Gesture,
                        ["story"] = command.Story,
                        ["notBuilt"] = command.Incomplete?.Missing,
                        ["canRun"] = command.IsIncomplete ? false : command.CanRun(),
                    })]),
                };
            case "run":
                // a command of the controller, as a menu item or shortcut runs it
                result["ran"] = await model.Commands.RunAsync(Text(arguments, "id"));
                break;
            case "mode":
                model.SelectMode(Text(arguments, "id"));
                break;
            case "tool":
                model.SelectTool(Text(arguments, "id"));
                break;
            case "ui":
                // the window's controls: what an agent can read and use, with their places for hit and the mouse
                return new JsonObject { ["ui"] = UiTree.Describe(window, arguments["hidden"]?.GetValue<bool>() == true) };
            case "perf":
                // where the time goes: per measured span, and the recent stalls of the user interface
                if (arguments["reset"]?.GetValue<bool>() == true)
                {
                    Performance.Reset();
                }
                return Performance.Snapshot();
            case "logs":
                // what the Console shows, newest last
                {
                    int count = (int)(Number(arguments, "count") ?? 50);
                    bool problems = arguments["problems"]?.GetValue<bool>() == true;
                    return new JsonObject
                    {
                        ["logs"] = new JsonArray([.. (problems ? model.ConsoleProblems : model.Console).TakeLast(count).Select(entry => (JsonNode)new JsonObject
                        {
                            ["time"] = entry.Time.ToString("O"),
                            ["level"] = entry.Level.ToString(),
                            ["category"] = entry.Category,
                            ["message"] = entry.Message,
                        })]),
                    };
                }
            case "diagnostics":
                // the choices about diagnostics, and the session's files
                if (arguments["record"] is JsonNode record)
                {
                    model.RecordDiagnostics = record.GetValue<bool>();
                }
                if (arguments["send"] is JsonNode send)
                {
                    model.SendDiagnostics = send.GetValue<bool>();
                }
                if (arguments["endpoint"] is JsonNode endpoint)
                {
                    model.DiagnosticsEndpoint = endpoint.GetValue<string>();
                }
                if (arguments["flush"]?.GetValue<bool>() == true)
                {
                    model.Telemetry.Flush();
                }
                int? uploaded = arguments["upload"]?.GetValue<bool>() == true ? await model.Telemetry.UploadAsync() : null;
                return new JsonObject
                {
                    ["record"] = model.RecordDiagnostics,
                    ["send"] = model.SendDiagnostics,
                    ["endpoint"] = model.Telemetry.Settings.Endpoint,
                    ["folder"] = model.Telemetry.Folder,
                    ["session"] = model.Telemetry.SessionId,
                    ["logsFile"] = model.Telemetry.LogsFile,
                    ["metricsFile"] = model.Telemetry.MetricsFile,
                    ["warnings"] = model.Telemetry.Warnings,
                    ["errors"] = model.Telemetry.Errors,
                    ["uploaded"] = uploaded,
                    ["uploadError"] = model.Telemetry.LastUploadError,
                };
            case "setup":
                // the setup page as the person fills it in: the fields not given keep what it suggests
                model.SetupFolder = Text(arguments, "folder");
                model.SetupGameFolder = arguments["game"]?.GetValue<string>() ?? model.SetupGameFolder;
                model.SetupFafDataFolder = arguments["faf"]?.GetValue<string>() ?? model.SetupFafDataFolder;
                model.SetupVaultFolder = arguments["vault"]?.GetValue<string>() ?? model.SetupVaultFolder;
                result["setUp"] = await model.SetUpWorkspaceAsync();
                break;
            case "update-assets":
                result["converted"] = await model.ConvertAssetsAsync();
                break;
            case "open":
                await model.OpenAsync(Text(arguments, "path"));
                break;
            case "import":
                await model.ImportMapFolderAsync(Text(arguments, "path"));
                break;
            case "save":
                await model.SaveAsync();
                break;
            case "save-as":
                await model.SaveAsAsync(Text(arguments, "path"));
                break;
            case "version-control":
                // the version control of the open project; enable turns it on with the given choices
                if (arguments["name"] is JsonNode name)
                {
                    model.GitAuthorName = name.GetValue<string>();
                }
                if (arguments["email"] is JsonNode email)
                {
                    model.GitAuthorEmail = email.GetValue<string>();
                }
                if (arguments["lfs"] is JsonNode lfs)
                {
                    model.GitUseLfs = lfs.GetValue<bool>();
                }
                if (arguments["remote"] is JsonNode remote)
                {
                    model.GitRemote = remote.GetValue<string>();
                }
                if (arguments["open"]?.GetValue<bool>() == true || model.Repository is null)
                {
                    await model.OpenVersionControlAsync();
                }
                if (arguments["enable"]?.GetValue<bool>() == true)
                {
                    await model.EnableVersionControlAsync();
                }
                result["versionControl"] = new JsonObject
                {
                    ["git"] = model.Repository?.GitInstalled,
                    ["lfsInstalled"] = model.Repository?.LfsInstalled,
                    ["repository"] = model.Repository?.IsRepository,
                    ["usesLfs"] = model.Repository?.UsesLfs,
                    ["changes"] = model.Repository?.Changes,
                    ["branch"] = model.Repository?.Branch,
                    ["lastCommit"] = model.Repository?.LastCommit,
                    ["remote"] = model.Repository?.Remote,
                    ["pageOpen"] = model.IsVersionControlOpen,
                    ["error"] = model.VersionControlError,
                };
                break;
            case "commit":
                result["commit"] = await model.CommitAsync(Text(arguments, "message"));
                break;
            case "export":
                result["folder"] = await model.ExportAsync(Text(arguments, "output"), arguments["version"]?.GetValue<int>());
                break;
            case "undo":
                result["done"] = model.Undo();
                break;
            case "redo":
                result["done"] = model.Redo();
                break;
            case "goto":
                result["done"] = model.GoTo((int)(Number(arguments, "step") ?? throw new ArgumentException("step is missing (-1 for the map as it was opened)")));
                break;
            case "brush":
                if (Number(arguments, "layer") is double layer)
                {
                    model.SelectPaintTarget((int)layer);
                }
                if (arguments["mode"] is JsonNode mode)
                {
                    model.BrushMode = Enum.Parse<HeightBrushMode>(mode.GetValue<string>(), ignoreCase: true);
                }
                if (arguments["erase"] is JsonNode erase)
                {
                    model.MaskErase = erase.GetValue<bool>();
                }
                model.BrushRadius = Number(arguments, "radius") ?? model.BrushRadius;
                if (model.IsPaintingLayer)
                {
                    model.MaskStrength = Number(arguments, "strength") ?? model.MaskStrength;
                }
                else
                {
                    model.BrushStrength = Number(arguments, "strength") ?? model.BrushStrength;
                }
                model.BrushTarget = Number(arguments, "target") ?? model.BrushTarget;
                break;
            case "stroke":
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
            case "fit":
                window.MapView.Fit();
                break;
            case "gpu-debug":
                window.View3D.Debug = (int)(Number(arguments, "mode") ?? 0);
                break;
            case "tab":
                window.ShownView = Text(arguments, "name");
                break;
            case "camera":
                model.Camera.Look((float?)Number(arguments, "x"), (float?)Number(arguments, "z"), (float?)Number(arguments, "distance"), (float?)Number(arguments, "yaw"), (float?)Number(arguments, "pitch"));
                model.NotifyCameraMoved();
                break;
            case "focus":
                // as F in the 3D view, or control and a click in the top view without a radius; the camera glides there
                if (Number(arguments, "radius") is double radius)
                {
                    model.FocusCamera((float)(Number(arguments, "x") ?? 0), (float)(Number(arguments, "z") ?? 0), (float)radius);
                }
                else
                {
                    model.MoveCameraTo((float)(Number(arguments, "x") ?? 0), (float)(Number(arguments, "z") ?? 0));
                }
                break;
            case "show":
                if (arguments["markers"] is JsonNode markers)
                {
                    model.ShowMarkers = markers.GetValue<bool>();
                }
                if (arguments["pathNodes"] is JsonNode pathNodes)
                {
                    model.ShowPathNodes = pathNodes.GetValue<bool>();
                }
                if (arguments["camera"] is JsonNode camera)
                {
                    model.ShowCamera = camera.GetValue<bool>();
                }
                break;
            case "view":
                window.MapView.Show(Number(arguments, "zoom") ?? window.MapView.Zoom, Number(arguments, "x") ?? 0, Number(arguments, "z") ?? 0);
                break;
            case "screenshot":
                result["file"] = arguments["gpu"]?.GetValue<bool>() == true
                    ? window.ShownView == "3d"
                        ? await window.View3D.CaptureAsync(Text(arguments, "path")).WaitAsync(TimeSpan.FromSeconds(10))
                        : throw new ArgumentException("The 3D view draws only while it shows: tab name=3d first")
                    : Screenshot(Text(arguments, "path"), arguments["map"]?.GetValue<bool>() == true);
                break;
            case "close":
                // after the answer has gone out
                Dispatcher.UIThread.Post(window.CloseWithoutAsking, DispatcherPriority.Background);
                break;
            default:
                throw new ArgumentException($"Unknown command '{command}'; GET /help lists them");
        }

        foreach ((string key, JsonNode? value) in State())
        {
            result[key] = value?.DeepClone();
        }
        result["ok"] = true;
        return result;
    }

    private JsonObject State()
    {
        MapDocument? document = model.Map?.Document;
        return new JsonObject
        {
            ["title"] = model.Title,
            ["stage"] = model.Stage.ToString(),
            ["mode"] = model.SelectedMode?.Definition.Id,
            ["tool"] = model.ActiveTool?.Definition.Id,
            ["toolNotBuilt"] = model.ActiveTool?.Definition.Missing,
            ["palette"] = model.IsPaletteOpen,
            ["workspace"] = model.Workspace?.Folder,
            ["setupError"] = model.SetupError,
            ["assetReasons"] = new JsonArray([.. model.AssetReasons.Select(reason => (JsonNode)JsonValue.Create(reason))]),
            ["conversion"] = model.IsConverting ? model.ConversionText : null,
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
                ["paint"] = model.SelectedPaintTarget?.Name,
                ["mode"] = model.BrushMode.ToString(),
                ["radius"] = model.BrushRadius,
                ["strength"] = model.IsPaintingLayer ? model.MaskStrength : model.BrushStrength,
                ["erase"] = model.MaskErase,
                ["target"] = model.BrushTarget,
            },
            ["zoom"] = window.MapView.Zoom,
            ["tab"] = window.ShownView,
            ["camera"] = new JsonObject
            {
                ["x"] = model.Camera.Now.X,
                ["z"] = model.Camera.Now.Z,
                ["distance"] = model.Camera.Now.Distance,
                ["yaw"] = model.Camera.Now.Yaw,
                ["pitch"] = model.Camera.Now.Pitch,
            },
            ["show"] = new JsonObject { ["markers"] = model.ShowMarkers, ["pathNodes"] = model.ShowPathNodes, ["camera"] = model.ShowCamera },
            ["gpu"] = new JsonObject
            {
                ["renderer"] = window.View3D.Renderer,
                ["error"] = window.View3D.Error,
                ["textures"] = window.View3D.TextureState.Textures,
                ["textureError"] = window.View3D.TextureState.Error,
                ["decals"] = window.View3D.DecalState.Decals,
                ["decalDraws"] = window.View3D.DecalState.Draws,
                ["decalTextures"] = window.View3D.DecalState.Textures,
            },
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

    private static string Text(JsonObject arguments, string name) =>
        arguments[name]?.GetValue<string>() ?? throw new ArgumentException($"{name} is missing");

    private static double? Number(JsonObject arguments, string name) => arguments[name] switch
    {
        null => null,
        JsonValue value when value.TryGetValue(out double number) => number,
        JsonValue value when value.TryGetValue(out string? text) && double.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out double parsed) => parsed,
        _ => throw new ArgumentException($"{name} is not a number"),
    };
}
