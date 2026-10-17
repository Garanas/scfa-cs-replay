namespace FAForever.Vault.Viewer.Features.Players;

/// <summary>One choice of a filter: the token that goes in the URL and the label that is shown.</summary>
public sealed record ChoiceOption(string Token, string Label);

/// <summary>
/// The choices of the filter rows (the plan's URL table). The leaderboard list is the well known
/// boards; a page that has loaded the API's boards passes its own list to <c>BoardSelect</c>.
/// </summary>
public static class PlayerFilters
{
    public const string DefaultPeriod = "1y";

    public const string DefaultBoard = "ladder_1v1";

    public static readonly IReadOnlyList<ChoiceOption> Periods =
    [
        new("1w", "1 week"),
        new("1m", "1 month"),
        new("3m", "3 months"),
        new("1y", "1 year"),
        new("all", "All time"),
    ];

    public static readonly IReadOnlyList<ChoiceOption> Boards =
    [
        new("global", "Global"),
        new("ladder_1v1", "Ladder 1v1"),
        new("tmm_2v2", "2v2"),
        new("tmm_3v3", "3v3"),
        new("tmm_4v4_full_share", "4v4 full share"),
        new("tmm_4v4_share_until_death", "4v4 share until death"),
    ];

    public static readonly IReadOnlyList<ChoiceOption> Modes =
    [
        new("all", "All modes"),
        new("ladder_1v1", "Ladder 1v1"),
        new("tmm_2v2", "2v2"),
        new("tmm_4v4_full_share", "4v4 full share"),
        new("custom", "Custom games"),
    ];

    public static readonly IReadOnlyList<ChoiceOption> Factions =
    [
        new("any", "Any faction"),
        new("uef", "UEF"),
        new("cybran", "Cybran"),
        new("aeon", "Aeon"),
        new("seraphim", "Seraphim"),
    ];

    public static readonly IReadOnlyList<ChoiceOption> Results =
    [
        new("any", "Any result"),
        new("win", "Wins"),
        new("loss", "Losses"),
    ];

    public static readonly IReadOnlyList<ChoiceOption> Activity =
    [
        new("30d", "Last 30 days"),
        new("90d", "Last 90 days"),
        new("1y", "Last year"),
        new("all", "Any time"),
    ];

    public static readonly IReadOnlyList<ChoiceOption> AchievementStates =
    [
        new("all", "All"),
        new("unlocked", "Unlocked"),
        new("progress", "In progress"),
        new("locked", "Locked"),
    ];
}

/// <summary>A name a player had, and when it stopped being theirs (null: unknown).</summary>
public sealed record FormerName(string Name, DateTimeOffset? Until);
