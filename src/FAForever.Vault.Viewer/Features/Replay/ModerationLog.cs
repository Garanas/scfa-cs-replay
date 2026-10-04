using System.Globalization;
using System.Text;
using FAForever.FileFormats.Lua;
using FAForever.FileFormats.Replay;
using FAForever.Vault.Viewer.Services;
using FAForever.Vault.Viewer.Services.Replays;

namespace FAForever.Vault.Viewer.Features.Replay;

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
    Drawing,
    Server,
    Other,
}

/// <summary>One line of the Moderation tab. <see cref="Message"/> is the game's own text where there is one.</summary>
public sealed record ModerationEntry(TimeSpan Timestamp, string Player, ModerationKind Kind, string Message);

/// <summary>
/// Everything in a replay a moderator may want to read through: what the game itself logs for
/// moderators (the ModeratorEvent sim callback), plus the inputs that log leaves out (chat, units
/// given away, recall votes, pause requests, players leaving) and the pings and drawings with
/// their position on the map.
/// </summary>
public static class ModerationLog
{
    /// <summary>
    /// How players talked to each other: chat, pings, markers and drawings. The AI prompt always
    /// includes these for the selected players, as the context of everything else.
    /// </summary>
    public static bool IsCommunication(ModerationKind kind)
        => kind is ModerationKind.Chat or ModerationKind.Marker or ModerationKind.Ping or ModerationKind.Drawing;

