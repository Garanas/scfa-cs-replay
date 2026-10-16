using System.Collections.Immutable;
using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// The edits of the sketch's symbols and labels and of selected items (user story N-4): stamping a
    /// symbol (repeated by the map's symmetry, an arrow turned with it), writing a label, and moving,
    /// recolouring, resizing and deleting strokes, symbols and labels chosen by <see cref="SketchItem"/>.
    /// Every one returns the document itself when it changes nothing. Places are in ogrids.
    /// </summary>
    public static class SketchMarkEdits
    {
        /// <summary>
        /// The longest a label's text is.
        /// </summary>
        public const int LabelLength = 120;

        #region Symbols and labels

        /// <summary>
        /// The symbols a stamp makes in a map with a symmetry: the symbol itself, then its image under each of
        /// the other parts' transforms (an arrow points the way its image does); an image on the symbol or on
        /// an earlier image is left out.
        /// </summary>
        /// <param name="parts">The parts of the map (<c>MapSymmetry.Transforms</c>), the identity first;
        /// empty or the identity alone for none.</param>
        public static IReadOnlyList<SketchSymbol> WithImages(SketchSymbol symbol, IReadOnlyList<MapSymmetryTransform> parts)
        {
            List<SketchSymbol> symbols = [symbol];
            foreach (MapSymmetryTransform transform in parts)
            {
                if (transform == MapSymmetryTransform.Identity)
                {
                    continue;
                }
                Vector2 place = transform.Apply(symbol.Place);
                float turn = symbol.Turn;
                if (symbol.Kind == SketchSymbolKind.Arrow)
                {
                    // where the arrow's tip goes tells which way its image points
                    float radians = symbol.Turn * MathF.PI / 180;
                    Vector2 tip = transform.Apply(symbol.Place + new Vector2(MathF.Cos(radians), MathF.Sin(radians)));
                    Vector2 way = tip - place;
                    turn = Normalized(MathF.Atan2(way.Y, way.X) * 180 / MathF.PI);
                }
                SketchSymbol image = symbol with { Place = place, Turn = turn };
                if (!symbols.Any(other => Vector2.Distance(other.Place, image.Place) <= 0.01f && (symbol.Kind != SketchSymbolKind.Arrow || Math.Abs(Normalized(other.Turn - image.Turn)) <= 0.5f || Math.Abs(Normalized(other.Turn - image.Turn)) >= 359.5f)))
                {
                    symbols.Add(image);
                }
            }
            return symbols;
        }

        /// <summary>
        /// An angle in degrees from 0 up to 360.
        /// </summary>
        public static float Normalized(float degrees)
        {
            degrees %= 360;
            return degrees < 0 ? degrees + 360 : degrees;
        }

        /// <summary>
        /// The document with symbols added to a sheet; a sketch without sheets gets its first for them.
        /// </summary>
        /// <param name="sheet">The sheet; null the first one (the one a sketch without sheets gets).</param>
        /// <param name="used">The name of the sheet the symbols went to.</param>
        /// <exception cref="ArgumentException">No such sheet, a size outside the range, or a place that is not a number.</exception>
        public static MapDocument AddSymbols(this MapDocument document, string? sheet, IEnumerable<SketchSymbol> symbols, out string used)
        {
            ImmutableArray<SketchSymbol> added = [.. symbols];
            foreach (SketchSymbol symbol in added)
            {
                CheckSize(symbol.Size);
                CheckPlace(symbol.Place);
            }
            document = SheetOf(document, sheet, out used);
            if (added.IsEmpty)
            {
                return document;
            }
            SketchSheet held = document.SheetNamed(used);
            return SketchEdits.ChangeSheet(document, held, held with { Symbols = held.Symbols.AddRange(added) });
        }

        /// <summary>
        /// The document with a label written on a sheet; a sketch without sheets gets its first for it.
        /// </summary>
        /// <exception cref="ArgumentException">No such sheet, no text or too much, a size outside the range, or a place that is not a number.</exception>
        public static MapDocument AddLabel(this MapDocument document, string? sheet, SketchLabel label, out string used)
        {
            label = label with { Text = CheckedText(label.Text) };
            CheckSize(label.Size);
            CheckPlace(label.Place);
            document = SheetOf(document, sheet, out used);
            SketchSheet held = document.SheetNamed(used);
            return SketchEdits.ChangeSheet(document, held, held with { Labels = held.Labels.Add(label) });
        }

        /// <summary>
        /// The document with the text of a label changed.
        /// </summary>
        /// <exception cref="ArgumentException">No such sheet or label, or no text or too much.</exception>
        public static MapDocument SetLabelText(this MapDocument document, string sheet, int label, string text)
        {
            SketchSheet held = document.SheetNamed(sheet);
            if (label < 0 || label >= held.Labels.Length)
            {
                throw new ArgumentException($"The sheet {held.Name} has no label {label}: it has {held.Labels.Length}");
            }
            text = CheckedText(text);
            return held.Labels[label].Text == text ? document
                : SketchEdits.ChangeSheet(document, held, held with { Labels = held.Labels.SetItem(label, held.Labels[label] with { Text = text }) });
        }

        private static string CheckedText(string text)
        {
            text = text.Trim();
            if (text.Length == 0)
            {
                throw new ArgumentException("A label needs a text", nameof(text));
            }
            if (text.Length > LabelLength)
            {
                throw new ArgumentException($"A label holds at most {LabelLength} characters, not {text.Length}", nameof(text));
            }
            return text.ReplaceLineEndings(" ");
        }

        private static void CheckSize(float size)
        {
            if (!(size >= SketchLook.MinimumOgrids && size <= SketchLook.MaximumOgrids))
            {
                throw new ArgumentException($"The size of a symbol or a label lies between {SketchLook.MinimumOgrids} and {SketchLook.MaximumOgrids} ogrids, not {size}", nameof(size));
            }
        }

        private static void CheckPlace(Vector2 place)
        {
            if (!float.IsFinite(place.X) || !float.IsFinite(place.Y))
            {
                throw new ArgumentException("A place is two numbers", nameof(place));
            }
        }

        // the sheet named (or the first); a sketch without sheets gets its first, named as asked
        private static MapDocument SheetOf(MapDocument document, string? sheet, out string used)
        {
            if (document.Sketch.IsEmpty)
            {
                return document.AddSheet(sheet, out used);
            }
            used = sheet is null ? document.Sketch.Sheets[0].Name : document.SheetNamed(sheet).Name;
            return document;
        }

        #endregion

        #region Items

        /// <summary>
        /// The document with items of a sheet moved by an offset in ogrids.
        /// </summary>
        /// <exception cref="ArgumentException">No such sheet or item, or the offset is not a number.</exception>
        public static MapDocument MoveItems(this MapDocument document, string sheet, IReadOnlyCollection<SketchItem> items, float dx, float dz)
        {
            if (!float.IsFinite(dx) || !float.IsFinite(dz))
            {
                throw new ArgumentException("An offset is two numbers", nameof(dx));
            }
            SketchSheet held = document.SheetNamed(sheet);
            if (items.Count == 0 || (dx == 0 && dz == 0))
            {
                return document;
            }
            Vector2 offset = new Vector2(dx, dz);
            return SketchEdits.ChangeSheet(document, held, Changed(held, items,
                stroke => stroke with { Points = [.. stroke.Points.Select(point => point + offset)] },
                symbol => symbol with { Place = symbol.Place + offset },
                label => label with { Place = label.Place + offset }));
        }

        /// <summary>
        /// The document with items of a sheet in another colour.
        /// </summary>
        /// <exception cref="ArgumentException">No such sheet or item.</exception>
        public static MapDocument RecolourItems(this MapDocument document, string sheet, IReadOnlyCollection<SketchItem> items, SketchColour colour)
        {
            SketchSheet held = document.SheetNamed(sheet);
            SketchSheet changed = Changed(held, items, stroke => stroke with { Colour = colour }, symbol => symbol with { Colour = colour }, label => label with { Colour = colour });
            return Same(changed, held) ? document : SketchEdits.ChangeSheet(document, held, changed);
        }

        /// <summary>
        /// The document with the symbols and labels among the items of a sheet given a size, in ogrids.
        /// </summary>
        /// <exception cref="ArgumentException">No such sheet or item, or a size outside the range.</exception>
        public static MapDocument ResizeItems(this MapDocument document, string sheet, IReadOnlyCollection<SketchItem> items, float size)
        {
            CheckSize(size);
            SketchSheet held = document.SheetNamed(sheet);
            SketchSheet changed = Changed(held, items, stroke => stroke, symbol => symbol with { Size = size }, label => label with { Size = size });
            return Same(changed, held) ? document : SketchEdits.ChangeSheet(document, held, changed);
        }

        /// <summary>
        /// The document without items of a sheet.
        /// </summary>
        /// <exception cref="ArgumentException">No such sheet or item.</exception>
        public static MapDocument DeleteItems(this MapDocument document, string sheet, IReadOnlyCollection<SketchItem> items)
        {
            SketchSheet held = document.SheetNamed(sheet);
            Check(held, items);
            if (items.Count == 0)
            {
                return document;
            }
            HashSet<int> strokes = [.. items.Where(item => item.Kind == SketchItemKind.Stroke).Select(item => item.Index)];
            HashSet<int> symbols = [.. items.Where(item => item.Kind == SketchItemKind.Symbol).Select(item => item.Index)];
            HashSet<int> labels = [.. items.Where(item => item.Kind == SketchItemKind.Label).Select(item => item.Index)];
            return SketchEdits.ChangeSheet(document, held, held with
            {
                Strokes = [.. held.Strokes.Where((_, i) => !strokes.Contains(i))],
                Symbols = [.. held.Symbols.Where((_, i) => !symbols.Contains(i))],
                Labels = [.. held.Labels.Where((_, i) => !labels.Contains(i))],
            });
        }

        // whether two versions of a sheet hold the same things (the arrays are compared item by item)
        private static bool Same(SketchSheet a, SketchSheet b) =>
            a.Strokes.SequenceEqual(b.Strokes) && a.Symbols.SequenceEqual(b.Symbols) && a.Labels.SequenceEqual(b.Labels);

        private static void Check(SketchSheet sheet, IReadOnlyCollection<SketchItem> items)
        {
            foreach (SketchItem item in items)
            {
                if (!SketchPicking.Exists(sheet, item))
                {
                    throw new ArgumentException($"The sheet {sheet.Name} has no {item.Kind.ToString().ToLowerInvariant()} {item.Index}");
                }
            }
        }

        private static SketchSheet Changed(SketchSheet sheet, IReadOnlyCollection<SketchItem> items, Func<SketchStroke, SketchStroke> stroke,
            Func<SketchSymbol, SketchSymbol> symbol, Func<SketchLabel, SketchLabel> label)
        {
            Check(sheet, items);
            ImmutableArray<SketchStroke>.Builder strokes = sheet.Strokes.ToBuilder();
            ImmutableArray<SketchSymbol>.Builder symbols = sheet.Symbols.ToBuilder();
            ImmutableArray<SketchLabel>.Builder labels = sheet.Labels.ToBuilder();
            foreach (SketchItem item in items)
            {
                switch (item.Kind)
                {
                    case SketchItemKind.Stroke:
                        strokes[item.Index] = stroke(strokes[item.Index]);
                        break;
                    case SketchItemKind.Symbol:
                        symbols[item.Index] = symbol(symbols[item.Index]);
                        break;
                    default:
                        labels[item.Index] = label(labels[item.Index]);
                        break;
                }
            }
            return sheet with { Strokes = strokes.ToImmutable(), Symbols = symbols.ToImmutable(), Labels = labels.ToImmutable() };
        }

        #endregion
    }
}
