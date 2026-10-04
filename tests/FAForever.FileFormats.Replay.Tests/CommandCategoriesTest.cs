using FAForever.FileFormats.Replay;

namespace FAForever.FileFormats.Replay.Tests
{
    [TestClass]
    public class CommandCategoriesTest
    {
        /// <summary>
        /// Every command type maps to a defined category - a guard for future additions to
        /// the CommandType enum.
        /// </summary>
        [TestMethod]
        public void EveryCommandTypeHasACategoryTest()
        {
            foreach (CommandType type in Enum.GetValues<CommandType>())
            {
                CommandCategory category = CommandCategories.GetCategory(type);
                Assert.IsTrue(Enum.IsDefined(category), $"{type} maps to undefined category {category}");
            }
        }

        [TestMethod]
        [DataRow(CommandType.IssueMove, CommandCategory.Move)]
        [DataRow(CommandType.IssueFormAggressiveMove, CommandCategory.Aggressive)]
        [DataRow(CommandType.IssueBuildFactory, CommandCategory.Build)]
        [DataRow(CommandType.IssueSiloBuildNuke, CommandCategory.Launch)]
        [DataRow(CommandType.IssueSacrifice, CommandCategory.Reclaim)]
        [DataRow(CommandType.BuildAssist, CommandCategory.Repair)]
        [DataRow(CommandType.DOCK, CommandCategory.Transport)]
        [DataRow(CommandType.IssueKillSelf, CommandCategory.Stop)]
        [DataRow(CommandType.None, CommandCategory.Special)]
        public void GetCategoryTest(CommandType type, CommandCategory expected)
        {
            Assert.AreEqual(expected, CommandCategories.GetCategory(type));
        }
    }
}
