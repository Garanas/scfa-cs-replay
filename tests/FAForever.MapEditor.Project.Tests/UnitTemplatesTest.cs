using System.Collections.Immutable;
using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The scan of the unit groups of real maps, units presets made from groups it found, the
    /// example unit presets and placing a preset by its army role.
    /// </summary>
    [TestClass]
    public class UnitTemplatesTest
    {
        private static string Unit(string name, string blueprint, float x, float z, float heading = 0) =>
            FormattableString.Invariant($"['{name}'] = {{ type = '{blueprint}', orders = '', platoon = '', Position = VECTOR3( {x}, 10, {z} ), Orientation = VECTOR3( 0, {heading}, 0 ) }},");

        private static string Group(string name, params string[] entries) =>
            $"['{name}'] = GROUP {{ orders = '', platoon = '', Units = {{ {string.Join(" ", entries)} }} }},";

        private static string Army(string name, params string[] groups) =>
            $"['{name}'] = {{ personality = '', plans = '', color = 0, faction = 0, Units = GROUP {{ orders = '', platoon = '', Units = {{ {string.Join(" ", groups)} }} }} }},";

        private static void WriteMap(string folder, string name, string[] teamArmies, string extraArmies, params string[] armies)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, $"{name}_save.lua"), $"Scenario = {{ Armies = {{ {string.Join("\n", armies)} }} }}");
            File.WriteAllText(Path.Combine(folder, $"{name}_scenario.lua"), $$"""
                version = 3
                ScenarioInfo = {
                    name = '{{name}}', type = 'skirmish', size = {256, 256}, map = '/maps/{{name}}/{{name}}.scmap',
                    Configurations = {
                        ['standard'] = {
                            teams = { { name = 'FFA', armies = { {{string.Join(", ", teamArmies.Select(a => $"'{a}'"))}} } } },
                            customprops = { ['ExtraArmies'] = STRING( '{{extraArmies}}' ) },
                        },
                    },
                }
                """);
        }

        /// <summary>
        /// Three map folders and one that is none: a skirmish map with a civilian town, a campaign
        /// map with a base split by role over two difficulty levels, walls, a patrol, wrecks and a
        /// commander, and a map whose save cannot be read.
        /// </summary>
        private static string Fixture()
        {
            string root = ProjectFolders.New("unit-scan");
            WriteMap(Path.Combine(root, "alpha.v0001"), "alpha", ["ARMY_1", "ARMY_2"], "ARMY_17",
                Army("ARMY_1", Group("INITIAL")),
                Army("ARMY_17", Group("INITIAL", Unit("UNIT_1", "uec1101", 100, 100), Unit("UNIT_2", "uec1201", 106, 100))));
            WriteMap(Path.Combine(root, "BETA"), "BETA", ["Player", "UEF", "Civilians"], "",
                Army("Player", Group("INITIAL", Unit("UNIT_3", "uel0001", 20, 20))),
                Army("UEF",
                    Group("M1_Base_D2",
                        Group("Defense", Unit("UNIT_4", "ueb2101", 50, 40), Unit("UNIT_5", "ueb2101", 70, 40)),
                        Group("Factories", Unit("UNIT_6", "ueb0101", 60, 50)),
                        Group("M1_Base_D1",
                            Group("Energy", Unit("UNIT_7", "ueb1101", 55, 60), Unit("UNIT_8", "ueb1101", 57, 60)),
                            Group("Mass", Unit("UNIT_9", "ueb1103", 65.5f, 60.5f)),
                            Group("Engineers", Unit("UNIT_10", "uel0105", 60, 62)))),
                    Group("M1_Walls", Unit("UNIT_11", "ueb5101", 80, 80), Unit("UNIT_12", "ueb5101", 81, 80), Unit("UNIT_13", "ueb5101", 82, 80), Unit("UNIT_14", "ueb5101", 83, 80)),
                    Group("M2LandPatrol", Unit("UNIT_15", "uel0201", 200, 200))),
                Army("Civilians",
                    Group("WRECKAGE", Unit("UNIT_16", "uel0201", 150, 150), Unit("UNIT_17", "uel0201", 154, 152, 1.5f)),
                    Group("Town", Unit("UNIT_18", "uec1101", 120, 120), Unit("UNIT_19", "uec1101", 124, 120), Unit("UNIT_20", "uec1301", 128, 124))));
            Directory.CreateDirectory(Path.Combine(root, "broken"));
            File.WriteAllText(Path.Combine(root, "broken", "broken_save.lua"), "Other = {}");
            Directory.CreateDirectory(Path.Combine(root, "notes"));
            return root;
        }

        [TestMethod]
        public void TheScanFindsTheGroupsOfEveryMapWithTheirKindRoleAndFaction()
        {
            UnitScanResult result = UnitGroupScan.Scan([Fixture()]);

            CollectionAssert.AreEqual(new[] { "alpha.v0001", "BETA", "broken" }, result.Maps.Select(map => map.Name).ToArray(), "folders without a save are no maps");
            Assert.IsNotNull(result.Maps.Single(map => map.Name == "broken").Error, "a map that cannot be read is listed with its error");
            ScannedMap beta = result.Maps.Single(map => map.Name == "BETA");
            CollectionAssert.AreEqual(new[] { "player", "enemy", "civilian" }, beta.Armies.Select(army => army.Role).ToArray());
            Assert.AreEqual("civilian", result.Maps[0].Armies.Single(army => army.Name == "ARMY_17").Role);

            ScannedGroup Find(string path) => result.Groups.Single(group => group.Map == "BETA" && group.Path == path);
            ScannedGroup baseGroup = Find("M1_Base_D2");
            Assert.AreEqual(UnitGroupCategory.Base, baseGroup.Category);
            Assert.AreEqual(2, baseGroup.Level);
            Assert.AreEqual(7, baseGroup.Units);
            Assert.AreEqual("UEF", baseGroup.Faction);
            CollectionAssert.AreEquivalent(
                new[] { "Defense=2", "Factories=1", "Energy=2", "Mass=1", "Engineers=1" },
                baseGroup.Roles!.Select(pair => $"{pair.Key}={pair.Value}").ToArray(),
                "the role groups of both difficulty levels");
            Assert.AreEqual(UnitGroupCategory.Base, Find("M1_Base_D2/M1_Base_D1").Category);
            Assert.AreEqual(UnitGroupCategory.BaseRole, Find("M1_Base_D2/Defense").Category);
            Assert.AreEqual(UnitGroupCategory.Walls, Find("M1_Walls").Category);
            Assert.AreEqual("M_Walls", Find("M1_Walls").Pattern, "numbers stripped");
            Assert.AreEqual(UnitGroupCategory.Scripting, Find("M2LandPatrol").Category);
            Assert.AreEqual(UnitGroupCategory.Wreckage, Find("WRECKAGE").Category);
            Assert.AreEqual(UnitGroupCategory.Civilian, Find("Town").Category);
            Assert.AreEqual("Civilian", Find("Town").Faction);
            Assert.AreEqual(UnitGroupCategory.Start, Find("INITIAL").Category);

            PatternSummary initial = result.Patterns.Single(pattern => pattern.Pattern == "INITIAL");
            Assert.AreEqual(2, initial.Groups, "the empty INITIAL of ARMY_1 holds no units");
            Assert.AreEqual(2, initial.Maps);
        }

        [TestMethod]
        public void APickOfCiviliansOnlyLeavesOutWallsAndDefencesAndWithoutLeavesOutBlueprints()
        {
            string root = ProjectFolders.New("unit-civilians");
            WriteMap(Path.Combine(root, "town"), "town", ["ARMY_1"], "NEUTRAL_CIVILIAN",
                Army("ARMY_1", Group("INITIAL")),
                Army("NEUTRAL_CIVILIAN", Group("INITIAL",
                    Unit("UNIT_1", "uec1101", 100, 100), Unit("UNIT_2", "uec1401", 106, 100), Unit("UNIT_3", "uec0001", 104, 104),
                    Unit("UNIT_4", "ueb5101", 96, 96), Unit("UNIT_5", "xec8001", 96, 98), Unit("UNIT_6", "xec9003", 96, 100),
                    Unit("UNIT_7", "ueb2101", 110, 110), Unit("UNIT_8", "xsc9002", 100, 108))));
            (IReadOnlyList<Preset> presets, IReadOnlyList<string> problems) = UnitTemplates.Make(
            [
                new UnitTemplatePick("Town", "town", "NEUTRAL_CIVILIAN", "INITIAL", ArmyRoles.Civilian, CivilianOnly: true),
                new UnitTemplatePick("Town without trucks", "town", "NEUTRAL_CIVILIAN", "INITIAL", ArmyRoles.Civilian, CivilianOnly: true, Without: ["UEC0001"]),
            ], [root]);

            Assert.AreEqual(0, problems.Count, string.Join("; ", problems));
            string[] Blueprints(Preset preset) => [.. preset.Read<UnitPresetSettings>().Group.Units.Select(unit => unit.Blueprint).Order(StringComparer.Ordinal)];
            CollectionAssert.AreEqual(new[] { "uec0001", "uec1101", "uec1401", "xsc9002" }, Blueprints(presets[0]), "the faction's walls and turret and the civilian wall segments left out");
            CollectionAssert.AreEqual(new[] { "uec1101", "uec1401", "xsc9002" }, Blueprints(presets[1]), "a blueprint left out, whatever its case");
            Assert.IsTrue(UnitGroupScan.IsCivilianWall("xrc8105"));
            Assert.IsFalse(UnitGroupScan.IsCivilianWall("xsc9002"), "the jammer crystal is no wall");
        }

        [TestMethod]
        public void APickBecomesAPresetAroundItsMiddleWithTheBasesRoleGroups()
        {
            string root = Fixture();
            UnitTemplatePick[] picks =
            [
                new UnitTemplatePick("UEF base, tiny", "BETA", "UEF", "M1_Base_D2", ArmyRoles.Enemy, Leave: ["Engineers"]),
                new UnitTemplatePick("Wrecks", "BETA", "Civilians", "WRECKAGE", ArmyRoles.Civilian, Wrecks: true),
                new UnitTemplatePick("Missing", "BETA", "UEF", "M9_Nothing", ArmyRoles.Enemy),
            ];
            (IReadOnlyList<Preset> presets, IReadOnlyList<string> problems) = UnitTemplates.Make(picks, [root]);

            Assert.AreEqual(2, presets.Count);
            StringAssert.Contains(problems.Single(), "Missing");
            UnitPresetSettings settings = presets[0].Read<UnitPresetSettings>();
            Assert.AreEqual(6, settings.Count, "the engineers left out");
            Assert.AreEqual(ArmyRoles.Enemy, settings.Role);
            Assert.AreEqual("BETA, army UEF, group M1_Base_D2", settings.Source);
            CollectionAssert.AreEqual(new[] { "Factories", "Energy", "Mass", "Defense" }, settings.Group.Groups.Select(group => group.Name).ToArray(), "in the order of the roles");
            // the middle of 50..70 by 40..60.5, rounded to a whole ogrid
            UnitPresetUnit mass = settings.Group.Groups.Single(group => group.Name == "Mass").Units.Single();
            Assert.AreEqual((5.5f, 10.5f), (mass.X, mass.Z), "the mass extractor keeps its half ogrid");

            UnitPresetSettings wrecks = presets[1].Read<UnitPresetSettings>();
            Assert.IsTrue(wrecks.Wrecks);
            Assert.AreEqual(1.5f, wrecks.Group.Units[1].Heading, 1e-3);

            string written = UnitTemplates.Write(presets, Path.Combine(root, "presets"))[0];
            Assert.AreEqual(Path.Combine(root, "presets", "units", "UEF base, tiny.json"), written);
        }

        private static MapDocument HardFfa() => MapImporter.ImportFolder(ProjectFolders.HardFfa);

        /// <summary>
        /// A preset of a role no example has (the examples are civilians'): two structures.
        /// </summary>
        private static UnitPresetSettings OfRole(string role) =>
            new UnitPresetSettings(new UnitPresetGroup(role, [new UnitPresetUnit("ueb2101", -2, 0, 0, 0), new UnitPresetUnit("ueb1101", 2, 0, 0, 0)], []), 2, role);

        private static UnitPresetSettings Example(string name)
        {
            Workspaces.Serialized.PresetDto dto = System.Text.Json.JsonSerializer.Deserialize<Workspaces.Serialized.PresetDto>(ExampleUnitPresets.Text(name + ".json"), ProjectJson.Options)!;
            return new Preset(UnitPresets.Kind, dto.Name, dto.Settings).Read<UnitPresetSettings>();
        }

        [TestMethod]
        public void ARolePicksTheArmyOfTheMapItIsPlacedOn()
        {
            MapDocument document = HardFfa();
            Assert.AreEqual("NEUTRAL_CIVILIAN", UnitPresets.ArmyFor(document, ArmyRoles.Civilian, 10, 10));
            Assert.AreEqual("ARMY_9", UnitPresets.ArmyFor(document, ArmyRoles.Enemy, 10, 10), "an army no player takes besides the civilians");
            Spawn spawn = document.Markers.Spawns.First(s => s.Name == "ARMY_3");
            Assert.AreEqual("ARMY_3", UnitPresets.ArmyFor(document, ArmyRoles.Player, spawn.Position.X + 5, spawn.Position.Z - 5), "the nearest spawn's army");
            Assert.IsNull(UnitPresets.ArmyFor(document, null, 0, 0));

            UnitPresetSettings town = Example("UEF civilian town");
            MapDocument placed = UnitPresets.Place(document, town, "UEF civilian town", null, "ARMY_1", ["INITIAL"], null, 128, 128, 0,
                out ImmutableArray<string> names, out ImmutableArray<string> group, out string army);
            Assert.AreEqual("NEUTRAL_CIVILIAN", army, "the role wins over the tool's army");
            Assert.AreEqual(town.Count, names.Length);
            CollectionAssert.AreEqual(new[] { "INITIAL", "UEF civilian town" }, group.ToArray());
            Assert.IsTrue(names.All(name => placed.FindUnit(name)!.Army == "NEUTRAL_CIVILIAN"));

            UnitPresets.Place(document, town, "Town", "ARMY_2", "ARMY_1", ["INITIAL"], null, 128, 128, 0, out _, out _, out army);
            Assert.AreEqual("ARMY_2", army, "an army given wins over the role");
        }

        [TestMethod]
        public void AnEnemyPresetOnANewMapGetsAnArmyOfItsOwn()
        {
            // a new map has the civilians' ARMY_17 and NEUTRAL_CIVILIAN, no army for enemies
            MapDocument document = NewMap.Create(new NewMapSettings { Size = 256 });
            string civilians = UnitPresets.ArmyFor(document, ArmyRoles.Civilian, 10, 10)!;
            Assert.IsNull(UnitPresets.ArmyFor(document, ArmyRoles.Enemy, 10, 10), "neither civilian army is the enemy's");
            Assert.AreEqual((UnitPresets.EnemyArmy, true), UnitPresets.ArmyOrNewFor(document, ArmyRoles.Enemy, 10, 10));

            UnitPresetSettings town = Example("UEF civilian town");
            document = UnitPresets.Place(document, town, "Town", null, null, ["INITIAL"], null, 64, 64, 0, out _, out _, out string townArmy);
            UnitPresetSettings enemy = OfRole(ArmyRoles.Enemy);
            document = UnitPresets.Place(document, enemy, "Base", null, null, ["INITIAL"], null, 180, 180, 0, out ImmutableArray<string> names, out _, out string enemyArmy);

            Assert.AreEqual(civilians, townArmy);
            Assert.AreEqual(UnitPresets.EnemyArmy, enemyArmy, "the enemy base is not the town's army");
            CollectionAssert.Contains(document.Scenario.ExtraArmies.ToArray(), UnitPresets.EnemyArmy, "added as an extra army");
            Assert.IsFalse(document.IsPlayable(UnitPresets.EnemyArmy));
            Assert.IsTrue(names.All(name => document.FindUnit(name)!.Army == UnitPresets.EnemyArmy));

            // the next enemy preset goes to the army added, without another
            int armies = document.Armies.Length;
            UnitPresets.Place(document, enemy, "Base 2", null, null, ["INITIAL"], null, 100, 180, 0, out _, out _, out string again);
            Assert.AreEqual(UnitPresets.EnemyArmy, again);
            Assert.AreEqual(armies, document.Armies.Length);
        }

        [TestMethod]
        public void AWreckFieldGoesIntoTheArmysWreckageGroup()
        {
            MapDocument document = HardFfa();
            UnitPresetSettings field = Example("Wreck field, mixed");
            Assert.IsTrue(field.Wrecks);
            MapDocument placed = UnitPresets.Place(document, field, "Wreck field, mixed", null, null, ["INITIAL"], null, 128, 128, 0,
                out ImmutableArray<string> names, out ImmutableArray<string> group, out string army);
            Assert.AreEqual("NEUTRAL_CIVILIAN", army);
            Assert.AreEqual(UnitWrecks.GroupName, group[0], "below the army's WRECKAGE group, which the game turns into wrecks");
            Assert.IsTrue(names.All(name => UnitWrecks.IsWreck(placed.FindUnit(name)!)));
        }

        [TestMethod]
        public void APlayersPresetMirroredByTheSymmetryGoesToThePartnerSpawnsArmy()
        {
            MapDocument document = HardFfa();
            Spawn first = document.Markers.Spawns.First(s => s.Name == "ARMY_1");
            Spawn second = document.Markers.Spawns.First(s => s.Name == "ARMY_2");
            // ARMY_2's spawn on the image of ARMY_1's in a rotation by half a turn
            Vector2 image = new Vector2(document.Width - first.Position.X, document.Height - first.Position.Z);
            document = document.MoveObjects([ObjectRef.Marker(second.Id)], image.X - second.Position.X, image.Y - second.Position.Z);
            IReadOnlyList<MapSymmetryTransform> transforms = MapSymmetry.Transforms(MapSymmetryKind.Rotational, 2, document.Width, document.Height);

            UnitPresetSettings start = OfRole(ArmyRoles.Player);
            Assert.AreEqual(ArmyRoles.Player, start.Role);
            float x = MathF.Round(first.Position.X), z = MathF.Round(first.Position.Z);
            document = UnitPresets.Place(document, start, "Start", null, null, ["INITIAL"], null, x, z, 0,
                out ImmutableArray<string> names, out _, out string army);
            Assert.AreEqual("ARMY_1", army);
            document = document.MirrorObjects(names.Select(ObjectRef.Unit), transforms, out ImmutableArray<ObjectRef> images);

            Assert.AreEqual(names.Length, images.Length);
            Assert.IsTrue(images.All(i => document.FindUnit(i.Name!)!.Army == "ARMY_2"), "the images belong to the partner spawn's army");
        }

        [TestMethod]
        public void TheExamplesAreWrittenOnceAndAnUntouchedOrDeletedCopyIsLeftAsTheUserLeftIt()
        {
            string folder = Path.Combine(ProjectFolders.New("example-units"), "presets", "units");
            IReadOnlyList<string> written = ExampleUnitPresets.Ensure(folder);
            Assert.IsTrue(written.Count >= 30, "tens of examples");
            CollectionAssert.AreEquivalent(ExampleUnitPresets.Names.ToArray(), written.ToArray());
            PresetLibrary library = new PresetLibrary(Path.GetDirectoryName(folder)!);
            Assert.AreEqual(written.Count, library.List(UnitPresets.Kind).Count, "every example reads as a units preset");
            Assert.IsTrue(library.List(UnitPresets.Kind).All(p => p.Read<UnitPresetSettings>().Role == ArmyRoles.Civilian), "every example is the civilians'");

            File.Delete(Path.Combine(folder, "UEF farm.json"));
            File.WriteAllText(Path.Combine(folder, "Aeon civilian hamlet.json"), "{ \"formatVersion\": 1, \"kind\": \"units\", \"name\": \"Aeon civilian hamlet\", \"settings\": { \"group\": { \"name\": \"x\", \"units\": [], \"groups\": [] }, \"count\": 0 } }");
            Assert.AreEqual(0, ExampleUnitPresets.Ensure(folder).Count, "nothing again");
            Assert.IsFalse(File.Exists(Path.Combine(folder, "UEF farm.json")), "a deleted example stays deleted");
            Assert.AreEqual(0, library.Find(UnitPresets.Kind, "Aeon civilian hamlet")!.Read<UnitPresetSettings>().Count, "an edited example is left alone");
        }

        [TestMethod]
        public void AnUntouchedCopyOfAnExampleThatNoLongerShipsIsDeletedAndAnEditedOneStays()
        {
            string folder = Path.Combine(ProjectFolders.New("retired-units"), "presets", "units");
            Directory.CreateDirectory(folder);
            // an example of an earlier release, as a workspace still holds it (CRLF or LF alike)
            string retired = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets", "retired-unit-presets", "Player start, UEF outpost.json"));
            File.WriteAllText(Path.Combine(folder, "Player start, UEF outpost.json"), retired.ReplaceLineEndings("\r\n"));
            File.WriteAllText(Path.Combine(folder, "UEF base, small.json"), retired.Replace("Player start, UEF outpost", "UEF base, small", StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(folder, "My own.json"), retired.Replace("Player start, UEF outpost", "My own", StringComparison.Ordinal));

            ExampleUnitPresets.Ensure(folder);

            Assert.IsFalse(File.Exists(Path.Combine(folder, "Player start, UEF outpost.json")), "untouched, it goes");
            Assert.IsTrue(File.Exists(Path.Combine(folder, "UEF base, small.json")), "a retired example the user changed stays");
            Assert.IsTrue(File.Exists(Path.Combine(folder, "My own.json")), "the user's own presets stay");
            Assert.IsTrue(File.Exists(Path.Combine(folder, "UEF farm.json")), "the current examples are written");
        }

        [TestMethod]
        public void EveryExampleHoldsKnownRolesAndOnlyBlueprintIdsAndPlaces()
        {
            foreach (string file in ExampleUnitPresets.Names)
            {
                UnitPresetSettings settings = Example(Path.GetFileNameWithoutExtension(file));
                Assert.IsTrue(ArmyRoles.All.Contains(settings.Role), file);
                Assert.AreEqual(settings.Count, UnitPresets.CountUnits(settings.Group), file);
                UnitPresetUnit[] units = [.. All(settings.Group)];
                Assert.IsTrue(units.All(unit => System.Text.RegularExpressions.Regex.IsMatch(unit.Blueprint, "^[a-z]{3}[0-9]{4}$")), file);
                Assert.IsTrue(units.All(unit => unit.Y == 0), $"{file}: on the terrain");
                Assert.IsTrue(units.Max(unit => MathF.Max(MathF.Abs(unit.X), MathF.Abs(unit.Z))) < 120, $"{file}: around its middle");
            }

            static IEnumerable<UnitPresetUnit> All(UnitPresetGroup group) => group.Units.Concat(group.Groups.SelectMany(All));
        }
    }
}
