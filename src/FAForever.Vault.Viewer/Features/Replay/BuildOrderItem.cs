using FAForever.FileFormats.Blueprints;
using FAForever.FileFormats.Replay;

namespace FAForever.Vault.Viewer.Features.Replay;

/// <summary>
/// An order, or a change to a queued order, on the Build order tab. <see cref="Key"/> is
/// stable for a replay: the index in <see cref="ReplaySemantics.GetCommands(Replay)"/>, or that
/// count plus the index in <see cref="ReplaySemantics.GetQueueChanges(Replay)"/>.
/// </summary>
public sealed record BuildOrderItem(
    int Key,
    TimeSpan Timestamp,
    int SourceId,
    ReplayAnalysis.MapPosition? Position,
    string? BlueprintId,
    CommandCategory Category,
    bool IsBuild,
    bool IsQueueChange,
    string Text)
{
    /// <summary>
    /// How many units the order adds: a factory order builds one in each selected factory, a
    /// construction order by engineers one structure, a queue change its (signed) delta.
    /// </summary>
    public int Quantity { get; init; } = 1;

    /// <summary>
    /// The key of the first of the equal orders the player gave in the same tick (dragging a
    /// line of walls, shift-clicking a unit ten times): the ledger shows them as one "10× …"
    /// row, and highlighting one highlights them all. Its own key when it stands alone.
    /// </summary>
    public int Group { get => group ?? Key; init => group = value; }

    private readonly int? group;

    /// <summary>The commander upgrade the order starts, readable ("Advanced Engineering"), or null.</summary>
    public string? Enhancement { get; init; }

    /// <summary>The display name of the unit being built, e.g. "Mass Extractor".</summary>
    public string? UnitName => BlueprintId is { } blueprintId ? UnitNames.GetOrNull(blueprintId) ?? blueprintId : null;

    public static BuildOrderItem From(int key, ReplayCommand command)
    {
        CommandCategory category = CommandCategories.GetCategory(command.CommandType);
        bool isBuild = category == CommandCategory.Build && command.BlueprintId is not null;
        string verb = ReplayInputFormatter.Verb(command.CommandType);
        string unit = isBuild ? $" {UnitNames.GetOrNull(command.BlueprintId!) ?? command.BlueprintId}" : "";
        string count = command.UnitCount > 1 ? $" · {command.UnitCount} units" : "";
        string factory = command.FromFactory ? " (factory order)" : "";
        string? enhancement = command.Enhancement is { } name ? EnhancementName(name) : null;
        if (enhancement is not null)
        {
            verb = "Upgrade ACU:";
            unit = $" {enhancement}";
        }

        return new BuildOrderItem(key, command.Timestamp, command.SourceId, command.Position, command.BlueprintId,
            category, isBuild, IsQueueChange: false, $"{verb}{unit}{count}{factory}")
        {
            Quantity = command.CommandType == CommandType.IssueBuildFactory ? Math.Max(1, command.UnitCount) : 1,
            Enhancement = enhancement,
        };
    }

    /// <summary>A queue change reads as "Queue +2 Engineer" or "Dequeue 1 Engineer".</summary>
    public static BuildOrderItem From(int key, ReplayQueueChange change)
    {
        string unit = change.BlueprintId is { } blueprintId ? $" {UnitNames.GetOrNull(blueprintId) ?? blueprintId}" : " (unknown order)";
        string text = change.Delta switch
        {
            > 0 => $"Queue +{change.Delta}{unit}",
            < 0 => $"Dequeue {-change.Delta}{unit}",
            _ => $"Dequeue{unit}",
        };
        CommandCategory category = change.CommandType is { } type ? CommandCategories.GetCategory(type) : CommandCategory.Build;
        return new BuildOrderItem(key, change.Timestamp, change.SourceId, null, change.BlueprintId,
            category, change.BlueprintId is not null, IsQueueChange: true, text)
        {
            Quantity = change.Delta,
        };
    }

    /// <summary>
    /// An enhancement id as words: "AdvancedEngineering" → "Advanced Engineering",
    /// "T3EngineeringRemove" → "Remove T3 Engineering". The FAF_ prefix of balance-mod variants is dropped.
    /// </summary>
    public static string EnhancementName(string id)
    {
        string name = id.StartsWith("FAF_", StringComparison.Ordinal) ? id[4..] : id;
        bool remove = name.EndsWith("Remove", StringComparison.Ordinal) && name.Length > 6;
        if (remove)
        {
            name = name[..^6];
        }

        System.Text.StringBuilder words = new();
        for (int index = 0; index < name.Length; index++)
        {
            char current = name[index];
            bool boundary = index > 0 && char.IsUpper(current)
                && (char.IsLower(name[index - 1]) || char.IsDigit(name[index - 1])
                    || (index + 1 < name.Length && char.IsLower(name[index + 1]) && char.IsUpper(name[index - 1])));
            if (boundary)
            {
                words.Append(' ');
            }

            words.Append(current);
        }

        return remove ? $"Remove {words}" : words.ToString();
    }

    /// <summary>The label of an order type in the comparison, e.g. "Attack-move".</summary>
    public static string CategoryLabel(CommandCategory category) => category switch
    {
        CommandCategory.Aggressive => "Attack-move",
        CommandCategory.Special => "Other",
        _ => category.ToString(),
    };
}
