using System.Text.Json;
using FAForever.MapEditor.Project.Workspaces;
using FAForever.MapEditor.Project.Workspaces.Serialized;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// Unit palettes (user story U-8): ten slots of units or units presets as presets of kind
    /// <c>palettes</c>, their slots set and cleared, and the example palettes written once.
    /// </summary>
    [TestClass]
    public class UnitPalettesTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static UnitPaletteSettings Example(string name)
        {
            PresetDto dto = JsonSerializer.Deserialize<PresetDto>(ExampleUnitPalettes.Text(name + ".json"), ProjectJson.Options)!;
            Assert.AreEqual(PresetKinds.Palettes, dto.Kind, name);
            Assert.AreEqual(name, dto.Name, name);
            return UnitPalettes.Read(new Preset(UnitPalettes.Kind, dto.Name, dto.Settings));
        }

        [TestMethod]
        public void APaletteRoundTripsThroughThePresetLibraryWithTenSlots()
        {
            PresetLibrary library = new PresetLibrary(ProjectFolders.New("palettes"));
            UnitPaletteSettings settings = UnitPalettes.SetSlot(UnitPalettes.Empty, 0, UnitPaletteSlot.OfUnit("UEB0101"));
            settings = UnitPalettes.SetSlot(settings, 9, UnitPaletteSlot.OfPreset("UEF farm"));

            string file = library.Save(UnitPalettes.ToPreset("My base", settings));

            Assert.AreEqual(Path.Combine(library.Folder, "palettes", "My base.json"), file);
            string text = File.ReadAllText(file);
            StringAssert.Contains(text, "\"kind\": \"palettes\"");
            StringAssert.Contains(text, "\"unit\": \"ueb0101\"", "an id is kept in lower case");
            StringAssert.Contains(text, "\"preset\": \"UEF farm\"");
            StringAssert.Contains(text, "{}", "an empty slot is an empty object");
            Assert.IsFalse(text.Contains("isEmpty", StringComparison.Ordinal), "what a slot is comes from what it holds");

            Preset preset = library.List(PresetKinds.Palettes).Single();
            UnitPaletteSettings read = UnitPalettes.Read(preset);
            Assert.AreEqual(UnitPalettes.SlotCount, read.Slots.Count);
            Assert.AreEqual(UnitPaletteSlot.OfUnit("ueb0101"), read.Slots[0]);
            Assert.IsTrue(read.Slots[0].IsUnit);
            Assert.IsTrue(read.Slots[1].IsEmpty);
            Assert.AreEqual(UnitPaletteSlot.OfPreset("UEF farm"), read.Slots[9]);
            Assert.IsTrue(read.Slots[9].IsPreset);
            CollectionAssert.Contains(PresetKinds.All.ToArray(), PresetKinds.Palettes, "a kind the editor knows");
        }

        [TestMethod]
        public void SlotsAreSetClearedAndKeptAtTen()
        {
            UnitPaletteSettings settings = UnitPalettes.SetSlot(UnitPalettes.Empty, 3, UnitPaletteSlot.OfUnit("uab1103"));
            Assert.AreEqual("uab1103", settings.Slots[3].Unit);
            Assert.IsTrue(UnitPalettes.ClearSlot(settings, 3).Slots[3].IsEmpty);
            Assert.AreEqual(UnitPalettes.SlotCount, UnitPalettes.ClearSlot(settings, 3).Slots.Count);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => UnitPalettes.SetSlot(settings, 10, UnitPaletteSlot.Empty));
            Assert.ThrowsException<ArgumentException>(() => UnitPaletteSlot.OfUnit(" "));

            // a file with fewer or more slots reads as ten
            UnitPaletteSettings short3 = UnitPalettes.Normalize(new UnitPaletteSettings([UnitPaletteSlot.OfUnit("a"), UnitPaletteSlot.OfUnit("b"), UnitPaletteSlot.OfUnit("c")]));
            Assert.AreEqual(UnitPalettes.SlotCount, short3.Slots.Count);
            Assert.AreEqual("c", short3.Slots[2].Unit);
            Assert.IsTrue(short3.Slots[3].IsEmpty);
            UnitPaletteSettings long12 = UnitPalettes.Normalize(new UnitPaletteSettings([.. Enumerable.Range(0, 12).Select(i => UnitPaletteSlot.OfUnit($"u{i}"))]));
            Assert.AreEqual(UnitPalettes.SlotCount, long12.Slots.Count);
            Assert.AreEqual("u9", long12.Slots[9].Unit);
            Assert.AreEqual(UnitPalettes.SlotCount, UnitPalettes.Normalize(null).Slots.Count);

            // the keys: 1 to 9 the first nine slots, 0 the tenth
            Assert.AreEqual("1", UnitPalettes.KeyLabel(0));
            Assert.AreEqual("9", UnitPalettes.KeyLabel(8));
            Assert.AreEqual("0", UnitPalettes.KeyLabel(9));
            Assert.AreEqual(0, UnitPalettes.SlotOfKey("1"));
            Assert.AreEqual(9, UnitPalettes.SlotOfKey("0"));
            Assert.IsNull(UnitPalettes.SlotOfKey("Q"));
            Assert.AreEqual(0, UnitPalettes.IndexOf(1));
            Assert.AreEqual(9, UnitPalettes.IndexOf(10));
            Assert.AreEqual(9, UnitPalettes.IndexOf(0));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => UnitPalettes.IndexOf(11));
        }

        [TestMethod]
        public void TheExamplePalettesAreWrittenOnceAndADeletedOneStaysDeleted()
        {
            string folder = Path.Combine(ProjectFolders.New("example-palettes"), "presets", "palettes");
            IReadOnlyList<string> written = ExampleUnitPalettes.Ensure(folder);

            Assert.AreEqual(20, written.Count, "five palettes per faction: a T1 base, T2, defences, naval and civilian");
            CollectionAssert.AreEquivalent(ExampleUnitPalettes.Names.ToArray(), written.ToArray());
            PresetLibrary library = new PresetLibrary(Path.GetDirectoryName(folder)!);
            IReadOnlyList<Preset> presets = library.List(PresetKinds.Palettes);
            Assert.AreEqual(written.Count, presets.Count, "every example reads as a palette");
            Assert.IsTrue(presets.All(preset => UnitPalettes.Read(preset).Slots.Count == UnitPalettes.SlotCount));

            File.Delete(Path.Combine(folder, "Aeon naval.json"));
            Assert.AreEqual(0, ExampleUnitPalettes.Ensure(folder).Count, "nothing again");
            Assert.IsFalse(File.Exists(Path.Combine(folder, "Aeon naval.json")), "a deleted example stays deleted");
        }

        [TestMethod]
        public void AWorkspaceStartsWithTheExamplePalettesBesideTheUnitPresets()
        {
            Workspace workspace = Workspace.Create(ProjectFolders.New("workspace"), "C:/game", null, "D:/vault");
            CollectionAssert.AreEqual(new[] { "palettes", "units" }, workspace.Presets.Kinds().ToArray());
            Assert.AreEqual(20, workspace.Presets.List(PresetKinds.Palettes).Count);
        }

        [TestMethod]
        public void EveryExamplePaletteNamesUnitsOfItsFactionOrTheExampleCivilianPresets()
        {
            string[] presets = [.. ExampleUnitPresets.Names.Select(Path.GetFileNameWithoutExtension)];
            foreach ((string faction, string prefix) in new[] { ("UEF", "ueb"), ("Aeon", "uab"), ("Cybran", "urb"), ("Seraphim", "xsb") })
            {
                foreach (string kind in new[] { "T1 base", "T2", "defences", "naval" })
                {
                    UnitPaletteSettings palette = Example($"{faction} {kind}");
                    UnitPaletteSlot[] filled = [.. palette.Slots.Where(slot => !slot.IsEmpty)];
                    Assert.IsTrue(filled.Length >= 9, $"{faction} {kind}: {filled.Length} slots filled");
                    Assert.IsTrue(filled.All(slot => slot.IsUnit && slot.Unit!.StartsWith(prefix, StringComparison.Ordinal) && slot.Unit.Length == 7), $"{faction} {kind}: structures of the faction only");
                }
                UnitPaletteSettings civilian = Example($"{faction} civilian");
                UnitPaletteSlot[] settlements = [.. civilian.Slots.Where(slot => !slot.IsEmpty)];
                Assert.IsTrue(settlements.Length >= 3, $"{faction} civilian");
                Assert.IsTrue(settlements.All(slot => slot.IsPreset && slot.Preset!.StartsWith(faction, StringComparison.Ordinal) && presets.Contains(slot.Preset)),
                    $"{faction} civilian: the example civilian presets of its building style only");
            }
            // the Cybran have no T3 shield generator: its slot of the defences palette is empty
            Assert.IsTrue(Example("Cybran defences").Slots[7].IsEmpty);
            Assert.AreEqual("ueb4301", Example("UEF defences").Slots[7].Unit);
        }

        [TestMethod]
        public void TheUserSettingsKeepThePaletteAndTheUnitsPlacedLast()
        {
            UserSettings settings = new UserSettings(Path.Combine(ProjectFolders.New("settings"), "settings.json"));
            Assert.IsNull(settings.Units.Palette);
            Assert.IsNull(settings.Units.LastPlaced);

            settings.SetUnits(new UnitsSettingsDto { Palette = "UEF T1 base", LastPlaced = ["unit:ueb0101", "preset:UEF farm"] });

            UserSettings again = new UserSettings(settings.File);
            Assert.AreEqual("UEF T1 base", again.Units.Palette);
            CollectionAssert.AreEqual(new[] { "unit:ueb0101", "preset:UEF farm" }, again.Units.LastPlaced!.ToArray());
            Assert.IsNull(again.Problem);
        }
    }
}
