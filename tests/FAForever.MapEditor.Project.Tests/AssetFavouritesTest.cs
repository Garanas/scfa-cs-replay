using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The stars and recents of a workspace (user story F-9).
    /// </summary>
    [TestClass]
    public class AssetFavouritesTest
    {
        [TestMethod]
        public void StarsAndRecentsAreKeptInTheWorkspacesFile()
        {
            string folder = ProjectFolders.New("favourites");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, WorkspaceLayout.Favourites);
            AssetFavourites favourites = new AssetFavourites();
            int changes = 0;
            favourites.Changed += () => changes++;

            favourites.Open(file);
            Assert.AreEqual(0, favourites.Starred.Count, "a missing file is nothing starred");
            Assert.IsTrue(favourites.Star("/env/evergreen/props/rocks/rock01_prop.bp", true));
            Assert.IsFalse(favourites.Star("/env/evergreen/props/rocks/rock01_prop.bp", true), "starred already");
            favourites.Star("uec1101", true);
            favourites.Used("a");
            favourites.Used("b");
            favourites.Used("a");
            CollectionAssert.AreEqual(new[] { "a", "b" }, favourites.Recent.ToArray(), "the newest first, each once");
            Assert.IsTrue(changes >= 4);

            AssetFavourites again = new AssetFavourites();
            again.Open(file);
            CollectionAssert.AreEqual(new[] { "/env/evergreen/props/rocks/rock01_prop.bp", "uec1101" }, again.Starred.ToArray());
            CollectionAssert.AreEqual(new[] { "a", "b" }, again.Recent.ToArray());
            Assert.IsTrue(again.IsStarred("uec1101"));
            Assert.AreEqual(1, again.RecentIndex("b"));

            again.Star("uec1101", false);
            for (int i = 0; i < AssetFavourites.MaxRecent + 5; i++)
            {
                again.Used($"key{i}");
            }
            Assert.AreEqual(AssetFavourites.MaxRecent, again.Recent.Count, "the oldest go");
            Assert.AreEqual($"key{AssetFavourites.MaxRecent + 4}", again.Recent[0]);
            Assert.IsFalse(again.IsStarred("uec1101"));
        }

        [TestMethod]
        public void NothingIsWrittenBeforeAWorkspaceOpensIt()
        {
            AssetFavourites favourites = new AssetFavourites();
            favourites.Star("x", true);
            Assert.IsNull(favourites.File);
            Assert.IsTrue(favourites.IsStarred("x"));
        }
    }
}
