using FAForever.Replay;

namespace FAForever.Replay.Viewer.Features.Replay;

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
        return new BuildOrderItem(key, command.Timestamp, command.SourceId, command.Position, command.BlueprintId,
            category, isBuild, IsQueueChange: false, $"{verb}{unit}{count}{factory}");
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
            category, change.BlueprintId is not null, IsQueueChange: true, text);
    }

    /// <summary>The label of an order type in the comparison, e.g. "Attack-move".</summary>
    public static string CategoryLabel(CommandCategory category) => category switch
    {
        CommandCategory.Aggressive => "Attack-move",
        CommandCategory.Special => "Other",
        _ => category.ToString(),
    };
}
