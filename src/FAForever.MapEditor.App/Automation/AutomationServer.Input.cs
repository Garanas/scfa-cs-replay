using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.Rendering.Gpu;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;
using Point = Avalonia.Point;

namespace FAForever.MapEditor.App.Automation;

/// <summary>
/// Automation's input as a person gives it, and the reads that help aim it: the mouse, keys, text
/// and drag and drop through the window's own input (<see cref="WindowInput"/>, never the
/// operating system's), commands started without waiting, places of the map in the views' pixels,
/// the objects on the map, and waiting until what the window shows has settled.
/// </summary>
public sealed partial class AutomationServer
{
    private WindowInput? _input;

    private WindowInput Input => _input ??= new WindowInput(window);

    /// <summary>
    /// Automation's input commands and the reads that aim them, for <c>help</c>.
    /// </summary>
    private static readonly string[] InputCommands =
    [
        "mouse {view?: top|3d|window, x, y, button?: left|right|middle, action?: move|down|up|click|doubleClick|drag|wheel, to?: [x, y], steps?, notches?, modifiers?}",
        "key {key: E|Delete|Escape|Ctrl+K|Shift..., action?: press|down|up, modifiers?, repeat?}",
        "type {text}",
        "drag {view?, from: [x, y], to?: [x, y], data?: asset key, button?, steps?, modifiers?}",
        "view-point {x, z, y?} or {ref}",
        "get {ref} or {name}",
        "objects {kind?: markers|pathNodes|props|units|decals|areas|all, name?, near?: [x, z], count?}",
    ];

    /// <summary>
    /// Runs an automation command as the HTTP server does, without one: for the tests and for a
    /// driver in the same process.
    /// </summary>
    public Task<JsonNode> HandleAsync(string command, JsonObject arguments) => Dispatcher.UIThread.CheckAccess()
        ? RunAsync(command, arguments)
        : Dispatcher.UIThread.InvokeAsync(() => RunAsync(command, arguments));

