using System.Globalization;
using FAForever.FileFormats.Blueprints;
using FAForever.FileFormats.Replay;
using FAForever.Vault.Viewer.Services.Api;
using FAForever.Vault.Viewer.Services.Players.Statistics;

namespace FAForever.Vault.Viewer.Services.Players;

/// <summary>
/// Reads the JSON:API documents of the FAF API into the Players models. The real API
/// (<see cref="FafPlayersApi"/>) and the fixtures (<see cref="SamplePlayersApi"/>) go through the same
/// functions, so the fixtures exercise the mapping. Every function reads defensively: a missing
/// attribute or include gives a null or a default, never an exception.
/// </summary>
public static class PlayerMappers
{
    /// <summary>The players whose login matched, then the players of the former names that matched (no player twice).</summary>
    public static IReadOnlyList<PlayerSearchHit> MapSearchHits(JsonApiDocument players, JsonApiDocument? names)
    {
        List<PlayerSearchHit> hits = [.. MapLoginHits(players)];
        if (names is not null)
        {
            hits.AddRange(MapNameHits(names, hits));
        }
        return hits;
    }

    /// <summary>The players of a <c>/data/player</c> document.</summary>
    public static IReadOnlyList<PlayerSearchHit> MapLoginHits(JsonApiDocument players)
        => [.. players.Data.Select(player => new PlayerSearchHit(Id(player.Id), player.GetString("login") ?? $"Player {player.Id}", ClanOf(player, players)?.Tag))];

    /// <summary>The players of a <c>/data/nameRecord</c> document, with the former name that matched; players in <paramref name="known"/> are left out.</summary>
    public static IReadOnlyList<PlayerSearchHit> MapNameHits(JsonApiDocument names, IReadOnlyList<PlayerSearchHit> known)
    {
        List<PlayerSearchHit> hits = [];
        foreach (JsonApiResource record in names.Data)
        {
            if (names.FindIncluded(record.Relationship("player")) is not { } player
                || known.Any(hit => hit.Id == Id(player.Id)) || hits.Any(hit => hit.Id == Id(player.Id)))
            {
                continue;
            }

            hits.Add(new PlayerSearchHit(Id(player.Id), player.GetString("login") ?? $"Player {player.Id}", ClanOf(player, names)?.Tag)
            {
                MatchedFormerName = record.GetString("name"),
            });
        }
        return hits;
    }

    /// <summary>The first player of the document, or null.</summary>
    public static PlayerSearchHit? MapFirstHit(JsonApiDocument players)
        => MapSearchHits(players, null).FirstOrDefault();

    /// <summary>A profile from <c>/data/player/{id}?include=names,clanMembership.clan,currentAvatar.avatar</c>.</summary>
    public static PlayerProfile? MapProfile(JsonApiDocument document)
    {
        if (document.Data.FirstOrDefault() is not { } player)
        {
            return null;
        }

        string login = player.GetString("login") ?? $"Player {player.Id}";
        List<PlayerName> formerNames = [];
        foreach ((string Type, string Id) reference in player.Relationships("names"))
        {
            if (document.FindIncluded(reference) is { } record
                && record.GetString("name") is { Length: > 0 } name
                && !string.Equals(name, login, StringComparison.Ordinal))
            {
                formerNames.Add(new PlayerName(name, record.GetDateTimeOffset("changeTime")));
            }
        }
        formerNames.Sort((left, right) => (right.ChangedAt ?? DateTimeOffset.MinValue).CompareTo(left.ChangedAt ?? DateTimeOffset.MinValue));

        JsonApiResource? assignment = document.FindIncluded(player.Relationship("currentAvatar"));
        JsonApiResource? avatar = document.FindIncluded(assignment?.Relationship("avatar")) ?? assignment; // unverified: the assignment may carry the url itself

        return new PlayerProfile(Id(player.Id), login, player.GetDateTimeOffset("createTime"), ClanOf(player, document), formerNames)
        {
            AvatarUrl = avatar?.GetString("url"),
            AvatarTooltip = avatar?.GetString("tooltip"),
        };
    }

