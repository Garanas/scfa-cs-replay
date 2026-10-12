using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Files.Serialized;

namespace FAForever.MapEditor.Project
{
    // The map maker's sketch (user story N-1): an optional file of the project, never exported.
    public static partial class MapProjectStore
    {
        /// <summary>
        /// Writes the sketch (<c>sketch/sketch.json</c>); a map without sheets gets no such file, and
        /// the folder goes when nothing else of the project's own lies in it.
        /// </summary>
        private static void SaveSketch(MapDocument document, string folder)
        {
            string file = Path.Combine(folder, MapProjectLayout.Sketch);
            if (document.Sketch.IsEmpty)
            {
                string sketch = Path.Combine(folder, MapProjectLayout.SketchFolder);
                if (Directory.Exists(sketch))
                {
                    File.Delete(file);
                    if (!Directory.EnumerateFileSystemEntries(sketch).Any())
                    {
                        Directory.Delete(sketch);
                    }
                }
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            ProjectJson.Write(file, ToDto(document.Sketch));
        }

        /// <summary>
        /// The document with the sketch of the project's file, if it has one.
        /// </summary>
        /// <exception cref="MapProjectException">The file does not read.</exception>
        private static MapDocument WithSketch(MapDocument document, string folder)
        {
            string file = Path.Combine(folder, MapProjectLayout.Sketch);
            return File.Exists(file) ? document with { Sketch = ToModel(ProjectJson.Read<SketchDto>(file), file) } : document;
        }

        /// <summary>
        /// The document with the sketch read again after its file changed or went; a file that holds
        /// what the document has changes nothing, so the editor's own saves are no edits.
        /// </summary>
        private static MapDocument ReloadSketch(MapDocument document, string folder)
        {
            string file = Path.Combine(folder, MapProjectLayout.Sketch);
            if (!File.Exists(file))
            {
                return document.Sketch.IsEmpty ? document : document with { Sketch = Sketch.Empty };
            }
            return ReloadJson(document, file, ToDto(document.Sketch), (d, dto) => d with { Sketch = ToModel(dto, file) });
        }

        /// <summary>
        /// The newest version of the sketch's file this editor reads (2: symbols and labels).
        /// </summary>
        private const int SketchVersion = 2;

        private static SketchDto ToDto(Sketch sketch) => new SketchDto
        {
            // version 1 while no sheet holds a symbol or a label: an older editor reads that file too
            Version = sketch.Sheets.Any(sheet => !sheet.Symbols.IsEmpty || !sheet.Labels.IsEmpty) ? SketchVersion : 1,
            Sheets = [.. sketch.Sheets.Select(sheet => new SketchSheetDto
            {
                Name = sheet.Name,
                Shown = sheet.Shown,
                Opacity = sheet.Opacity,
                Legend = sheet.HasLegend ? [.. sheet.Legend] : null,
                Strokes = [.. sheet.Strokes.Select(stroke => new SketchStrokeDto { Colour = stroke.Colour, Width = stroke.Width, Points = PointsText(stroke.Points) })],
                Symbols = sheet.Symbols.IsEmpty ? null : [.. sheet.Symbols.Select(symbol => new SketchSymbolDto
                {
                    Kind = symbol.Kind,
                    Place = PointsText([symbol.Place]),
                    Colour = symbol.Colour,
                    Size = MathF.Round(symbol.Size, 2),
                    Turn = MathF.Round(symbol.Turn, 1) + 0f,
                })],
                Labels = sheet.Labels.IsEmpty ? null : [.. sheet.Labels.Select(label => new SketchLabelDto
                {
                    Text = label.Text,
                    Place = PointsText([label.Place]),
                    Colour = label.Colour,
                    Size = MathF.Round(label.Size, 2),
                })],
            })],
        };

        private static Sketch ToModel(SketchDto dto, string file)
        {
            if (dto.Version > SketchVersion)
            {
                throw new MapProjectException($"{file} is version {dto.Version} of the sketch's file; this editor reads versions 1 and {SketchVersion}: update the editor");
            }
            ImmutableArray<SketchSheet>.Builder sheets = ImmutableArray.CreateBuilder<SketchSheet>();
            foreach (SketchSheetDto sheet in dto.Sheets)
            {
                if (string.IsNullOrWhiteSpace(sheet.Name))
                {
                    throw new MapProjectException($"{file}: a sheet has no name");
                }
                if (sheets.Any(other => other.Name.Equals(sheet.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new MapProjectException($"{file}: two sheets are named '{sheet.Name}'");
                }
                ImmutableArray<string> legend = SketchSheet.NoLegend;
                if (sheet.Legend is { } texts)
                {
                    legend = [.. Enumerable.Range(0, SketchLook.ColourCount).Select(i => i < texts.Count ? texts[i] ?? "" : "")];
                }
                sheets.Add(new SketchSheet
                {
                    Name = sheet.Name,
                    Shown = sheet.Shown,
                    Opacity = Math.Clamp(sheet.Opacity, 0, 1),
                    Legend = legend,
                    Strokes = [.. sheet.Strokes.Select(stroke => new SketchStroke(stroke.Colour, stroke.Width, ParsePoints(stroke.Points, file, sheet.Name)))],
                    Symbols = [.. (sheet.Symbols ?? []).Select(symbol => new SketchSymbol(
                        Enum.IsDefined(symbol.Kind) ? symbol.Kind : throw new MapProjectException($"{file}: the sheet '{sheet.Name}' has a symbol of a kind that does not exist"),
                        ParsePoints(symbol.Place, file, sheet.Name)[0],
                        symbol.Colour,
                        Math.Clamp(symbol.Size, SketchLook.MinimumOgrids, SketchLook.MaximumOgrids),
                        float.IsFinite(symbol.Turn) ? symbol.Turn : 0))],
                    Labels = [.. (sheet.Labels ?? []).Select(label => new SketchLabel(
                        label.Text ?? "",
                        ParsePoints(label.Place, file, sheet.Name)[0],
                        label.Colour,
                        Math.Clamp(label.Size, SketchLook.MinimumOgrids, SketchLook.MaximumOgrids)))],
                });
            }
            return new Sketch { Sheets = sheets.ToImmutable() };
        }

        private static string PointsText(ImmutableArray<Vector2> points)
        {
            StringBuilder text = new StringBuilder(points.Length * 12);
            foreach (Vector2 point in points)
            {
                if (text.Length > 0)
                {
                    text.Append(' ');
                }
                text.Append(Number(point.X)).Append(',').Append(Number(point.Y));
            }
            return text.ToString();
        }

        private static string Number(float value) => (MathF.Round(value, 2) + 0f).ToString("0.##", CultureInfo.InvariantCulture);

        private static ImmutableArray<Vector2> ParsePoints(string text, string file, string sheet)
        {
            ImmutableArray<Vector2>.Builder points = ImmutableArray.CreateBuilder<Vector2>();
            foreach (string pair in text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = pair.Split(',');
                if (parts.Length != 2
                    || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                    || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
                    || !float.IsFinite(x) || !float.IsFinite(z))
                {
                    throw new MapProjectException($"{file}: the sheet '{sheet}' has a stroke with a place '{pair}' that is not 'x,z' in ogrids");
                }
                points.Add(new Vector2(x, z));
            }
            if (points.Count == 0)
            {
                throw new MapProjectException($"{file}: the sheet '{sheet}' has a stroke without places");
            }
            return points.ToImmutable();
        }
    }
}
