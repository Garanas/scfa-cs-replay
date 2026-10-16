using FAForever.Vault.Viewer.Features.Players.Compare;
using FAForever.Vault.Viewer.Features.Players.Matches;
using FAForever.Vault.Viewer.Features.Players.Profile;

namespace FAForever.Vault.Viewer.Tests.Players;

[TestClass]
public sealed class ProfilePagesTest
{
    [TestMethod]
    [DataRow(1694L, "1,694")]
    [DataRow(1692L, "1,692")]
    [DataRow(12L, "12")]
    [DataRow(3750L, "3,750")]
    public void RatingsHaveThousandsSeparators(long value, string expected)
        => Assert.AreEqual(expected, MatchFormat.Count(value));

    [TestMethod]
    public void PercentagesHaveNoSpace()
    {
        Assert.AreEqual("52%", MatchFormat.Percent(0.52));
        Assert.AreEqual("54%", ProfileFormat.Percent(0.54));
    }

    [TestMethod]
    public void NegativesUseTheMinusSign()
    {
        Assert.AreEqual("\u22125", ProfileFormat.Signed(-5));
        Assert.AreEqual("\u22121,250", ProfileFormat.Number(-1250));
        Assert.AreEqual("\u22123", MatchFormat.Signed(-3));
        Assert.IsFalse(ProfileFormat.SignedOneDecimal(-0.8).Contains('-'));
    }

    [TestMethod]
    public void TheSamePlayerTwiceIsRefused()
    {
        Assert.IsTrue(CompareInput.IsSamePlayer(2049311, 2049311));
        Assert.IsFalse(CompareInput.IsSamePlayer(2049311, 100285));
        Assert.IsFalse(CompareInput.IsSamePlayer(null, null));
        Assert.AreEqual((2049311, null), CompareInput.Resolved(2049311, 2049311));
        Assert.AreEqual((2049311, 100285), CompareInput.Resolved(2049311, 100285));
        Assert.AreEqual((null, 100285), CompareInput.Resolved(null, 100285));
    }

    [TestMethod]
    public void AnUnknownNameIsNamedInTheMessage()
    {
        string message = CompareInput.UnknownMessage("  NobodyHereXyz ");
        StringAssert.Contains(message, "NobodyHereXyz");
        Assert.IsFalse(message.Contains('-'));
    }
}
