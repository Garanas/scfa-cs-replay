using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using FAForever.MapEditor.Project.Checks;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// Groups placed from unit presets (user story U-9): the link a placed group keeps, the
    /// fingerprint of a preset's content, the project files, the export, saving an instance back,
    /// detaching it and the check.
    /// </summary>
    [TestClass]
    public class PresetInstancesTest
    {
        private static MapDocument original = null!;

        private static readonly DateTimeOffset PlacedAt = new DateTimeOffset(2026, 10, 9, 14, 30, 0, TimeSpan.FromHours(2));

        [ClassInitialize]
        public static void Initialize(TestContext _) => original = MapImporter.ImportFolder(ProjectFolders.HardFfa);

        /// <summary>
        /// A farm of three units in two groups.
        /// </summary>
        private static UnitPresetSettings Farm() => new UnitPresetSettings(
            new UnitPresetGroup("Farm",
                [new UnitPresetUnit("uec1201", -4, 0, 0, 0)],
                [new UnitPresetGroup("Houses", [new UnitPresetUnit("uec1101", 4, 0, 2, 0), new UnitPresetUnit("uec1101", 4, 0, -2, MathF.PI / 2)], [])]),
            3, ArmyRoles.Civilian, false, "a test");

        private static MapDocument PlaceFarm(MapDocument document, out ImmutableArray<string> group, float x = 100, float z = 100) =>
            UnitPresets.Place(document, Farm(), "Farm", "ARMY_1", null, ["INITIAL"], null, x, z, MathF.PI / 2, out _, out group, out _, PlacedAt);

        private static PresetLibrary Library()
        {
            PresetLibrary library = new PresetLibrary(ProjectFolders.New("presets"));
            library.Save(Preset.Of(UnitPresets.Kind, "Farm", Farm()));
            return library;
        }

        [TestMethod]
        public void PlacingAPresetMakesTheGroupAnInstance()
        {
            MapDocument placed = PlaceFarm(original, out ImmutableArray<string> group);
            UnitGroup farm = placed.FindGroup("ARMY_1", group)!;
            PresetInstance instance = farm.Instance ?? throw new AssertFailedException("the group placed is the instance");
            Assert.AreEqual("Farm", instance.Preset);
            Assert.AreEqual(UnitPresets.Fingerprint(Farm()), instance.Fingerprint);
            Assert.AreEqual((100f, 100f, MathF.PI / 2), (instance.X, instance.Z, instance.Heading), "where and how it was placed");
            Assert.AreEqual(PlacedAt, instance.PlacedAt);
            Assert.IsNull(farm.Groups.Single().Instance, "the groups in it are part of the instance, not instances");
            Assert.IsNull(placed.FindGroup("ARMY_1", ["INITIAL"])!.Instance);

            PlacedInstance[] instances = [.. placed.Instances()];
            Assert.AreEqual(1, instances.Length);
            Assert.AreEqual(("ARMY_1", "INITIAL/Farm"), (instances[0].Army, UnitGroupEdits.PathText(instances[0].Path)));
            Assert.AreEqual(1, placed.CountInstances("farm"), "counted by the preset's name, ignoring case");

            // a unit of the group, and of a group in it, belongs to the instance; a unit added to it later too
            PlacedUnit house = placed.UnitsIn("ARMY_1", [.. group, "Houses"]).First();
            PlacedInstance of = placed.InstanceOf(house.Unit.Name) ?? throw new AssertFailedException("a unit of a group in the instance belongs to it");
            Assert.AreEqual(instances[0].Instance, of.Instance);
            CollectionAssert.AreEqual(group.ToArray(), of.Path.ToArray());
            MapDocument more = placed.AddUnit("ARMY_1", group, new Unit { Name = "UNIT_900", Blueprint = "uec1101", Position = new Vector3(110, 0, 110) });
            Assert.AreEqual("Farm", more.InstanceOf("UNIT_900")?.Instance.Preset);
            Assert.IsNull(placed.InstanceOf("ARMY_1", ["INITIAL"]), "a group placed by hand belongs to none");

            // the root group is never an instance, and a template placed as a selection preset makes none
            Assert.ThrowsException<ArgumentException>(() => placed.SetInstance("ARMY_1", [], instances[0].Instance));
            MapDocument template = original.PlaceUnits(UnitPresets.ToTemplate(Farm().Group), "ARMY_2", ["INITIAL"], 50, 50, 0, out _, out ImmutableArray<string> plain);
            Assert.IsNull(template.FindGroup("ARMY_2", plain)!.Instance);
        }

        [TestMethod]
        public void TheFingerprintIsTheSameEverywhereAndChangesWithTheContent()
        {
            UnitPresetSettings farm = Farm();
            Assert.AreEqual(UnitPresets.Fingerprint(farm), UnitPresets.Fingerprint(Farm()), "the same content gives the same fingerprint");
            Assert.AreEqual(16, UnitPresets.Fingerprint(farm).Length);
            // the value is pinned: it must be the same on every computer and for every editor version that reads these projects
            Assert.AreEqual("daab69722aa32e86", UnitPresets.Fingerprint(farm));

            Preset written = Preset.Of(UnitPresets.Kind, "Farm", farm);
            Assert.AreEqual(UnitPresets.Fingerprint(farm), UnitPresets.Fingerprint(written), "through a preset's JSON and back");
            PresetLibrary library = Library();
            Assert.AreEqual(UnitPresets.Fingerprint(farm), PresetInstances.FingerprintOf(library, "farm"), "from the file, found in any case");
            Assert.IsNull(PresetInstances.FingerprintOf(library, "Nowhere"));

            UnitPresetUnit house = farm.Group.Groups[0].Units[0];
            UnitPresetGroup Houses(UnitPresetUnit changed) => new UnitPresetGroup("Houses", [changed, farm.Group.Groups[0].Units[1]], []);
            UnitPresetSettings With(UnitPresetGroup houses) => farm with { Group = farm.Group with { Groups = [houses] } };
            Assert.AreNotEqual(UnitPresets.Fingerprint(farm), UnitPresets.Fingerprint(With(Houses(house with { X = 5 }))), "a unit moved");
            Assert.AreNotEqual(UnitPresets.Fingerprint(farm), UnitPresets.Fingerprint(With(Houses(house with { Heading = 1 }))), "a unit turned");
            Assert.AreNotEqual(UnitPresets.Fingerprint(farm), UnitPresets.Fingerprint(With(Houses(house with { Blueprint = "uec1301" }))), "another unit");
            Assert.AreNotEqual(UnitPresets.Fingerprint(farm), UnitPresets.Fingerprint(With(new UnitPresetGroup("Houses", [house], []))), "a unit taken out");
            Assert.AreNotEqual(UnitPresets.Fingerprint(farm), UnitPresets.Fingerprint(farm with { Wrecks = true }), "placed as wrecks");
        }

        [TestMethod]
        public void AnInstanceIsKeptInTheProjectAndAnOlderProjectHasNone()
        {
            MapDocument placed = PlaceFarm(original, out ImmutableArray<string> group);
            string project = ProjectFolders.New("instances");
            MapProjectStore.Save(placed, project);
            string armies = File.ReadAllText(Path.Combine(project, MapProjectLayout.Armies));
            StringAssert.Contains(armies, "\"instance\"");
            StringAssert.Contains(armies, "\"preset\": \"Farm\"");

            MapDocument loaded = MapProjectStore.Load(project);
            Assert.AreEqual(placed.FindGroup("ARMY_1", group)!.Instance, loaded.FindGroup("ARMY_1", group)!.Instance, "the same link, time and offset included");
            Assert.AreEqual(1, loaded.Instances().Count());

            // a project saved before instances holds no "instance" at all and loads with none
            string plain = ProjectFolders.New("plain");
            MapProjectStore.Save(original, plain);
            Assert.IsFalse(File.ReadAllText(Path.Combine(plain, MapProjectLayout.Armies)).Contains("\"instance\"", StringComparison.Ordinal));
            Assert.IsFalse(MapProjectStore.Load(plain).Instances().Any());
            Assert.IsFalse(original.Instances().Any(), "an import never makes one");
        }

        [TestMethod]
        public void TheExportLeavesTheInstanceOut()
        {
            MapDocument placed = PlaceFarm(original, out ImmutableArray<string> group);
            MapDocument detached = placed.Detach("ARMY_1", group);
            IReadOnlyDictionary<string, byte[]> withInstance = MapExporter.ToBytes(placed);
            IReadOnlyDictionary<string, byte[]> without = MapExporter.ToBytes(detached);
            foreach ((string file, byte[] bytes) in withInstance)
            {
                CollectionAssert.AreEqual(bytes, without[file], $"{file} is the same with and without the link");
            }
            string save = Encoding.Latin1.GetString(withInstance.First(file => file.Key.EndsWith("_save.lua", StringComparison.Ordinal)).Value);
            Assert.IsFalse(save.Contains("fingerprint", StringComparison.OrdinalIgnoreCase));
            StringAssert.Contains(save, "Farm = GROUP", "the group itself is exported");

            string folder = ProjectFolders.New("exported");
            Directory.CreateDirectory(folder);
            foreach ((string name, byte[] bytes) in withInstance)
            {
                File.WriteAllBytes(Path.Combine(folder, name), bytes);
            }
            Assert.IsFalse(MapImporter.ImportFolder(folder).Instances().Any(), "imported again, the group is an ordinary group");
        }

        [TestMethod]
        public void SavingBackMakesTheInstanceCurrentAndDetachingForgetsIt()
        {
            PresetLibrary library = Library();
            MapDocument placed = PlaceFarm(original, out ImmutableArray<string> group);
            PresetInstance instance = placed.FindGroup("ARMY_1", group)!.Instance!;
            Assert.AreEqual(PresetInstanceState.Current, PresetInstances.StateOf(instance, library));

            // the preset's file changes on disk: the instance is out of date; without the file it is missing
            library.Save(Preset.Of(UnitPresets.Kind, "Farm", Farm() with { Count = 4 }));
            Assert.AreEqual(PresetInstanceState.Changed, PresetInstances.StateOf(instance, library));
            library.Delete(UnitPresets.Kind, "Farm");
            Assert.AreEqual(PresetInstanceState.Missing, PresetInstances.StateOf(instance, library));
            library.Save(Preset.Of(UnitPresets.Kind, "Farm", Farm() with { Role = ArmyRoles.Enemy }));

            // a unit added to the instance, then saved back: the preset holds the group's layout, keeps its role, and the instance is current
            MapDocument more = placed.AddUnit("ARMY_1", group, new Unit { Name = "UNIT_900", Blueprint = "uec1301", Position = new Vector3(110, 0, 110) });
            MapDocument saved = more.SaveInstance("ARMY_1", group, library, out Preset preset);
            Assert.AreEqual("Farm", preset.Name);
            UnitPresetSettings settings = library.Find(UnitPresets.Kind, "Farm")!.Read<UnitPresetSettings>();
            Assert.AreEqual(4, settings.Count);
            Assert.AreEqual(4, UnitPresets.CountUnits(settings.Group));
            Assert.AreEqual(ArmyRoles.Enemy, settings.Role, "the file's role stays");
            Assert.AreEqual("Houses", settings.Group.Groups.Single().Name, "with the groups in it");
            Assert.IsTrue(settings.Group.Units.Any(unit => unit.Blueprint == "uec1301"), "the unit added is in it");
            PresetInstance after = saved.FindGroup("ARMY_1", group)!.Instance!;
            Assert.AreEqual(PresetInstanceState.Current, PresetInstances.StateOf(after, library));
            Assert.AreEqual(UnitPresets.Fingerprint(settings), after.Fingerprint);
            Assert.AreEqual((instance.X, instance.Z, instance.Heading, instance.PlacedAt), (after.X, after.Z, after.Heading, after.PlacedAt), "where and when it was placed stays");
            Assert.AreEqual(PresetInstanceState.Changed, PresetInstances.StateOf(instance, library), "the other instances of the preset are out of date now");

            // saving back into a preset that is gone makes the file again
            library.Delete(UnitPresets.Kind, "Farm");
            MapDocument again = saved.SaveInstance("ARMY_1", group, library, out _);
            Assert.IsNotNull(library.Find(UnitPresets.Kind, "Farm"));
            Assert.AreEqual(PresetInstanceState.Current, PresetInstances.StateOf(again.FindGroup("ARMY_1", group)!.Instance!, library));

            MapDocument detached = saved.Detach("ARMY_1", group);
            Assert.IsNull(detached.FindGroup("ARMY_1", group)!.Instance);
            Assert.AreEqual(4, detached.UnitsIn("ARMY_1", group).Count(), "the units stay");
            Assert.ThrowsException<ArgumentException>(() => detached.Detach("ARMY_1", group), "no instance");
            Assert.ThrowsException<ArgumentException>(() => detached.SaveInstance("ARMY_1", group, library, out _));
            Assert.ThrowsException<KeyNotFoundException>(() => detached.Detach("ARMY_1", ["Nowhere"]));
        }

        [TestMethod]
        public void TheCheckListsInstancesWhosePresetChangedOrIsMissing()
        {
            MapDocument placed = PlaceFarm(PlaceFarm(original, out ImmutableArray<string> first), out ImmutableArray<string> second, 200, 200);
            string fingerprint = UnitPresets.Fingerprint(Farm());
            Problem[] current = [.. MapChecks.PresetsChanged(new ProblemContext(placed, PresetFingerprint: _ => fingerprint))];
            Assert.AreEqual(0, current.Length, "both are current");

            Problem[] changed = [.. MapChecks.PresetsChanged(new ProblemContext(placed, PresetFingerprint: _ => "another"))];
            Assert.AreEqual(2, changed.Length);
            Assert.IsTrue(changed.All(problem => problem.Check == "preset-changed" && problem.Severity == ProblemSeverity.Info));
            StringAssert.Contains(changed[0].Message, $"The group {UnitGroupEdits.PathText(first)} of ARMY_1 was placed from the preset Farm, which changed since");
            Assert.AreEqual(ObjectRef.Unit(placed.UnitsIn("ARMY_1", first).First().Unit.Name), changed[0].Thing);
            Assert.IsNotNull(changed[0].Place);
            StringAssert.Contains(changed[1].Message, UnitGroupEdits.PathText(second));

            Problem[] missing = [.. MapChecks.PresetsChanged(new ProblemContext(placed, PresetFingerprint: _ => null))];
            StringAssert.Contains(missing[0].Message, "which the workspace has no preset of");
            Assert.AreEqual(0, MapChecks.PresetsChanged(new ProblemContext(placed)).Count(), "without a workspace nothing is known");
            Assert.IsTrue(new ProblemChecks().All.Any(check => check.Id == "preset-changed"), "among the editor's checks");
        }
    }
}
