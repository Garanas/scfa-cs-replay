using System.Globalization;
using FAForever.FileFormats.Blueprints;

namespace FAForever.Vault.Viewer.Services.Units;

/// <summary>
/// The values of a unit as labelled lines, the same labels for every version of it, so versions can
/// be compared line by line (the unit page's history). Weapons get one line per value in a group named
/// after the weapon, so the history shows the name once, above its values; identical weapons are
/// counted, as on the unit card.
/// </summary>
public static class UnitStats
{
    /// <summary>A value of a unit: its label, the text to show, for numbers the number, and for a weapon's values the weapon.</summary>
    public sealed record Line(string Label, string Text, double? Number = null, string? Group = null)
    {
        /// <summary>What identifies the line across versions: its label within its group.</summary>
        public string Key => Group is null ? Label : $"{Group}: {Label}";
    }

    public static IReadOnlyList<Line> For(UnitSummary unit)
    {
        List<Line> lines = [];

        void Text(string label, string? text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                lines.Add(new Line(label, text));
            }
        }

        void Number(string label, double? number, string? group = null)
        {
            if (number is > 0)
            {
                lines.Add(new Line(label, Format(number), number, group));
            }
        }

        Text("Moves on", UnitRoles.Layer(unit));
        Number("Mass", unit.BuildCostMass);
        Number("Energy", unit.BuildCostEnergy);
        Number("Build time", unit.BuildTime);
        Number("Build power", unit.BuildRate);
        Number("Health", unit.MaxHealth);
        Number("Regeneration", unit.RegenRate);
        Number("Shield", unit.ShieldMaxHealth);
        Number("Speed", unit.MaxSpeed);
        Number("Vision", unit.VisionRadius);
        Number("Water vision", unit.WaterVisionRadius);
        Number("Radar", unit.RadarRadius);
        Number("Sonar", unit.SonarRadius);
        Number("Omni", unit.OmniRadius);
        Number("Mass per second", unit.ProductionPerSecondMass);
        Number("Energy per second", unit.ProductionPerSecondEnergy);
        Number("Can build", unit.Builds.Count);

        foreach (var (weapon, count) in unit.Weapons.Where(UnitRoles.IsRealWeapon).GroupBy(weapon => weapon).Select(group => (group.Key, group.Count())))
        {
            string name = $"{(count > 1 ? $"{count} × " : "")}{weapon.DisplayName ?? weapon.WeaponCategory ?? "Weapon"}";
            Number("Damage", weapon.Damage, name);
            Number("Salvo", weapon.MuzzleSalvoSize > 1 ? weapon.MuzzleSalvoSize : null, name);
            Number("Damage pulses", weapon.DoTPulses > 1 ? weapon.DoTPulses : null, name);
            Number("Seconds of damage over time", weapon.DoTTime, name);
            Number("Area", weapon.DamageRadius, name);
            Number("Range", weapon.MaxRadius, name);
            if (weapon.RateOfFire is > 0 and var rate)
            {
                lines.Add(new Line("Seconds between shots", Format(1 / rate), 1 / rate, name));
            }
        }

        return lines;
    }

    public static string Format(double? value) => value?.ToString("#,0.##", CultureInfo.InvariantCulture) ?? "";
}
