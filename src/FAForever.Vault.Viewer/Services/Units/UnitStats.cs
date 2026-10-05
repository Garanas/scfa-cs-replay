using System.Globalization;
using FAForever.FileFormats.Blueprints;

namespace FAForever.Vault.Viewer.Services.Units;

/// <summary>
/// The values of a unit as labelled lines, the same labels for every version of it, so versions can
/// be compared line by line (the unit page's history). Weapons get one line per value, labelled with
/// the weapon's name; identical weapons are counted, as on the unit card.
/// </summary>
public static class UnitStats
{
    /// <summary>A value of a unit: its label, the text to show and, for numbers, the number.</summary>
    public sealed record Line(string Label, string Text, double? Number = null);

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

        void Number(string label, double? number)
        {
            if (number is > 0)
            {
                lines.Add(new Line(label, Format(number), number));
            }
        }

        Text("Moves on", UnitRoles.Layer(unit));
        Text("Roles", string.Join(", ", UnitRoles.For(unit)));
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
            Number($"{name}: damage", weapon.Damage);
            Number($"{name}: salvo", weapon.MuzzleSalvoSize > 1 ? weapon.MuzzleSalvoSize : null);
            Number($"{name}: area", weapon.DamageRadius);
            Number($"{name}: range", weapon.MaxRadius);
            if (weapon.RateOfFire is > 0 and var rate)
            {
                lines.Add(new Line($"{name}: seconds between shots", Format(1 / rate), 1 / rate));
            }
        }

        return lines;
    }

    public static string Format(double? value) => value?.ToString("#,0.##", CultureInfo.InvariantCulture) ?? "";
}
