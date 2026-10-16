using FAForever.FileFormats.Blueprints;
using FAForever.Vault.Viewer.Features.Players.Army;
using FAForever.Vault.Viewer.Features.Players.Charts;
using FAForever.Vault.Viewer.Features.Players.Matches;
using FAForever.Vault.Viewer.Services.Players;
using FAForever.Vault.Viewer.Services.Players.Statistics;

namespace FAForever.Vault.Viewer.Tests.Players;

[TestClass]
public class MatchUrlTest
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private static Func<string, string?> Address(params (string Name, string Value)[] parameters)
        => name => parameters.Where(parameter => parameter.Name == name).Select(parameter => parameter.Value).FirstOrDefault();

    [TestMethod]
    public void AnEmptyAddressIsTheDefaultFilters()
    {
        MatchFilters filters = MatchUrl.Read(Address());

        Assert.AreEqual(new MatchFilters("1y", "all", "any", "any", "", "", "newest", 1), filters);
        Assert.AreEqual(MatchFilters.Default, filters);
        Assert.IsFalse(MatchUrl.AnyFilter(Address()));
    }

    [TestMethod]
    public void ValuesBecomeAQuery()
    {
        MatchFilters filters = MatchUrl.Read(Address(
            ("period", "3m"), ("mode", "tmm_2v2"), ("faction", "Cybran"), ("result", "win"),
            ("map", " Theta "), ("with", "Ravelin"), ("sort", "longest"), ("page", "3")));

        MatchQuery query = filters.ToQuery(Now);

        Assert.AreEqual(Now.AddMonths(-3), query.Since);
        Assert.AreEqual("tmm_2v2", query.Mode);
        Assert.AreEqual(Faction.Cybran, query.Faction);
        Assert.AreEqual(MatchResult.Victory, query.Result);
        Assert.AreEqual("Theta", query.Map);
        Assert.AreEqual("Ravelin", query.WithPlayer);
        Assert.AreEqual(MatchSort.Longest, query.Sort);
        Assert.AreEqual(3, query.Page);
        Assert.IsTrue(MatchUrl.AnyFilter(Address(("map", "Theta"))));
        Assert.IsFalse(MatchUrl.AnyFilter(Address(("page", "3"))), "a page alone is not a filter");
    }

    [TestMethod]
    public void AllTimeAndNoFiltersAskForEverything()
    {
        MatchQuery query = MatchFilters.Default.ToQuery(Now) with { };
        MatchQuery everything = MatchUrl.Read(Address(("period", "all"))).ToQuery(Now);

        Assert.IsNull(everything.Since);
        Assert.IsNull(query.Faction);
        Assert.IsNull(query.Result);
        Assert.IsNull(query.Map);
        Assert.IsNull(query.WithPlayer);
        Assert.AreEqual(MatchSort.Newest, query.Sort);
        Assert.AreEqual(1, query.Page);
    }

    [TestMethod]
    [DataRow("period", "decade")]
    [DataRow("mode", "tmm_9v9")]
    [DataRow("faction", "random")]
    [DataRow("result", "draw")]
    [DataRow("sort", "loudest")]
    [DataRow("page", "0")]
    [DataRow("page", "abc")]
    public void UnknownValuesAreTheDefault(string name, string value)
        => Assert.AreEqual(MatchFilters.Default, MatchUrl.Read(Address((name, value))));

    [TestMethod]
    public void TheChartsIgnoreOrderAndPage()
    {
        MatchFilters a = MatchUrl.Read(Address(("mode", "custom"), ("sort", "oldest"), ("page", "4")));
        MatchFilters b = MatchUrl.Read(Address(("mode", "custom")));

        Assert.AreNotEqual(a, b);
        Assert.AreEqual(a.ForCharts(), b.ForCharts());
    }

    [TestMethod]
    public void ResetClearsEveryFilterParameter()
        => CollectionAssert.AreEquivalent(new[] { "period", "mode", "faction", "result", "map", "with", "sort" }, MatchUrl.Parameters.ToArray());
}

