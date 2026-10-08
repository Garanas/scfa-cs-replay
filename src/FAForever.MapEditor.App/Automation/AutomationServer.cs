using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Automation;

/// <summary>
/// Lets a program drive the running editor, as Playwright drives a browser: commands over HTTP on
/// <c>http://localhost:port/</c>, each run on the user interface's thread as if chosen from the
/// menu, each answered with the editor's state as JSON. <c>faf-map editor</c> is its client.
/// </summary>
/// <remarks>
/// <para>
/// The commands: <c>GET /state</c>, <c>GET /height?x=&amp;z=</c>, <c>GET /mask?layer=&amp;x=&amp;z=</c> (0 to 255), <c>GET /help</c>, and with a JSON
/// body <c>POST /open {path}</c>, <c>/import {path}</c>, <c>/save</c>, <c>/save-as {path}</c>,
/// <c>/export {output, version?}</c>, <c>/undo</c>, <c>/redo</c>, <c>/goto {step}</c> (a step of the history, -1 for the map as opened),
/// <c>/brush {layer?, mode?, radius?, strength?, target?, erase?}</c> (layer 0 paints the heights, 1 to 8 a layer's mask), <c>/stroke {points: [[x, z], ...]}</c> (in
/// ogrids), <c>/fit</c>, <c>/tab {name}</c> (<c>top</c> or <c>3d</c>), <c>/camera {x?, z?, distance?, yaw?, pitch?}</c> (the 3D view's, in ogrids and degrees), <c>/show {markers?, pathNodes?}</c> (what the view shows), <c>/view {zoom, x, z}</c> (centre the view on a place of the map),
/// <c>/screenshot {path, map?}</c> (the window, or with <c>map: true</c> the map alone, as PNG) and
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
                return new JsonObject { ["commands"] = new JsonArray("state", "height?x=&z=", "mask?layer=&x=&z=", "open {path}", "import {path}", "save", "save-as {path}", "export {output, version?}", "undo", "redo", "goto {step}", "brush {layer?, mode?, radius?, strength?, target?, erase?}", "stroke {points: [[x, z], ...]}", "fit", "tab {name: top|3d}", "camera {x?, z?, distance?, yaw?, pitch?}", "show {markers?, pathNodes?}", "view {zoom, x, z}", "screenshot {path, map?}", "close") };
            case "state":
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
            case "fit":
                window.MapView.Fit();
                break;
            case "tab":
                window.ShownView = Text(arguments, "name");
                break;
            case "camera":
                window.View3D.Look(Number(arguments, "x"), Number(arguments, "z"), Number(arguments, "distance"), Number(arguments, "yaw"), Number(arguments, "pitch"));
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
                break;
            case "view":
                window.MapView.Show(Number(arguments, "zoom") ?? window.MapView.Zoom, Number(arguments, "x") ?? 0, Number(arguments, "z") ?? 0);
                break;
            case "screenshot":
                result["file"] = Screenshot(Text(arguments, "path"), arguments["map"]?.GetValue<bool>() == true);
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
        MapDocument? document = model.Workspace?.Document;
        return new JsonObject
        {
            ["title"] = model.Title,
            ["status"] = model.Status,
            ["projectFolder"] = model.Workspace?.ProjectFolder,
            ["map"] = document is null ? null : new JsonObject
            {
                ["name"] = document.Scenario.Name,
                ["folder"] = document.Folder.ToString(),
                ["width"] = document.Width,
                ["height"] = document.Height,
                ["scmapVersion"] = document.Terrain.ScmapVersion,
            },
            ["modified"] = model.Workspace?.Session.IsModified ?? false,
            ["busy"] = model.IsBusy,
            ["undo"] = model.Workspace?.Session.UndoDescription,
            ["redo"] = model.Workspace?.Session.RedoDescription,
            ["history"] = new JsonArray([.. (model.Workspace?.Session.History ?? []).Select(step => (JsonNode)JsonValue.Create(step.IsCurrent ? $"> {step.Description}" : step.Description))]),
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
                ["x"] = window.View3D.Camera.X,
                ["z"] = window.View3D.Camera.Z,
                ["distance"] = window.View3D.Camera.Distance,
                ["yaw"] = window.View3D.Camera.Yaw,
                ["pitch"] = window.View3D.Camera.Pitch,
            },
            ["show"] = new JsonObject { ["markers"] = model.ShowMarkers, ["pathNodes"] = model.ShowPathNodes },
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
