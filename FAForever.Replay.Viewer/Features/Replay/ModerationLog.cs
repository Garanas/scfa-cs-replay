using System.Globalization;
using System.Text;
using FAForever.Replay;
using FAForever.Replay.Viewer.Services;
using FAForever.Replay.Viewer.Services.Replays;

namespace FAForever.Replay.Viewer.Features.Replay;

/// <summary>What a moderation entry is about. The query values (<c>kinds</c>) are the lowercase names.</summary>
public enum ModerationKind
{
    Chat,
    SelfDestruct,
    GiveUnits,
    Recall,
    Pause,
    Left,
    Focus,
    Marker,
    Ping,
    Server,
    Other,
}

/// <summary>One line of the Moderation tab. <see cref="Message"/> is the game's own text where there is one.</summary>
public sealed record ModerationEntry(TimeSpan Timestamp, string Player, ModerationKind Kind, string Message);

/// <summary>
/// Everything in a replay a moderator may want to read through: what the game itself logs for
/// moderators (the ModeratorEvent sim callback), plus the inputs that log leaves out — chat, units
/// given away, recall votes, pause requests and players leaving.
/// </summary>
public static class ModerationLog
{
    /// <summary>Chat is the bulk of most games, so it is opt-in; every other kind shows by default.</summary>
    public static bool ShownByDefault(ModerationKind kind) => kind != ModerationKind.Chat;

