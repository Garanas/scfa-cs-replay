using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The preset files a group's "Save back to the preset" overwrote (user story U-9). The step of undo
/// holds the map's side of the save (the instance's link to the file); this keeps the file's side:
/// undo of the step writes the file as it was, redo writes what the save wrote.
/// </summary>
public sealed class SavedBackPresets(ILogger log)
{
    private sealed record Save(string Army, IReadOnlyList<string> Path, string Preset, string Before, string After, byte[]? FileBefore, byte[] FileAfter);

    private readonly List<Save> _saves = [];

    /// <summary>
    /// How many saves are kept.
    /// </summary>
    public int Count => _saves.Count;

    /// <summary>
    /// Notes a save: the instance's fingerprint and the preset file's bytes before and after.
    /// </summary>
    public void Note(string army, IReadOnlyList<string> path, string preset, string before, string after, byte[]? fileBefore, byte[] fileAfter) =>
        _saves.Add(new Save(army, [.. path], preset, before, after, fileBefore, fileAfter));

    /// <summary>
    /// Forgets the saves of a map that closed.
    /// </summary>
    public void Clear() => _saves.Clear();

    /// <summary>
    /// A step of undo or redo happened: for each save whose instance went from one link to the other,
    /// the preset file gets the content of that side.
    /// </summary>
    /// <returns>Whether a file was written.</returns>
    public bool Follow(DocumentChange change, PresetLibrary? library)
    {
        if (library is null || _saves.Count == 0 || change.Reason is not (ChangeReason.Undo or ChangeReason.Redo))
        {
            return false;
        }
        bool written = false;
        foreach (Save save in _saves)
        {
            string? was = change.Previous.InstanceOf(save.Army, save.Path)?.Instance.Fingerprint;
            string? now = change.Current.InstanceOf(save.Army, save.Path)?.Instance.Fingerprint;
            if (was == now || was is null || now is null)
            {
                continue;
            }
            bool undone = was == save.After && now == save.Before;
            if (!undone && !(was == save.Before && now == save.After))
            {
                continue;
            }
            try
            {
                string file = library.FileOf(UnitPresets.Kind, save.Preset);
                if (undone && save.FileBefore is null)
                {
                    // the save made the file
                    File.Delete(file);
                }
                else
                {
                    File.WriteAllBytes(file, undone ? save.FileBefore! : save.FileAfter);
                }
                written = true;
                log.LogInformation("The preset {Preset} is as it was {When} the save back of {Army}", save.Preset, now == save.Before ? "before" : "after", save.Army);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                log.LogError(exception, "The preset {Preset} could not be put back by undo or redo of a save back: {Reason}", save.Preset, exception.Message);
            }
        }
        return written;
    }
}
