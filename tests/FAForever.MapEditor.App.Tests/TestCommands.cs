using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// Commands the tests register themselves, so what is checked about commands not built yet does
    /// not depend on a feature of the editor that waits to be built.
    /// </summary>
    internal static class TestCommands
    {
        public const string NotBuiltId = "test.notBuilt";

        public const string NotBuiltTitle = "Something not built yet...";

        public const string NotBuiltMissing = "Do what the test asks";

        /// <summary>
        /// Registers a command not built yet (<see cref="IncompleteAttribute"/>, user story F-1) at the
        /// end of the Plugins menu, as a plugin's command lies there, and has the menus made again.
        /// </summary>
        public static EditorCommand RegisterNotBuilt(MainViewModel model)
        {
            EditorCommand command = new EditorCommand
            {
                Id = NotBuiltId,
                Title = NotBuiltTitle,
                Story = "F-1",
                Owner = "tests",
                Incomplete = new IncompleteAttribute("F-1", NotBuiltMissing),
            };
            model.Commands.Register(command);
            // into the Plugins menu, then registered again so the window makes its menus with it there
            model.Commands.Arrange([new MenuLayout(Menus.PluginsMenu, [NotBuiltId])]);
            model.Commands.Unregister(NotBuiltId);
            model.Commands.Register(command);
            return command;
        }
    }
}