    /// <summary>The leaderboards, by id.</summary>
    public static IReadOnlyList<Leaderboard> MapLeaderboards(JsonApiDocument document)
        => [.. document.Data.Select(MapLeaderboard).Where(board => board.TechnicalName.Length > 0).OrderBy(board => board.Id)];

    /// <summary>The ratings of a player, the board with most games first.</summary>
    public static IReadOnlyList<PlayerRating> MapRatings(JsonApiDocument document)
    {
        List<PlayerRating> ratings = [];
        foreach (JsonApiResource rating in document.Data)
        {
            if (document.FindIncluded(rating.Relationship("leaderboard")) is not { } board)
            {
                continue;
            }

            ratings.Add(new PlayerRating(
                MapLeaderboard(board),
                rating.GetNumber("mean") ?? 0,
                rating.GetNumber("deviation") ?? 0,
                rating.GetInt32("totalGames") ?? 0,
                rating.GetInt32("wonGames") ?? 0)
            {
                UpdatedAt = rating.GetDateTimeOffset("updateTime"),
            });
        }
        return [.. ratings.OrderByDescending(rating => rating.TotalGames)];
    }

    /// <summary>The rated games of a player on one board, oldest first.</summary>
    public static IReadOnlyList<RatingJournalEntry> MapJournal(JsonApiDocument document, int playerId)
    {
        List<RatingJournalEntry> entries = [];
        foreach (JsonApiResource journal in document.Data)
        {
            JsonApiResource? stats = document.FindIncluded(journal.Relationship("gamePlayerStats"));
            JsonApiResource? game = document.FindIncluded(stats?.Relationship("game"));
            (string? mapName, string? folder) = MapOf(game, document);
            int gameId = Id(game?.Id ?? stats?.Id);

            entries.Add(new RatingJournalEntry(
                gameId,
                stats?.GetDateTimeOffset("scoreTime") ?? game?.GetDateTimeOffset("endTime") ?? game?.GetDateTimeOffset("startTime") ?? journal.GetDateTimeOffset("createTime"),
                journal.GetNumber("meanBefore") ?? 0,
                journal.GetNumber("deviationBefore") ?? 0,
                journal.GetNumber("meanAfter") ?? 0,
                journal.GetNumber("deviationAfter") ?? 0)
            {
                GameName = game?.GetString("name"),
                MapName = mapName,
                MapFolder = folder,
                Opponents = Opponents(game, document, playerId, stats?.GetInt32("team")),
            });
        }
        return [.. entries.OrderBy(entry => entry.Date ?? DateTimeOffset.MinValue).ThenBy(entry => entry.GameId)];
    }

    /// <summary>The games of a player, in the document's order (the request sorts them).</summary>
    public static IReadOnlyList<PlayerMatch> MapMatches(JsonApiDocument document, int playerId)
    {
        List<PlayerMatch> matches = [];
        foreach (JsonApiResource stats in document.Data)
        {
            JsonApiResource? game = document.FindIncluded(stats.Relationship("game"));
            (string? mapName, string? folder) = MapOf(game, document);

            // The rating change of the board that rated the game, when the statistics carry no rating of their own.
            JsonApiResource? change = stats.Relationships("ratingChanges")
                .Select(reference => document.FindIncluded(reference)).OfType<JsonApiResource>().FirstOrDefault();
            JsonApiResource? board = document.FindIncluded(change?.Relationship("leaderboard"));

            int? before = ReplayPlayerOptions.DisplayRating(stats.GetNumber("beforeMean"), stats.GetNumber("beforeDeviation"))
                ?? ReplayPlayerOptions.DisplayRating(change?.GetNumber("meanBefore"), change?.GetNumber("deviationBefore"));
            int? after = ReplayPlayerOptions.DisplayRating(stats.GetNumber("afterMean"), stats.GetNumber("afterDeviation"))
                ?? ReplayPlayerOptions.DisplayRating(change?.GetNumber("meanAfter"), change?.GetNumber("deviationAfter"));

            matches.Add(new PlayerMatch(
                Id(game?.Id ?? stats.Relationship("game")?.Id),
                game?.GetString("name") ?? $"Game {game?.Id}",
                game?.GetDateTimeOffset("startTime") ?? stats.GetDateTimeOffset("scoreTime"),
                ResultOf(stats.GetString("result")))
            {
                Duration = DurationOf(game),
                MapName = mapName,
                MapFolder = folder,
                FeaturedMod = document.FindIncluded(game?.Relationship("featuredMod")) is { } mod
                    ? mod.GetString("displayName") ?? mod.GetString("technicalName")
                    : null,
                Faction = FactionOf(stats),
                Team = stats.GetInt32("team"),
                Score = stats.GetInt32("score"),
                RatingBefore = before,
                RatingAfter = after,
                Board = board?.GetString("technicalName"),
                Validity = game?.GetString("validity"),
                Players = Participants(game, document),
            });
        }
        return matches;
    }

