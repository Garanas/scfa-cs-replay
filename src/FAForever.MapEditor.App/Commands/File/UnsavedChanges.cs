using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// The open map's unsaved changes before a command run without a person (automation, a Lua
/// plugin's <c>editor.run</c>) replaces the map: nobody can be asked whether to save them, so the
/// command refuses unless told to throw them away.
/// </summary>
internal static class UnsavedChanges
{
    /// <summary>
    /// Refuses, with what to do instead, when the command runs without a person, the open map has
    /// unsaved changes and <paramref name="discard"/> is not true.
    /// </summary>
    /// <param name="what">What the command does to the map, e.g. <c>open another map</c>.</param>
    /// <exception cref="InvalidOperationException">It would discard the changes.</exception>
    public static void RefuseToDiscard(MainViewModel model, IEditorDialogs dialogs, bool? discard, string what)
    {
        if (dialogs is DialogGuard { Quiet: true } && discard != true && model.Map?.Session.IsModified == true)
        {
            throw new InvalidOperationException(
                $"{model.Map.Document.Scenario.Name} has unsaved changes, which would be lost to {what}: save it first (file.save), or give discard: true to throw them away");
        }
    }
}
