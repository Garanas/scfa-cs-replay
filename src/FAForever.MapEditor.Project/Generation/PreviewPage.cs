using System.Globalization;
using System.Net;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Generation
{
    /// <summary>
    /// The map's preview page, <c>index.html</c> (<see cref="PreviewPageFiles"/>): one HTML file
    /// without scripts that shows the map from above with switchable layers (water, cliffs, the
    /// regions of a layer, starts, no-rush circles, deposits, wrecks), the map's facts, its terrain,
    /// what lies around each start, its wrecks, its pathing layers and the pictures of the folder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The switches are checkboxes and radio buttons placed before everything they change, so the
    /// style (<c>PreviewPage.css</c>, embedded) shows a layer with <c>:checked ~</c>; pointing at a
    /// row of the starts enlarges its start with <c>:has()</c>; the cards of the wrecks open on
    /// <c>:hover</c>. The fonts are the system's and every picture is in <c>assets</c> beside it, so
    /// the page works from its folder without a network.
    /// </para>
    /// <para>
    /// Reclaim needs the workspace's blueprints (the catalogue): without them the page leaves out the
    /// reclaim of the starts and the wrecks' value, and gives the scenario's reclaim, if any.
    /// </para>
    /// </remarks>
    public static class PreviewPage
    {
        /// <summary>
        /// Ogrids per kilometre as maps are named: 256 ogrids is a 5 km map, 1024 a 20 km map. The
        /// regions' areas follow the game's AI instead (<see cref="NavRegion.Area"/>, 20 metres an ogrid).
        /// </summary>
        private const double OgridsPerKm = 51.2;

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>
        /// The layers as the page names them: the switch's and picture's name, the title, who paths there.
        /// </summary>
        private static readonly (NavLayer Layer, string Key, string Title, string Who)[] PathLayers =
        [
            (NavLayer.Land, "land", "Land", "Tanks, bots and engineers"),
            (NavLayer.Water, "naval", "Naval", "Ships and submarines"),
            (NavLayer.Hover, "hover", "Hover", "Hover tanks, over land and water"),
            (NavLayer.Amphibious, "amphibious", "Amphibious", "Units that walk the seabed"),
        ];

        /// <summary>
        /// The size of a wreck's block on the map, in pixels, by its value.
        /// </summary>
        private static int BlockSize(WreckValue value) => value switch { WreckValue.Low => 7, WreckValue.Medium => 11, _ => 16 };

        private static string Limit(WreckValue value) => value switch { WreckValue.Low => "under 100", WreckValue.Medium => "100 to 500", _ => "500 or more" };

        /// <summary>
        /// The page.
        /// </summary>
        /// <param name="mesh">The navigational mesh the region pictures were drawn from.</param>
        /// <param name="catalog">The workspace's assets, for reclaim and the units' names; null for none.</param>
        /// <param name="icons">The units (<see cref="ReclaimValues.UnitId"/>) whose icon is in <c>assets/icons</c>.</param>
        /// <param name="stamp">The editor and the moment of the export; null names neither.</param>
        public static string Write(MapDocument document, NavigationMesh mesh, AssetCatalog? catalog, IReadOnlySet<string> icons, EditorStamp? stamp)
        {
            Page page = new Page(document, mesh, catalog, icons, stamp);
            return page.Write();
        }

        /// <summary>
        /// The style of the page.
        /// </summary>
        public static string Style { get; } = ReadStyle();

        private static string ReadStyle()
        {
            using Stream stream = typeof(PreviewPage).Assembly.GetManifestResourceStream("PreviewPage.css")!;
            using StreamReader reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static string H(string? text) => WebUtility.HtmlEncode(text ?? "");

        private static string F(double value, string format = "0") => value.ToString(format, Invariant);

        /// <summary>
        /// 610, 1.5k, 13.8k.
        /// </summary>
        private static string Mass(double mass) => mass >= 1000 ? F(mass / 1000, "0.0") + "k" : F(mass);

        /// <summary>
        /// The text with every web address made a link.
        /// </summary>
        private static string Linked(string text) =>
            Regex.Replace(H(text), @"https?://[^\s<]+", match => $"<a href=\"{match.Value}\">{match.Value.Replace("https://", "", StringComparison.Ordinal)}</a>");

        private sealed class Page(MapDocument document, NavigationMesh mesh, AssetCatalog? catalog, IReadOnlySet<string> icons, EditorStamp? stamp)
        {
            private readonly StringBuilder _html = new StringBuilder();

            private readonly int _side = Math.Max(document.Width, document.Height);

            private readonly IReadOnlyList<Spawn> _starts = [.. document.Markers.Spawns.OrderBy(spawn => ArmyNumber(spawn.Army)).ThenBy(spawn => spawn.Army, StringComparer.Ordinal)];

            private readonly IReadOnlyList<(PlacedUnit Unit, double Mass)> _wrecks =
                [.. document.AllUnits().Where(UnitWrecks.IsWreck).Select(unit => (unit, catalog is null ? 0 : ReclaimValues.WreckMass(unit, catalog)))];

            private void W(string text) => _html.Append(text);

            /// <summary>
            /// A place on the map as a share of the plate, which is square: a map that is not fills it from the top left.
            /// </summary>
            private string Pct(double ogrids) => F(ogrids / _side * 100, "0.###") + "%";

            private static int ArmyNumber(string army) =>
                army.StartsWith("ARMY_", StringComparison.Ordinal) && int.TryParse(army[5..], NumberStyles.None, Invariant, out int number) ? number : int.MaxValue;

            private static string Short(string army) => ArmyNumber(army) is int number and not int.MaxValue ? number.ToString(Invariant) : army;

            public string Write()
            {
                IReadOnlyList<StartSurroundings> around = StartSurroundings.Of(document,
                    prop => catalog is null ? 0 : ReclaimValues.PropMass(prop, catalog),
                    unit => catalog is null ? 0 : ReclaimValues.WreckMass(unit, catalog));
                Dictionary<string, StartSurroundings> aroundOf = around.ToDictionary(start => start.Start.Army, StringComparer.Ordinal);
                IReadOnlyDictionary<string, string> twins = Twins();

                W("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
                W($"<title>{H(document.Scenario.Name)}</title>\n<style>\n{Style}");
                foreach (Spawn start in _starts)
                {
                    string id = H(Short(start.Army));
                    W($".page:has(.starts tr[data-army=\"{id}\"]:hover) .mk-start[data-army=\"{id}\"] {{ display: grid; scale: 1.5; z-index: 2; }}\n");
                }
                W("</style>\n</head>\n<body>\n<div class=\"page\">\n");

                // the switches come first: everything after them follows their state
                W("<input class=\"sr\" type=\"checkbox\" id=\"t-water\"><input class=\"sr\" type=\"checkbox\" id=\"t-cliffs\"><input class=\"sr\" type=\"checkbox\" id=\"t-dim\">\n");
                W("<input class=\"sr\" type=\"radio\" name=\"zones\" id=\"z-off\" checked>");
                foreach ((_, string key, _, _) in PathLayers)
                {
                    W($"<input class=\"sr\" type=\"radio\" name=\"zones\" id=\"z-{key}\">");
                }
                W("\n<input class=\"sr\" type=\"checkbox\" id=\"m-starts\" checked><input class=\"sr\" type=\"checkbox\" id=\"m-norush\">"
                    + "<input class=\"sr\" type=\"checkbox\" id=\"m-mass\" checked><input class=\"sr\" type=\"checkbox\" id=\"m-hydro\" checked>"
                    + (_wrecks.Count > 0 ? "<input class=\"sr\" type=\"checkbox\" id=\"m-wrecks\" checked>" : "") + "\n");

                string editor = stamp is null ? EditorBuild.WindowName : $"{EditorBuild.WindowName} · {stamp.Version}";
                W($"<header class=\"strip\"><span class=\"folder\">{H(document.Folder.ToString())} · preview page</span><span class=\"badge\">{H(editor)}</span></header>\n");

                W("<div class=\"hero\">\n<div>\n");
                Plate();
                Switches();
                W("</div>\n");
                Briefing();
                W("</div>\n");

                Starts(aroundOf, twins);
                Wrecks();
                Pathing();
                Pictures();
                Footer();
                W("</div>\n</body>\n</html>\n");
                return _html.ToString();
            }

            private void Plate()
            {
                double km = _side / OgridsPerKm;
                double step = km >= 20 ? 5 : km >= 10 ? 2.5 : 1;
                List<double> ticks = [];
                for (double t = 0; t <= km + 1e-6; t += step)
                {
                    ticks.Add(t);
                }
                W("<div class=\"survey\">\n<div></div><div class=\"ruler ruler-top\" aria-hidden=\"true\">");
                foreach (double t in ticks)
                {
                    W($"<span style=\"left:{F(t / km * 100, "0.##")}%\">{F(t, "0.#")}{(t == ticks[^1] ? " km" : "")}</span>");
                }
                W("</div>\n<div class=\"ruler ruler-left\" aria-hidden=\"true\">");
                foreach (double t in ticks.Skip(1).SkipLast(1))
                {
                    W($"<span style=\"top:{F(t / km * 100, "0.##")}%\">{F(t, "0.#")}</span>");
                }
                W("</div>\n<div class=\"plate\">\n<div class=\"clip\">\n");
                W($"<img class=\"base\" src=\"{Asset("preview", 512)}\" srcset=\"{SrcSet("preview")}\" sizes=\"{Sizes}\" alt=\"{H(document.Scenario.Name)} from above\">\n");
                W($"<img class=\"ov ov-water\" src=\"{Asset("water", 512)}\" srcset=\"{SrcSet("water")}\" sizes=\"{Sizes}\" alt=\"\">\n");
                foreach ((_, string key, _, _) in PathLayers)
                {
                    W($"<img class=\"ov ov-zones ov-{key}\" src=\"{Asset("zones_" + key, 512)}\" srcset=\"{SrcSet("zones_" + key)}\" sizes=\"{Sizes}\" alt=\"\">\n");
                }
                W($"<img class=\"ov ov-cliffs\" src=\"{Asset("cliffs", 512)}\" srcset=\"{SrcSet("cliffs")}\" sizes=\"{Sizes}\" alt=\"\">\n");
                string noRush = Pct(2 * document.Scenario.NoRushRadius);
                foreach (Spawn start in _starts)
                {
                    W($"<span class=\"mk mk-norush\" style=\"left:{Pct(start.Position.X)};top:{Pct(start.Position.Z)};width:{noRush}\"></span>");
                }
                W("\n</div>\n<div class=\"marks\">\n");
                foreach (ResourceMarker resource in document.Markers.Resources)
                {
                    string kind = resource.Kind == ResourceKind.Mass ? "mass" : "hydro";
                    W($"<span class=\"mk mk-{kind}\" style=\"left:{Pct(resource.Position.X)};top:{Pct(resource.Position.Z)}\"></span>");
                }
                W("\n");
                // the largest wrecks first, so smaller ones lie on top and can be pointed at
                foreach ((PlacedUnit unit, double mass) in _wrecks.OrderByDescending(wreck => wreck.Mass))
                {
                    Vector3 at = unit.Unit.Position;
                    WreckValue? value = catalog is null ? null : ReclaimValues.ValueOf(mass);
                    int size = BlockSize(value ?? WreckValue.Medium);
                    // the card opens away from the plate's edges
                    string side = (at.X > _side * .8 ? " left" : at.X < _side * .2 ? " right" : "") + (at.Z < _side * .2 ? " below" : "");
                    string id = ReclaimValues.UnitId(unit.Unit.Blueprint);
                    string icon = icons.Contains(id) ? $"<img src=\"assets/icons/{H(id)}.png\" alt=\"\">" : "";
                    string name = catalog is not null && ReclaimValues.UnitOf(unit.Unit.Blueprint, catalog) is { } entry ? entry.Name : id;
                    string worth = value is null ? "" : $"<span class=\"mass\">{value} value</span>";
                    W($"<span class=\"mk mk-wreck\" style=\"left:{Pct(at.X)};top:{Pct(at.Z)};width:{size}px;height:{size}px\"><span class=\"tip{side}\">"
                        + $"{icon}<span class=\"text\"><b>{H(name)}</b>{worth}<span class=\"id\">{H(id)}</span></span></span></span>");
                }
                W("\n");
                foreach (Spawn start in _starts)
                {
                    int massNear = document.Markers.Resources.Count(resource => resource.Kind == ResourceKind.Mass
                        && Vector2.Distance(new Vector2(resource.Position.X, resource.Position.Z), new Vector2(start.Position.X, start.Position.Z)) <= StartSurroundings.Near);
                    W($"<span class=\"mk mk-start\" data-army=\"{H(Short(start.Army))}\" data-label=\"{H(start.Army)} · {massNear} mass close by\" "
                        + $"style=\"left:{Pct(start.Position.X)};top:{Pct(start.Position.Z)}\">{H(Short(start.Army))}</span>\n");
                }
                W("</div>\n</div>\n</div>\n");
            }

            private const string Sizes = "(min-width: 1100px) 600px, (min-width: 700px) 70vw, 92vw";

            private static string Asset(string kind, int size) => $"assets/{kind}_{size}.png";

            private static string SrcSet(string kind) => string.Join(", ", PreviewPageFiles.Sizes.Reverse().Select(size => $"{Asset(kind, size)} {size}w"));

            private void Switches()
            {
                W("<div class=\"switches\">\n");
                W("<div class=\"group\"><span class=\"eyebrow\">Terrain</span>"
                    + "<label class=\"chip\" for=\"t-water\"><i style=\"background:var(--water)\"></i>Water</label>"
                    + "<label class=\"chip\" for=\"t-cliffs\"><i style=\"background:var(--cliff)\"></i>Cliffs</label>"
                    + "<label class=\"chip\" for=\"t-dim\">Dim the ground</label></div>\n");
                W("<div class=\"group\"><span class=\"eyebrow\">Pathing</span><label class=\"chip\" for=\"z-off\">Off</label>");
                foreach ((_, string key, string title, _) in PathLayers)
                {
                    W($"<label class=\"chip\" for=\"z-{key}\">{title}</label>");
                }
                W("</div>\n<div class=\"group\"><span class=\"eyebrow\">Markers</span>"
                    + "<label class=\"chip\" for=\"m-starts\"><i style=\"background:var(--accent);border-radius:50%\"></i>Starts</label>"
                    + "<label class=\"chip\" for=\"m-norush\"><i style=\"border:1.5px dashed var(--accent);border-radius:50%\"></i>No-rush</label>"
                    + "<label class=\"chip\" for=\"m-mass\"><i style=\"background:var(--mass)\"></i>Mass</label>"
                    + "<label class=\"chip\" for=\"m-hydro\"><i style=\"background:var(--hydro);rotate:45deg;width:8px;height:8px\"></i>Hydrocarbon</label>"
                    + (_wrecks.Count > 0 ? "<label class=\"chip\" for=\"m-wrecks\"><i style=\"background:var(--wreck)\"></i>Wrecks</label>" : "")
                    + "</div>\n</div>\n");
                foreach ((NavLayer layer, string key, string title, _) in PathLayers)
                {
                    IReadOnlyList<NavRegion> regions = mesh.RegionsOf(layer);
                    W($"<div class=\"legend legend-{key}\">");
                    foreach (NavRegion region in regions.Take(10))
                    {
                        W($"<span class=\"swatch\" style=\"background:{Colour(region.Id)}\" title=\"Region {region.Id}: {F(region.Area, "0.##")} km²\"></span>");
                    }
                    W($"<br>{title}: {regions.Count} region{(regions.Count == 1 ? "" : "s")}, each in its own colour. Ground no {title.ToLowerInvariant()} unit can path over stays clear.</div>\n");
                }
            }

            private static string Colour(int label)
            {
                (byte r, byte g, byte b) = PreviewPageFiles.RegionColour(label);
                return $"#{r:x2}{g:x2}{b:x2}";
            }

            private void Briefing()
            {
                ScenarioInfo scenario = document.Scenario;
                int separator = scenario.Name.IndexOf(" - ", StringComparison.Ordinal);
                string title = separator > 0 ? scenario.Name[..separator] : scenario.Name;
                string subtitle = separator > 0 ? scenario.Name[(separator + 3)..] : "";
                int players = scenario.PlayableArmies.Length > 0 ? scenario.PlayableArmies.Length : _starts.Count;

                W("<div class=\"brief\">\n<div><p class=\"eyebrow\">");
                W(H(scenario.Type.ToString()) + $" · {players} player{(players == 1 ? "" : "s")}" + (document.Version is int version ? $" · version {version}" : ""));
                W($"</p>\n<h1>{H(title)}{(subtitle.Length > 0 ? $"<small>{H(subtitle)}</small>" : "")}</h1></div>\n");
                if (!string.IsNullOrWhiteSpace(scenario.Description))
                {
                    W($"<p class=\"desc\">{Linked(scenario.Description)}</p>\n");
                }

                int mass = document.Markers.Resources.Count(resource => resource.Kind == ResourceKind.Mass);
                int hydrocarbons = document.Markers.Resources.Length - mass;
                W("<dl class=\"facts\">\n");
                Fact("Size", $"{F(document.Width / OgridsPerKm, "0.#")} × {F(document.Height / OgridsPerKm, "0.#")} km",
                    document.Width == document.Height ? $"{document.Width} ogrids" : $"{document.Width} × {document.Height} ogrids");
                Fact("Mass", mass.ToString(Invariant), $"and {hydrocarbons} hydrocarbon{(hydrocarbons == 1 ? "" : "s")}");
                if (catalog is not null)
                {
                    double props = document.Props.Sum(prop => ReclaimValues.PropMass(prop, catalog));
                    double wrecks = _wrecks.Sum(wreck => wreck.Mass);
                    Fact("Reclaim", Mass(props + wrecks), _wrecks.Count > 0 ? $"mass: {Mass(props)} in props, {Mass(wrecks)} in {_wrecks.Count} wreck{(_wrecks.Count == 1 ? "" : "s")}" : "mass in props");
                }
                else if (scenario.Reclaim is Reclaim reclaim)
                {
                    Fact("Reclaim", Mass(reclaim.Mass), "mass, as the scenario says");
                }
                Fact("No-rush radius", F(scenario.NoRushRadius), $"ogrids, {F(scenario.NoRushRadius / OgridsPerKm, "0.#")} km");
                W("</dl>\n");
                Terrain();
                W("</div>\n");
            }

            private void Fact(string label, string value, string extra) =>
                W($"<div class=\"fact\"><dt>{H(label)}</dt><dd>{H(value)}<span>{H(extra)}</span></dd></div>\n");

            /// <summary>
            /// The share under water and too steep, and how high the ground lies.
            /// </summary>
            private void Terrain()
            {
                Heightmap heightmap = document.Terrain.Heightmap;
                ScmapHeightmap samples = Walkability.HeightmapOf(heightmap);
                float? water = Walkability.WaterLevel(document);
                int width = document.Width, height = document.Height;
                float[] heights = new float[width * height];
                int wet = 0, steep = 0;
                for (int z = 0; z < height; z++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float h = heightmap.HeightAt(x + .5f, z + .5f);
                        heights[z * width + x] = h;
                        wet += water is float level && h < level ? 1 : 0;
                        steep += NavGenerator.IsFlat(samples, x, z) ? 0 : 1;
                    }
                }
                double cells = width * (double)height;

                W("<div class=\"terrain\">\n<p class=\"eyebrow\">Terrain</p>\n");
                Meter("Under water", wet / cells, "var(--water-bar)");
                Meter("Too steep", steep / cells, "var(--cliff)");

                float low = heights.Min(), high = heights.Max();
                if (high - low < 1e-3f)
                {
                    W("</div>\n");
                    return;
                }
                const int bins = 24;
                int[] counts = new int[bins];
                foreach (float h in heights)
                {
                    counts[Math.Min(bins - 1, (int)((h - low) / (high - low) * bins))]++;
                }
                int peak = counts.Max();
                double X(double h) => 8 + (h - low) / (high - low) * 464;
                double barWidth = 464.0 / bins;
                W($"<svg class=\"chart\" viewBox=\"0 0 480 150\" role=\"img\" aria-label=\"How high the ground lies{(water is float w ? $": {F(wet / cells * 100)}% below the water at {F(w, "0.#")}" : "")}\">");
                for (int i = 0; i < bins; i++)
                {
                    double from = low + (high - low) * i / bins, to = low + (high - low) * (i + 1) / bins;
                    double bar = Math.Max(1.5, counts[i] / (double)peak * 96);
                    string kind = water is float level && (from + to) / 2 < level ? "wet" : "dry";
                    W($"<rect class=\"{kind}\" x=\"{F(8 + i * barWidth + 1, "0.#")}\" y=\"{F(112 - bar, "0.#")}\" width=\"{F(barWidth - 2, "0.#")}\" height=\"{F(bar, "0.#")}\" rx=\"1.5\">"
                        + $"<title>{F(from)} to {F(to)}: {F(counts[i] / cells * 100, "0.0")}% of the map</title></rect>");
                }
                if (water is float surface && surface > low && surface < high)
                {
                    W($"<line class=\"waterline\" x1=\"{F(X(surface), "0.#")}\" x2=\"{F(X(surface), "0.#")}\" y1=\"6\" y2=\"116\"/>");
                    W($"<text class=\"label\" x=\"{F(Math.Min(X(surface) + 5, 400), "0.#")}\" y=\"14\">water at {F(surface, "0.#")}</text>");
                }
                W("<line class=\"axis\" x1=\"8\" x2=\"472\" y1=\"112.5\" y2=\"112.5\"/>");
                double tick = (high - low) > 100 ? 50 : (high - low) > 40 ? 10 : 5;
                for (double t = Math.Ceiling(low / tick) * tick; t <= high; t += tick)
                {
                    W($"<text x=\"{F(X(t), "0.#")}\" y=\"128\" text-anchor=\"middle\">{F(t)}</text>");
                }
                W("<text class=\"label\" x=\"8\" y=\"146\">elevation, in game units</text></svg>\n</div>\n");
            }

            private void Meter(string label, double share, string colour) =>
                W($"<div class=\"meter\"><span>{label}</span><span class=\"bar\"><b style=\"width:{F(share * 100, "0.#")}%;background:{colour}\"></b></span><span class=\"num\">{F(share * 100, "0.0")}%</span></div>\n");

            /// <summary>
            /// Each start's twin: its place turned or mirrored by the map's symmetry, when that has two parts.
            /// </summary>
            private Dictionary<string, string> Twins()
            {
                Dictionary<string, string> twins = new Dictionary<string, string>(StringComparer.Ordinal);
                if (_starts.Count < 2)
                {
                    return twins;
                }
                if (MapSymmetry.Detect(document.Width, document.Height,
                    [.. _starts.Select(start => start.Position), .. document.Markers.Resources.Select(resource => resource.Position)]) is not { Parts: 2 } match)
                {
                    return twins;
                }
                MapSymmetryTransform turn = MapSymmetry.Transforms(match.Kind, match.Count, document.Width, document.Height)[1];
                foreach (Spawn start in _starts)
                {
                    Vector2 image = turn.Apply(new Vector2(start.Position.X, start.Position.Z));
                    Spawn twin = _starts.MinBy(other => Vector2.DistanceSquared(new Vector2(other.Position.X, other.Position.Z), image))!;
                    if (twin != start)
                    {
                        twins[start.Army] = twin.Army;
                    }
                }
                return twins;
            }

            private void Starts(Dictionary<string, StartSurroundings> around, IReadOnlyDictionary<string, string> twins)
            {
                if (_starts.Count == 0)
                {
                    return;
                }
                bool reclaim = catalog is not null;
                double most = reclaim ? around.Values.Max(start => start.ReclaimClosest.Total) : 0;
                W("<section>\n<div><h2>Starts</h2><p class=\"lead\">Where each army begins and what lies around it."
                    + (reclaim ? " Each prop and wreck counts for the start nearest to it only, so reclaim between two starts is never counted twice." : "")
                    + " Point at a row to find the start on the map.</p></div>\n");
                if (reclaim)
                {
                    W("<span class=\"key\"><span><i style=\"background:var(--prop)\"></i>Props</span><span><i style=\"background:var(--wreck)\"></i>Wrecks</span></span>\n");
                }
                W("<div class=\"scroll\"><table class=\"starts\"><thead><tr><th>Army</th><th class=\"r\">Position</th>"
                    + $"<th>Mass within {F(StartSurroundings.Near)} / {F(StartSurroundings.Far)} ogrids</th><th class=\"r\">Hydro within {F(StartSurroundings.Far)}</th>"
                    + (reclaim ? $"<th class=\"r\">Reclaim within {F(StartSurroundings.Near)} / {F(StartSurroundings.Far)}</th><th>All reclaim closest to it</th>" : "")
                    + "<th>Land region</th>" + (twins.Count > 0 ? "<th>Twin by symmetry</th>" : "") + "</tr></thead><tbody>\n");
                foreach (Spawn start in _starts)
                {
                    StartSurroundings near = around[start.Army];
                    string pips = string.Concat(Enumerable.Repeat("<b></b>", near.MassNear)) + string.Concat(Enumerable.Repeat("<b class=\"far\"></b>", near.MassFar - near.MassNear));
                    NavRegion? region = mesh.RegionAt(NavLayer.Land, start.Position.X, start.Position.Z);
                    W($"<tr data-army=\"{H(Short(start.Army))}\"><td><span class=\"army\"><i>{H(Short(start.Army))}</i>{H(start.Army)}</span></td>"
                        + $"<td class=\"r num\">{F(start.Position.X)}, {F(start.Position.Z)}</td>"
                        + $"<td><span class=\"pips\">{pips}</span>{near.MassNear} / {near.MassFar}</td><td class=\"r\">{near.HydrocarbonsFar}</td>");
                    if (reclaim)
                    {
                        ReclaimShare all = near.ReclaimClosest;
                        string props = most > 0 ? F(all.Props / most * 100, "0.#") : "0";
                        string wrecks = most > 0 ? F(all.Wrecks / most * 100, "0.#") : "0";
                        W($"<td class=\"r\" title=\"Within {F(StartSurroundings.Far)} ogrids: {F(near.ReclaimFar.Props)} mass in props, {F(near.ReclaimFar.Wrecks)} in wrecks\">"
                            + $"{Mass(near.ReclaimNear.Total)} / {Mass(near.ReclaimFar.Total)}</td>"
                            + $"<td><span class=\"reclaim\" title=\"{F(all.Props)} mass in props, {F(all.Wrecks)} in wrecks\"><span class=\"stack\">"
                            + $"<b class=\"props\" style=\"width:{props}%\"></b><b class=\"wrecks\" style=\"width:{wrecks}%\"></b></span>{Mass(all.Total)}</span></td>");
                    }
                    W(region is null ? "<td>none</td>" : $"<td><span class=\"swatch\" style=\"background:{Colour(region.Id)}\"></span>{F(region.Area)} km²</td>");
                    if (twins.Count > 0)
                    {
                        W($"<td>{H(twins.GetValueOrDefault(start.Army, "none"))}</td>");
                    }
                    W("</tr>\n");
                }
                W("</tbody></table></div>\n</section>\n");
            }

            private void Wrecks()
            {
                if (_wrecks.Count == 0)
                {
                    return;
                }
                bool value = catalog is not null;
                double total = _wrecks.Sum(wreck => wreck.Mass);
                W("<section>\n<div><h2>Wrecks</h2><p class=\"lead\">");
                W(value ? $"{_wrecks.Count} wreck{(_wrecks.Count == 1 ? "" : "s")} hold {Mass(total)} mass. Each shows on the map as a grey block, sized by its value; point at one for the unit."
                    : $"{_wrecks.Count} wreck{(_wrecks.Count == 1 ? "" : "s")}, each a grey block on the map; point at one for the unit.");
                W(" Wrecks of a playable army show only when a player takes its slot.</p></div>\n");
                if (value)
                {
                    W("<span class=\"key\">");
                    foreach (WreckValue band in Enum.GetValues<WreckValue>())
                    {
                        int size = BlockSize(band);
                        W($"<span><i class=\"block\" style=\"width:{size}px;height:{size}px\"></i>{band}: {Limit(band)} mass</span>");
                    }
                    W("</span>\n");
                }
                W($"<div class=\"scroll\"><table class=\"wrecks\"><thead><tr><th>Unit</th>{(value ? "<th>Value</th>" : "")}<th class=\"r\">Count</th><th>Closest to</th></tr></thead><tbody>\n");
                IEnumerable<(string Id, double Mass, List<(PlacedUnit Unit, double Mass)> Wrecks)> groups = _wrecks.GroupBy(wreck => ReclaimValues.UnitId(wreck.Unit.Unit.Blueprint))
                    .Select(group => (Id: group.Key, Mass: group.First().Mass, Wrecks: group.ToList()))
                    .OrderByDescending(group => group.Mass * group.Wrecks.Count).ThenBy(group => group.Id, StringComparer.Ordinal);
                foreach ((string id, double mass, List<(PlacedUnit Unit, double Mass)> wrecks) in groups)
                {
                    string name = catalog is not null && ReclaimValues.UnitOf(id, catalog) is { } entry ? entry.Name : id;
                    string icon = icons.Contains(id) ? $"<img src=\"assets/icons/{H(id)}.png\" alt=\"\" loading=\"lazy\">" : "";
                    string closest = string.Join(", ", wrecks
                        .Select(wreck => NearestStart(wreck.Unit.Unit.Position))
                        .OfType<string>()
                        .GroupBy(army => army)
                        .OrderByDescending(group => group.Count()).ThenBy(group => ArmyNumber(group.Key))
                        .Select(group => $"{group.Key} ×{group.Count()}"));
                    W($"<tr><td><span class=\"unit\">{icon}<span><b>{H(name)}</b><small>{H(id)}</small></span></span></td>");
                    if (value)
                    {
                        WreckValue band = ReclaimValues.ValueOf(mass);
                        int size = BlockSize(band);
                        W($"<td><span class=\"band\"><i class=\"block\" style=\"width:{size}px;height:{size}px\"></i>{band}</span></td>");
                    }
                    W($"<td class=\"r\">{wrecks.Count}</td><td>{H(closest.Length > 0 ? closest : "none")}</td></tr>\n");
                }
                W("</tbody></table></div>\n</section>\n");
            }

            private string? NearestStart(Vector3 position) =>
                _starts.Count == 0 ? null : _starts.MinBy(start => Vector2.DistanceSquared(new Vector2(start.Position.X, start.Position.Z), new Vector2(position.X, position.Z)))!.Army;

            private void Pathing()
            {
                W("<section>\n<div><h2>Pathing</h2><p class=\"lead\">The regions the game's AI works with, per kind of unit. A unit cannot path from one region to another. "
                    + "The game leaves out small regions without deposits, and so does this page.</p></div>\n<div class=\"layers\">\n");
                foreach ((NavLayer layer, string key, string title, string who) in PathLayers)
                {
                    IReadOnlyList<NavRegion> regions = mesh.RegionsOf(layer);
                    W($"<div class=\"layer\"><div class=\"thumb\"><img src=\"{Asset("preview", 256)}\" alt=\"\"><img src=\"{Asset("zones_" + key, 256)}\" alt=\"{title} regions\"></div>\n");
                    W($"<div><h3>{title}</h3><p>{who}</p></div>\n");
                    if (regions.Count == 0)
                    {
                        W("<p>No region: no unit of this kind can path anywhere.</p></div>\n");
                        continue;
                    }
                    W($"<p><span class=\"stat\">{regions.Count}</span> region{(regions.Count == 1 ? "" : "s")}, the largest <span class=\"stat\">{F(regions[0].Area)}</span> km²</p>\n");
                    W($"<details><summary>All {regions.Count} region{(regions.Count == 1 ? "" : "s")}</summary><table><thead><tr><th>Region</th><th class=\"r\">km²</th><th class=\"r\">Mass</th><th class=\"r\">Starts</th></tr></thead><tbody>");
                    foreach (NavRegion region in regions)
                    {
                        W($"<tr><td><span class=\"swatch\" style=\"background:{Colour(region.Id)}\"></span>{region.Id}</td><td class=\"r\">{F(region.Area, "0.0")}</td>"
                            + $"<td class=\"r\">{(region.Mass > 0 ? region.Mass.ToString(Invariant) : "none")}</td><td class=\"r\">{(region.Starts.Count > 0 ? region.Starts.Count.ToString(Invariant) : "none")}</td></tr>");
                    }
                    W("</tbody></table></details></div>\n");
                }
                W("</div>\n</section>\n");
            }

            private void Pictures()
            {
                W($"<section>\n<div><h2>Pictures in this folder</h2><p class=\"lead\">Every picture at {string.Join(", ", PreviewPageFiles.Sizes.Reverse().SkipLast(1))} and {PreviewPageFiles.Sizes[0]} pixels, in assets/ beside this page. "
                    + "Apart from the preview they are transparent where they show nothing, ready to lay over the map.</p></div>\n<div class=\"files\">\n");
                List<(string Kind, string Title)> pictures = [("preview", "Preview"), ("water", "Water"), ("cliffs", "Cliffs")];
                pictures.AddRange(PathLayers.Select(layer => ("zones_" + layer.Key, $"{layer.Title} regions")));
                foreach ((string kind, string title) in pictures)
                {
                    W($"<div class=\"file\"><div class=\"thumb{(kind == "preview" ? "" : " checker")}\"><img src=\"{Asset(kind, 256)}\" alt=\"\" loading=\"lazy\"></div><span class=\"name\">{title}</span><span class=\"sizes\">");
                    foreach (int size in PreviewPageFiles.Sizes.Reverse())
                    {
                        W($"<a href=\"{Asset(kind, size)}\">{size}</a>");
                    }
                    W("</span></div>\n");
                }
                W("</div>\n</section>\n");
            }

            private void Footer()
            {
                if (stamp is null)
                {
                    W($"<footer><span>Made with {H(EditorBuild.WindowName)}. This page runs no scripts.</span></footer>\n");
                    return;
                }
                DateTimeOffset at = stamp.Exported.ToLocalTime();
                W($"<footer><span>Exported on {at.ToString("d MMMM yyyy", Invariant)} at {at.ToString("HH:mm", Invariant)} with {H(EditorBuild.WindowName)}. This page runs no scripts.</span>\n"
                    + $"<span class=\"mono\">{H(stamp.Version)} · {H(stamp.Commit ?? "commit unknown")} · project format {stamp.ProjectFormat}</span></footer>\n");
            }
        }
    }
}