    /// <summary>The counters of a player, by lower-case event id.</summary>
    public static PlayerEventCounts MapEventCounts(JsonApiDocument document)
    {
        Dictionary<string, long> counts = [];
        foreach (JsonApiResource record in document.Data)
        {
            string? eventId = record.Relationship("event")?.Id;
            if (!string.IsNullOrEmpty(eventId))
            {
                counts[eventId.ToLowerInvariant()] = (long)(record.GetNumber("currentCount") ?? 0);
            }
        }
        return new PlayerEventCounts(counts);
    }

    /// <summary>The achievements of the game, in the game's order.</summary>
    public static IReadOnlyList<AchievementDefinition> MapAchievements(JsonApiDocument document)
        => [.. document.Data.Select(achievement => new AchievementDefinition(
                achievement.Id,
                achievement.GetString("name") ?? achievement.Id,
                achievement.GetString("description") ?? string.Empty,
                string.Equals(achievement.GetString("type"), "INCREMENTAL", StringComparison.OrdinalIgnoreCase) ? AchievementKind.Incremental : AchievementKind.Standard,
                achievement.GetInt32("totalSteps") ?? 0,
                achievement.GetInt32("order") ?? 0)
            {
                RevealedIconUrl = achievement.GetString("revealedIconUrl"),
                UnlockedIconUrl = achievement.GetString("unlockedIconUrl"),
                ExperiencePoints = achievement.GetInt32("experiencePoints") ?? 0,
                UnlockersCount = achievement.GetInt32("unlockersCount") ?? 0,
                UnlockersPercent = achievement.GetNumber("unlockersPercent") ?? 0,
            })
            .OrderBy(achievement => achievement.Order)];

    /// <summary>A player's progress on achievements.</summary>
    public static IReadOnlyList<PlayerAchievement> MapPlayerAchievements(JsonApiDocument document)
        => [.. document.Data
            .Where(progress => progress.Relationship("achievement") is not null)
            .Select(progress => new PlayerAchievement(
                progress.Relationship("achievement")!.Value.Id,
                (progress.GetString("state") ?? string.Empty).ToUpperInvariant() switch
                {
                    "UNLOCKED" => AchievementState.Unlocked,
                    "REVEALED" => AchievementState.Revealed,
                    _ => AchievementState.Hidden,
                },
                progress.GetInt32("currentSteps") ?? 0,
                progress.GetDateTimeOffset("updateTime")))];

    /// <summary>A page of a ranking; the first row has rank <c>(page - 1) x pageSize + 1</c>.</summary>
    public static PlayerPage<RankingRow> MapRanking(JsonApiDocument document, int page, int pageSize)
    {
        List<RankingRow> rows = [];
        int rank = (Math.Max(page, 1) - 1) * pageSize;
        foreach (JsonApiResource rating in document.Data)
        {
            rank++;
            JsonApiResource? player = document.FindIncluded(rating.Relationship("player"));
            rows.Add(new RankingRow(
                rank,
                Id(player?.Id ?? rating.Relationship("player")?.Id),
                player?.GetString("login") ?? "Unknown",
                rating.GetNumber("mean") ?? 0,
                rating.GetNumber("deviation") ?? 0,
                rating.GetInt32("totalGames") ?? 0,
                rating.GetInt32("wonGames") ?? 0)
            {
                ClanTag = player is null ? null : ClanOf(player, document)?.Tag,
                UpdatedAt = rating.GetDateTimeOffset("updateTime"),
            });
        }
        return new PlayerPage<RankingRow>(rows, Math.Max(page, 1), document.TotalPages, document.TotalRecords);
    }

