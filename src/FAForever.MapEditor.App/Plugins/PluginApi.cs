using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using Lua;
using Lua.Standard;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Plugins;

/// <summary>
/// What the Lua API of the plugins asks of the editor.
/// </summary>
internal interface IPluginHost
{
    ILogger Log { get; }

    AssetCatalog? Catalog { get; }

    SelectionViewModel Selection { get; }

    void SetStatus(string text);

    /// <summary>
    /// Runs a command of the registry without a person.
    /// </summary>
    Task<CommandRun> RunCommandAsync(string id, JsonObject arguments);

    /// <summary>
    /// A field's value as text: the user's, else its default.
    /// </summary>
    string ValueOf(LuaPlugin plugin, PluginField field);

    /// <summary>
    /// The parts of the map edits are mirrored into, the identity first: only it while the symmetry
    /// does not mirror edits (user story F-12).
    /// </summary>
    IReadOnlyList<FAForever.FileFormats.Map.MapSymmetryTransform> Mirrors(MapDocument document);

    /// <summary>
    /// Adds the images of things just placed in the other parts of the map while the symmetry mirrors edits.
    /// </summary>
    MapDocument MirrorPlaced(MapDocument document, IReadOnlyList<ObjectRef> placed);
}

/// <summary>
/// The Lua API of the plugins (user story F-15): a facade over the editor, never the document.
/// Reads give plain tables of values; edits go into the run's transaction (<see cref="PluginRun"/>),
/// so a run is one step of undo and a failing run changes nothing; every other operation is a
/// command of the registry (<c>editor.run</c>). The Lua has the base, string, table, math, bit32
/// and coroutine libraries, without <c>dofile</c> and <c>loadfile</c>: no file system or operating
/// system beyond the plugin's own folder (<c>plugin.read</c>, <c>require</c>). The guide of the App
/// documents the API for plugin authors.
/// </summary>
internal static class PluginApi
{
    /// <summary>
    /// A Lua state for a plugin, with the libraries a plugin may use and the tables <c>plugin</c>,
    /// <c>editor</c> and <c>map</c>.
    /// </summary>
    public static LuaState Create(LuaPlugin plugin, IPluginHost host)
    {
        LuaState state = LuaState.Create();
        state.OpenBasicLibrary();
        state.OpenStringLibrary();
        state.OpenTableLibrary();
        state.OpenMathLibrary();
        state.OpenBitwiseLibrary();
        state.OpenCoroutineLibrary();
        LuaTable globals = state.Environment;
        // the base library reads files through these; a plugin reads only its own folder
        globals["dofile"] = LuaValue.Nil;
        globals["loadfile"] = LuaValue.Nil;
        globals["print"] = Function("print", (context, plugin_) => Log(host, plugin, LogLevel.Information, context), plugin);
        globals["require"] = Require(plugin);
        globals["plugin"] = PluginTable(plugin, host);
        globals["editor"] = EditorTable(plugin, host);
        globals["map"] = MapTable(plugin, host);
        return state;
    }

    #region plugin

