using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace FAForever.MapEditor.App.Tests
{
    [TestClass]
    public sealed class AssetsViewModelTest
    {
        private const string HardFfa = "assets/maps/HardFFA.v0001";

        private static AssetsViewModel Shelf()
        {
            AssetsViewModel shelf = new AssetsViewModel(NullLogger.Instance);
            shelf.Use(TestAssets.Catalog());
            return shelf;
        }

        [TestMethod]
        public void ShowsAKindAtATimeWithThePropsVariantsAsOneTile()
        {
            AssetsViewModel shelf = Shelf();

            Assert.AreEqual(AssetKind.Prop, shelf.Kind);
            CollectionAssert.AreEquivalent(new[] { "pine06", "oak01", "rock01", "tund_rock01" }, shelf.Tiles.Select(tile => tile.Title).ToArray());
            AssetTile pine = shelf.Tiles.Single(tile => tile.Title == "pine06");
            Assert.AreEqual(3, pine.Members.Count);
            Assert.AreEqual("6 props in 4 families", shelf.Summary);

            shelf.ToggleFamiliesCommand.Execute(null);
            Assert.AreEqual(6, shelf.Tiles.Count);

            shelf.NextTileSizeCommand.Execute(null);
            Assert.AreEqual(2, shelf.TileSize);
            shelf.NextTileSizeCommand.Execute(null);
            Assert.AreEqual(0, shelf.TileSize, "after large comes small");

            Assert.AreEqual(6, shelf.Kinds.Single(kind => kind.Value == "Prop").Count);
            Assert.AreEqual(2, shelf.Kinds.Single(kind => kind.Value == "Decal").Count);
        }

        [TestMethod]
        public void FollowsTheModeAndFiltersByBiomeCategoryAndWords()
        {
            AssetsViewModel shelf = Shelf();

            shelf.FollowMode("paint");
            Assert.AreEqual(AssetKind.Texture, shelf.Kind);
            Assert.AreEqual(2, shelf.Tiles.Count, "an albedo and its normal map are one texture");
            shelf.FollowMode("markers");
            Assert.AreEqual(AssetKind.Texture, shelf.Kind, "a mode without assets keeps the kind");

            shelf.FollowMode("props");
            shelf.Group = "evergreen";
            Assert.AreEqual(3, shelf.Tiles.Count);
            CollectionAssert.AreEquivalent(new[] { "rocks", "trees" }, shelf.Categories.Select(category => category.Value).ToArray());
            shelf.Category = "rocks";
            Assert.AreEqual("rock01", shelf.Tiles.Single().Title);

            shelf.Kind = AssetKind.Unit;
            Assert.IsNull(shelf.Group, "a biome means nothing to units");
            Assert.AreEqual("UEF Residential Building", shelf.Tiles.Single().Title, "the civilian units first");
            shelf.Scope = AssetScope.Everything;
            shelf.Query = "snoop";
            Assert.AreEqual("uel0101", shelf.Tiles.Single().Entry.Key);
        }

        [TestMethod]
        public void TheMapsBiomesAndWhatItUsesNarrowTheShelf()
        {
            AssetsViewModel shelf = Shelf();
            MapDocument document = EditedMap.ImportMap(HardFfa).Document;
            // the map's first prop is a pine of the test assets; its other biomes the test assets lack
            Prop first = document.Props[0];
            document = document with { Props = document.Props.SetItem(0, first with { Blueprint = "/env/evergreen/props/trees/pine06_s2_prop.bp" }) };

            shelf.UseMap(document);

            StringAssert.Contains(shelf.NarrowHelp, "Evergreen");
            CollectionAssert.AreEquivalent(new[] { "pine06", "oak01", "rock01" }, shelf.Tiles.Select(tile => tile.Title).ToArray(), "not the tundra's rock");
            Assert.AreEqual(1, shelf.Tiles.Single(tile => tile.Title == "pine06").Uses);

            shelf.Scope = AssetScope.InMap;
            Assert.AreEqual("pine06_s2", shelf.Tiles.Single().Title, "the variant the map uses, not its family");
            Assert.AreEqual(1, shelf.InMapCount);

            shelf.Kind = AssetKind.Unit;
            Assert.IsTrue(shelf.IsEmpty);
            StringAssert.Contains(shelf.EmptyText, "uses none");
        }

        [TestMethod]
        public void SelectingAnAssetShowsItWhereverTheFiltersWere()
        {
            AssetsViewModel shelf = Shelf();
            shelf.Query = "oak";

            shelf.Select("/env/Evergreen/Props/Trees/Pine06_s2_prop.bp");

            Assert.AreEqual(AssetKind.Prop, shelf.Kind);
            Assert.AreEqual("", shelf.Query, "the search that hid it is cleared");
            Assert.AreEqual("pine06", shelf.Selected!.Title);
            Assert.AreEqual("pine06_s2", shelf.Shown!.Name, "the variant asked for");
            Assert.IsTrue(shelf.Selected.IsSelected);
            Assert.IsTrue(shelf.IsFamilySelected);

            shelf.Select("uec1101");
            Assert.AreEqual(AssetKind.Unit, shelf.Kind);
            Assert.AreEqual("UEF Residential Building", shelf.Shown!.Name);
            StringAssert.Contains(shelf.ShownPlanned, "U-2");

            Assert.ThrowsException<ArgumentException>(() => shelf.Select("/env/none/props/nothing_prop.bp"));
        }

        [TestMethod]
        public void TheTerrainShowsTheWorkspacesBrushes()
        {
            AssetsViewModel shelf = Shelf();

            shelf.FollowMode("terrain");

            Assert.AreEqual(AssetKind.Brush, shelf.Kind);
            CollectionAssert.AreEquivalent(new[] { "crater01", "ridge" }, shelf.Tiles.Select(tile => tile.Title).ToArray());
            CollectionAssert.AreEquivalent(new[] { "brushes", "craters" }, shelf.Groups.Select(group => group.Value).ToArray());
            Assert.AreEqual("Folder", shelf.GroupsTitle);
            Assert.AreEqual("2 brushes", shelf.Summary);
            Assert.IsTrue(shelf.Tiles.All(tile => tile.HasImage));

            shelf.Select("brushes/craters/crater01.png");
            Assert.AreEqual("Brush · Craters · shape", shelf.ShownDetail);
            StringAssert.Contains(shelf.ShownPlanned, "T-1");
            Assert.AreEqual("In the workspace", shelf.ShownKeyLabel);
            Assert.AreEqual("", shelf.ShownUses, "a map holds no brushes");

            shelf.Use(TestAssets.Catalog());
            Assert.AreEqual("crater01", shelf.Shown?.Name, "the selection stays when the assets are read again");

            AssetsViewModel empty = new AssetsViewModel(NullLogger.Instance);
            empty.Use(TestAssets.WithoutBrushes());
            empty.FollowMode("terrain");
            Assert.IsTrue(empty.IsEmpty);
            StringAssert.Contains(empty.EmptyText, "brushes folder");
        }

        [TestMethod]
        public void TheArrowKeysMoveThroughTheRows()
        {
            AssetsViewModel shelf = Shelf();
            shelf.Scope = AssetScope.Everything;
            shelf.Families = false;
            shelf.Columns = 4;
            Assert.AreEqual(2, shelf.Rows.Count);

            shelf.Move(1, 0);
            Assert.AreSame(shelf.Tiles[0], shelf.Selected, "the first move selects the first tile");
            shelf.Move(0, 1);
            Assert.AreSame(shelf.Tiles[4], shelf.Selected);
            shelf.Move(1, 1);
            Assert.AreSame(shelf.Tiles[5], shelf.Selected, "the last tile stops it");
            shelf.ClearSelectionCommand.Execute(null);
            Assert.IsFalse(shelf.HasSelection);
        }
    }
}
