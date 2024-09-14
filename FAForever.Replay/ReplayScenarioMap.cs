
namespace FAForever.Replay
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="Name">The name of the map.</param>
    /// <param name="Description">The description of the map.</param>
    /// <param name="SCMapReference">The (local) path to the map. Note that the game mounts folders and files that may have a different starting point then a regular local path.</param>
    /// <param name="PreviewReference">The (local) path to the preview. Is optional, if not provided then the baked-in preview of the binary scmap is used instead.</param>
    /// <param name="Repository">A URL that points to a repository.</param>
    /// <param name="SizeX">The size of the map over the in-game x axis. A value of 1 corresponds to the size of a wall. A value of 8 corresponds to the size of a factory.</param>
    /// <param name="SizeZ">The size of the map over the in-game z axis. Note that the y-axis is up/down. A value of 1 corresponds to the size of a wall. A value of 8 corresponds to the size of a factory.</param>
    public record ReplayScenarioMap(string? Name, string? Description, string? SCMapReference, string? PreviewReference, string? Repository, int? Version, int? SizeX, int? SizeZ, int? MassReclaim, int? EnergyReclaim);
}
