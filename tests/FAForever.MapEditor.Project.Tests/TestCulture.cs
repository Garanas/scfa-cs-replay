using System.Globalization;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The tests run in one culture on every computer, so a number written or read in the
    /// computer's own culture fails the same everywhere.
    /// </summary>
    [TestClass]
    public static class TestCulture
    {
        [AssemblyInitialize]
        public static void UseOneCulture(TestContext _)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        }
    }
}
