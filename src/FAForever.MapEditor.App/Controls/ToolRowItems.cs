using System.Collections;
using Avalonia;
using Avalonia.Controls;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// Settings made from a list for a <see cref="ToolRow"/>, each a setting of the row of its own (a
/// <see cref="ContentControl"/> showing the item through the row's data templates), so the row puts
/// behind More only those that do not fit rather than all of them as one: a Lua plugin's tool's
/// fields (user story F-15). They come first, before the row's own children.
/// </summary>
public static class ToolRowItems
{
    public static readonly AttachedProperty<IEnumerable?> SourceProperty =
        AvaloniaProperty.RegisterAttached<ToolRow, IEnumerable?>("Source", typeof(ToolRowItems));

    private const string Made = "toolrowitem";

    static ToolRowItems()
    {
        SourceProperty.Changed.AddClassHandler<ToolRow>((row, e) => Fill(row, e.NewValue as IEnumerable));
    }

    public static IEnumerable? GetSource(ToolRow row) => row.GetValue(SourceProperty);

    public static void SetSource(ToolRow row, IEnumerable? value) => row.SetValue(SourceProperty, value);

    private static void Fill(ToolRow row, IEnumerable? items)
    {
        foreach (Control old in row.Children.Where(child => child.Classes.Contains(Made)).ToList())
        {
            row.Children.Remove(old);
        }
        int at = 0;
        foreach (object? item in items ?? Array.Empty<object>())
        {
            ContentControl setting = new ContentControl { Content = item };
            setting.Classes.Add(Made);
            row.Children.Insert(at++, setting);
        }
    }
}
