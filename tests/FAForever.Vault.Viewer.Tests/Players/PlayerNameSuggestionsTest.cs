using FAForever.Vault.Viewer.Features.Players;
using FAForever.Vault.Viewer.Services.Players;

namespace FAForever.Vault.Viewer.Tests.Players;

[TestClass]
public class PlayerNameSuggestionsTest
{
    private static PlayerSearchHit Current(int id, string login) => new(id, login, null);

    private static PlayerSearchHit Former(int id, string login, string former) => new(id, login, null) { MatchedFormerName = former };

    [TestMethod]
    [DataRow(null, false)]
    [DataRow("", false)]
    [DataRow("a", false)]
    [DataRow(" a ", false)]
    [DataRow("ab", true)]
    [DataRow(" ab", true)]
    public void SearchesFromTwoCharacters(string? text, bool expected)
        => Assert.AreEqual(expected, PlayerNameSuggestions.ShouldSearch(text));

    [TestMethod]
    public void CurrentNamesComeBeforeFormerNames()
    {
        IReadOnlyList<PlayerSearchHit> shown = PlayerNameSuggestions.Arrange(
            "stomp",
            [Former(1, "Ashgrove", "grovestomp"), Current(2, "Stomper"), Current(3, "Footstomp")]);
        CollectionAssert.AreEqual(new[] { 2, 3, 1 }, shown.Select(hit => hit.Id).ToArray());
    }

    [TestMethod]
    public void AnExactMatchComesFirstOfAll()
    {
        IReadOnlyList<PlayerSearchHit> shown = PlayerNameSuggestions.Arrange(
            "rusticpylon",
            [Current(1, "RusticPylon_NL"), Former(2, "Other", "RusticPylon"), Current(3, "RusticPylon")]);
        CollectionAssert.AreEqual(new[] { 3, 1, 2 }, shown.Select(hit => hit.Id).ToArray());
    }

    [TestMethod]
    public void AFormerNameThatEqualsTheTextDoesNotJumpTheQueue()
    {
        IReadOnlyList<PlayerSearchHit> shown = PlayerNameSuggestions.Arrange("rust", [Former(1, "Other", "rust"), Current(2, "Rusty")]);
        Assert.AreEqual(2, shown[0].Id);
    }

    [TestMethod]
    public void APlayerAppearsOnce()
    {
        IReadOnlyList<PlayerSearchHit> shown = PlayerNameSuggestions.Arrange(
            "ash",
            [Current(1, "Ashgrove"), Former(1, "Ashgrove", "Ash_Grove"), Current(2, "Ashen")]);
        CollectionAssert.AreEqual(new[] { 1, 2 }, shown.Select(hit => hit.Id).ToArray());
        Assert.IsNull(shown[0].MatchedFormerName);
    }

    [TestMethod]
    public void AtMostEightAreShownInTheOrderGiven()
    {
        PlayerSearchHit[] hits = [.. Enumerable.Range(1, 12).Select(id => Current(id, $"Player{id}"))];
        IReadOnlyList<PlayerSearchHit> shown = PlayerNameSuggestions.Arrange("pla", hits);
        Assert.AreEqual(PlayerNameSuggestions.MaximumShown, shown.Count);
        CollectionAssert.AreEqual(Enumerable.Range(1, 8).ToArray(), shown.Select(hit => hit.Id).ToArray());
    }

    [TestMethod]
    public void NothingFoundShowsNothing() => Assert.AreEqual(0, PlayerNameSuggestions.Arrange("zz", []).Count);
}