[TestClass]
public class MatchFormatTest
{
    private static PlayerMatch Game(int id, string map, MatchResult result, Faction? faction = null, int minutes = 20, string? folder = null)
        => new(id, $"Game {id}", new DateTimeOffset(2026, 10, 10, 18, 30, 0, TimeSpan.Zero), result)
        {
            MapName = map,
            MapFolder = folder,
            Faction = faction,
            Duration = TimeSpan.FromMinutes(minutes),
        };

    [TestMethod]
    public void NumbersCarryTheirSign()
    {
        Assert.AreEqual("+14", MatchFormat.Signed(14));
        Assert.AreEqual("−3", MatchFormat.Signed(-3));
        Assert.AreEqual("0", MatchFormat.Signed(0));
        Assert.AreEqual("+1.5", MatchFormat.SignedDecimal(1.46));
        Assert.AreEqual("−0.8", MatchFormat.SignedDecimal(-0.76));
        Assert.AreEqual(DeltaTone.Good, MatchFormat.Tone(2));
        Assert.AreEqual(DeltaTone.Bad, MatchFormat.Tone(-2));
        Assert.AreEqual(DeltaTone.Neutral, MatchFormat.Tone(0));
    }

    [TestMethod]
    public void VisibleTextHasNoHyphenOrDash()
    {
        string[] texts =
        [
            MatchFormat.Signed(-3), MatchFormat.SignedDecimal(-1.2), MatchFormat.Length(null), MatchFormat.Percent(null),
            MatchFormat.When(null), MatchFormat.Mode("tmm_4v4_full_share"), MatchFormat.CellText(5, 4, 12),
            MatchFormat.LengthLabel(new LengthBucket("x", 10, 20, 0, 0, 0)),
        ];
        foreach (string text in texts)
        {
            Assert.IsFalse(text.Contains('-') || text.Contains('–') || text.Contains('—'), text);
        }
    }

    [TestMethod]
    public void LengthsAndShares()
    {
        Assert.AreEqual("42:10", MatchFormat.Length(new TimeSpan(0, 42, 10)));
        Assert.AreEqual("1:02:10", MatchFormat.Length(new TimeSpan(1, 2, 10)));
        Assert.AreEqual("Unknown", MatchFormat.Length(null));
        Assert.AreEqual("32 min", MatchFormat.Minutes(TimeSpan.FromMinutes(31.6)));
        Assert.AreEqual("58%", MatchFormat.Percent(0.576));
        Assert.AreEqual("No result", MatchFormat.Percent(null));
        Assert.AreEqual("1,234 games", MatchFormat.Games(1234));
        Assert.AreEqual("1 game", MatchFormat.Games(1));
    }

    [TestMethod]
    public void TheWhenIsInTheGivenTimeZone()
    {
        DateTimeOffset start = new(2026, 10, 14, 23, 30, 0, TimeSpan.Zero);

        Assert.AreEqual("14 Oct 2026, 23:30", MatchFormat.When(start, TimeZoneInfo.Utc));
        Assert.AreEqual("15 Oct 2026, 01:30", MatchFormat.When(start, TimeZoneInfo.CreateCustomTimeZone("plus2", TimeSpan.FromHours(2), "plus2", "plus2")));
    }

    [TestMethod]
    public void ModeIsTheBoardOrCustom()
    {
        Assert.AreEqual("Custom", MatchFormat.Mode(null));
        Assert.AreEqual("Custom", MatchFormat.Mode("global"));
        Assert.AreEqual("Ladder 1v1", MatchFormat.Mode("ladder_1v1"));
        Assert.AreEqual("3v3", MatchFormat.Mode("tmm_3v3"));
    }

    [TestMethod]
    public void ResultsHaveAWord()
    {
        Assert.AreEqual("Win", MatchFormat.ResultWord(MatchResult.Victory));
        Assert.AreEqual("Loss", MatchFormat.ResultWord(MatchResult.Defeat));
        Assert.AreEqual("Draw", MatchFormat.ResultWord(MatchResult.Draw));
        Assert.AreEqual("Unknown", MatchFormat.ResultWord(MatchResult.Unknown));
    }

