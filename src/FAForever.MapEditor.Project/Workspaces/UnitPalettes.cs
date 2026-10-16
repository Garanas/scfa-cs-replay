using System.Text.Json.Serialization;

namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// One slot of a unit palette (user story U-8): a unit by its blueprint id, a units preset by
    /// its name, or nothing. Written as <c>{ "unit": "ueb0101" }</c>, <c>{ "preset": "UEF farm" }</c>
    /// or <c>{}</c>.
    /// </summary>
    /// <param name="Unit">The unit's id (<c>ueb0101</c>), in lower case; null for a preset or an empty slot.</param>
    /// <param name="Preset">The units preset's name (<see cref="PresetKinds.Units"/>); null for a unit or an empty slot.</param>
    public sealed record UnitPaletteSlot(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Unit = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Preset = null)
    {
        /// <summary>
        /// A slot that holds nothing.
        /// </summary>
        public static UnitPaletteSlot Empty { get; } = new UnitPaletteSlot();

        /// <summary>
        /// A slot of a unit, by its id in lower case.
        /// </summary>
        /// <exception cref="ArgumentException">No id.</exception>
        public static UnitPaletteSlot OfUnit(string id) => string.IsNullOrWhiteSpace(id)
            ? throw new ArgumentException("A slot's unit needs its id", nameof(id))
            : new UnitPaletteSlot(Unit: id.Trim().ToLowerInvariant());

        /// <summary>
        /// A slot of a units preset, by its name.
        /// </summary>
        /// <exception cref="ArgumentException">No name.</exception>
        public static UnitPaletteSlot OfPreset(string name) => string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("A slot's preset needs its name", nameof(name))
            : new UnitPaletteSlot(Preset: name.Trim());

        /// <summary>
        /// Whether the slot holds nothing (a slot with both a unit and a preset holds the unit).
        /// </summary>
        [JsonIgnore]
        public bool IsEmpty => string.IsNullOrWhiteSpace(Unit) && string.IsNullOrWhiteSpace(Preset);

        /// <summary>
        /// Whether the slot holds a unit.
        /// </summary>
        [JsonIgnore]
        public bool IsUnit => !string.IsNullOrWhiteSpace(Unit);

        /// <summary>
        /// Whether the slot holds a units preset.
        /// </summary>
        [JsonIgnore]
        public bool IsPreset => !IsUnit && !string.IsNullOrWhiteSpace(Preset);
    }

    /// <summary>
    /// What a unit palette holds (user story U-8): ten slots, each a unit or a units preset, which
    /// the keys 1 to 9 and 0 pick while the Units mode's place tool is active.
    /// </summary>
    /// <param name="Slots">Exactly <see cref="UnitPalettes.SlotCount"/> slots, the key 1 first and
    /// the key 0 last; <see cref="UnitPalettes.Normalize"/> makes any list so.</param>
    public sealed record UnitPaletteSettings(IReadOnlyList<UnitPaletteSlot> Slots);

    /// <summary>
    /// Unit palettes (user story U-8): presets of kind <see cref="PresetKinds.Palettes"/>, one JSON
    /// file per palette in the workspace's <c>presets/palettes</c>, read and written through the
    /// <see cref="PresetLibrary"/> as the other kinds. The slots name units and presets only; what
    /// a slot shows (the unit's icon, the preset's count) comes from the workspace when it is shown.
    /// </summary>
    public static class UnitPalettes
    {
        public const string Kind = PresetKinds.Palettes;

        /// <summary>
        /// The slots of a palette: the keys 1 to 9, then 0.
        /// </summary>
        public const int SlotCount = 10;

        /// <summary>
        /// A palette of ten empty slots.
        /// </summary>
        public static UnitPaletteSettings Empty { get; } = new UnitPaletteSettings([.. Enumerable.Repeat(UnitPaletteSlot.Empty, SlotCount)]);

        /// <summary>
        /// The key that picks a slot, as its label: <c>1</c> to <c>9</c> for the first nine, <c>0</c> for the tenth.
        /// </summary>
        /// <param name="index">0 to 9.</param>
        public static string KeyLabel(int index) => index == SlotCount - 1 ? "0" : (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// The slot a key picks, as the key is labelled: <c>1</c> to <c>9</c> give 0 to 8, <c>0</c> gives 9.
        /// </summary>
        /// <returns>The slot's index, or null for another key.</returns>
        public static int? SlotOfKey(string key) => key.Length == 1 && char.IsAsciiDigit(key[0])
            ? key[0] == '0' ? SlotCount - 1 : key[0] - '1'
            : null;

        /// <summary>
        /// The slot a number names as a person counts them: 1 to 9 for the first nine, 10 or 0 for the tenth.
        /// </summary>
        /// <returns>The slot's index.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Not a slot.</exception>
        public static int IndexOf(int number) => number switch
        {
            0 or SlotCount => SlotCount - 1,
            >= 1 and < SlotCount => number - 1,
            _ => throw new ArgumentOutOfRangeException(nameof(number), number, $"A palette has slots 1 to {SlotCount} (0 is the tenth)"),
        };

        /// <summary>
        /// The settings with exactly <see cref="SlotCount"/> slots: missing ones empty, extra ones
        /// dropped, a slot read as null empty.
        /// </summary>
        public static UnitPaletteSettings Normalize(UnitPaletteSettings? settings)
        {
            IReadOnlyList<UnitPaletteSlot?> slots = settings?.Slots ?? [];
            UnitPaletteSlot[] normal = new UnitPaletteSlot[SlotCount];
            for (int i = 0; i < SlotCount; i++)
            {
                normal[i] = i < slots.Count && slots[i] is UnitPaletteSlot slot ? slot : UnitPaletteSlot.Empty;
            }
            return new UnitPaletteSettings(normal);
        }

        /// <summary>
        /// The settings with one slot replaced.
        /// </summary>
        /// <param name="index">0 to 9.</param>
        /// <exception cref="ArgumentOutOfRangeException">Not a slot.</exception>
        public static UnitPaletteSettings SetSlot(UnitPaletteSettings settings, int index, UnitPaletteSlot slot)
        {
            if (index < 0 || index >= SlotCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, $"A palette has slots 0 to {SlotCount - 1}");
            }
            UnitPaletteSlot[] slots = [.. Normalize(settings).Slots];
            slots[index] = slot;
            return new UnitPaletteSettings(slots);
        }

        /// <summary>
        /// The settings with one slot emptied.
        /// </summary>
        public static UnitPaletteSettings ClearSlot(UnitPaletteSettings settings, int index) => SetSlot(settings, index, UnitPaletteSlot.Empty);

        /// <summary>
        /// A palette as a preset, to save with the <see cref="PresetLibrary"/>.
        /// </summary>
        public static Preset ToPreset(string name, UnitPaletteSettings settings) => Preset.Of(Kind, name, Normalize(settings));

        /// <summary>
        /// The slots a palette preset holds, always ten.
        /// </summary>
        /// <exception cref="MapProjectException">The preset holds no palette.</exception>
        public static UnitPaletteSettings Read(Preset preset) => Normalize(preset.Read<UnitPaletteSettings>());
    }
}