    /// <summary>
    /// The input commands; false for any other.
    /// </summary>
    private async Task<bool> RunInputAsync(string command, JsonObject arguments, JsonObject result)
    {
        switch (command)
        {
            case "mouse":
            {
                Control view = ViewOf(arguments);
                Point at = InWindow(view, Number(arguments, "x") ?? throw new ArgumentException("x is missing"), Number(arguments, "y") ?? throw new ArgumentException("y is missing"));
                RawInputModifiers modifiers = ModifiersOf(arguments);
                MouseButton button = WindowInput.ParseButton(arguments["button"]?.GetValue<string>());
                string action = arguments["action"]?.GetValue<string>() ?? "click";
                switch (action.ToLowerInvariant())
                {
                    case "move":
                        Input.Move(at, modifiers);
                        break;
                    case "down":
                        Input.Move(at, modifiers);
                        Input.Down(at, button, modifiers);
                        break;
                    case "up":
                        Input.Up(at, button, modifiers);
                        break;
                    case "click":
                        Input.Move(at, modifiers);
                        Input.Down(at, button, modifiers);
                        Input.Up(at, button, modifiers);
                        break;
                    case "doubleclick":
                        Input.Move(at, modifiers);
                        for (int i = 0; i < 2; i++)
                        {
                            Input.Down(at, button, modifiers);
                            Input.Up(at, button, modifiers);
                        }
                        break;
                    case "drag":
                        Input.Drag(at, InWindow(view, Pair(arguments, "to")), button, (int)(Number(arguments, "steps") ?? 10), modifiers);
                        break;
                    case "wheel":
                        Input.Move(at, modifiers);
                        Input.Wheel(at, Number(arguments, "notches") ?? 1, modifiers);
                        break;
                    default:
                        throw new ArgumentException($"There is no mouse action '{action}': move, down, up, click, doubleClick, drag or wheel");
                }
                await LetInputSettleAsync();
                result["mouse"] = MouseAnswer();
                return true;
            }
            case "key":
            {
                (Key key, RawInputModifiers modifiers) = WindowInput.ParseKey(Text(arguments, "key"));
                modifiers |= ModifiersOf(arguments);
                switch (arguments["action"]?.GetValue<string>()?.ToLowerInvariant() ?? "press")
                {
                    case "down":
                        // held until up: across mouse actions (shift, X or Z held while dragging)
                        Input.KeyDown(key, modifiers);
                        break;
                    case "up":
                        Input.KeyUp(key, modifiers);
                        break;
                    case "press":
                        int repeat = Math.Max(1, (int)(Number(arguments, "repeat") ?? 1));
                        for (int i = 0; i < repeat; i++)
                        {
                            Input.Key(key, modifiers);
                        }
                        break;
                    default:
                        throw new ArgumentException("The action of a key is press (the default), down or up");
                }
                await LetInputSettleAsync();
                result["focused"] = Focused();
                result["held"] = new JsonArray([.. Input.Held.Select(held => (JsonNode)JsonValue.Create(held.ToString()))]);
                return true;
            }
            case "type":
                Input.Type(Text(arguments, "text"));
                await LetInputSettleAsync();
                result["focused"] = Focused();
                return true;
            case "drag":
            {
                Control view = ViewOf(arguments);
                Point from = InWindow(view, Pair(arguments, "from"));
                Point to = arguments["to"] is null ? from : InWindow(view, Pair(arguments, "to"));
                RawInputModifiers modifiers = ModifiersOf(arguments);
                int steps = (int)(Number(arguments, "steps") ?? 5);
                if (arguments["data"]?.GetValue<string>() is string key)
                {
                    // as a tile dragged from the shelf: the asset's key, dropped where the drag ends
                    result["dropped"] = Input.DropData(from, to, AssetDrag.Of(key), steps, modifiers).ToString();
                }
                else
                {
                    Input.Drag(from, to, WindowInput.ParseButton(arguments["button"]?.GetValue<string>()), steps, modifiers);
                }
                await LetInputSettleAsync();
                result["mouse"] = MouseAnswer();
                return true;
            }
            case "view-point":
                result["point"] = ViewPoint(arguments);
                return true;
            case "get":
            {
                MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
                ObjectRef thing = arguments["name"]?.GetValue<string>() is string name && arguments["ref"] is null
                    ? Named(document, name)
                    : ObjectRef.Parse(Text(arguments, "ref"));
                result["object"] = Describe(document, thing) ?? throw new ArgumentException($"The map has no {thing}");
                return true;
            }
            case "objects":
                result["objects"] = Objects(arguments);
                return true;
            default:
                return await RunToolingAsync(command, arguments, result);
        }
    }