    /// <summary>The number of records a count request (<c>page[size]=1&amp;page[totals]</c>) reported.</summary>
    public static int MapCount(JsonApiDocument document) => document.TotalRecords ?? 0;

    /// <summary>The enabled leagues.</summary>
    public static IReadOnlyList<League> MapLeagues(JsonApiDocument document)
        => [.. document.Data.Select(league => new League(Id(league.Id), league.GetString("technicalName") ?? $"league{league.Id}", league.GetString("nameKey") ?? string.Empty)
        {
            ImageUrl = league.GetString("mediumImageUrl") ?? league.GetString("imageUrl"),
            SmallImageUrl = league.GetString("smallImageUrl"),
        })];

    /// <summary>The seasons, the latest first.</summary>
    public static IReadOnlyList<LeagueSeason> MapSeasons(JsonApiDocument document)
        => [.. document.Data.Select(season =>
        {
            JsonApiResource? league = document.FindIncluded(season.Relationship("league"));
            JsonApiResource? board = document.FindIncluded(season.Relationship("leaderboard"));
            return new LeagueSeason(
                Id(season.Id),
                Id(league?.Id ?? season.Relationship("league")?.Id),
                league?.GetString("technicalName") ?? string.Empty,
                season.GetInt32("seasonNumber") ?? 0,
                season.GetString("nameKey") ?? string.Empty,
                season.GetDateTimeOffset("startDate"),
                season.GetDateTimeOffset("endDate"))
            {
                BoardTechnicalName = board?.GetString("technicalName"),
                PlacementGames = season.GetInt32("placementGames") ?? 0,
                PlacementGamesReturningPlayer = season.GetInt32("placementGamesReturningPlayer") ?? 0,
            };
        }).OrderByDescending(season => season.StartDate ?? DateTimeOffset.MinValue)];

    /// <summary>The subdivisions of a season, the lowest division and subdivision first.</summary>
    public static IReadOnlyList<LeagueSubdivision> MapSubdivisions(JsonApiDocument document)
        => [.. document.Data.Select(subdivision => ReadSubdivision(subdivision, document))
            .OrderBy(subdivision => subdivision.Division.Index).ThenBy(subdivision => subdivision.Index)];

    /// <summary>The places of a player in seasons.</summary>
    public static IReadOnlyList<LeagueEntry> MapLeagueEntries(JsonApiDocument document)
        => [.. document.Data.Select(score => new LeagueEntry(
            LoginId(score),
            Id(score.Relationship("leagueSeason")?.Id),
            score.GetInt32("score") ?? 0,
            score.GetInt32("gameCount") ?? 0,
            score.GetBoolean("returningPlayer") ?? false,
            document.FindIncluded(score.Relationship("leagueSeasonDivisionSubdivision")) is { } subdivision ? ReadSubdivision(subdivision, document) : null))];

    /// <summary>A page of standings; <paramref name="players"/> resolves the names.</summary>
    public static PlayerPage<LeagueStanding> MapStandings(JsonApiDocument scores, JsonApiDocument? players, int page, int pageSize)
    {
        Dictionary<int, string> logins = [];
        foreach (JsonApiResource player in players?.Data ?? [])
        {
            logins[Id(player.Id)] = player.GetString("login") ?? $"Player {player.Id}";
        }

        int rank = (Math.Max(page, 1) - 1) * pageSize;
        List<LeagueStanding> rows = [];
        foreach (JsonApiResource score in scores.Data)
        {
            rank++;
            int playerId = LoginId(score);
            rows.Add(new LeagueStanding(rank, playerId, logins.GetValueOrDefault(playerId, $"Player {playerId}"), score.GetInt32("score") ?? 0, score.GetInt32("gameCount") ?? 0));
        }
        return new PlayerPage<LeagueStanding>(rows, Math.Max(page, 1), scores.TotalPages, scores.TotalRecords);
    }