    private static LuaTable PluginTable(LuaPlugin plugin, IPluginHost host)
    {
        LuaTable table = new LuaTable();
        table["id"] = plugin.Id;
        table["name"] = plugin.Manifest.Name;
        table["command"] = Function("plugin.command", (context, ctx) =>
        {
            LuaTable spec = Loading(ctx, context, "plugin.command");
            string id = LocalId(LuaConvert.Text(spec, "id"), "plugin.command");
            LuaFunction run = LuaConvert.Function(spec, "run") ?? throw new ArgumentException("plugin.command needs run, a function");
            if (plugin.Commands.Any(command => command.Id == FullId(plugin, id)))
            {
                throw new ArgumentException($"The plugin registered a command '{id}' already");
            }
            plugin.Commands.Add(new PluginCommand(FullId(plugin, id), LuaConvert.Text(spec, "title") ?? id, LuaConvert.Text(spec, "description"), run));
            return FullId(plugin, id);
        }, plugin);
        table["check"] = Function("plugin.check", (context, ctx) =>
        {
            LuaTable spec = Loading(ctx, context, "plugin.check");
            string id = LocalId(LuaConvert.Text(spec, "id"), "plugin.check");
            LuaFunction run = LuaConvert.Function(spec, "run") ?? throw new ArgumentException("plugin.check needs run, a function");
            if (plugin.Checks.Any(check => check.Id == FullId(plugin, id)))
            {
                throw new ArgumentException($"The plugin registered a check '{id}' already");
            }
            plugin.Checks.Add(new PluginCheck(FullId(plugin, id), LuaConvert.Text(spec, "title") ?? id, run));
            return FullId(plugin, id);
        }, plugin);
        table["field"] = Function("plugin.field", (context, ctx) =>
        {
            AddField(plugin, Loading(ctx, context, "plugin.field"));
            return LuaValue.Nil;
        }, plugin);
        table["tool"] = Function("plugin.tool", (context, ctx) =>
        {
            LuaTable spec = Loading(ctx, context, "plugin.tool");
            string id = LocalId(LuaConvert.Text(spec, "id"), "plugin.tool");
            if (plugin.Tools.Any(tool => tool.Id == FullId(plugin, id)))
            {
                throw new ArgumentException($"The plugin registered a tool '{id}' already");
            }
            string mode = LuaConvert.Text(spec, "mode") ?? "terrain";
            if (!EditorModes.All.Any(m => m.Id == mode))
            {
                throw new ArgumentException($"There is no mode '{mode}': {string.Join(", ", EditorModes.All.Select(m => m.Id))}");
            }
            LuaFunction? press = LuaConvert.Function(spec, "press");
            LuaFunction? drag = LuaConvert.Function(spec, "drag");
            LuaFunction? release = LuaConvert.Function(spec, "release");
            if (press is null && drag is null && release is null)
            {
                throw new ArgumentException("plugin.tool needs press, drag or release, a function");
            }
            // its options: the plugin's fields by key, or fields made here
            List<string> fields = [];
            foreach (LuaValue item in LuaConvert.Field(spec, "fields") is { Type: LuaValueType.Table } list ? LuaConvert.Items(list.Read<LuaTable>()) : [])
            {
                fields.Add(item.Type == LuaValueType.Table ? AddField(plugin, item.Read<LuaTable>()).Key : item.Read<string>());
            }
            LuaValue radius = LuaConvert.Field(spec, "radius");
            string? radiusField = radius.Type == LuaValueType.String ? radius.Read<string>() : null;
            foreach (string key in fields.Append(radiusField).OfType<string>())
            {
                if (!plugin.Fields.Any(field => field.Key == key))
                {
                    throw new ArgumentException($"The tool '{id}' names a field '{key}' the plugin does not have: plugin.field first, or give the field's table");
                }
            }
            plugin.Tools.Add(new PluginToolSpec(FullId(plugin, id), LuaConvert.Text(spec, "title") ?? id, mode, LuaConvert.Text(spec, "description"),
                radius.Type == LuaValueType.Number ? Math.Max(0, radius.Read<double>()) : 0, radiusField, fields, press, drag, release,
                LuaConvert.Field(spec, "symmetry") is not { Type: LuaValueType.Boolean } symmetry || symmetry.Read<bool>()));
            return FullId(plugin, id);
        }, plugin);
        table["value"] = Function("plugin.value", (context, ctx) =>
        {
            string key = context.GetArgument<string>(0);
            PluginField field = plugin.Fields.FirstOrDefault(f => f.Key == key) ?? throw new ArgumentException($"The plugin has no field '{key}'");
            string text = host.ValueOf(plugin, field);
            return field.Type switch
            {
                "number" => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : 0,
                "bool" => text == "true",
                _ => text,
            };
        }, plugin);
        table["read"] = Function("plugin.read", (context, ctx) => File.ReadAllText(InFolder(plugin, context.GetArgument<string>(0))), plugin);
        return table;
    }

