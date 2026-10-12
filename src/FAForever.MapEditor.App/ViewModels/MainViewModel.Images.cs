using CommunityToolkit.Mvvm.ComponentModel;

namespace FAForever.MapEditor.App.ViewModels;

/// <summary>
/// The projected images (user story N-2): the list the Sketch mode's panel shows and the inspector
/// edits, and the overlay that shows or hides them in the top view (<c>Ctrl+Shift+J</c>).
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The map's projected images, the one chosen, and its fields.
    /// </summary>
    public ProjectedImagesViewModel Images { get; private set; } = null!;

    /// <summary>
    /// Whether the top view draws the projected images (the Images overlay, <c>Ctrl+Shift+J</c>); the 3D view never does.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowImages { get; set; } = true;

    partial void OnShowImagesChanged(bool value) => RefreshOverlays();

    private void InitializeImages()
    {
        Images = new ProjectedImagesViewModel(RunWithAsync);
        // another map has its own images: none of the last one's stays chosen
        AnotherMap += (_, _) => Images.Forget();
        DocumentChanged += (_, _) => Images.Refresh(ShownDocument);
        // a click on an image, or a choice in the panel, gives the inspector its fields
        Images.ChosenChanged += (_, _) =>
        {
            if (Images.HasChosen)
            {
                InspectTheMode();
            }
        };
    }
}