    /// <summary>The player ids a standings page needs names for.</summary>
    public static IReadOnlyList<int> StandingPlayerIds(JsonApiDocument scores)
        => [.. scores.Data.Select(LoginId).Where(id => id > 0).Distinct()];

    /// <summary>The league journal of a player, in game order.</summary>
    public static IReadOnlyList<LeagueJournalEntry> MapLeagueJournal(JsonApiDocument document)
        => [.. document.Data.Select(entry => new LeagueJournalEntry(
                entry.GetInt32("gameId") ?? 0,
                entry.GetInt32("scoreBefore") ?? 0,
                entry.GetInt32("scoreAfter") ?? 0,
                entry.GetInt32("gameCount") ?? 0,
                document.FindIncluded(entry.Relationship("leagueSeasonDivisionSubdivisionBefore")) is { } before ? ReadSubdivision(before, document) : null,
                document.FindIncluded(entry.Relationship("leagueSeasonDivisionSubdivisionAfter")) is { } after ? ReadSubdivision(after, document) : null))
            .OrderBy(entry => entry.GameId)];

    /// <summary>The games two players were in, newest first; games without both are left out.</summary>
    public static IReadOnlyList<HeadToHeadGame> MapHeadToHead(JsonApiDocument document, int playerA, int playerB)
    {
        List<HeadToHeadGame> games = [];
        foreach (JsonApiResource game in document.Data)
        {
            IReadOnlyList<MatchParticipant> participants = Participants(game, document);
            if (participants.FirstOrDefault(participant => participant.PlayerId == playerA) is not { } a
                || participants.FirstOrDefault(participant => participant.PlayerId == playerB) is not { } b)
            {
                continue;
            }

            (string? mapName, string? folder) = MapOf(game, document);
            games.Add(new HeadToHeadGame(Id(game.Id), game.GetString("name") ?? $"Game {game.Id}", game.GetDateTimeOffset("startTime"), a, b)
            {
                Duration = DurationOf(game),
                MapName = mapName,
                MapFolder = folder,
                FeaturedMod = document.FindIncluded(game.Relationship("featuredMod")) is { } mod ? mod.GetString("displayName") ?? mod.GetString("technicalName") : null,
            });
        }
        return [.. games.OrderByDescending(game => game.StartTime ?? DateTimeOffset.MinValue)];
    }

    /// <summary>The result of a statistics row: <c>VICTORY</c>, <c>DEFEAT</c>, a draw or unknown.</summary>
    public static MatchResult ResultOf(string? result) => result switch
    {
        "VICTORY" => MatchResult.Victory,
        "DEFEAT" => MatchResult.Defeat,
        "DRAW" or "MUTUAL_DRAW" => MatchResult.Draw,
        _ => MatchResult.Unknown,
    };

    private static Leaderboard MapLeaderboard(JsonApiResource board)
        => new(Id(board.Id), board.GetString("technicalName") ?? string.Empty);

    private static LeagueSubdivision ReadSubdivision(JsonApiResource subdivision, JsonApiDocument document)
    {
        JsonApiResource? division = document.FindIncluded(subdivision.Relationship("leagueSeasonDivision"));
        int divisionIndex = division?.GetInt32("divisionIndex") ?? 0;
        int index = subdivision.GetInt32("subdivisionIndex") ?? 0;

        return new LeagueSubdivision(
            Id(subdivision.Id),
            index,
            LeagueNames.Subdivision(subdivision.GetString("nameKey"), index),
            new LeagueDivision(Id(division?.Id ?? subdivision.Relationship("leagueSeasonDivision")?.Id), divisionIndex, LeagueNames.Division(division?.GetString("nameKey"), divisionIndex)))
        {
            MinRating = subdivision.GetNumber("minRating"),
            MaxRating = subdivision.GetNumber("maxRating"),
            HighestScore = subdivision.GetInt32("highestScore"),
            ImageUrl = subdivision.GetString("mediumImageUrl") ?? subdivision.GetString("imageUrl"),
            SmallImageUrl = subdivision.GetString("smallImageUrl"),
        };
    }

