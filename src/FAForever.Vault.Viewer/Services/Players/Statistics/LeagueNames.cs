using System.Globalization;
using System.Text;

namespace FAForever.Vault.Viewer.Services.Players.Statistics;

/// <summary>
/// Names of divisions and subdivisions. The API gives translation keys; a division's name is the last
/// segment of its key, capitalised, and a subdivision is a roman numeral of its index.
/// </summary>
public static class LeagueNames
{
    /// <summary>The name of a division from its key (<c>divisions.silver.name</c> is "Silver"), or its index when the key says nothing.</summary>
    public static string Division(string? nameKey, int index)
        => FromKey(nameKey) ?? index.ToString(CultureInfo.InvariantCulture);

    /// <summary>The name of a subdivision: a roman numeral of its index (1 is "I").</summary>
    public static string Subdivision(string? nameKey, int index) => Roman(index);

    /// <summary>The capitalised last segment of a key, ignoring <c>name</c>, <c>description</c> and numbers; null when there is none.</summary>
    public static string? FromKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        string[] segments = key.Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (int i = segments.Length - 1; i >= 0; i--)
        {
            string segment = segments[i];
            if (segment is "name" or "description" || segment.All(char.IsDigit))
            {
                continue;
            }

            segment = segment.Replace('_', ' ').Replace('-', ' ').Trim();
            return segment.Length == 0 ? null : char.ToUpperInvariant(segment[0]) + segment[1..];
        }

        return null;
    }

    /// <summary>A roman numeral for 1 to 3,999; the number itself outside that range.</summary>
    public static string Roman(int number)
    {
        if (number is < 1 or > 3999)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        (int Value, string Symbol)[] table = [(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")];
        StringBuilder text = new();
        foreach ((int value, string symbol) in table)
        {
            while (number >= value)
            {
                text.Append(symbol);
                number -= value;
            }
        }
        return text.ToString();
    }
}

/// <summary>Statistics over a player's league journal.</summary>
public static class LeagueStatistics
{
    /// <summary>
    /// How often the player moved up and down: a step from one subdivision to another in the journal,
    /// up when its division index is higher (or equal with a higher subdivision index). Entering from
    /// placement does not count.
    /// </summary>
    public static (int Promotions, int Demotions) Moves(IReadOnlyList<LeagueJournalEntry> journal)
    {
        int promotions = 0;
        int demotions = 0;
        foreach (LeagueJournalEntry entry in journal)
        {
            if (entry.Before is not { } before || entry.After is not { } after || before.Id == after.Id)
            {
                continue;
            }

            int difference = Position(after).CompareTo(Position(before));
            promotions += difference > 0 ? 1 : 0;
            demotions += difference < 0 ? 1 : 0;
        }
        return (promotions, demotions);
    }

    private static int Position(LeagueSubdivision subdivision) => subdivision.Division.Index * 1000 + subdivision.Index;
}
