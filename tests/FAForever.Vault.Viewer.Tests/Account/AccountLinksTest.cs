using FAForever.Vault.Viewer.Services.Auth;

namespace FAForever.Vault.Viewer.Tests.Account;

[TestClass]
public class AccountLinksTest
{
    [TestMethod]
    public void ProfileUsesTheNumericId()
    {
        Assert.AreEqual("players/2049311", AccountLinks.Profile("2049311"));
        Assert.IsNull(AccountLinks.Profile("abc"));
        Assert.IsNull(AccountLinks.Profile(null));
    }

    [TestMethod]
    [DataRow("Ashgrove", "replays/search?player=Ashgrove", "maps/search?author=Ashgrove")]
    [DataRow("a b&c", "replays/search?player=a%20b%26c", "maps/search?author=a%20b%26c")]
    public void SearchLinksEncodeTheName(string name, string replays, string maps)
    {
        Assert.AreEqual(replays, AccountLinks.Replays(name));
        Assert.AreEqual(maps, AccountLinks.Maps(name));
    }
}
