using FAForever.FileFormats.Blueprints;
using FAForever.Vault.Viewer.Features.Players.Matches;
using FAForever.Vault.Viewer.Services.Players;

namespace FAForever.Vault.Viewer.Tests.Players;

[TestClass]
public class MatchPlayersLineTest
{
    private static MatchParticipant P(int id, int? team, bool ai = false)
        => new(id, "P" + id, team, Faction.Uef, 1000 + id, MatchResult.Victory) { IsAi = ai };

    private static PlayerMatch Game(int? team, params MatchParticipant[] players)
        => new(1, "g", null, MatchResult.Victory) { Team = team, Players = players };

    [TestMethod]
    public void AOneVersusOneNamesTheOpponent()
    {
        MatchPlayersLine line = MatchPlayersLine.Of(Game(2, P(1, 2), P(2, 3)), 1);
        Assert.AreEqual("vs P2", line.Text);
        Assert.AreEqual(2, line.Opponent?.PlayerId);
    }

    [TestMethod]
    public void ATeamGameCountsBothSidesWithoutTheOwnRow()
    {
        MatchPlayersLine line = MatchPlayersLine.Of(
            Game(2, P(1, 2), P(2, 2), P(3, 2), P(4, 2), P(5, 3), P(6, 3), P(7, 3), P(8, 3)), 1);
        Assert.AreEqual("3 with · 4 against", line.Text);
        Assert.IsNull(line.Opponent);
        Assert.IsFalse(line.Mates.Concat(line.Opponents).Any(player => player.PlayerId == 1));
    }

    [TestMethod]
    public void AFreeForAllCountsOthers()
    {
        Assert.AreEqual("3 others", MatchPlayersLine.Of(Game(1, P(1, 1), P(2, 1), P(3, 1), P(4, 1)), 1).Text);
        Assert.AreEqual("2 others", MatchPlayersLine.Of(Game(null, P(1, null), P(2, null), P(3, null)), 1).Text);
    }

    [TestMethod]
    public void ATeamWithoutOpponentsOrAloneIsWorded()
    {
        Assert.AreEqual("1 with", MatchPlayersLine.Of(Game(2, P(1, 2), P(2, 2)), 1).Text);
        Assert.AreEqual("No other players", MatchPlayersLine.Of(Game(2, P(1, 2)), 1).Text);
        Assert.AreEqual("1 with · 1 against", MatchPlayersLine.Of(Game(2, P(1, 2), P(2, 2), P(3, 3)), 1).Text);
    }

    [TestMethod]
    public void AnAiOpponentOfAOneVersusOneIsStillTheOpponent()
    {
        MatchPlayersLine line = MatchPlayersLine.Of(Game(2, P(1, 2), P(0, 3, ai: true)), 1);
        Assert.IsTrue(line.Opponent!.IsAi);
    }
}