    public static List<ModerationEntry> Collect(FAForever.Replay.Replay replay)
    {
        ReplayHeader header = replay.Header;
        List<ModerationEntry> entries = [];

        foreach (ReplayModeratorEvent entry in ReplaySemantics.GetModeratorEvents(replay))
        {
            entries.Add(new ModerationEntry(entry.Timestamp, ClientName(header, entry.SourceId), Classify(entry.Message), entry.Message));
        }

        foreach (ReplayInput input in replay.Body.UserInput)
        {
            TimeSpan timestamp = ReplayAnalysis.GetTimestamp(input);
            string player = ClientName(header, input.SourceId);
            ModerationEntry? entry = input switch
            {
                ReplayInput.RequestPause => new(timestamp, player, ModerationKind.Pause, "Requested a pause"),
                // Every client resumes at the start of the game; only later resumes end a pause.
                ReplayInput.RequestResume when input.Tick > 0 => new(timestamp, player, ModerationKind.Pause, "Requested to resume"),
                ReplayInput.CommandSourceTerminated => new(timestamp, player, ModerationKind.Left, "Left the game"),
                ReplayInput.SimCallback { Endpoint: "GiveUnitsToPlayer", LuaParameters: LuaData.Table table } callback
                    => new(timestamp, player, ModerationKind.GiveUnits, DescribeGift(header, table, callback.Units.EntityIds.Length)),
                ReplayInput.SimCallback { Endpoint: "SetRecallVote", LuaParameters: LuaData.Table table }
                    => new(timestamp, player, ModerationKind.Recall, DescribeVote(table)),
                _ => null,
            };
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        foreach (ReplayChatMessage message in ReplaySemantics.GetChatMessages(replay))
        {
            // "notify" carries the automatic upgrade notices, not something a player typed.
            if (!message.Receiver.Equals("notify", StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(new ModerationEntry(message.Timestamp, message.Sender, ModerationKind.Chat, $"to {message.Receiver}: {message.Message}"));
            }
        }

        return entries.OrderBy(entry => entry.Timestamp).ToList();
    }

    public static string Label(ModerationKind kind) => kind switch
    {
        ModerationKind.Chat => "Chat",
        ModerationKind.SelfDestruct => "Self-destruct",
        ModerationKind.GiveUnits => "Gave units",
        ModerationKind.Recall => "Recall vote",
        ModerationKind.Pause => "Pause",
        ModerationKind.Left => "Left the game",
        ModerationKind.Focus => "Focus army",
        ModerationKind.Marker => "Marker",
        ModerationKind.Ping => "Ping",
        ModerationKind.Server => "To FAF server",
        _ => "Other",
    };

    /// <summary>
    /// A prompt for an AI assistant: the game, the players, what each kind means and the entries
    /// the moderator selected. The instructions keep the model to the log, with game times as
    /// evidence, and leave the decision to the moderator.
    /// </summary>
    public static string BuildPrompt(LoadedReplay model, IReadOnlyList<ModerationEntry> entries, string link)
    {
        FAForever.Replay.Replay replay = model.Replay;
        StringBuilder prompt = new();
        prompt.AppendLine("You are helping a moderator of Forged Alliance Forever (FAF), the community-run online service for the RTS game Supreme Commander: Forged Alliance, review a reported game. Below is a moderation log taken from the game's replay.");
        prompt.AppendLine();
        prompt.AppendLine("Please:");
        prompt.AppendLine("- Summarise per player what they did, in time order, and point out patterns worth a closer look: abusive or harassing chat, repeatedly self-destructing units, giving units away (to opponents, or everything at once), leaving early, pausing often or for long, voting patterns.");
        prompt.AppendLine("- Cite the game time (m:ss) of every entry you base a statement on.");
        prompt.AppendLine("- Stick to the log. It does not show the fighting, the economy or who won, and chat in other languages may need translating: say so where something cannot be determined, and do not guess intent.");
        prompt.AppendLine("- Do not decide on a punishment; the moderator decides. End with the open questions the moderator could check in the replay itself.");
        prompt.AppendLine();
        prompt.AppendLine("## Game");
        prompt.AppendLine();
        if (model.Origin is ReplayOrigin.Vault vault)
        {
            prompt.AppendLine(CultureInfo.InvariantCulture, $"- Replay: #{vault.ReplayId} ({link})");
        }
        if (model.Metadata?.title is { Length: > 0 } title)
        {
            prompt.AppendLine(CultureInfo.InvariantCulture, $"- Title: {title}");
        }
        prompt.AppendLine(CultureInfo.InvariantCulture, $"- Map: {replay.Header.Scenario.Map.DisplayName ?? model.Metadata?.mapname ?? "unknown"}");
        prompt.AppendLine(CultureInfo.InvariantCulture, $"- Game length: {GameTime.Format(ReplayAnalysis.GetDuration(replay))}");
        prompt.AppendLine();
        prompt.AppendLine("## Players");
        prompt.AppendLine();
        prompt.AppendLine("Army numbers are the ones used in the log (\"army 3\").");
        prompt.AppendLine();
        for (int index = 0; index < replay.Header.Armies.Length; index++)
        {
            ReplayPlayerOptions army = replay.Header.Armies[index];
            if (army.Civilian == true)
            {
                continue;
            }

            List<string> details = [];
            if (army.Team is { } team)
            {
                details.Add(team == 1 ? "no team (free for all)" : $"team {team - 1}");
            }
            if (army.FactionName is { } faction)
            {
                details.Add(faction);
            }
            if (army.Rating is { } rating)
            {
                details.Add($"rating {rating}");
            }
            if (army.Human == false)
            {
                details.Add("AI");
            }
            prompt.AppendLine(CultureInfo.InvariantCulture, $"- Army {index + 1}: {army.PlayerName ?? "unknown"} ({string.Join(", ", details)})");
        }
        prompt.AppendLine();
        prompt.AppendLine("## What the entries mean");
        prompt.AppendLine();
        prompt.AppendLine("- Chat: a message, \"to all\" is visible to everyone, \"to allies\" only to the team.");
        prompt.AppendLine("- Self-destruct: the player blew up their own units (Ctrl+K).");
        prompt.AppendLine("- Gave units: the player transferred units to another army; this also happens when a defeated or leaving player's units go to a teammate.");
        prompt.AppendLine("- Recall vote: a vote to concede the game as a team.");
        prompt.AppendLine("- Pause: a request to pause or resume the game.");
        prompt.AppendLine("- Left the game: the player's connection ended; at the very end of a game everyone leaves.");
        prompt.AppendLine("- Focus army: the army the player views; switching to -1 means they became an observer, usually after being defeated.");
        prompt.AppendLine("- Marker / Ping: a mark the player put on the map for their team.");
        prompt.AppendLine("- To FAF server: messages the game sent to the FAF server, e.g. GameResult (army, result and score) at the end.");
        prompt.AppendLine();
        prompt.AppendLine(CultureInfo.InvariantCulture, $"## Log ({entries.Count} entries: game time, player, kind, message)");
        prompt.AppendLine();
        foreach (ModerationEntry entry in entries)
        {
            prompt.AppendLine(CultureInfo.InvariantCulture, $"{GameTime.Format(entry.Timestamp)} {entry.Player} [{Label(entry.Kind)}] {entry.Message}");
        }

        return prompt.ToString();
    }

    private static ModerationKind Classify(string message) => message switch
    {
        _ when message.StartsWith("Self-destructed", StringComparison.Ordinal) => ModerationKind.SelfDestruct,
        _ when message.Contains("focus army", StringComparison.Ordinal) => ModerationKind.Focus,
        _ when message.StartsWith("Created a marker", StringComparison.Ordinal) => ModerationKind.Marker,
        _ when message.StartsWith("Created a ping", StringComparison.Ordinal) => ModerationKind.Ping,
        _ when message.StartsWith("GpgNetSend", StringComparison.Ordinal) => ModerationKind.Server,
        _ => ModerationKind.Other,
    };

    private static string DescribeGift(ReplayHeader header, LuaData.Table table, int unitCount)
    {
        table.TryGetNumberValue("To", out double? to);
        string units = unitCount == 1 ? "1 unit" : $"{unitCount} units";
        return to is { } army ? $"Gave {units} to {ArmyName(header, (int)army)}" : $"Gave {units} away";
    }

    private static string DescribeVote(LuaData.Table table)
    {
        table.TryGetBooleanValue("Vote", out bool? vote);
        return vote switch
        {
            true => "Voted yes to recall (concede)",
            false => "Voted no to recall",
            null => "Voted on a recall",
        };
    }

    /// <summary>The player of a 1-based army number, as the sim callbacks carry it.</summary>
    private static string ArmyName(ReplayHeader header, int army)
        => army >= 1 && army <= header.Armies.Length && header.Armies[army - 1].PlayerName is { } name
            ? $"{name} (army {army})"
            : $"army {army}";

    private static string ClientName(ReplayHeader header, int sourceId)
        => sourceId >= 0 && sourceId < header.Clients.Length ? header.Clients[sourceId].PlayerName : $"Source {sourceId}";
}
