namespace FAForever.Vault.Viewer.Features.Players.Charts;

/// <summary>The box of a text label in plot pixels.</summary>
public readonly record struct LabelBox(double X, double Y, double Width, double Height);

/// <summary>Estimates the room labels take, so the charts can keep two labels apart.</summary>
public static class LabelLayout
{
    /// <summary>The width of a label, estimated from its length and the width of an average character.</summary>
    public static double Width(string text, double perCharacter) => text.Length * perCharacter;

    /// <summary>True when the boxes overlap, with a margin of 2 px around each.</summary>
    public static bool Overlap(LabelBox a, LabelBox b)
        => a.X < b.X + b.Width + 2 && b.X < a.X + a.Width + 2 && a.Y < b.Y + b.Height + 2 && b.Y < a.Y + a.Height + 2;
}