    [TestMethod]
    public void LengthColumnsNameTheGamesAndTheShare()
    {
        PlayerMatch[] matches = [Game(1, "A", MatchResult.Victory, minutes: 5), Game(2, "A", MatchResult.Defeat, minutes: 6), Game(3, "A", MatchResult.Victory, minutes: 50)];

        IReadOnlyList<ColumnItemView> columns = [.. MatchFormat.LengthColumns(MatchStatistics.ByLength(matches)).Select(item => new ColumnItemView(item.Label, item.Value, item.TopLabel))];

        Assert.AreEqual(5, columns.Count);
        Assert.AreEqual("Under 10\n2 games", columns[0].Label);
        Assert.AreEqual(50, columns[0].Value, 1e-9);
        Assert.AreEqual("50%", columns[0].TopLabel);
        Assert.AreEqual("10 to 20\n0 games", columns[1].Label);
        Assert.IsNull(columns[1].TopLabel, "no decided games, no label");
        Assert.AreEqual("45+\n1 game", columns[4].Label);
    }

    private sealed record ColumnItemView(string Label, double Value, string? TopLabel);

    [TestMethod]
    public void FactionRowsUseTheFactionColourAndName()
    {
        PlayerMatch[] matches = [Game(1, "A", MatchResult.Victory, Faction.Aeon), Game(2, "A", MatchResult.Defeat, Faction.Aeon), Game(3, "A", MatchResult.Victory, Faction.Aeon)];

        var rows = MatchFormat.FactionRows(MatchStatistics.ByFaction(matches));

        Assert.AreEqual(4, rows.Count);
        Assert.AreEqual("Aeon", rows[1].Label);
        Assert.AreEqual("67%", rows[1].ValueText);
        Assert.AreEqual("3 games", rows[1].Sub);
        Assert.AreEqual("var(--swatch-aeon)", rows[1].CssColor);
        Assert.AreEqual("No games", rows[0].ValueText);
    }

    [TestMethod]
    public void MapRowsLinkToTheMapPageWhenTheFolderIsKnown()
    {
        PlayerMatch[] matches =
        [
            Game(1, "Theta Passage", MatchResult.Victory, folder: "theta_passage.v0001"),
            Game(2, "Theta Passage", MatchResult.Defeat, folder: "theta_passage.v0001"),
            Game(3, "Wonder", MatchResult.Victory),
        ];

        var rows = MatchFormat.MapRows(matches);

        Assert.AreEqual("Theta Passage", rows[0].Label);
        Assert.AreEqual("maps/theta_passage.v0001", rows[0].Href);
        Assert.AreEqual("2 games", rows[0].Sub);
        Assert.AreEqual("Wonder", rows[1].Label);
        Assert.IsNull(rows[1].Href);
    }

    [TestMethod]
    public void TheBusiestCellIsNamed()
    {
        // 2026-10-10 is a Saturday; 18:30 UTC is in the block 16 to 20.
        PlayerMatch[] matches = [Game(1, "A", MatchResult.Victory), Game(2, "A", MatchResult.Victory)];
        var cells = MatchStatistics.Heatmap(matches, TimeZoneInfo.Utc);

        Assert.AreEqual("Saturday, 16 to 20: 2 games", MatchFormat.Busiest(cells));
        Assert.IsNull(MatchFormat.Busiest(MatchStatistics.Heatmap([], TimeZoneInfo.Utc)));
        Assert.AreEqual(7, MatchFormat.DayLabels.Count);
        Assert.AreEqual(6, MatchFormat.BlockLabels.Count);
    }

    [TestMethod]
    public void OthersSplitIntoTeamMatesAndOpponents()
    {
        PlayerMatch team = Game(1, "A", MatchResult.Victory) with
        {
            Team = 2,
            Players =
            [
                new MatchParticipant(1, "Me", 2, Faction.Uef, 1500, MatchResult.Victory),
                new MatchParticipant(2, "Mate", 2, Faction.Aeon, 1400, MatchResult.Victory),
                new MatchParticipant(3, "Foe", 3, Faction.Cybran, 1600, MatchResult.Defeat),
            ],
        };
        var (mates, opponents) = MatchFormat.Others(team, 1);

        CollectionAssert.AreEqual(new[] { "Mate" }, mates.Select(player => player.Login).ToArray());
        CollectionAssert.AreEqual(new[] { "Foe" }, opponents.Select(player => player.Login).ToArray());

        PlayerMatch free = team with { Team = 1 };
        (mates, opponents) = MatchFormat.Others(free, 1);
        Assert.AreEqual(0, mates.Count, "team 1 is no team");
        Assert.AreEqual(2, opponents.Count);
    }
}