    public static List<ModerationEntry> Collect(FAForever.FileFormats.Replay.Replay replay)
    {
        ReplayHeader header = replay.Header;
        List<ModerationEntry> entries = [];

        foreach (ReplayModeratorEvent entry in ReplaySemantics.GetModeratorEvents(replay))
        {
            ModerationKind kind = Classify(entry.Message);
            // The game logs pings and markers without a position; they come from GetPings below.
            if (kind is not (ModerationKind.Ping or ModerationKind.Marker))
            {
                entries.Add(new ModerationEntry(entry.Timestamp, ClientName(header, entry.SourceId), kind, Trim(entry.Message)));
            }
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

        foreach (ReplayPing ping in ReplaySemantics.GetPings(replay))
        {
            entries.Add(ping.Type.Equals("Marker", StringComparison.OrdinalIgnoreCase)
                ? new ModerationEntry(ping.Timestamp, ClientName(header, ping.SourceId), ModerationKind.Marker, $"Marker \"{ping.Name}\" at {Position(ping.X, ping.Z)}")
                : new ModerationEntry(ping.Timestamp, ClientName(header, ping.SourceId), ModerationKind.Ping, $"{ping.Type} ping at {Position(ping.X, ping.Z)}"));
        }

        foreach (ReplayDrawing drawing in ReplaySemantics.GetDrawings(replay))
        {
            if (drawing.Points.Count > 0)
            {
                entries.Add(new ModerationEntry(drawing.Timestamp, ClientName(header, drawing.SourceId), ModerationKind.Drawing, DescribeDrawing(drawing)));
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
        ModerationKind.Drawing => "Drawing",
        ModerationKind.Server => "To FAF server",
        _ => "Other",
    };

    /// <summary>
    /// A prompt for an AI assistant: the game, the players, what each kind means, how to link to
    /// a view in the web app, and the entries. The instructions keep the model to the log, with
    /// game times as evidence, and leave the decision to the moderator.
    /// </summary>
    /// <param name="link">The address of the view the moderator is looking at.</param>
    /// <param name="appBase">The web app's base address (ends with a slash), to build links from.</param>
    public static string BuildPrompt(LoadedReplay model, IReadOnlyList<ModerationEntry> entries, string link, string appBase)
    {
        FAForever.FileFormats.Replay.Replay replay = model.Replay;
        string? replayUrl = model.Origin is ReplayOrigin.Vault vault ? $"{appBase}replay/{vault.ReplayId}" : null;
        (int sizeX, int sizeZ) = MapCanvas.MapSize(replay.Header);

        StringBuilder prompt = new();
        prompt.AppendLine("You are helping a moderator of Forged Alliance Forever (FAF), the community-run online service for the RTS game Supreme Commander: Forged Alliance, review a reported game. Below is a moderation log taken from the game's replay.");
        prompt.AppendLine();
        prompt.AppendLine("Please:");
        prompt.AppendLine("- Summarise per player what they did, in time order, and point out patterns worth a closer look: abusive or harassing chat, repeatedly self-destructing units, giving units away (to opponents, or everything at once), leaving early, pausing often or for long, voting patterns, misleading or spammed pings and drawings.");
        prompt.AppendLine("- Cite the game time (m:ss) of every entry you base a statement on.");
        if (replayUrl is not null)
        {
            prompt.AppendLine("- Give every finding a link to the view that shows it, built as described under \"Links\", so the moderator can check it in one click.");
        }
        prompt.AppendLine("- Stick to the log. It does not show the fighting, the economy or who won, and chat in other languages may need translating: say so where something cannot be determined, and do not guess intent.");
        prompt.AppendLine("- Do not decide on a punishment; the moderator decides. End with the open questions the moderator could check in the replay itself.");
        prompt.AppendLine();
        prompt.AppendLine("## Game");
        prompt.AppendLine();
        if (model.Origin is ReplayOrigin.Vault replayInVault)
        {
            prompt.AppendLine(CultureInfo.InvariantCulture, $"- Replay: #{replayInVault.ReplayId}, the moderator's current view: {link}");
        }
        if (model.Metadata?.title is { Length: > 0 } title)
        {
            prompt.AppendLine(CultureInfo.InvariantCulture, $"- Title: {title}");
        }
        prompt.AppendLine(CultureInfo.InvariantCulture, $"- Map: {replay.Header.Scenario.Map.DisplayName ?? model.Metadata?.mapname ?? "unknown"}, {sizeX} x {sizeZ} world units");
        prompt.AppendLine(CultureInfo.InvariantCulture, $"- Game length: {GameTime.Format(ReplayAnalysis.GetDuration(replay))}");
        prompt.AppendLine();
        prompt.AppendLine("## Players");
        prompt.AppendLine();
        prompt.AppendLine("Army numbers are the ones used in the log (\"army 3\"). Teammates are allies; everyone else is an opponent.");
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
        prompt.AppendLine(CultureInfo.InvariantCulture, $"Positions are map coordinates (x, z) in world units: (0, 0) is the top-left (north-west) corner of the map, x grows to the east up to {sizeX}, z grows to the south up to {sizeZ}.");
        prompt.AppendLine();
        prompt.AppendLine("- Chat: a typed message. \"to all\" is visible to everyone, \"to allies\" only to the player's team, \"to <name>\" is a private message. The game also sends some automatic chat, such as \"Paused the game\".");
        prompt.AppendLine("- Ping: a signal on the map for the team, of type Move, Attack or Alert (\"nuke\" warns of a nuclear launch). Only allies see it.");
        prompt.AppendLine("- Marker: a ping with a typed text, shown on the map to allies.");
        prompt.AppendLine("- Drawing: a line the player painted on the map, visible to allies and observers only, fading after about 25 seconds. The log gives the area it covers. Drawings by observers are not in the replay.");
        prompt.AppendLine("- Self-destruct: the player blew up their own units (Ctrl+K).");
        prompt.AppendLine("- Gave units: the player transferred units to another army; this also happens when a defeated or leaving player's units go to a teammate.");
        prompt.AppendLine("- Recall vote: a vote to concede the game as a team.");
        prompt.AppendLine("- Pause: a request to pause or resume the game.");
        prompt.AppendLine("- Left the game: the player's connection ended; at the very end of a game everyone leaves.");
        prompt.AppendLine("- Focus army: the army the player views; switching to -1 means they became an observer, usually after being defeated.");
        prompt.AppendLine("- To FAF server: messages the game sent to the FAF server, e.g. GameResult (army, result and score) at the end. End-of-game statistics (JsonStats) are left out.");
        prompt.AppendLine();
        if (replayUrl is not null)
        {
            AppendLinks(prompt, replayUrl, entries);
        }
        else
        {
            prompt.AppendLine("This replay was opened from a file, not from the FAF vault, so there are no links to it.");
            prompt.AppendLine();
        }
        prompt.AppendLine(CultureInfo.InvariantCulture, $"## Log ({entries.Count} entries: game time, player, kind, message)");
        prompt.AppendLine();
        foreach (ModerationEntry entry in entries)
        {
            prompt.AppendLine(CultureInfo.InvariantCulture, $"{GameTime.Format(entry.Timestamp)} {entry.Player} [{Label(entry.Kind)}] {entry.Message}");
        }

        return prompt.ToString();
    }

    /// <summary>
    /// How to build links into the web app. The view state lives in the query string (see the
    /// Viewer's AGENTS.md, "Shareable view state"); these are the parameters a reviewer needs.
    /// </summary>
    private static void AppendLinks(StringBuilder prompt, string replayUrl, IReadOnlyList<ModerationEntry> entries)
    {
        prompt.AppendLine("## Links");
        prompt.AppendLine();
        prompt.AppendLine(CultureInfo.InvariantCulture, $"Every view of this replay in the web app is a link: {replayUrl}?<parameters>. Useful views:");
        prompt.AppendLine();
        prompt.AppendLine("- `tab=chat&from=<time>&to=<time>`: the chat in that window, with the pings and drawings drawn on the map. Best to check chat, pings, markers and drawings.");
        prompt.AppendLine("- `tab=events&players=<name>&from=<time>&to=<time>`: every input of that player (orders, callbacks) in that window. Best to see exactly what a player did around an entry, e.g. from 15 seconds before to 15 seconds after it.");
        prompt.AppendLine("- `tab=playthrough&at=<time>`: the game played back on the map, paused at that moment.");
        prompt.AppendLine("- `tab=moderation&players=<names>&kinds=<kinds>`: this log. Kinds: chat, selfdestruct, giveunits, recall, pause, left, focus, marker, ping, drawing, server, other. Without `kinds` every kind is shown.");
        prompt.AppendLine();
        prompt.AppendLine("Rules:");
        prompt.AppendLine("- Times are game time as m:ss or h:mm:ss, e.g. 10:42 or 1:02:30.");
        prompt.AppendLine("- A from/to window is at most 4 minutes; a longer one is cut back to its first 4 minutes.");
        prompt.AppendLine("- `players` takes player names exactly as listed above, separated by commas; leave it out for everyone. Percent-encode names (a space is %20).");
        prompt.AppendLine("- Join parameters with &; the order does not matter.");

        if (entries.FirstOrDefault(entry => entry.Timestamp >= TimeSpan.FromSeconds(30)) is { } example)
        {
            TimeSpan moment = TimeSpan.FromSeconds(Math.Floor(example.Timestamp.TotalSeconds));
            string from = GameTime.Format(moment - TimeSpan.FromSeconds(15));
            string to = GameTime.Format(moment + TimeSpan.FromSeconds(15));
            prompt.AppendLine();
            prompt.AppendLine(CultureInfo.InvariantCulture, $"Example: the inputs of {example.Player} around {GameTime.Format(moment)}: {replayUrl}?tab=events&players={Uri.EscapeDataString(example.Player)}&from={from}&to={to}");
        }
        prompt.AppendLine();
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

    /// <summary>
    /// The end-of-game statistics (GpgNetSend 'JsonStats') are kilobytes of JSON per player and
    /// tell a moderator nothing; they are reduced to one line. The Callbacks tab still has them.
    /// </summary>
    private static string Trim(string message)
        => message.StartsWith("GpgNetSend with command 'JsonStats'", StringComparison.Ordinal)
            ? string.Create(CultureInfo.InvariantCulture, $"GpgNetSend with command 'JsonStats' (end-of-game statistics, {message.Length:N0} characters, left out)")
            : message;

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

    /// <summary>Where a stroke is: its bounding box, which says more than the raw samples.</summary>
    private static string DescribeDrawing(ReplayDrawing drawing)
    {
        float minX = drawing.Points.Min(point => point.X);
        float maxX = drawing.Points.Max(point => point.X);
        float minZ = drawing.Points.Min(point => point.Z);
        float maxZ = drawing.Points.Max(point => point.Z);
        return $"Drew a line around {Position((minX + maxX) / 2, (minZ + maxZ) / 2)}, spanning {Math.Round(maxX - minX):0} x {Math.Round(maxZ - minZ):0}";
    }

    private static string Position(float x, float z)
        => string.Create(CultureInfo.InvariantCulture, $"({Math.Round(x):0}, {Math.Round(z):0})");

    /// <summary>The player of a 1-based army number, as the sim callbacks carry it.</summary>
    private static string ArmyName(ReplayHeader header, int army)
        => army >= 1 && army <= header.Armies.Length && header.Armies[army - 1].PlayerName is { } name
            ? $"{name} (army {army})"
            : $"army {army}";

    private static string ClientName(ReplayHeader header, int sourceId)
        => sourceId >= 0 && sourceId < header.Clients.Length ? header.Clients[sourceId].PlayerName : $"Source {sourceId}";
}