    /// <summary>
    /// A field of the plugin's panel from its table (<c>plugin.field{...}</c>, or a tool's <c>fields</c>).
    /// </summary>
    private static PluginField AddField(LuaPlugin plugin, LuaTable spec)
    {
        string key = LocalId(LuaConvert.Text(spec, "key"), "plugin.field", "key");
        string type = LuaConvert.Text(spec, "type") ?? "text";
        if (type is not ("number" or "text" or "bool" or "choice"))
        {
            throw new ArgumentException($"A field's type is number, text, bool or choice, not '{type}'");
        }
        IReadOnlyList<string> choices = LuaConvert.Texts(LuaConvert.Field(spec, "choices"));
        if (type == "choice" && choices.Count == 0)
        {
            throw new ArgumentException($"The field '{key}' is a choice without choices");
        }
        LuaValue fallback = LuaConvert.Field(spec, "default");
        string value = fallback.Type switch
        {
            LuaValueType.Nil => type switch { "number" => "0", "bool" => "false", "choice" => choices[0], _ => "" },
            LuaValueType.Number => fallback.Read<double>().ToString(CultureInfo.InvariantCulture),
            LuaValueType.Boolean => fallback.Read<bool>() ? "true" : "false",
            _ => fallback.ToString(),
        };
        if (plugin.Fields.Any(field => field.Key == key))
        {
            throw new ArgumentException($"The plugin has a field '{key}' already");
        }
        PluginField made = new PluginField(key, LuaConvert.Text(spec, "label") ?? key, type, value,
            LuaConvert.Number(spec, "min"), LuaConvert.Number(spec, "max"), choices, LuaConvert.Text(spec, "help"));
        plugin.Fields.Add(made);
        return made;
    }

    /// <summary>
    /// <c>require("name")</c>: <c>name.lua</c> in the plugin's folder (dots are folders), run once.
    /// </summary>
    private static LuaFunction Require(LuaPlugin plugin)
    {
        Dictionary<string, LuaValue> loaded = new Dictionary<string, LuaValue>(StringComparer.Ordinal);
        return new LuaFunction("require", async (context, cancellation) =>
        {
            string name = context.GetArgument<string>(0);
            if (!loaded.TryGetValue(name, out LuaValue module))
            {
                string file = InFolder(plugin, name.Replace('.', '/') + ".lua");
                LuaValue[] results = await context.State.DoStringAsync(await File.ReadAllTextAsync(file, cancellation), name + ".lua", cancellation);
                module = results.Length > 0 && results[0].Type != LuaValueType.Nil ? results[0] : true;
                loaded[name] = module;
            }
            return context.Return(module);
        });
    }

    private static LuaTable Loading(PluginContext context, LuaFunctionExecutionContext call, string function) =>
        context.IsLoading
            ? call.GetArgument<LuaTable>(0)
            : throw new InvalidOperationException($"{function} works only while plugin.lua is loaded, at its top level");