    /// <summary>
    /// <c>run</c> with <c>interactive</c> or <c>wait: false</c>: a command started as a person
    /// starts it (it may ask: a picker, a folder), answered at once when not waited for, so the
    /// question can then be driven with keys. False for a plain <c>run</c>, which waits and asks nothing.
    /// </summary>
    private async Task<bool> StartCommandAsync(string id, JsonObject arguments, JsonObject result)
    {
        bool interactive = arguments["interactive"]?.GetValue<bool>() == true;
        bool wait = arguments["wait"]?.GetValue<bool>() != false;
        arguments.Remove("interactive");
        arguments.Remove("wait");
        if (wait && !interactive)
        {
            return false;
        }
        if (model.Commands.Find(id) is null)
        {
            throw new ArgumentException($"There is no command '{id}'; commands lists them");
        }
        if (wait)
        {
            CommandRun run = await model.Commands.RunAsync(id, arguments, interactive: true);
            result["ran"] = run.Ran;
            result["why"] = run.Why;
            result["result"] = run.Result is null ? null : JsonSerializer.SerializeToNode(run.Result, run.Result.GetType());
            return true;
        }

        // as the menu runs it: what fails shows in the status line
        Task running = interactive
            ? model.RunWithAsync(id, arguments)
            : model.Commands.RunAsync(id, arguments, interactive: false).ContinueWith(task =>
            {
                if (task.Exception is { } exception)
                {
                    model.Logger("Automation").LogError(exception, "The command {Command} failed: {Reason}", id, exception.GetBaseException().Message);
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        result["started"] = id;
        await LetInputSettleAsync();
        result["finished"] = running.IsCompleted;
        return true;
    }

    /// <summary>
    /// <c>ui</c>: the whole window, or with <c>under</c> the part under the control of that name,
    /// label or text.
    /// </summary>
    private JsonArray DescribeUi(JsonObject arguments)
    {
        bool hidden = arguments["hidden"]?.GetValue<bool>() == true;
        if (arguments["under"]?.GetValue<string>() is not { Length: > 0 } under)
        {
            return UiTree.Describe(window, hidden);
        }
        Control control = UiTree.Find(window, under, hidden)
            ?? throw new ArgumentException($"No control is named, labelled or reads '{under}'");
        return UiTree.Describe(control, window, hidden);
    }

    /// <summary>
    /// Waits until what the window shows has settled: no operation busy, the assets read, no
    /// thumbnail being drawn, and (for the 3D view) no model or texture being read, each checked
    /// after a frame. At most 15 seconds.
    /// </summary>
    private async Task<JsonObject> SettleAsync(bool gpu)
    {
        Stopwatch clock = Stopwatch.StartNew();
        string? waiting = "a frame";
        bool clean = false;
        while (clock.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (model.Shows3DView)
            {
                window.View3D.RequestNextFrameRendering();
            }
            await NextFrameAsync();
            waiting = Waiting(gpu || model.Shows3DView);
            if (waiting is null && clean)
            {
                break;
            }
            // the frame may have asked for models or textures: settled is two clean checks in a row
            clean = waiting is null;
            await Task.Delay(50);
        }
        return new JsonObject
        {
            ["settled"] = waiting is null,
            ["milliseconds"] = Math.Round(clock.Elapsed.TotalMilliseconds),
            ["waitingFor"] = waiting,
        };
    }

    private string? Waiting(bool threeD)
    {
        if (model.IsBusy)
        {
            return "a busy operation";
        }
        if (model.Setup.Workspace is not null && !model.Assets.IsLoaded)
        {
            return "the assets";
        }
        if (AssetThumbnail.Cache.Pending > 0)
        {
            return $"{AssetThumbnail.Cache.Pending} thumbnails";
        }
        if (threeD && window.View3D.ObjectState.ModelsLoading > 0)
        {
            return $"{window.View3D.ObjectState.ModelsLoading} models";
        }
        if (threeD && window.View3D.TexturesLoading > 0)
        {
            return $"{window.View3D.TexturesLoading} textures";
        }
        return null;
    }

    /// <summary>
    /// The next frame of the window; at most a second (a window that does not draw).
    /// </summary>
    private async Task NextFrameAsync()
    {
        TaskCompletionSource drawn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.RequestAnimationFrame(_ => drawn.TrySetResult());
        await Task.WhenAny(drawn.Task, Task.Delay(1000));
    }

    /// <summary>
    /// Lets what the input posted to the dispatcher (focus, layout) run before the answer.
    /// </summary>
    private static async Task LetInputSettleAsync() =>
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);

    /// <summary>
    /// The views' places in the window and sizes, for aiming the mouse: places are in the window's
    /// device-independent pixels; a view's pixels on the card are its size times the scaling.
    /// </summary>
    private JsonObject ViewsState()
    {
        double scaling = window.RenderScaling;
        JsonObject View(Control view)
        {
            Point at = view.TranslatePoint(default, window) ?? default;
            return new JsonObject
            {
                ["visible"] = view.IsEffectivelyVisible,
                ["bounds"] = new JsonArray(Math.Round(at.X, 1), Math.Round(at.Y, 1), Math.Round(view.Bounds.Width, 1), Math.Round(view.Bounds.Height, 1)),
                ["pixels"] = new JsonArray((int)(view.Bounds.Width * scaling), (int)(view.Bounds.Height * scaling)),
            };
        }
        return new JsonObject
        {
            ["scaling"] = scaling,
            ["window"] = new JsonArray(Math.Round(window.Bounds.Width), Math.Round(window.Bounds.Height)),
            ["top"] = View(window.MapView),
            ["3d"] = View(window.View3D),
            ["mouse"] = _input is null ? null : new JsonArray(Math.Round(_input.Position.X, 1), Math.Round(_input.Position.Y, 1)),
            ["keysHeld"] = new JsonArray([.. (_input?.Held ?? []).Select(held => (JsonNode)JsonValue.Create(held.ToString()))]),
        };
    }

    /// <summary>
    /// What is still being read: the asset catalogue (and its count per kind), thumbnails, the 3D
    /// view's models and textures. <c>screenshot settle=true</c> waits for them.
    /// </summary>
    private JsonObject LoadingState()
    {
        AssetCatalog catalog = model.Assets.Catalog;
        return new JsonObject
        {
            ["assetsLoaded"] = model.Assets.IsLoaded,
            ["assets"] = new JsonObject([.. catalog.Entries.GroupBy(entry => entry.Kind).OrderBy(group => group.Key)
                .Select(group => KeyValuePair.Create(group.Key.ToString(), (JsonNode?)group.Count()))]),
            ["thumbnailsPending"] = AssetThumbnail.Cache.Pending,
            ["modelsLoading"] = window.View3D.ObjectState.ModelsLoading,
            ["texturesLoading"] = window.View3D.TexturesLoading,
        };
    }

    /// <summary>
    /// Where a place of the map shows in the views, in each view's own pixels (what <c>mouse</c>
    /// and <c>pick</c> take): the top view by its zoom and offset, the 3D view through the camera;
    /// null in 3D for a place behind the camera.
    /// </summary>
    private JsonObject ViewPoint(JsonObject arguments)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        Vector3 place;
        if (arguments["ref"]?.GetValue<string>() is string text)
        {
            ObjectRef thing = ObjectRef.Parse(text);
            place = document.PlaceOf(thing) ?? throw new ArgumentException($"The map has no {thing}");
        }
        else
        {
            float x = (float)(Number(arguments, "x") ?? throw new ArgumentException("x (or ref) is missing"));
            float z = (float)(Number(arguments, "z") ?? throw new ArgumentException("z is missing"));
            place = new Vector3(x, (float)(Number(arguments, "y") ?? document.Terrain.Heightmap.HeightAt(x, z)), z);
        }

        Point top = window.MapView.MapToControl(place.X, place.Z);
        Size topSize = window.MapView.Bounds.Size;
        Point? threeD = Project(model.Camera, window.View3D.Bounds.Size, place);
        Size size = window.View3D.Bounds.Size;
        return new JsonObject
        {
            ["place"] = new JsonArray(Round(place.X), Round(place.Y), Round(place.Z)),
            ["top"] = new JsonObject
            {
                ["x"] = Round(top.X),
                ["y"] = Round(top.Y),
                ["inside"] = new Rect(topSize).Contains(top),
            },
            ["3d"] = threeD is Point p ? new JsonObject
            {
                ["x"] = Round(p.X),
                ["y"] = Round(p.Y),
                ["inside"] = new Rect(size).Contains(p),
            } : null,
        };
    }

    /// <summary>
    /// The point of a view of <paramref name="size"/> where the camera shows a place; null behind it.
    /// </summary>
    public static Point? Project(EditorCamera camera, Size size, Vector3 place)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            // a view never laid out (hidden since the start) shows nothing
            return null;
        }
        float aspect = (float)(size.Width / Math.Max(1, size.Height));
        Vector4 clip = Vector4.Transform(new Vector4(place, 1), camera.ViewProjection(aspect));
        if (clip.W <= 1e-6f || !float.IsFinite(clip.X / clip.W) || !float.IsFinite(clip.Y / clip.W))
        {
            return null;
        }
        return new Point((clip.X / clip.W + 1) / 2 * size.Width, (1 - clip.Y / clip.W) / 2 * size.Height);
    }

    /// <summary>
    /// One thing on the map: its kind, place, heading and what it is.
    /// </summary>
    private JsonObject? Describe(MapDocument document, ObjectRef thing)
    {
        object? found = document.Find(thing);
        if (found is null)
        {
            return null;
        }
        Vector3 place = document.PlaceOf(thing)!.Value;
        JsonObject node = new JsonObject
        {
            ["ref"] = thing.ToString(),
            ["kind"] = thing.Kind.ToString(),
            ["place"] = new JsonArray(Round(place.X), Round(place.Y), Round(place.Z)),
            ["heading"] = document.HeadingOf(thing) is float heading ? Round(heading * 180 / MathF.PI) : null,
            ["selected"] = model.Selection.Contains(thing),
        };
        switch (found)
        {
            case Prop prop:
                node["blueprint"] = prop.Blueprint;
                node["scale"] = Vector(prop.Scale);
                node["rotation"] = new JsonArray(Vector(prop.RotationX), Vector(prop.RotationY), Vector(prop.RotationZ));
                break;
            case PlacedUnit unit:
                node["name"] = unit.Unit.Name;
                node["blueprint"] = unit.Unit.Blueprint;
                node["army"] = unit.Army;
                node["group"] = string.Join("/", unit.Group);
                node["wreck"] = UnitWrecks.IsWreck(unit);
                node["orientation"] = Vector(unit.Unit.Orientation);
                break;
            case Marker marker:
                node["name"] = marker.Name;
                node["type"] = marker is AiMarker ai ? ai.Type : marker.GetType().Name;
                node["orientation"] = Vector(marker.Orientation);
                if (marker is PathNode pathNode)
                {
                    node["layer"] = pathNode.Layer.ToString();
                    node["links"] = new JsonArray([.. pathNode.AdjacentTo.Select(id => (JsonNode)JsonValue.Create(id))]);
                }
                break;
            case Decal decal:
                node["type"] = decal.Type.ToString();
                node["textures"] = new JsonArray([.. decal.Textures.Where(texture => texture.Length > 0).Select(texture => (JsonNode)JsonValue.Create(texture))]);
                node["position"] = Vector(decal.Position);
                node["scale"] = Vector(decal.Scale);
                node["rotation"] = Vector(decal.Rotation);
                break;
            case Area area:
                node["name"] = area.Name;
                node["rectangle"] = new JsonArray(area.X0, area.Z0, area.X1, area.Z1);
                break;
        }
        return node;
    }

    /// <summary>
    /// A few things on the map, nearest a place first when given, to aim the camera or the mouse at.
    /// </summary>
    private JsonObject Objects(JsonObject arguments)
    {
        MapDocument document = model.ShownDocument ?? throw new InvalidOperationException("No map is open");
        string kind = arguments["kind"]?.GetValue<string>() ?? "all";
        ObjectKinds kinds = kind.ToLowerInvariant() switch
        {
            "all" => ObjectKinds.All,
            "markers" or "marker" => ObjectKinds.Markers,
            "pathnodes" or "pathnode" => ObjectKinds.PathNodes,
            "props" or "prop" => ObjectKinds.Props,
            "units" or "unit" => ObjectKinds.Units,
            "decals" or "decal" => ObjectKinds.Decals,
            "areas" or "area" => ObjectKinds.Areas,
            _ => throw new ArgumentException($"There is no kind '{kind}': markers, pathNodes, props, units, decals, areas or all"),
        };
        int count = Math.Clamp((int)(Number(arguments, "count") ?? 20), 1, 1000);
        List<ObjectRef> all = [.. document.All(kinds)];
        if (arguments["name"]?.GetValue<string>() is { Length: > 0 } name)
        {
            // by part of the name, blueprint or texture, ignoring case
            all = [.. all.Where(thing => WhatOf(document, thing)?.Contains(name, StringComparison.OrdinalIgnoreCase) == true)];
        }
        IEnumerable<ObjectRef> chosen = all;
        if (arguments["near"] is JsonArray)
        {
            (double x, double z) = Pair(arguments, "near");
            Vector2 near = new Vector2((float)x, (float)z);
            chosen = all.OrderBy(thing => document.PlaceOf(thing) is Vector3 place ? Vector2.DistanceSquared(new Vector2(place.X, place.Z), near) : float.MaxValue);
        }
        return new JsonObject
        {
            ["total"] = all.Count,
            ["items"] = new JsonArray([.. chosen.Take(count).Select(thing =>
            {
                Vector3 place = document.PlaceOf(thing) ?? default;
                return (JsonNode)new JsonObject
                {
                    ["ref"] = thing.ToString(),
                    ["place"] = new JsonArray(Round(place.X), Round(place.Y), Round(place.Z)),
                    ["what"] = WhatOf(document, thing),
                };
            })]),
        };
    }

    /// <summary>
    /// The view a place of the mouse is given in: <c>top</c>, <c>3d</c> or the whole <c>window</c> (the default).
    /// </summary>
    private Control ViewOf(JsonObject arguments)
    {
        string name = arguments["view"]?.GetValue<string>()?.ToLowerInvariant() ?? "window";
        Control view = name switch
        {
            "window" => window,
            "top" => window.MapView,
            "3d" => window.View3D,
            _ => throw new ArgumentException($"There is no view '{name}': top, 3d or window"),
        };
        if (!view.IsEffectivelyVisible)
        {
            throw new ArgumentException($"The {name} view is hidden: tab name={name} first");
        }
        return view;
    }

    private Point InWindow(Control view, double x, double y) =>
        view.TranslatePoint(new Point(x, y), window) ?? throw new InvalidOperationException("The view is not in the window");

    private Point InWindow(Control view, (double X, double Y) point) => InWindow(view, point.X, point.Y);

    private JsonObject MouseAnswer() => new JsonObject
    {
        ["at"] = new JsonArray(Math.Round(Input.Position.X, 1), Math.Round(Input.Position.Y, 1)),
        ["hit"] = HitAt(Input.Position.X, Input.Position.Y),
    };

    /// <summary>
    /// The control with the keyboard: its type and name or label.
    /// </summary>
    private string? Focused() => window.FocusManager?.GetFocusedElement() is Control control
        ? $"{control.GetType().Name} {control.Name ?? Avalonia.Automation.AutomationProperties.GetName(control)}".TrimEnd()
        : null;

    private static RawInputModifiers ModifiersOf(JsonObject arguments) => arguments["modifiers"] switch
    {
        null => RawInputModifiers.None,
        JsonArray names => WindowInput.ParseModifiers(names.Select(name => name!.GetValue<string>())),
        JsonNode name => WindowInput.ParseModifiers([name.GetValue<string>()]),
    };

    private static (double X, double Y) Pair(JsonObject arguments, string name) => arguments[name] is JsonArray { Count: 2 } pair
        ? (Number(new JsonObject { [name] = pair[0]!.DeepClone() }, name)!.Value, Number(new JsonObject { [name] = pair[1]!.DeepClone() }, name)!.Value)
        : throw new ArgumentException($"{name}: [x, y] is missing");

    private static double Round(double value) => Math.Round(value, 2);

    private static JsonArray Vector(Vector3 vector) => new JsonArray(Round(vector.X), Round(vector.Y), Round(vector.Z));
}
