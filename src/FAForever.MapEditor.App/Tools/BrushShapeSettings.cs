using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Workspaces;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// A shape a brush can have: round, or a picture of the workspace's brushes folder.
/// </summary>
/// <param name="Key">The brush's key in the asset catalogue; null for round.</param>
/// <param name="Title">Its name, without its folder.</param>
/// <param name="Path">Its folder and name in the brushes folder: <c>craters/Crater01</c>.</param>
/// <param name="File">The picture; null for round.</param>
public sealed record BrushChoice(string? Key, string Title, string Path, string? File)
{
    public static BrushChoice Round { get; } = new BrushChoice(null, "Round", "Round", null);

    public override string ToString() => Title;
}

/// <summary>
/// A tool that paints with a brush whose shape can be chosen: the asset shelf's brushes become its
/// shape when one is chosen there.
/// </summary>
public interface IShapedBrushTool : IMapTool
{
    BrushShapeSettings Shape { get; }
}

/// <summary>
/// The shape of a tool's brush and how it is turned (user stories T-1, S-1): round, or one of the
/// workspace's brushes (T-2) scaled to the radius.
/// </summary>
public sealed partial class BrushShapeSettings : ObservableObject
{
    private readonly ILogger _log;
    private readonly SynchronizationContext? _ui;

    public BrushShapeSettings(MainViewModel model)
    {
        _log = model.Logger("Editor");
        _ui = SynchronizationContext.Current;
    }

    /// <summary>
    /// Round and the workspace's brushes, by folder and name.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<BrushChoice> Choices { get; private set; } = [BrushChoice.Round];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShaped), nameof(Shown))]
    public partial BrushChoice Chosen { get; set; } = BrushChoice.Round;

    /// <summary>
    /// How far the shape is turned, in degrees clockwise in the top view, from -180 to 180.
    /// </summary>
    [ObservableProperty]
    public partial double Rotation { get; set; }

    /// <summary>
    /// Whether each dab is turned at random, so that a stroke does not repeat the picture.
    /// </summary>
    [ObservableProperty]
    public partial bool RandomRotation { get; set; }

    /// <summary>
    /// Whether the brush has the shape of a picture: only then does turning it matter.
    /// </summary>
    public bool IsShaped => Chosen.File is not null;

    /// <summary>
    /// Reads the choices again from the asset shelf's catalogue; the chosen brush stays when the
    /// workspace still has it.
    /// </summary>
    public void Refresh(AssetCatalog catalog)
    {
        Choices = [BrushChoice.Round, .. catalog.Entries
            .Where(entry => entry.Kind == AssetKind.Brush)
            .OrderBy(entry => entry.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new BrushChoice(entry.Key, entry.Name, entry.Group == WorkspaceLayout.Brushes ? entry.Name : $"{entry.Group}/{entry.Name}", entry.File))];
        Chosen = Choices.FirstOrDefault(choice => choice.Key == Chosen.Key) ?? BrushChoice.Round;
    }

    /// <summary>
    /// Gives the brush the shape of the workspace's brush <paramref name="key"/>; null or
    /// <c>round</c> for round.
    /// </summary>
    /// <exception cref="ArgumentException">The workspace has no such brush.</exception>
    public void Choose(string? key)
    {
        if (key is null || key.Equals("round", StringComparison.OrdinalIgnoreCase))
        {
            Chosen = BrushChoice.Round;
            return;
        }
        string wanted = key.ToLowerInvariant();
        Chosen = Choices.FirstOrDefault(choice => choice.Key == wanted || choice.Key == $"{WorkspaceLayout.Brushes}/{wanted}")
            ?? throw new ArgumentException($"The workspace has no brush '{key}'");
    }

    /// <summary>
    /// The rotation of the next dab, in degrees.
    /// </summary>
    public float NextRotation() => RandomRotation ? Random.Shared.NextSingle() * 360 - 180 : (float)Rotation;

    /// <summary>
    /// The chosen shape, read when it is chosen and kept; null for round. A brush that cannot be
    /// read shows in the status line, and the brush stays round.
    /// </summary>
    /// <exception cref="MapProjectException">The picture cannot be read.</exception>
    public BrushShape? Read() => Chosen.File is string file ? BrushShapes.Get(file) : null;

    /// <summary>
    /// The chosen shape once it has been read, for the views to draw under the mouse; null for
    /// round and while it is being read. It never touches the disk.
    /// </summary>
    public BrushShape? Shown => Chosen.File is string file ? BrushShapes.Find(file) : null;

    /// <summary>
    /// The chosen shape for a stroke about to start, or false when its picture cannot be read: the
    /// status line says why, and the stroke does not start.
    /// </summary>
    public static (bool Read, BrushShape? Shape) TryRead(MainViewModel model, BrushShapeSettings settings)
    {
        try
        {
            return (true, settings.Read());
        }
        catch (MapProjectException exception)
        {
            settings._log.LogError(exception, "Reading the brush {File} failed: {Reason}", settings.Chosen.File, exception.Message);
            model.Status = $"The brush {settings.Chosen.Title} cannot be read: {exception.Message}";
            return (false, null);
        }
    }

    partial void OnChosenChanged(BrushChoice value)
    {
        if (value.File is not string file)
        {
            return;
        }
        // read it now, so that the first dab need not wait; the views then draw it
        _ = Task.Run(() =>
        {
            try
            {
                BrushShapes.Get(file);
                UiThread.Run(_ui, () => OnPropertyChanged(nameof(Shown)));
            }
            catch (MapProjectException exception)
            {
                _log.LogError(exception, "Reading the brush {File} failed: {Reason}", file, exception.Message);
            }
        });
    }
}

/// <summary>
/// The brush pictures read, by file and the time it was written: a changed picture is read again.
/// </summary>
internal static class BrushShapes
{
    private const int Kept = 16;
    private static readonly Lock Gate = new Lock();
    private static readonly List<(string File, DateTime Written, BrushShape Shape)> Read = [];

    /// <summary>
    /// The picture read last of a file, whatever its time; null when it has not been read.
    /// </summary>
    public static BrushShape? Find(string file)
    {
        lock (Gate)
        {
            return Read.FindLast(entry => entry.File == file).Shape;
        }
    }

    /// <exception cref="MapProjectException">The picture cannot be read.</exception>
    public static BrushShape Get(string file)
    {
        DateTime written = File.GetLastWriteTimeUtc(file);
        lock (Gate)
        {
            int found = Read.FindIndex(entry => entry.File == file && entry.Written == written);
            if (found >= 0)
            {
                return Read[found].Shape;
            }
        }
        BrushShape shape;
        using (Performance.Measure("tool.brushShape"))
        {
            shape = BrushShape.Read(file);
        }
        lock (Gate)
        {
            Read.RemoveAll(entry => entry.File == file);
            Read.Add((file, written, shape));
            if (Read.Count > Kept)
            {
                Read.RemoveAt(0);
            }
        }
        return shape;
    }
}
