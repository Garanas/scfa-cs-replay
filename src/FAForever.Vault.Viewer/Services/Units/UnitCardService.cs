namespace FAForever.Vault.Viewer.Services.Units;

/// <summary>
/// Which unit card is shown, and where: rows that show a unit call <see cref="Show"/> on hover and
/// <see cref="Hide"/> when the pointer leaves; <c>UnitCardHost</c> renders the card. Transient view
/// state (hover), so it never goes in the URL.
/// </summary>
public sealed class UnitCardService
{
    /// <summary>The unit whose card is shown, at a pointer position in viewport pixels.</summary>
    public sealed record Request(string BlueprintId, double X, double Y);

    public Request? Current { get; private set; }

    public event Action? Changed;

    public void Show(string blueprintId, double x, double y)
    {
        Current = new Request(blueprintId, x, y);
        Changed?.Invoke();
    }

    public void Hide()
    {
        if (Current is null)
        {
            return;
        }

        Current = null;
        Changed?.Invoke();
    }
}
