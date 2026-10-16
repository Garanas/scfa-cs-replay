using System.Globalization;
using FAForever.Vault.Viewer.Services.Players;

namespace FAForever.Vault.Viewer.Features.Players.Army;

/// <summary>Where a player stands on an achievement.</summary>
public enum AchievementProgress
{
    /// <summary>Unlocked.</summary>
    Unlocked,

    /// <summary>Started (some steps done) but not unlocked.</summary>
    InProgress,

    /// <summary>Not started.</summary>
    Locked,
}

/// <summary>An achievement with the player's standing on it.</summary>
public sealed record AchievementRow(AchievementDefinition Definition, AchievementProgress Progress, int Steps)
{
    /// <summary>The steps done as a share of the steps needed (0 to 1); 1 when unlocked, 0 for a standard one that is not.</summary>
    public double Fraction => Progress == AchievementProgress.Unlocked
        ? 1
        : Definition.Kind == AchievementKind.Incremental && Definition.TotalSteps > 0
            ? Math.Clamp((double)Steps / Definition.TotalSteps, 0, 1)
            : 0;

    /// <summary>True for an achievement that counts steps.</summary>
    public bool HasSteps => Definition.Kind == AchievementKind.Incremental && Definition.TotalSteps > 0;
}

/// <summary>The achievements grid of the Army tab: the rows, the <c>show</c> filter, the counts and the wording.</summary>
public static class AchievementFilter
{
    /// <summary>The values of the <c>show</c> parameter.</summary>
    public const string ShowAll = "all";

    public const string ShowUnlocked = "unlocked";

    public const string ShowProgress = "progress";

    public const string ShowLocked = "locked";

    /// <summary>
    /// Every achievement in the game's order with the player's standing: unlocked when the player has
    /// unlocked it, in progress when they have revealed it and done some steps, otherwise locked.
    /// </summary>
    public static IReadOnlyList<AchievementRow> Build(IReadOnlyList<AchievementDefinition> definitions, IReadOnlyList<PlayerAchievement> mine)
    {
        Dictionary<string, PlayerAchievement> byId = [];
        foreach (PlayerAchievement entry in mine)
        {
            byId[entry.AchievementId.ToLowerInvariant()] = entry;
        }

        return [.. definitions.OrderBy(definition => definition.Order).Select(definition =>
        {
            byId.TryGetValue(definition.Id.ToLowerInvariant(), out PlayerAchievement? entry);
            AchievementProgress progress = entry switch
            {
                { State: AchievementState.Unlocked } => AchievementProgress.Unlocked,
                { CurrentSteps: > 0 } => AchievementProgress.InProgress,
                _ => AchievementProgress.Locked,
            };
            return new AchievementRow(definition, progress, entry?.CurrentSteps ?? 0);
        })];
    }

    /// <summary>The value of <c>show</c> a URL holds; anything unknown is all.</summary>
    public static string Normalize(string? show) => show?.ToLowerInvariant() switch
    {
        ShowUnlocked => ShowUnlocked,
        ShowProgress => ShowProgress,
        ShowLocked => ShowLocked,
        _ => ShowAll,
    };

    /// <summary>The rows a <c>show</c> value keeps, in order.</summary>
    public static IReadOnlyList<AchievementRow> Filter(IReadOnlyList<AchievementRow> rows, string? show) => Normalize(show) switch
    {
        ShowUnlocked => [.. rows.Where(row => row.Progress == AchievementProgress.Unlocked)],
        ShowProgress => [.. rows.Where(row => row.Progress == AchievementProgress.InProgress)],
        ShowLocked => [.. rows.Where(row => row.Progress == AchievementProgress.Locked)],
        _ => rows,
    };

    /// <summary>The line above the grid: 5 of 12 unlocked, 450 experience points.</summary>
    public static string Summary(IReadOnlyList<AchievementRow> rows)
    {
        int unlocked = rows.Count(row => row.Progress == AchievementProgress.Unlocked);
        int points = rows.Where(row => row.Progress == AchievementProgress.Unlocked).Sum(row => row.Definition.ExperiencePoints);
        return $"{unlocked} of {rows.Count} unlocked, {points.ToString("N0", CultureInfo.InvariantCulture)} experience points.";
    }

    /// <summary>The word for a row's state.</summary>
    public static string StateWord(AchievementProgress progress) => progress switch
    {
        AchievementProgress.Unlocked => "Unlocked",
        AchievementProgress.InProgress => "In progress",
        _ => "Locked",
    };

    /// <summary>How rare an achievement is: 12% of players, or 0.4% for the rare ones.</summary>
    public static string Rarity(double percent)
    {
        string text = percent >= 10 ? Math.Round(percent).ToString("0", CultureInfo.InvariantCulture) : Math.Round(percent, 1).ToString("0.#", CultureInfo.InvariantCulture);
        return $"{text}% of players";
    }

    /// <summary>The steps of an incremental achievement: 31 of 50.</summary>
    public static string Steps(AchievementRow row)
        => $"{row.Steps.ToString("N0", CultureInfo.InvariantCulture)} of {row.Definition.TotalSteps.ToString("N0", CultureInfo.InvariantCulture)}";

    /// <summary>The icon to draw: the unlocked one for an unlocked achievement, the revealed one otherwise; null when there is none.</summary>
    public static string? IconUrl(AchievementRow row)
    {
        string? url = row.Progress == AchievementProgress.Unlocked ? row.Definition.UnlockedIconUrl : row.Definition.RevealedIconUrl;
        return string.IsNullOrWhiteSpace(url) ? null : url;
    }
}
