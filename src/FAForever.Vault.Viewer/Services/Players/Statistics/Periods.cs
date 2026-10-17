namespace FAForever.Vault.Viewer.Services.Players.Statistics;

/// <summary>The periods of the Players pages (<c>period</c> in the address).</summary>
public static class Periods
{
    /// <summary>A week.</summary>
    public const string Week = "1w";

    /// <summary>A month.</summary>
    public const string Month = "1m";

    /// <summary>Three months.</summary>
    public const string Quarter = "3m";

    /// <summary>A year, the default.</summary>
    public const string Year = "1y";

    /// <summary>Everything.</summary>
    public const string All = "all";

    /// <summary>Every period, shortest first.</summary>
    public static IReadOnlyList<string> Values { get; } = [Week, Month, Quarter, Year, All];

    /// <summary>The period itself when it is known, otherwise the default (a year).</summary>
    public static string Normalize(string? period)
        => period is not null && Values.Contains(period) ? period : Year;

    /// <summary>The start of a period that ends at <paramref name="now"/>; null for <c>all</c>.</summary>
    public static DateTimeOffset? Since(string? period, DateTimeOffset now) => Normalize(period) switch
    {
        Week => now.AddDays(-7),
        Month => now.AddMonths(-1),
        Quarter => now.AddMonths(-3),
        Year => now.AddYears(-1),
        _ => null,
    };
}