    private static string LocalId(string? id, string function, string what = "id") =>
        id is { Length: > 0 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
            ? id
            : throw new ArgumentException($"{function} needs {what}, of letters, digits, dashes and underscores");

    public static string FullId(LuaPlugin plugin, string id) => $"plugin.{plugin.Id}.{id}";

    /// <summary>
    /// A file of the plugin's folder by its relative path; nothing outside it.
    /// </summary>
    private static string InFolder(LuaPlugin plugin, string relative)
    {
        string path = Path.GetFullPath(Path.Combine(plugin.Folder, relative));
        string root = plugin.Folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"'{relative}' is outside the plugin's folder: a plugin reads only its own files");
        }
        return path;
    }

    #endregion

    #region editor

    private static LuaTable EditorTable(LuaPlugin plugin, IPluginHost host)
    {
        LuaTable table = new LuaTable();
        table["log"] = Function("editor.log", (context, _) => Log(host, plugin, LogLevel.Information, context), plugin);
        table["warn"] = Function("editor.warn", (context, _) => Log(host, plugin, LogLevel.Warning, context), plugin);
        table["error"] = Function("editor.error", (context, _) => Log(host, plugin, LogLevel.Error, context), plugin);
        table["status"] = Function("editor.status", (context, _) =>
        {
            host.SetStatus(context.GetArgument<string>(0));
            return LuaValue.Nil;
        }, plugin);
        table["on"] = Function("editor.on", (context, _) =>
        {
            string name = context.GetArgument<string>(0);
            if (!PluginsViewModel.Events.Contains(name))
            {
                throw new ArgumentException($"There is no event '{name}': {string.Join(", ", PluginsViewModel.Events)}");
            }
            LuaFunction handler = context.GetArgument<LuaFunction>(1);
            if (!plugin.Handlers.TryGetValue(name, out List<LuaFunction>? handlers))
            {
                plugin.Handlers[name] = handlers = [];
            }
            handlers.Add(handler);
            return LuaValue.Nil;
        }, plugin);
        table["selection"] = Function("editor.selection", (context, _) =>
            LuaConvert.List(host.Selection.Items.Select(thing => (LuaValue)thing.ToString())), plugin);
        table["select"] = Function("editor.select", (context, ctx) =>
        {
            ctx.RequireRun();
            host.Selection.Set(Refs(context.GetArgument(0)));
            return LuaValue.Nil;
        }, plugin);
        table["assets"] = Function("editor.assets", (context, _) => Assets(host, context.HasArgument(0) ? context.GetArgument<LuaTable>(0) : new LuaTable()), plugin);
        table["run"] = new LuaFunction("editor.run", async (context, cancellation) =>
        {
            PluginContext ctx = ContextOf(plugin);
            PluginRun run = ctx.RequireRun();
            string id = context.GetArgument<string>(0);
            if (id.StartsWith("plugin.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A plugin cannot run a plugin's command");
            }
            JsonObject arguments = context.HasArgument(1) && context.GetArgument(1).Type != LuaValueType.Nil
                ? LuaConvert.ToJson(context.GetArgument(1)) as JsonObject ?? throw new ArgumentException("editor.run takes the arguments as a table of names")
                : [];
            run.Flush();
            ctx.Clock.Pause();
            CommandRun result;
            try
            {
                result = await host.RunCommandAsync(id, arguments);
            }
            finally
            {
                ctx.Clock.Resume();
            }
            if (!result.Ran)
            {
                throw new InvalidOperationException($"{id} did not run: {result.Why}");
            }
            return context.Return(LuaConvert.ToLua(result.Result));
        });
        return table;
    }

    private static LuaValue Log(IPluginHost host, LuaPlugin plugin, LogLevel level, LuaFunctionExecutionContext context)
    {
        StringBuilder text = new StringBuilder();
        for (int i = 0; i < context.ArgumentCount; i++)
        {
            if (i > 0)
            {
                text.Append(' ');
            }
            text.Append(context.GetArgument(i).ToString());
        }
        host.Log.Log(level, "{Plugin}: {Message}", plugin.Id, text.ToString());
        return LuaValue.Nil;
    }

    private static LuaValue Assets(IPluginHost host, LuaTable filter)
    {
        AssetCatalog catalog = host.Catalog ?? AssetCatalog.Empty;
        string? kind = LuaConvert.Text(filter, "kind");
        string? search = LuaConvert.Text(filter, "search");
        int limit = (int)Math.Clamp(LuaConvert.Number(filter, "limit") ?? 200, 1, 5000);
        AssetKind? wanted = kind is null ? null : Enum.TryParse(kind, ignoreCase: true, out AssetKind parsed)
            ? parsed
            : throw new ArgumentException($"There is no kind of asset '{kind}': {string.Join(", ", Enum.GetNames<AssetKind>().Select(n => n.ToLowerInvariant()))}");
        return LuaConvert.List(catalog.Entries
            .Where(entry => wanted is null || entry.Kind == wanted)
            .Where(entry => search is null || entry.Key.Contains(search, StringComparison.OrdinalIgnoreCase) || entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(entry =>
            {
                LuaTable item = new LuaTable();
                item["key"] = entry.Key;
                item["path"] = "/" + entry.Key;
                item["name"] = entry.Name;
                item["kind"] = entry.Kind.ToString().ToLowerInvariant();
                item["category"] = entry.Category;
                item["reclaimMass"] = entry.ReclaimMass;
                item["reclaimEnergy"] = entry.ReclaimEnergy;
                if (entry.MassCost is double mass)
                {
                    item["massCost"] = mass;
                }
                if (entry.EnergyCost is double energy)
                {
                    item["energyCost"] = energy;
                }
                return (LuaValue)item;
            }));
    }

    #endregion

    #region map

    private static LuaTable MapTable(LuaPlugin plugin, IPluginHost host)
    {
        LuaTable table = new LuaTable();
        table["name"] = Function("map.name", (context, ctx) => ctx.RequireDocument().Scenario.Name, plugin);
        table["size"] = Multi("map.size", (context, ctx) =>
        {
            MapDocument document = ctx.RequireDocument();
            return context.Return(document.Width, document.Height);
        }, plugin);
        table["height"] = Function("map.height", (context, ctx) =>
            ctx.RequireDocument().Terrain.Heightmap.HeightAt((float)context.GetArgument<double>(0), (float)context.GetArgument<double>(1)), plugin);
        table["slope"] = Function("map.slope", (context, ctx) =>
            PaintLimits.SlopeAt(ctx.RequireDocument().Terrain.Heightmap, (float)context.GetArgument<double>(0), (float)context.GetArgument<double>(1)), plugin);
        table["water"] = Function("map.water", (context, ctx) =>
            ctx.RequireDocument().Water is { HasWater: true } water ? water.Elevation : LuaValue.Nil, plugin);
        table["markers"] = Function("map.markers", (context, ctx) =>
        {
            MapDocument document = ctx.RequireDocument();
            string? kind = context.HasArgument(0) && context.GetArgument(0).Type == LuaValueType.String ? context.GetArgument<string>(0) : null;
            return LuaConvert.List(document.Markers.All
                .Where(marker => kind is null || MarkerKind(marker) == kind)
                .Select(marker => (LuaValue)Describe(document, ObjectRef.Marker(marker.Id), marker)));
        }, plugin);
        table["props"] = Function("map.props", (context, ctx) =>
        {
            // made straight from the props, the most numerous things (8304 on Archsimkats Valley)
            System.Collections.Immutable.ImmutableArray<Prop> props = ctx.RequireDocument().Props;
            LuaTable list = new LuaTable(props.Length, 0);
            for (int i = 0; i < props.Length; i++)
            {
                list[i + 1] = PropTable(props[i]);
            }
            return list;
        }, plugin);
        table["units"] = Function("map.units", (context, ctx) => All(ctx.RequireDocument(), document => document.AllUnits().Select(unit => ObjectRef.Unit(unit.Unit.Name))), plugin);
        table["decals"] = Function("map.decals", (context, ctx) => All(ctx.RequireDocument(), document => document.Decals.Select(decal => ObjectRef.Decal(decal.Id))), plugin);
        table["areas"] = Function("map.areas", (context, ctx) => All(ctx.RequireDocument(), document => document.Markers.Areas.Select(area => ObjectRef.Area(area.Name))), plugin);
        table["armies"] = Function("map.armies", (context, ctx) =>
        {
            MapDocument document = ctx.RequireDocument();
            return LuaConvert.List(document.Armies.Select(army =>
            {
                LuaTable item = new LuaTable();
                item["name"] = army.Name;
                item["playable"] = document.Scenario.PlayableArmies.Contains(army.Name);
                return (LuaValue)item;
            }));
        }, plugin);
        table["get"] = Function("map.get", (context, ctx) =>
        {
            MapDocument document = ctx.RequireDocument();
            ObjectRef thing = ObjectRef.Parse(context.GetArgument<string>(0));
            return document.Find(thing) is object found ? Describe(document, thing, found) : LuaValue.Nil;
        }, plugin);
        table["revision"] = Function("map.revision", (context, ctx) =>
        {
            MapDocument document = ctx.RequireDocument();
            string part = context.HasArgument(0) ? context.GetArgument<string>(0) : "map";
            return RevisionOf(part switch
            {
                "map" => document,
                "props" => System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(document.Props) ?? (object)document,
                "markers" => document.Markers,
                "units" => System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(document.Armies) ?? (object)document,
                "decals" => System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(document.Decals) ?? (object)document,
                "heights" => document.Terrain.Heightmap.Samples,
                "masks" => document.Masks,
                _ => throw new ArgumentException($"There is no part '{part}': map, props, markers, units, decals, heights or masks"),
            });
        }, plugin);
        table["masks"] = Function("map.masks", (context, ctx) => LuaConvert.List(ctx.RequireDocument().Masks.Keys.Select(name => (LuaValue)name)), plugin);
        table["mask"] = Function("map.mask", (context, ctx) =>
        {
            MapDocument document = ctx.RequireDocument();
            TiledBuffer<byte> grid = MaskOf(document, context.GetArgument<string>(0));
            (int px, int pz) = Pixel(document, grid.Width, grid.Height, context.GetArgument<double>(1), context.GetArgument<double>(2));
            return grid[px, pz] / 255.0;
        }, plugin);
        table["reclaim"] = Multi("map.reclaim", (context, ctx) =>
        {
            AssetEntry? entry = host.Catalog?.Find(context.GetArgument<string>(0));
            return entry is { Kind: AssetKind.Prop } ? context.Return(entry.ReclaimMass, entry.ReclaimEnergy) : context.Return(0, 0);
        }, plugin);

        // edits: only while a plugin's command runs, into its one step
        table["addProp"] = Function("map.addProp", (context, ctx) =>
        {
            PluginRun run = ctx.RequireRun();
            LuaTable spec = context.GetArgument<LuaTable>(0);
            string blueprint = LuaConvert.Text(spec, "blueprint") ?? throw new ArgumentException("map.addProp needs blueprint, a game path");
            float x = (float)(LuaConvert.Number(spec, "x") ?? throw new ArgumentException("map.addProp needs x"));
            float z = (float)(LuaConvert.Number(spec, "z") ?? throw new ArgumentException("map.addProp needs z"));
            float? heading = LuaConvert.Number(spec, "heading") is double degrees ? (float)(degrees * Math.PI / 180) : null;
            float scale = (float)(LuaConvert.Number(spec, "scale") ?? 1);
            // a command's prop gets its images while the symmetry mirrors edits, as a placed one does;
            // a tool's handlers are called for every image themselves (and mirror = false leaves it alone)
            LuaValue mirrorField = LuaConvert.Field(spec, "mirror");
            bool mirror = !run.IsStroke && (mirrorField.Type != LuaValueType.Boolean || mirrorField.Read<bool>());
            MapDocument document = run.Document;
            Prop prop = new PropPlacement { RandomTurn = heading is null, ScaleMin = scale, ScaleMax = scale }
                .Make("/" + blueprint.TrimStart('/'), x, z, document.Terrain.Heightmap, Random.Shared, heading);
            int id = 0;
            run.Edit(now =>
            {
                MapDocument added = now.AddProp(prop);
                id = added.Props[^1].Id;
                return mirror ? host.MirrorPlaced(added, [ObjectRef.Prop(id)]) : added;
            });
            return ObjectRef.Prop(id).ToString();
        }, plugin);
        table["images"] = Function("map.images", (context, ctx) =>
        {
            // the place and its images in the other parts of the map while the symmetry mirrors edits
            float x = (float)context.GetArgument<double>(0);
            float z = (float)context.GetArgument<double>(1);
            IReadOnlyList<FAForever.FileFormats.Map.MapSymmetryTransform> parts = host.Mirrors(ctx.RequireDocument());
            return LuaConvert.List(parts.Select(part =>
            {
                Vector2 at = part.Apply(new Vector2(x, z));
                LuaTable image = new LuaTable(0, 4);
                image["x"] = at.X;
                image["z"] = at.Y;
                image["mirrored"] = part.IsMirror;
                image["turn"] = Math.Round(part.TurnAngle * 180 / Math.PI, 3);
                return (LuaValue)image;
            }));
        }, plugin);
        table["move"] = Function("map.move", (context, ctx) =>
        {
            IReadOnlyList<ObjectRef> things = Refs(context.GetArgument(0));
            float dx = (float)context.GetArgument<double>(1);
            float dz = (float)context.GetArgument<double>(2);
            ctx.RequireRun().Edit(document => document.MoveObjects(things, dx, dz));
            return LuaValue.Nil;
        }, plugin);
        table["place"] = Function("map.place", (context, ctx) =>
        {
            IReadOnlyList<ObjectRef> things = Refs(context.GetArgument(0));
            float x = (float)context.GetArgument<double>(1);
            float z = (float)context.GetArgument<double>(2);
            ctx.RequireRun().Edit(document => document.PlaceObjects(things, x, z));
            return LuaValue.Nil;
        }, plugin);
        table["rotate"] = Function("map.rotate", (context, ctx) =>
        {
            IReadOnlyList<ObjectRef> things = Refs(context.GetArgument(0));
            float radians = (float)(context.GetArgument<double>(1) * Math.PI / 180);
            ctx.RequireRun().Edit(document => document.RotateObjects(things, radians));
            return LuaValue.Nil;
        }, plugin);
        table["remove"] = Function("map.remove", (context, ctx) =>
        {
            IReadOnlyList<ObjectRef> things = Refs(context.GetArgument(0));
            ctx.RequireRun().Edit(document => document.RemoveObjects(things));
            return LuaValue.Nil;
        }, plugin);
        table["setHeight"] = new LuaFunction("map.setHeight", async (context, cancellation) =>
        {
            PluginRun run = ContextOf(plugin).RequireRun();
            MapDocument document = run.Document;
            int x0 = Math.Clamp((int)Math.Floor(context.GetArgument<double>(0)), 0, document.Width);
            int z0 = Math.Clamp((int)Math.Floor(context.GetArgument<double>(1)), 0, document.Height);
            int x1 = Math.Clamp((int)Math.Ceiling(context.GetArgument<double>(2)), 0, document.Width);
            int z1 = Math.Clamp((int)Math.Ceiling(context.GetArgument<double>(3)), 0, document.Height);
            LuaValue value = context.GetArgument(4);
            float scale = document.Terrain.Heightmap.Scale;
            TiledBuffer<ushort>.Builder heights = run.Transaction.Heightmap;
            for (int z = Math.Min(z0, z1); z <= Math.Max(z0, z1); z++)
            {
                for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
                {
                    double height;
                    if (value.Type == LuaValueType.Function)
                    {
                        LuaValue[] results = await context.State.CallAsync(value, [x, z, heights[x, z] * scale], cancellation);
                        if (results.Length == 0 || results[0].Type == LuaValueType.Nil)
                        {
                            continue;
                        }
                        height = results[0].Read<double>();
                    }
                    else
                    {
                        height = value.Read<double>();
                    }
                    heights[x, z] = (ushort)Math.Clamp(Math.Round(height / scale), 0, ushort.MaxValue);
                }
            }
            return context.Return();
        });
        table["paintMask"] = Function("map.paintMask", (context, ctx) =>
        {
            PluginRun run = ctx.RequireRun();
            MapDocument document = run.Document;
            string name = context.GetArgument<string>(0);
            TiledBuffer<byte> grid = MaskOf(document, name);
            double x = context.GetArgument<double>(1);
            double z = context.GetArgument<double>(2);
            double radius = context.GetArgument<double>(3);
            byte value = (byte)Math.Clamp(Math.Round(context.GetArgument<double>(4) * 255), 0, 255);
            TiledBuffer<byte>.Builder builder = run.Transaction.Grid(PaintTarget.Mask(name));
            double sx = (double)document.Width / grid.Width;
            double sz = (double)document.Height / grid.Height;
            int px0 = Math.Max(0, (int)Math.Floor((x - radius) / sx));
            int px1 = Math.Min(grid.Width - 1, (int)Math.Ceiling((x + radius) / sx));
            int pz0 = Math.Max(0, (int)Math.Floor((z - radius) / sz));
            int pz1 = Math.Min(grid.Height - 1, (int)Math.Ceiling((z + radius) / sz));
            int painted = 0;
            for (int pz = pz0; pz <= pz1; pz++)
            {
                for (int px = px0; px <= px1; px++)
                {
                    double cx = (px + 0.5) * sx - x;
                    double cz = (pz + 0.5) * sz - z;
                    if (cx * cx + cz * cz <= radius * radius)
                    {
                        builder[px, pz] = value;
                        painted++;
                    }
                }
            }
            return painted;
        }, plugin);
        return table;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, System.Runtime.CompilerServices.StrongBox<int>> Revisions = [];
    private static int _revisions;

    /// <summary>
    /// A number for a part of the document, the same while the part is the same object: versions
    /// share what they do not change, so a check can skip its work when the parts it reads are as
    /// they were (<c>map.revision("props")</c>).
    /// </summary>
    private static int RevisionOf(object part) =>
        Revisions.GetValue(part, _ => new System.Runtime.CompilerServices.StrongBox<int>(Interlocked.Increment(ref _revisions))).Value!;

    private static LuaValue All(MapDocument document, Func<MapDocument, IEnumerable<ObjectRef>> things) =>
        LuaConvert.List(things(document).Select(thing => document.Find(thing) is object found ? (LuaValue)Describe(document, thing, found) : LuaValue.Nil));

    /// <summary>
    /// A marker's kind as Lua names it: <c>spawn</c>, <c>mass</c>, <c>hydrocarbon</c>, <c>path</c>,
    /// <c>ai</c> or <c>weather</c>.
    /// </summary>
    private static string MarkerKind(Marker marker) => marker switch
    {
        Spawn => "spawn",
        ResourceMarker { Kind: ResourceKind.Mass } => "mass",
        ResourceMarker => "hydrocarbon",
        PathNode => "path",
        AiMarker => "ai",
        _ => "weather",
    };

    /// <summary>
    /// A prop as a plain table, as <see cref="Describe"/> makes it.
    /// </summary>
    private static LuaTable PropTable(Prop prop)
    {
        LuaTable item = new LuaTable(0, 8);
        item["ref"] = "prop:" + prop.Id.ToString(CultureInfo.InvariantCulture);
        item["id"] = prop.Id;
        item["blueprint"] = prop.Blueprint;
        item["x"] = prop.Position.X;
        item["y"] = prop.Position.Y;
        item["z"] = prop.Position.Z;
        item["heading"] = Math.Round(MapObjects.PropHeading(prop) * 180 / Math.PI, 3);
        item["scale"] = prop.Scale.X == 0 ? 1 : prop.Scale.X;
        return item;
    }

    /// <summary>
    /// A thing as a plain table: its reference, place and heading in degrees, and what it is.
    /// </summary>
    private static LuaTable Describe(MapDocument document, ObjectRef thing, object found)
    {
        if (found is Prop prop)
        {
            return PropTable(prop);
        }
        LuaTable item = new LuaTable();
        item["ref"] = thing.ToString();
        Vector3 place = document.PlaceOf(thing) ?? default;
        item["x"] = place.X;
        item["y"] = place.Y;
        item["z"] = place.Z;
        if (document.HeadingOf(thing) is float heading)
        {
            item["heading"] = Math.Round(heading * 180 / Math.PI, 3);
        }
        switch (found)
        {
            case PlacedUnit unit:
                item["name"] = unit.Unit.Name;
                item["blueprint"] = unit.Unit.Blueprint;
                item["army"] = unit.Army;
                item["group"] = string.Join("/", unit.Group);
                break;
            case Marker marker:
                item["id"] = marker.Id;
                item["name"] = marker.Name;
                item["kind"] = MarkerKind(marker);
                if (marker is AiMarker ai)
                {
                    item["type"] = ai.Type;
                }
                if (marker is PathNode node)
                {
                    item["layer"] = node.Layer.ToString().ToLowerInvariant();
                    item["links"] = LuaConvert.List(node.AdjacentTo.Select(id => (LuaValue)id));
                }
                break;
            case Decal decal:
                item["id"] = decal.Id;
                item["type"] = decal.Type.ToString().ToLowerInvariant();
                item["texture"] = decal.Textures.FirstOrDefault(texture => texture.Length > 0) ?? "";
                item["width"] = decal.Scale.X;
                item["length"] = decal.Scale.Z;
                break;
            case Area area:
                item["name"] = area.Name;
                item["x0"] = area.X0;
                item["z0"] = area.Z0;
                item["x1"] = area.X1;
                item["z1"] = area.Z1;
                break;
        }
        return item;
    }

    private static TiledBuffer<byte> MaskOf(MapDocument document, string name) =>
        document.Masks.TryGetValue(name, out TiledBuffer<byte>? grid) ? grid : throw new ArgumentException($"The map has no mask '{name}'");

    private static (int X, int Z) Pixel(MapDocument document, int width, int height, double x, double z) =>
        (Math.Clamp((int)Math.Floor(x * width / document.Width), 0, width - 1), Math.Clamp((int)Math.Floor(z * height / document.Height), 0, height - 1));

    /// <summary>
    /// References from Lua: one text (<c>prop:5</c>) or a table of them.
    /// </summary>
    private static IReadOnlyList<ObjectRef> Refs(LuaValue value) => [.. LuaConvert.Texts(value).Select(ObjectRef.Parse)];

    #endregion

    #region functions

    /// <summary>
    /// What the plugin's Lua may do now.
    /// </summary>
    private static PluginContext ContextOf(LuaPlugin plugin) =>
        plugin.Context ?? throw new InvalidOperationException("The editor's functions work only while the editor runs the plugin");

    /// <summary>
    /// A function of the API that answers one value at once.
    /// </summary>
    private static LuaFunction Function(string name, Func<LuaFunctionExecutionContext, PluginContext, LuaValue> body, LuaPlugin plugin) =>
        new LuaFunction(name, (context, _) => new ValueTask<int>(context.Return(body(context, ContextOf(plugin)))));

    /// <summary>
    /// A function of the API that answers several values (<c>context.Return(a, b)</c>).
    /// </summary>
    private static LuaFunction Multi(string name, Func<LuaFunctionExecutionContext, PluginContext, int> body, LuaPlugin plugin) =>
        new LuaFunction(name, (context, _) => new ValueTask<int>(body(context, ContextOf(plugin))));

    #endregion
}
