using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// How a placed instance stands to its preset's file (user story U-9).
    /// </summary>
    public enum PresetInstanceState
    {
        /// <summary>
        /// The preset's file holds what it held when the instance was placed (or saved back).
        /// </summary>
        Current,

        /// <summary>
        /// The preset's file changed since: its fingerprint differs from the instance's.
        /// </summary>
        Changed,

        /// <summary>
        /// The workspace has no preset of the name.
        /// </summary>
        Missing,
    }

    /// <summary>
    /// A group placed from a unit preset: its army, its path and the link.
    /// </summary>
    public sealed record PlacedInstance(string Army, ImmutableArray<string> Path, UnitGroup Group, PresetInstance Instance);

    /// <summary>
    /// The groups placed from unit presets (user story U-9, <see cref="UnitGroup.Instance"/>): placing
    /// a preset makes a group, and that group is the instance (units added to it later belong to it).
    /// The link is shown and used to select the instance, to save its layout back into the preset and
    /// to detach it; the map is never changed from the preset.
    /// </summary>
    public static class PresetInstances
    {
        /// <summary>
        /// Every group with an instance, in the order of the armies and their trees.
        /// </summary>
        public static IEnumerable<PlacedInstance> Instances(this MapDocument document)
        {
            foreach (Army army in document.Armies)
            {
                foreach (PlacedInstance instance in InstancesIn(army.Name, [], army.Units))
                {
                    yield return instance;
                }
            }
        }

        private static IEnumerable<PlacedInstance> InstancesIn(string army, ImmutableArray<string> path, UnitGroup group)
        {
            if (group.Instance is PresetInstance instance)
            {
                yield return new PlacedInstance(army, path, group, instance);
            }
            foreach (UnitGroup child in group.Groups)
            {
                foreach (PlacedInstance below in InstancesIn(army, path.Add(child.Name), child))
                {
                    yield return below;
                }
            }
        }

        /// <summary>
        /// The instance a group belongs to: the deepest group along its path that was placed from a
        /// preset (the group itself, or one holding it), or null when none was.
        /// </summary>
        public static PlacedInstance? InstanceOf(this MapDocument document, string army, IReadOnlyList<string> path)
        {
            UnitGroup? group = document.Armies.FirstOrDefault(a => a.Name == army)?.Units;
            PlacedInstance? found = null;
            for (int depth = 0; group is not null; depth++)
            {
                if (group.Instance is PresetInstance instance)
                {
                    found = new PlacedInstance(army, [.. path.Take(depth)], group, instance);
                }
                group = depth < path.Count ? group.Groups.FirstOrDefault(child => child.Name == path[depth]) : null;
            }
            return found;
        }

        /// <summary>
        /// The instance a unit belongs to (<see cref="InstanceOf(MapDocument, string, IReadOnlyList{string})"/>
        /// of its group), or null.
        /// </summary>
        public static PlacedInstance? InstanceOf(this MapDocument document, string unitName) =>
            document.FindUnit(unitName) is PlacedUnit unit ? document.InstanceOf(unit.Army, unit.Group) : null;

        /// <summary>
        /// How many groups of the map were placed from a preset of a name (ignoring case, as the
        /// preset files are named).
        /// </summary>
        public static int CountInstances(this MapDocument document, string preset) =>
            document.Instances().Count(instance => string.Equals(instance.Instance.Preset, preset, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// How an instance stands to the preset's file in a library: <see cref="PresetInstanceState.Missing"/>
        /// without one, <see cref="PresetInstanceState.Changed"/> when the file's fingerprint differs.
        /// Reads the file.
        /// </summary>
        public static PresetInstanceState StateOf(PresetInstance instance, PresetLibrary library) =>
            StateOf(instance, name => FingerprintOf(library, name));

        /// <summary>
        /// How an instance stands to its preset, given the fingerprint of the preset of a name (null
        /// for none): what the checks and the editor read without opening a file each time.
        /// </summary>
        public static PresetInstanceState StateOf(PresetInstance instance, Func<string, string?> fingerprintOf) =>
            fingerprintOf(instance.Preset) switch
            {
                null => PresetInstanceState.Missing,
                string fingerprint when fingerprint == instance.Fingerprint => PresetInstanceState.Current,
                _ => PresetInstanceState.Changed,
            };

        /// <summary>
        /// The fingerprint of a library's units preset of a name, or null when there is none or its
        /// file cannot be read.
        /// </summary>
        public static string? FingerprintOf(PresetLibrary library, string name)
        {
            try
            {
                return library.Find(UnitPresets.Kind, name) is Preset preset ? UnitPresets.Fingerprint(preset) : null;
            }
            catch (Exception exception) when (exception is MapProjectException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Saves an instance's layout back into its preset: the group's units (with those of the groups
        /// in it) captured as Save as a preset captures them (<see cref="UnitGroupEdits.CaptureUnits(MapDocument, IEnumerable{string}, string)"/>:
        /// around their middle, heights above the terrain), written over the preset's file (a file
        /// that is there keeps its role, wrecks and source), and the group's fingerprint set to the
        /// new content's: afterwards the instance is <see cref="PresetInstanceState.Current"/>. Where
        /// and when it was placed stays as it was.
        /// </summary>
        /// <param name="saved">The preset written.</param>
        /// <exception cref="KeyNotFoundException">The army or the group does not exist.</exception>
        /// <exception cref="ArgumentException">The group is no instance, or holds no units.</exception>
        /// <exception cref="IOException">The preset's file cannot be written.</exception>
        /// <exception cref="UnauthorizedAccessException">The preset's file cannot be written.</exception>
        public static MapDocument SaveInstance(this MapDocument document, string army, IReadOnlyList<string> path, PresetLibrary library, out Preset saved)
        {
            UnitGroup group = document.FindGroup(army, path) ?? throw new KeyNotFoundException($"The army {army} has no group {UnitGroupEdits.PathText(path)}");
            PresetInstance instance = group.Instance ?? throw new ArgumentException($"The group {UnitGroupEdits.PathText(path)} of {army} was not placed from a preset");
            string[] names = [.. document.UnitsIn(army, path).Select(unit => unit.Unit.Name)];
            if (names.Length == 0)
            {
                throw new ArgumentException($"The group {UnitGroupEdits.PathText(path)} of {army} holds no units to save into the preset {instance.Preset}");
            }
            UnitGroup template = document.CaptureUnits(names, instance.Preset);
            UnitPresetSettings? before = null;
            try
            {
                before = library.Find(UnitPresets.Kind, instance.Preset)?.Read<UnitPresetSettings>();
            }
            catch (MapProjectException)
            {
                // a file that holds no units preset is written over
            }
            UnitPresetSettings settings = new UnitPresetSettings(UnitPresets.ToPreset(template), names.Length, before?.Role, before?.Wrecks ?? false, before?.Source);
            saved = Preset.Of(UnitPresets.Kind, instance.Preset, settings);
            library.Save(saved);
            return document.SetInstance(army, path, instance with { Fingerprint = UnitPresets.Fingerprint(settings) });
        }

        /// <summary>
        /// Forgets the link between a group and its preset; the units stay as they are.
        /// </summary>
        /// <exception cref="KeyNotFoundException">The army or the group does not exist.</exception>
        /// <exception cref="ArgumentException">The group is no instance.</exception>
        public static MapDocument Detach(this MapDocument document, string army, IReadOnlyList<string> path)
        {
            UnitGroup group = document.FindGroup(army, path) ?? throw new KeyNotFoundException($"The army {army} has no group {UnitGroupEdits.PathText(path)}");
            if (group.Instance is null)
            {
                throw new ArgumentException($"The group {UnitGroupEdits.PathText(path)} of {army} was not placed from a preset");
            }
            return document.SetInstance(army, path, null);
        }

        /// <summary>
        /// The middle of an instance's units (x, z in ogrids), or the place it was placed at when it
        /// holds none: where to look at it.
        /// </summary>
        public static Vector2 PlaceOf(MapDocument document, PlacedInstance instance)
        {
            PlacedUnit[] units = [.. document.UnitsIn(instance.Army, instance.Path)];
            return units.Length == 0
                ? new Vector2(instance.Instance.X, instance.Instance.Z)
                : new Vector2(units.Average(unit => unit.Unit.Position.X), units.Average(unit => unit.Unit.Position.Z));
        }
    }
}
