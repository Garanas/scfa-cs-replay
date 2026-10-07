using System.Runtime.InteropServices;
using Microsoft.JSInterop;

namespace FAForever.Vault.Viewer.Services.Maps;

/// <summary>
/// The images of the map page (terrain views and colour grids), drawn by <c>fafMaps</c> in
/// <c>js/maps.js</c> as blob URLs at one pixel per ogrid. Each is drawn once per map and kept while
/// that map is shown; showing another map releases them.
/// </summary>
public sealed class MapImages(IJSRuntime js)
{
    private readonly Dictionary<string, Task<string>> images = [];
    private string? folder;

    /// <summary>
    /// The terrain: <c>preview</c> (the map's own preview, as the game lobby shows it), <c>relief</c>,
    /// <c>elevation</c>, <c>cliffs</c> or <c>muted</c> (a grey hillshade to draw other layers on).
    /// </summary>
    public Task<string> TerrainAsync(LoadedMap map, string mode) => mode == "preview" ? PreviewAsync(map) : Get(map, "terrain:" + mode, () =>
    {
        byte[] heights = MemoryMarshal.AsBytes(map.Scmap.Heightmap.Samples.AsSpan()).ToArray();
        float? water = map.Scmap.Water.HasWater ? map.Scmap.Water.Elevation : null;
        return js.InvokeAsync<string>("fafMaps.terrain", heights, map.Width, map.Height, map.Scmap.Heightmap.Scale, water, mode).AsTask();
    });

    /// <summary>The preview at the start of the .scmap; the relief when it is a kind of DDS the page does not read.</summary>
    private Task<string> PreviewAsync(LoadedMap map) => Get(map, "terrain:preview", async () =>
        await js.InvokeAsync<string?>("fafMaps.preview", map.Scmap.Preview) ?? await TerrainAsync(map, "relief"));

    /// <summary>
    /// A grid of colours: one value per cell, row by row, each an index into the palette of four
    /// bytes (red, green, blue, alpha) per colour.
    /// </summary>
    public Task<string> GridAsync(LoadedMap map, string key, int width, int height, Func<byte[]> values, byte[] palette)
        => Get(map, "grid:" + key, () => js.InvokeAsync<string>("fafMaps.grid", values(), width, height, palette).AsTask());

    private Task<string> Get(LoadedMap map, string key, Func<Task<string>> draw)
    {
        if (folder != map.Folder)
        {
            Release();
            folder = map.Folder;
        }

        if (!images.TryGetValue(key, out Task<string>? image) || image.IsFaulted)
        {
            image = draw();
            images[key] = image;
        }
        return image;
    }

    private void Release()
    {
        string[] urls = [.. images.Values.Where(image => image.IsCompletedSuccessfully).Select(image => image.Result)];
        images.Clear();
        if (urls.Length > 0)
        {
            _ = js.InvokeVoidAsync("fafMaps.release", (object)urls).AsTask();
        }
    }
}