    private static ClanInfo? ClanOf(JsonApiResource player, JsonApiDocument document)
    {
        JsonApiResource? membership = document.FindIncluded(player.Relationship("clanMembership"));
        JsonApiResource? clan = document.FindIncluded(membership?.Relationship("clan"));
        return clan is null ? null : new ClanInfo(Id(clan.Id), clan.GetString("name") ?? string.Empty, clan.GetString("tag") ?? string.Empty);
    }

    private static (string? Name, string? Folder) MapOf(JsonApiResource? game, JsonApiDocument document)
    {
        JsonApiResource? version = document.FindIncluded(game?.Relationship("mapVersion"));
        JsonApiResource? map = document.FindIncluded(version?.Relationship("map"));
        string? folder = version?.GetString("folderName");
        return (map?.GetString("displayName") ?? folder, folder);
    }

    private static TimeSpan? DurationOf(JsonApiResource? game)
    {
        if (game?.GetInt32("replayTicks") is > 0 and { } ticks)
        {
            return TimeSpan.FromSeconds(ticks / 10.0);
        }

        return game?.GetDateTimeOffset("startTime") is { } start && game.GetDateTimeOffset("endTime") is { } end && end > start ? end - start : null;
    }

    /// <summary>The faction of a statistics row: the game's index (1 to 4) or a name; random and unknown are null.</summary>
    private static Faction? FactionOf(JsonApiResource stats)
    {
        if (stats.GetInt32("faction") is { } index)
        {
            return index is >= 1 and <= 4 ? (Faction)index : null;
        }

        return stats.GetString("faction")?.ToUpperInvariant() switch
        {
            "UEF" => Faction.Uef,
            "AEON" => Faction.Aeon,
            "CYBRAN" => Faction.Cybran,
            "SERAPHIM" => Faction.Seraphim,
            _ => null,
        };
    }

    private static IReadOnlyList<MatchParticipant> Participants(JsonApiResource? game, JsonApiDocument document)
    {
        List<MatchParticipant> participants = [];
        foreach ((string Type, string Id) reference in game?.Relationships("playerStats") ?? [])
        {
            if (document.FindIncluded(reference) is not { } stats)
            {
                continue;
            }

            JsonApiResource? player = document.FindIncluded(stats.Relationship("player"));
            participants.Add(new MatchParticipant(
                Id(player?.Id ?? stats.Relationship("player")?.Id),
                player?.GetString("login") ?? "Unknown",
                stats.GetInt32("team"),
                FactionOf(stats),
                ReplayPlayerOptions.DisplayRating(stats.GetNumber("beforeMean"), stats.GetNumber("beforeDeviation")),
                ResultOf(stats.GetString("result")))
            {
                IsAi = stats.GetBoolean("ai") ?? false,
            });
        }
        return [.. participants.OrderBy(participant => participant.Team ?? int.MaxValue)];
    }

    /// <summary>The players on the other teams; with no teams (team 1 or unknown), everybody but the player.</summary>
    private static IReadOnlyList<PlayerReference> Opponents(JsonApiResource? game, JsonApiDocument document, int playerId, int? ownTeam)
        => [.. Participants(game, document)
            .Where(participant => participant.PlayerId != playerId && (ownTeam is not > 1 || participant.Team != ownTeam))
            .Select(participant => new PlayerReference(participant.PlayerId, participant.Login))];

    private static int LoginId(JsonApiResource score)
        => Id(score.GetString("loginId") ?? score.GetNumber("loginId")?.ToString("0", CultureInfo.InvariantCulture));

    private static int Id(string? id) => int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
}
