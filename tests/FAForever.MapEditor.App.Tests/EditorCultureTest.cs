using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia.Data;
using Avalonia.Threading;
using FAForever.MapEditor.App.Automation;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.App.Tools;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.App.Views;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Numbers show and are read alike on every computer (<see cref="EditorCulture"/>): the tests run
    /// in the culture the editor runs in, set once for the assembly, and these check that a computer
    /// with a comma as its decimal separator reads "12.5" and "12,5" alike.
    /// </summary>
    [TestClass]
    public sealed class EditorCultureTest
    {
        private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

        [AssemblyInitialize]
        public static void UseTheEditorsCulture(TestContext _) => EditorCulture.Use();

        [TestMethod]
        public void NumbersAreReadWithAPointOrACommaAndShownWithAPoint()
        {
            CultureInfo before = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = Dutch;
            try
            {
                Assert.IsTrue(EditorCulture.TryParse("12.5", out double point));
                Assert.AreEqual(12.5, point);
                Assert.IsTrue(EditorCulture.TryParse(" 12,5 ", out double comma));
                Assert.AreEqual(12.5, comma);
                Assert.IsFalse(EditorCulture.TryParse("1,234.5", out double _), "digits are never grouped");
                Assert.IsFalse(EditorCulture.TryParse("1,2,3", out double _));
                Assert.IsFalse(EditorCulture.TryParse("NaN", out double _));
                Assert.AreEqual("12.5", EditorCulture.Format(12.5));

                NumberTextConverter converter = NumberTextConverter.Instance;
                Assert.AreEqual("0.25", converter.Convert(0.25f, typeof(string), null, Dutch));
                Assert.AreEqual(7.25f, converter.ConvertBack("7,25", typeof(float), null, Dutch));
                Assert.AreEqual(3, converter.ConvertBack("3", typeof(int), null, Dutch));
                Assert.IsInstanceOfType<BindingNotification>(converter.ConvertBack("3.5", typeof(int), null, Dutch), "not a whole number");
                Assert.IsInstanceOfType<BindingNotification>(converter.ConvertBack("high", typeof(double), null, Dutch));
            }
            finally
            {
                CultureInfo.CurrentCulture = before;
            }
        }

        [TestMethod]
        public Task AFieldOfAToolReadsAPointAndACommaOnADutchComputer() => HeadlessApp.Run(async () =>
        {
            CultureInfo before = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = Dutch;
            try
            {
                MainWindow window = new MainWindow { Width = 1280, Height = 800 };
                MainViewModel model = new MainViewModel(new WindowDialogs(window));
                await model.OpenAsync("assets/maps/HardFFA.v0001");
                window.DataContext = model;
                window.Show();
                Dispatcher.UIThread.RunJobs();
                AutomationServer automation = new AutomationServer(window, model, port: 0);
                model.SelectTool("terrain.sculpt");
                SculptTool sculpt = model.Tool<SculptTool>();
                await automation.HandleAsync("set", new JsonObject { ["label"] = "Keep the heights within limits", ["value"] = true });

                JsonObject typed = (JsonObject)await automation.HandleAsync("set", new JsonObject { ["label"] = "Lowest height the brush makes", ["value"] = "12.5" });
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(12.5, sculpt.MinHeight, 1e-6, "a point");
                Assert.AreEqual("12.5", typed["set"]!["value"]!.GetValue<string>(), "shown with a point");

                await automation.HandleAsync("set", new JsonObject { ["label"] = "Highest height the brush makes", ["value"] = "40,75" });
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(40.75, sculpt.MaxHeight, 1e-6, "a comma");
                window.Close();
            }
            finally
            {
                CultureInfo.CurrentCulture = before;
            }
        });
    }
}