[TestClass]
public class ArmyAndAchievementsTest
{
    private static AchievementDefinition Standard(string id, int order, int xp = 5)
        => new(id, $"Name {id}", "Description", AchievementKind.Standard, 0, order) { ExperiencePoints = xp, UnlockersPercent = 40 };

    private static AchievementDefinition Incremental(string id, int order, int steps)
        => new(id, $"Name {id}", "Description", AchievementKind.Incremental, steps, order) { ExperiencePoints = 20, UnlockersPercent = 4.26 };

    private static IReadOnlyList<AchievementRow> Rows() => AchievementFilter.Build(
        [Incremental("b", 2, 50), Standard("a", 1), Standard("c", 3), Incremental("d", 4, 10), Standard("e", 5)],
        [
            new PlayerAchievement("a", AchievementState.Unlocked, 0, null),
            new PlayerAchievement("B", AchievementState.Revealed, 25, null),
            new PlayerAchievement("c", AchievementState.Revealed, 0, null),
            new PlayerAchievement("d", AchievementState.Unlocked, 10, null),
        ]);

    [TestMethod]
    public void RowsFollowTheGameOrderAndTheStanding()
    {
        var rows = Rows();

        CollectionAssert.AreEqual(new[] { "a", "b", "c", "d", "e" }, rows.Select(row => row.Definition.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { AchievementProgress.Unlocked, AchievementProgress.InProgress, AchievementProgress.Locked, AchievementProgress.Unlocked, AchievementProgress.Locked },
            rows.Select(row => row.Progress).ToArray());
        Assert.AreEqual(0.5, rows[1].Fraction, 1e-9);
        Assert.AreEqual(1, rows[3].Fraction, 1e-9);
        Assert.AreEqual(0, rows[2].Fraction, 1e-9);
        Assert.IsTrue(rows[1].HasSteps);
        Assert.IsFalse(rows[0].HasSteps);
    }

    [TestMethod]
    [DataRow("all", "a,b,c,d,e")]
    [DataRow(null, "a,b,c,d,e")]
    [DataRow("nonsense", "a,b,c,d,e")]
    [DataRow("unlocked", "a,d")]
    [DataRow("progress", "b")]
    [DataRow("locked", "c,e")]
    [DataRow("UNLOCKED", "a,d")]
    public void ShowKeepsTheMatchingRows(string? show, string expected)
        => Assert.AreEqual(expected, string.Join(",", AchievementFilter.Filter(Rows(), show).Select(row => row.Definition.Id)));

    [TestMethod]
    public void SummaryCountsUnlockedAndPoints()
        => Assert.AreEqual("2 of 5 unlocked, 25 experience points.", AchievementFilter.Summary(Rows()));

    [TestMethod]
    public void RarityAndSteps()
    {
        Assert.AreEqual("40% of players", AchievementFilter.Rarity(40.2));
        Assert.AreEqual("4.3% of players", AchievementFilter.Rarity(4.26));
        Assert.AreEqual("0.4% of players", AchievementFilter.Rarity(0.4));
        Assert.AreEqual("25 of 50", AchievementFilter.Steps(Rows()[1]));
        Assert.AreEqual("In progress", AchievementFilter.StateWord(AchievementProgress.InProgress));
    }

    [TestMethod]
    public void IconIsUnlockedOrRevealedOrNothing()
    {
        AchievementDefinition definition = new("a", "A", "D", AchievementKind.Standard, 0, 1) { RevealedIconUrl = "r.png", UnlockedIconUrl = "u.png" };

        Assert.AreEqual("u.png", AchievementFilter.IconUrl(new AchievementRow(definition, AchievementProgress.Unlocked, 0)));
        Assert.AreEqual("r.png", AchievementFilter.IconUrl(new AchievementRow(definition, AchievementProgress.Locked, 0)));
        Assert.IsNull(AchievementFilter.IconUrl(new AchievementRow(Standard("b", 1), AchievementProgress.Unlocked, 0)));
    }

    private static ArmySummary Army() => ArmyStatistics.Summarize(new PlayerEventCounts(new Dictionary<string, long>
    {
        [PlayerEvents.CustomGamesPlayed] = 30,
        [PlayerEvents.LadderGamesPlayed] = 70,
        [PlayerEvents.CybranPlays] = 60,
        [PlayerEvents.CybranWins] = 36,
        [PlayerEvents.UefPlays] = 40,
        [PlayerEvents.UefWins] = 14,
        [PlayerEvents.SecondsPlayed] = 100 * 3600,
        [PlayerEvents.AcusLost] = 25,
        [PlayerEvents.BuiltLand] = 1000,
        [PlayerEvents.LostLand] = 250,
        [PlayerEvents.BuiltAir] = 400,
        [PlayerEvents.LostAir] = 100,
    }));

    [TestMethod]
    public void ArmyNumbers()
    {
        ArmySummary army = Army();

        Assert.AreEqual(100, ArmyFormat.Games(army));
        Assert.AreEqual(0.5, ArmyFormat.WinShare(army)!.Value, 1e-9);
        Assert.AreEqual("100 h", ArmyFormat.Hours(army.HoursPlayed));
        Assert.AreEqual("60 min per game", ArmyFormat.AverageGame(army));
        Assert.AreEqual(1400, ArmyFormat.Built(army.ByCategory));
        Assert.AreEqual(350, ArmyFormat.Lost(army.ByCategory));
        Assert.AreEqual(0.25, ArmyFormat.LostShare(1400, 350)!.Value, 1e-9);
        Assert.IsNull(ArmyFormat.LostShare(0, 0));
        Assert.AreEqual("0.25", ArmyFormat.PerGame(army.AcusLostPerGame));
        Assert.AreEqual("No games", ArmyFormat.PerGame(null));
    }

    [TestMethod]
    public void FactionBarsAreWonThenLostInTheFactionColour()
    {
        var rows = ArmyFormat.FactionRows(Army().Factions);

        Assert.AreEqual("Cybran", rows[2].Label);
        Assert.AreEqual(36, rows[2].Value);
        Assert.AreEqual(24, rows[2].Extra);
        Assert.AreEqual("60 · 60% won", rows[2].ValueText);
        Assert.AreEqual("var(--swatch-cybran)", rows[2].CssColor);
        Assert.AreEqual("No games", rows[1].ValueText);

        FactionPlays main = ArmyFormat.MainFaction(Army().Factions)!;
        Assert.AreEqual(Faction.Cybran, main.Faction);
        Assert.AreEqual("Cybran, 60% of games", ArmyFormat.MainFactionText(main, 100));
        Assert.IsNull(ArmyFormat.MainFaction([new FactionPlays(Faction.Uef, 0, 0)]));
    }

    [TestMethod]
    public void BuiltAndLostShareOneScalePerPanel()
    {
        var rows = ArmyFormat.BuiltLostRows(Army().ByCategory);

        Assert.AreEqual("Air", rows[0].Label);
        Assert.AreEqual(0.4, rows[0].BuiltFraction, 1e-9);
        Assert.AreEqual(0.1, rows[0].LostFraction, 1e-9);
        Assert.AreEqual("25% lost", rows[0].LostShare);
        Assert.AreEqual(1.0, rows[1].BuiltFraction, 1e-9);
        Assert.AreEqual(0.0, rows[2].BuiltFraction, 1e-9);
        Assert.AreEqual("None built", rows[2].LostShare);
        Assert.AreEqual(0, ArmyFormat.BuiltLostRows([]).Count);
    }

    [TestMethod]
    public void ModesAreTwoSegments()
    {
        var segments = ArmyFormat.ModeSegments(Army());

        Assert.AreEqual(30, segments[0].Value);
        Assert.AreEqual(70, segments[1].Value);
        Assert.AreEqual("Custom games", segments[0].Label);
    }
}
