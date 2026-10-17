using FAForever.Vault.Viewer.Services.Api;

namespace FAForever.Vault.Viewer.Features.Players.Profile;

/// <summary>The outcome of one load: the value, or the sentence to show when it failed.</summary>
public sealed class Loadable<T>
{
    /// <summary>The value; default when the load failed.</summary>
    public T? Value { get; private init; }

    /// <summary>What to tell the viewer when the load failed; null when it worked.</summary>
    public string? Error { get; private init; }

    /// <summary>
    /// Runs the load. A cancelled load throws <see cref="OperationCanceledException"/> (the caller
    /// has moved on); any other failure becomes <see cref="Error"/>.
    /// </summary>
    public static async Task<Loadable<T>> RunAsync(Func<Task<T>> load)
    {
        try
        {
            return new Loadable<T> { Value = await load() };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NotAuthenticatedException)
        {
            return new Loadable<T> { Error = "Your session has expired. Sign in again to see this." };
        }
        catch (Exception exception)
        {
            return new Loadable<T> { Error = $"This could not be loaded from the FAForever API: {exception.Message}" };
        }
    }
}
