using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Mass and energy after their icons (user story F-8): the tiles of the shelf and the picker,
    /// the inspector's facts, the Props panel's reclaim, the sort; what a screen reader hears.
    /// </summary>
    [TestClass]
    public sealed class EconomyIconsTest
    {
        private static AssetsViewModel Units()
        {
            string folder = TestAssets.Folder();
            void Unit(string id, int mass, int energy) => File.WriteAllText(System.IO.Path.Combine(Directory.CreateDirectory(System.IO.Path.Combine(folder, "units", id)).FullName, id + "_unit.json"),
                $$"""{ "id": "{{id.ToUpperInvariant()}}", "name": "{{id}}", "faction": "Aeon", "categories": ["AEON", "STRUCTURE"], "massCost": {{mass}}, "energyCost": {{energy}} }""");
            Unit("uab0301", 5220, 47400);
            Unit("uab1101", 0, 0);
            AssetsViewModel assets = new AssetsViewModel(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            assets.Use(AssetCatalog.Load(folder));
            assets.Kind = AssetKind.Unit;
            assets.Scope = AssetScope.Everything;
            assets.Group = "Aeon";
            return assets;
        }

        [TestMethod]
        public void TilesFactsAndTotalsCarryTheirMassAndEnergy()
        {
            AssetsViewModel assets = Units();
            AssetTile factory = assets.Tiles.Single(tile => tile.Entry.Key == "uab0301");
            Assert.AreEqual((5220.0, 47400.0), (factory.Mass, factory.Energy));
            Assert.AreEqual("No cost", assets.Tiles.Single(tile => tile.Entry.Key == "uab1101").EconomyEmpty);

            AssetFact cost = factory.Entry.Facts.Single(fact => fact.Label == "Cost");
            Assert.IsTrue(cost.IsEconomy);
            Assert.AreEqual((5220.0, 47400.0), (cost.Mass, cost.Energy));
            Assert.IsFalse(factory.Entry.Facts.Single(fact => fact.Label == "Id").IsEconomy);

            ReclaimLine line = ReclaimLine.Of("North:", new ReclaimTotal(1200, 300, 45, 0));
            Assert.AreEqual(("in 45 props", "North: 1,200 mass, 300 energy in 45 props"), (line.Suffix, line.Text));
            Assert.AreEqual("in 1 prop", PropsViewModel.InProps(1));

            CollectionAssert.AreEqual(new[] { "As listed", "Most mass first", "Most energy first" }, AssetSearch.SortChoices.Select(choice => choice.ToString()).ToArray());
            Assert.IsTrue(AssetSearch.SortChoices[1].IsMass && AssetSearch.SortChoices[2].IsEnergy);
            assets.Sort = AssetSort.Energy;
            Assert.IsTrue(assets.IsSortedByEnergy && !assets.IsSortedByMass);
        }

        [TestMethod]
        public Task TheAmountSaysItsValuesInWords() => HeadlessApp.Run(() =>
        {
            EconomyAmount amount = new EconomyAmount { Mass = 5220, Energy = 47400, IsShort = true };
            Window window = new Window { Content = amount, Width = 300, Height = 60 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(("5.2k", "47.4k"), (amount.MassText, amount.EnergyText));
            Assert.AreEqual("5,220 mass, 47,400 energy", AutomationProperties.GetName(amount));

            amount.Mass = 0;
            amount.Energy = 0;
            amount.EmptyText = "No reclaim";
            Assert.IsTrue(amount.ShowsEmpty && !amount.ShowsValues);
            Assert.AreEqual("No reclaim", AutomationProperties.GetName(amount));

            amount.EmptyText = null;
            amount.Mass = 1200.5;
            amount.IsShort = false;
            amount.Prefix = "North:";
            amount.Suffix = "in 45 props";
            Assert.AreEqual("North: 1,200.5 mass, 0 energy in 45 props", AutomationProperties.GetName(amount));
            Assert.AreEqual("North: 1,200.5 0 in 45 props", amount.Text);

            amount.SetValue(AutomationProperties.NameProperty, "Reclaim of the map");
            amount.Mass = 10;
            Assert.AreEqual("Reclaim of the map", AutomationProperties.GetName(amount), "a name the view sets wins");
            window.Close();
            return Task.CompletedTask;
        });

        [TestMethod]
        public Task AShelfTileShowsTheMassAndEnergyIconsInTheirColours() => HeadlessApp.Run(() =>
        {
            AssetsViewModel assets = Units();
            AssetShelf shelf = new AssetShelf { DataContext = assets };
            Window window = new Window { Content = shelf, Width = 900, Height = 400 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            EconomyAmount[] amounts = [.. shelf.GetVisualDescendants().OfType<EconomyAmount>().Where(amount => amount.IsEffectivelyVisible)];
            EconomyAmount factory = amounts.Single(amount => amount.Mass == 5220);
            Assert.AreEqual("5,220 mass, 47,400 energy", AutomationProperties.GetName(factory), "a screen reader hears the values in full");
            Assert.AreEqual("5.2k 47.4k", factory.Text);
            Avalonia.Controls.Shapes.Path[] icons = [.. factory.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Where(path => path.IsEffectivelyVisible)];
            Assert.AreEqual(2, icons.Length, "a mass and an energy icon");
            Assert.AreEqual(factory.FontSize, icons[0].Bounds.Height, 0.5, "at the text's size");
            Assert.IsTrue(amounts.Any(amount => amount.Mass == 0 && amount.Energy == 0 && amount.Text == "No cost" && amount.ShowsEmpty), "a unit without a cost says so");

            Button tile = factory.FindAncestorOfType<Button>()!;
            byte[] png = HeadlessApp.Capture(tile);
            HeadlessApp.SaveCapture(png, "economy-shelf-tile");
            Color mass = Token("MassBrush", window);
            Color energy = Token("EnergyBrush", window);
            Color[] pixels = Pixels(png);
            Assert.IsTrue(pixels.Any(pixel => Near(pixel, mass)), "the mass icon in MassBrush");
            Assert.IsTrue(pixels.Any(pixel => Near(pixel, energy)), "the energy icon in EnergyBrush");
            window.Close();
            return Task.CompletedTask;
        });

        private static Color Token(string key, Window window) =>
            window.TryFindResource(key, window.ActualThemeVariant, out object? brush) && brush is ISolidColorBrush solid ? solid.Color : throw new AssertFailedException(key);

        private static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) < 24 && a.A > 240;

        private static Color[] Pixels(byte[] png)
        {
            using Bitmap bitmap = new Bitmap(new MemoryStream(png));
            int width = bitmap.PixelSize.Width;
            int height = bitmap.PixelSize.Height;
            byte[] bytes = new byte[width * height * 4];
            GCHandle pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                bitmap.CopyPixels(new PixelRect(0, 0, width, height), pinned.AddrOfPinnedObject(), bytes.Length, width * 4);
            }
            finally
            {
                pinned.Free();
            }
            List<Color> colours = [];
            for (int i = 0; i < bytes.Length; i += 4)
            {
                colours.Add(Color.FromArgb(bytes[i + 3], bytes[i + 2], bytes[i + 1], bytes[i]));
            }
            return [.. colours];
        }
    }
}
