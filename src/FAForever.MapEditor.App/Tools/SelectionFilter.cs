using System.ComponentModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using FAForever.MapEditor.App.Commands;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;

namespace FAForever.MapEditor.App.Tools;

/// <summary>
/// One kind a select tool may pick, as a check box of its options (the selection filter, user
/// story F-11): unchecked, the kind is locked (<see cref="MainViewModel.LockedKinds"/>), so clicks
/// and boxes leave it alone. A change runs <c>select.lockKind</c>.
/// </summary>
public sealed partial class SelectionFilterItem : ObservableObject
{
    private readonly MainViewModel _model;

    public SelectionFilterItem(MainViewModel model, ObjectKinds kind)
    {
        _model = model;
        Kind = kind;
        model.PropertyChanged += OnModelChanged;
    }

    public ObjectKinds Kind { get; }

    public string Title => SelectionKinds.Title(Kind);

    /// <summary>
    /// What a screen reader and automation call its check box.
    /// </summary>
    public string Label => $"Select {Title.ToLowerInvariant()}";

    /// <summary>
    /// Whether the tool picks the kind: it is not locked.
    /// </summary>
    public bool IsOn
    {
        get => !_model.LockedKinds.HasFlag(Kind);
        set
        {
            if (value != IsOn)
            {
                _ = _model.RunWithAsync("select.lockKind", new JsonObject { ["kind"] = SelectionKinds.Name(Kind), ["locked"] = !value });
            }
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.LockedKinds))
        {
            OnPropertyChanged(nameof(IsOn));
        }
    }
}
