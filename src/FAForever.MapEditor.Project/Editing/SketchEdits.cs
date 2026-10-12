using System.Collections.Immutable;
using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// What an eraser did to a sheet: the strokes it cut (some of each is left) and what it removed
    /// whole (a stroke with nothing left of it, a symbol, a label).
    /// </summary>
    public readonly record struct EraseCounts(int Cut, int Strokes, int Symbols, int Labels)
    {
        /// <summary>
        /// Everything the eraser changed; 0 when it touched nothing.
        /// </summary>
        public int Touched => Cut + Strokes + Symbols + Labels;

        /// <summary>
        /// <c>Cut 2 strokes, removed 1 stroke and 1 symbol</c>; what it did not do is left out.
        /// </summary>
        public string Describe()
        {
            List<string> removed = [];
            if (Strokes > 0)
            {
                removed.Add($"{Strokes} {(Strokes == 1 ? "stroke" : "strokes")}");
            }
            if (Symbols > 0)
            {
                removed.Add($"{Symbols} {(Symbols == 1 ? "symbol" : "symbols")}");
            }
            if (Labels > 0)
            {
                removed.Add($"{Labels} {(Labels == 1 ? "label" : "labels")}");
            }
            string gone = removed.Count switch
            {
                0 => "",
                1 => removed[0],
                _ => string.Join(", ", removed[..^1]) + " and " + removed[^1],
            };
            string cut = Cut > 0 ? $"Cut {Cut} {(Cut == 1 ? "stroke" : "strokes")}" : "";
            return (cut.Length > 0, gone.Length > 0) switch
            {
                (true, true) => $"{cut}, removed {gone}",
                (true, false) => cut,
                (false, true) => $"Removed {gone}",
                _ => "Touched nothing",
            };
        }

        public static EraseCounts operator +(EraseCounts a, EraseCounts b) =>
            new(a.Cut + b.Cut, a.Strokes + b.Strokes, a.Symbols + b.Symbols, a.Labels + b.Labels);
    }
    /// <summary>
    /// The edits of the map maker's sketch (user story N-1): sheets added, renamed, duplicated,
    /// deleted, shown, given a legend and cleared, strokes drawn (repeated by the map's symmetry when
    /// asked) and rubbed out. Every one returns the document itself when it changes nothing, so
    /// that the session keeps no step for it. Sheets are named; a name matches the exact one first,
    /// then without regard to case. Places are in ogrids.
    /// </summary>
    public static class SketchEdits
    {
        /// <summary>
        /// The longest a sheet's name is.
        /// </summary>
        public const int NameLength = 60;

        /// <summary>
        /// The longest text a legend holds for a colour.
        /// </summary>
        public const int LegendLength = 80;

        #region Sheets

        /// <summary>
        /// The sheet of a name.
        /// </summary>
        /// <exception cref="ArgumentException">The sketch has no such sheet.</exception>
        public static SketchSheet SheetNamed(this MapDocument document, string name) =>
            document.Sketch.Find(name) ?? throw new ArgumentException(document.Sketch.IsEmpty
                ? $"The sketch has no sheet '{name}': it has none yet"
                : $"The sketch has no sheet '{name}': {string.Join(", ", document.Sketch.Sheets.Select(sheet => sheet.Name))}");

        /// <summary>
        /// A name no sheet has: <paramref name="wanted"/>, else it with a number after it (from 2); without
        /// one <c>Sheet 1</c>, <c>Sheet 2</c>, ...
        /// </summary>
        public static string FreeSheetName(this MapDocument document, string? wanted = null)
        {
            bool free(string name) => document.Sketch.Find(name) is null;
            if (wanted is null)
            {
                return Enumerable.Range(1, int.MaxValue).Select(n => $"Sheet {n}").First(free);
            }
            return free(wanted) ? wanted : Enumerable.Range(2, int.MaxValue - 2).Select(n => $"{wanted} {n}").First(free);
        }

        /// <summary>
        /// The document with another sheet at the end.
        /// </summary>
        /// <param name="name">Its name; null gives it a free one (<c>Sheet 1</c>).</param>
        /// <param name="added">The name it got.</param>
        /// <exception cref="ArgumentException">The name is empty, too long, or another sheet's.</exception>
        public static MapDocument AddSheet(this MapDocument document, string? name, out string added)
        {
            added = name is null ? document.FreeSheetName() : CheckedName(document, name, except: null);
            return document with { Sketch = document.Sketch with { Sheets = document.Sketch.Sheets.Add(new SketchSheet { Name = added }) } };
        }

        /// <summary>
        /// The document with a sheet under another name.
        /// </summary>
        /// <exception cref="ArgumentException">No such sheet, or the name is empty, too long, or another sheet's.</exception>
        public static MapDocument RenameSheet(this MapDocument document, string sheet, string name)
        {
            SketchSheet held = document.SheetNamed(sheet);
            name = CheckedName(document, name, held);
            return name == held.Name ? document : ChangeSheet(document, held, held with { Name = name });
        }

        /// <summary>
        /// The document with a copy of a sheet (strokes, legend, look) right after it.
        /// </summary>
        /// <param name="name">The copy's name; null makes one of the original's (<c>Layout A copy</c>).</param>
        /// <param name="added">The name the copy got.</param>
        /// <exception cref="ArgumentException">No such sheet, or the name is empty, too long, or another sheet's.</exception>
        public static MapDocument DuplicateSheet(this MapDocument document, string sheet, string? name, out string added)
        {
            SketchSheet held = document.SheetNamed(sheet);
            added = name is null ? document.FreeSheetName(Truncated($"{held.Name} copy")) : CheckedName(document, name, except: null);
            int at = document.Sketch.Sheets.IndexOf(held);
            return document with { Sketch = document.Sketch with { Sheets = document.Sketch.Sheets.Insert(at + 1, held with { Name = added }) } };
        }

        /// <summary>
        /// The document without a sheet and its strokes.
        /// </summary>
        /// <exception cref="ArgumentException">No such sheet.</exception>
        public static MapDocument DeleteSheet(this MapDocument document, string sheet)
        {
            SketchSheet held = document.SheetNamed(sheet);
            return document with { Sketch = document.Sketch with { Sheets = document.Sketch.Sheets.Remove(held) } };
        }

        /// <summary>
        /// The document with a sheet shown or hidden in the views.
        /// </summary>
        /// <exception cref="ArgumentException">No such sheet.</exception>
        public static MapDocument ShowSheet(this MapDocument document, string sheet, bool shown)
        {
            SketchSheet held = document.SheetNamed(sheet);
            return held.Shown == shown ? document : ChangeSheet(document, held, held with { Shown = shown });
        }

        /// <summary>
        /// The document with a sheet's opacity outside the Sketch mode.
        /// </summary>
        /// <param name="opacity">From 0 to 1.</param>
        /// <exception cref="ArgumentException">No such sheet, or the opacity lies outside 0 to 1.</exception>
        public static MapDocument SetSheetOpacity(this MapDocument document, string sheet, float opacity)
        {
            if (!(opacity >= 0 && opacity <= 1))
            {
                throw new ArgumentException($"A sheet's opacity lies between 0 and 1, not {opacity}", nameof(opacity));
            }
            SketchSheet held = document.SheetNamed(sheet);
            return held.Opacity == opacity ? document : ChangeSheet(document, held, held with { Opacity = opacity });
        }

        /// <summary>
        /// The document with the text of one colour of a sheet's legend.
        /// </summary>
        /// <exception cref="ArgumentException">No such sheet, or the text is too long.</exception>
        public static MapDocument SetLegend(this MapDocument document, string sheet, SketchColour colour, string text)
        {
            text = text.Trim();
            if (text.Length > LegendLength)
            {
                throw new ArgumentException($"A legend text holds at most {LegendLength} characters, not {text.Length}", nameof(text));
            }
            SketchSheet held = document.SheetNamed(sheet);
            if (held.LegendOf(colour) == text)
            {
                return document;
            }
            ImmutableArray<string> legend = held.Legend.IsDefaultOrEmpty ? SketchSheet.NoLegend : held.Legend;
            return ChangeSheet(document, held, held with { Legend = legend.SetItem((int)colour, text) });
        }

        /// <summary>
        /// The document with every stroke, symbol and label of a sheet taken away; its legend stays.
        /// </summary>
        /// <exception cref="ArgumentException">No such sheet.</exception>
        public static MapDocument ClearSheet(this MapDocument document, string sheet)
        {
            SketchSheet held = document.SheetNamed(sheet);
            return held.IsBlank ? document : ChangeSheet(document, held, held with { Strokes = [], Symbols = [], Labels = [] });
        }

        private static string CheckedName(MapDocument document, string name, SketchSheet? except)
        {
            name = name.Trim();
            if (name.Length == 0)
            {
                throw new ArgumentException("A sheet needs a name", nameof(name));
            }
            if (name.Length > NameLength)
            {
                throw new ArgumentException($"A sheet's name holds at most {NameLength} characters, not {name.Length}", nameof(name));
            }
            if (document.Sketch.Sheets.Any(sheet => sheet != except && sheet.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"The sketch has a sheet '{name}' already", nameof(name));
            }
            return name;
        }

        private static string Truncated(string name) => name.Length <= NameLength - 4 ? name : name[..(NameLength - 4)];

        internal static MapDocument ChangeSheet(MapDocument document, SketchSheet held, SketchSheet changed) =>
            document with { Sketch = document.Sketch with { Sheets = document.Sketch.Sheets.SetItem(document.Sketch.Sheets.IndexOf(held), changed) } };

        #endregion

        #region Strokes

        /// <summary>
        /// The strokes a stroke makes in a map with a symmetry: the stroke itself, then its image under each
        /// of the other parts' transforms; an image that lies on the stroke (a line along a mirror's
        /// axis) or on an earlier image is left out.
        /// </summary>
        /// <param name="parts">The parts of the map (<c>MapSymmetry.Transforms</c>), the identity first;
        /// empty or the identity alone for none.</param>
        public static IReadOnlyList<SketchStroke> WithImages(SketchStroke stroke, IReadOnlyList<MapSymmetryTransform> parts)
        {
            List<SketchStroke> strokes = [stroke];
            foreach (MapSymmetryTransform transform in parts)
            {
                if (transform == MapSymmetryTransform.Identity)
                {
                    continue;
                }
                SketchStroke image = stroke with { Points = [.. stroke.Points.Select(point => transform.Apply(point))] };
                if (!strokes.Any(other => SameLine(other.Points, image.Points)))
                {
                    strokes.Add(image);
                }
            }
            return strokes;
        }

        /// <summary>
        /// The images of an eraser's path under the parts of the map, the path itself first.
        /// </summary>
        public static IReadOnlyList<ImmutableArray<Vector2>> WithImages(IReadOnlyList<Vector2> path, IReadOnlyList<MapSymmetryTransform> parts)
        {
            List<ImmutableArray<Vector2>> paths = [[.. path]];
            foreach (MapSymmetryTransform transform in parts)
            {
                if (transform == MapSymmetryTransform.Identity)
                {
                    continue;
                }
                ImmutableArray<Vector2> image = [.. path.Select(point => transform.Apply(point))];
                if (!paths.Any(other => SameLine(other, image)))
                {
                    paths.Add(image);
                }
            }
            return paths;
        }

        private static bool SameLine(ImmutableArray<Vector2> a, ImmutableArray<Vector2> b)
        {
            const float samePlace = 0.01f;
            if (a.Length != b.Length)
            {
                return false;
            }
            bool forwards = true;
            bool backwards = true;
            for (int i = 0; i < a.Length; i++)
            {
                forwards &= Vector2.Distance(a[i], b[i]) <= samePlace;
                backwards &= Vector2.Distance(a[i], b[^(i + 1)]) <= samePlace;
            }
            return forwards || backwards;
        }

        /// <summary>
        /// The document with strokes added to a sheet, after its others; a sketch without sheets gets
        /// its first for them (<c>Sheet 1</c>).
        /// </summary>
        /// <param name="sheet">The sheet to draw into; null the first one (the one a sketch without
        /// sheets gets).</param>
        /// <param name="used">The name of the sheet drawn into.</param>
        /// <exception cref="ArgumentException">No such sheet.</exception>
        public static MapDocument AddStrokes(this MapDocument document, string? sheet, IEnumerable<SketchStroke> strokes, out string used)
        {
            ImmutableArray<SketchStroke> added = [.. strokes.Where(stroke => !stroke.Points.IsDefaultOrEmpty)];
            if (document.Sketch.IsEmpty)
            {
                document = document.AddSheet(sheet, out used);
            }
            else
            {
                used = sheet is null ? document.Sketch.Sheets[0].Name : document.SheetNamed(sheet).Name;
            }
            if (added.IsEmpty)
            {
                return document;
            }
            SketchSheet held = document.SheetNamed(used);
            return ChangeSheet(document, held, held with { Strokes = held.Strokes.AddRange(added) });
        }

        /// <summary>
        /// The document with a sheet's strokes rubbed out where an eraser of a radius passed along a
        /// path: a stroke is cut in two where the eraser crosses its middle, and what is left of it
        /// shorter than <see cref="SketchGeometry.Shortest"/> goes. With <paramref name="whole"/> a
        /// stroke the eraser touches goes entirely.
        /// </summary>
        /// <param name="path">The places the eraser went through, in ogrids; one is a click.</param>
        /// <param name="parts">The parts of the map the path is repeated into (identity first);
        /// empty for the path alone.</param>
        /// <param name="erased">How many strokes the eraser touched (a stroke cut in two counts once), with
        /// <paramref name="whole"/> also the symbols and labels it removed.</param>
        /// <exception cref="ArgumentException">No such sheet, or the radius is not above 0.</exception>
        public static MapDocument Erase(this MapDocument document, string sheet, IReadOnlyList<Vector2> path, float radius, bool whole,
            IReadOnlyList<MapSymmetryTransform> parts, out int erased)
        {
            MapDocument result = document.EraseCounted(sheet, path, radius, whole, parts, out EraseCounts counts);
            erased = counts.Touched;
            return result;
        }

        /// <summary>
        /// As <see cref="Erase"/>, saying what the eraser did: how many strokes it cut and how many strokes,
        /// symbols and labels it removed. Labels are never mirrored when written, so the eraser's images by
        /// the symmetry do not reach them: only the path itself does.
        /// </summary>
        public static MapDocument EraseCounted(this MapDocument document, string sheet, IReadOnlyList<Vector2> path, float radius, bool whole,
            IReadOnlyList<MapSymmetryTransform> parts, out EraseCounts counts)
        {
            counts = default;
            if (!(radius > 0))
            {
                throw new ArgumentException($"The eraser's radius is above 0 ogrids, not {radius}", nameof(radius));
            }
            SketchSheet held = document.SheetNamed(sheet);
            if (path.Count == 0 || held.IsBlank)
            {
                return document;
            }

            IReadOnlyList<ImmutableArray<Vector2>> paths = WithImages(path, parts);
            ImmutableArray<SketchStroke> strokes = held.Strokes;
            ImmutableArray<SketchSymbol> symbols = held.Symbols;
            ImmutableArray<SketchLabel> labels = held.Labels;
            int cut = 0, removedStrokes = 0, removedSymbols = 0, removedLabels = 0;
            for (int i = 0; i < paths.Count; i++)
            {
                ImmutableArray<Vector2> eraser = paths[i];
                strokes = EraseAlong(strokes, eraser, radius, whole, ref cut, ref removedStrokes);
                if (whole)
                {
                    // whole strokes (Alt) take the symbols and labels the eraser touches too (user story N-4);
                    // symbols are repeated by the symmetry as strokes are, labels are not: the path itself only
                    int before = symbols.Length;
                    symbols = [.. symbols.Where(symbol => !Touched(SketchPicking.Bounds(symbol), eraser, radius))];
                    removedSymbols += before - symbols.Length;
                    if (i == 0)
                    {
                        before = labels.Length;
                        labels = [.. labels.Where(label => !Touched(SketchLabels.Bounds(label), eraser, radius))];
                        removedLabels += before - labels.Length;
                    }
                }
            }
            counts = new EraseCounts(cut, removedStrokes, removedSymbols, removedLabels);
            return counts.Touched == 0 ? document : ChangeSheet(document, held, held with { Strokes = strokes, Symbols = symbols, Labels = labels });
        }

        private static bool Touched((Vector2 Min, Vector2 Max) box, ImmutableArray<Vector2> path, float radius)
        {
            if (path.Length == 1)
            {
                return SketchPicking.Reaches(box, path[0], path[0], radius);
            }
            for (int i = 0; i + 1 < path.Length; i++)
            {
                if (SketchPicking.Reaches(box, path[i], path[i + 1], radius))
                {
                    return true;
                }
            }
            return false;
        }

        private static ImmutableArray<SketchStroke> EraseAlong(ImmutableArray<SketchStroke> strokes, ImmutableArray<Vector2> path, float radius, bool whole, ref int cut, ref int removed)
        {
            ImmutableArray<SketchStroke>.Builder result = ImmutableArray.CreateBuilder<SketchStroke>(strokes.Length);
            // one stroke may be cut again by a later segment: the pieces go through each segment in turn
            int segments = Math.Max(1, path.Length - 1);
            foreach (SketchStroke stroke in strokes)
            {
                List<ImmutableArray<Vector2>> pieces = [stroke.Points];
                bool touched = false;
                for (int s = 0; s < segments && pieces.Count > 0; s++)
                {
                    Vector2 from = path[Math.Min(s, path.Length - 1)];
                    Vector2 to = path[Math.Min(s + 1, path.Length - 1)];
                    if (!Reaches(stroke, from, to, radius))
                    {
                        continue;
                    }
                    if (whole)
                    {
                        if (pieces.Any(piece => SketchGeometry.Touches(piece, from, to, radius)))
                        {
                            pieces.Clear();
                            touched = true;
                        }
                        continue;
                    }
                    List<ImmutableArray<Vector2>> next = [];
                    foreach (ImmutableArray<Vector2> piece in pieces)
                    {
                        next.AddRange(SketchGeometry.Cut(piece, from, to, radius, out bool hit));
                        touched |= hit;
                    }
                    pieces = next;
                }
                if (!touched)
                {
                    result.Add(stroke);
                    continue;
                }
                if (pieces.Count == 0)
                {
                    removed++;
                }
                else
                {
                    cut++;
                }
                foreach (ImmutableArray<Vector2> piece in pieces)
                {
                    result.Add(stroke with { Points = piece });
                }
            }
            return result.ToImmutable();
        }

        // a quick no: the stroke's box and the eraser's capsule's box do not meet
        private static bool Reaches(SketchStroke stroke, Vector2 from, Vector2 to, float radius)
        {
            float low = Math.Min(from.X, to.X) - radius;
            float high = Math.Max(from.X, to.X) + radius;
            float top = Math.Min(from.Y, to.Y) - radius;
            float bottom = Math.Max(from.Y, to.Y) + radius;
            foreach (Vector2 point in stroke.Points)
            {
                // a stroke between two places far apart may cross the box with no place in it: so test the segment's box too
                if (point.X >= low && point.X <= high && point.Y >= top && point.Y <= bottom)
                {
                    return true;
                }
            }
            for (int i = 0; i + 1 < stroke.Points.Length; i++)
            {
                Vector2 p = stroke.Points[i];
                Vector2 q = stroke.Points[i + 1];
                if (Math.Max(p.X, q.X) >= low && Math.Min(p.X, q.X) <= high && Math.Max(p.Y, q.Y) >= top && Math.Min(p.Y, q.Y) <= bottom)
                {
                    return true;
                }
            }
            return false;
        }

        #endregion

        /// <summary>
        /// The sketch moved by an offset, as the map's resize moves what lies on it.
        /// </summary>
        public static Sketch Moved(Sketch sketch, float x, float z)
        {
            if (sketch.IsEmpty || (x == 0 && z == 0))
            {
                return sketch;
            }
            Vector2 offset = new Vector2(x, z);
            return sketch with
            {
                Sheets = [.. sketch.Sheets.Select(sheet => sheet with
                {
                    Strokes = [.. sheet.Strokes.Select(stroke => stroke with { Points = [.. stroke.Points.Select(point => point + offset)] })],
                    Symbols = [.. sheet.Symbols.Select(symbol => symbol with { Place = symbol.Place + offset })],
                    Labels = [.. sheet.Labels.Select(label => label with { Place = label.Place + offset })],
                })],
            };
        }
    }
}
