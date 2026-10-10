using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Cli
{
    /// <summary>
    /// <c>faf-map dev scan-units</c> and <c>dev unit-templates</c>: the unit groups of real maps
    /// (<see cref="UnitGroupScan"/>), and units presets made from chosen ones (<see cref="UnitTemplates"/>).
    /// Both only read the maps.
    /// </summary>
    public static class UnitScanCommand
    {
        /// <summary>
        /// Scans the folders, writes the result as JSON when asked, and prints the maps that could
        /// not be read and the most common patterns.
        /// </summary>
        public static int Scan(IReadOnlyList<string> folders, string? output)
        {
            UnitScanResult result = UnitGroupScan.Scan(folders);
            if (output is not null)
            {
                UnitGroupScan.Write(result, output);
            }
            foreach (ScannedMap map in result.Maps.Where(map => map.Error is not null))
            {
                Console.Error.WriteLine($"{map.Folder}: {map.Error}");
            }
            Console.WriteLine($"{result.Maps.Count} maps ({result.Maps.Count(m => m.Error is not null)} unreadable), {result.Groups.Count} groups with units, {result.Maps.Sum(m => m.Units)} units");
            foreach (IGrouping<UnitGroupCategory, ScannedGroup> category in result.Groups.GroupBy(g => g.Category).OrderByDescending(g => g.Count()))
            {
                Console.WriteLine($"  {category.Key}: {category.Count()} groups");
            }
            Console.WriteLine("Most common patterns:");
            foreach (PatternSummary pattern in result.Patterns.Take(30))
            {
                Console.WriteLine(FormattableString.Invariant($"  {pattern.Pattern} ({pattern.Category}): {pattern.Groups} groups on {pattern.Maps} maps, median {pattern.MedianUnits:0.#} units over {pattern.MedianSize:0} ogrids"));
            }
            if (output is not null)
            {
                Console.WriteLine($"Wrote {output}");
            }
            return 0;
        }

        /// <summary>
        /// Makes the presets a file of picks names from the maps under the folders, into a presets folder.
        /// </summary>
        public static int Templates(string picksFile, IReadOnlyList<string> folders, string? output)
        {
            if (output is null)
            {
                Console.Error.WriteLine("Give --out: the presets folder to write into");
                return 2;
            }
            try
            {
                IReadOnlyList<UnitTemplatePick> picks = UnitTemplates.ReadPicks(picksFile);
                (IReadOnlyList<Preset> presets, IReadOnlyList<string> problems) = UnitTemplates.Make(picks, folders);
                foreach (string problem in problems)
                {
                    Console.Error.WriteLine(problem);
                }
                foreach (string file in UnitTemplates.Write(presets, output))
                {
                    Console.WriteLine($"Wrote {file}");
                }
                return problems.Count == 0 ? 0 : 1;
            }
            catch (MapProjectException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }
    }
}
