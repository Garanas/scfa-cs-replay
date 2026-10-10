using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using FAForever.FileFormats.Mesh;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Rendering;

/// <summary>
/// The models of the blueprints the map places (props and units), read from the workspace's
/// converted assets: its levels of detail (<c>&lt;name&gt;_lod0.glb</c>, <c>_lod1.glb</c>, ...) with the
/// distances they show up to, scaled by the blueprint's <c>uniformScale</c>, each with its albedo and
/// the alpha its shader cuts out below. A model is read on a
/// thread of the pool the first time it is asked for; until then, and for a blueprint the
/// workspace does not have, there is none. Asking never touches the disk.
/// </summary>
/// <param name="loaded">Called (on a thread of the pool) when a model has been read: draw again.</param>
internal sealed class ObjectModels(Action loaded)
{
    /// <summary>
    /// A model ready to go on the graphics card.
    /// </summary>
    /// <param name="Lods">Its levels of detail, the most detailed first: at least one.</param>
    /// <param name="Bounds">The box around the first level.</param>
    public sealed record Model(string Key, ImmutableArray<Lod> Lods, ObjectBounds Bounds)
    {
        /// <summary>
        /// The level that shows at a distance from the camera: the first whose cutoff reaches it,
        /// and the last beyond them all (the game hides a prop there; the editor keeps it in sight).
        /// </summary>
        public int LodAt(float distance)
        {
            for (int i = 0; i < Lods.Length - 1; i++)
            {
                if (distance <= Lods[i].Cutoff)
                {
                    return i;
                }
            }
            return Lods.Length - 1;
        }
    }

    /// <summary>
    /// A level of detail of a model.
    /// </summary>
    /// <param name="Cutoff">Up to how far from the camera it shows, in ogrids.</param>
    /// <param name="Vertices">Eight floats a vertex: position (in ogrids), normal, texture coordinates.</param>
    /// <param name="Indices">Three a triangle.</param>
    /// <param name="Albedo">Its albedo texture; null for none.</param>
    /// <param name="AlphaTest">The albedo's alpha below which a pixel is cut out; null for none.</param>
    /// <param name="Shader">How the game shades it, which says how its albedo reads.</param>
    public sealed record Lod(float Cutoff, float[] Vertices, ushort[] Indices, TextureSource? Albedo, float? AlphaTest, PreviewShader Shader);

    private sealed class Entry
    {
        public volatile Model? Model;
        public volatile bool Missing;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new ConcurrentDictionary<string, Entry>(StringComparer.Ordinal);
    private AssetCatalog? _catalog;
    private int _loading;

    /// <summary>
    /// The models read.
    /// </summary>
    public int Loaded => _entries.Values.Count(entry => entry.Model is not null);

    /// <summary>
    /// The blueprints the workspace has no model for (or whose model could not be read).
    /// </summary>
    public int Missing => _entries.Values.Count(entry => entry.Missing);

    /// <summary>
    /// The models being read now.
    /// </summary>
    public int Loading => _loading;

    /// <summary>
    /// The last model that could not be read, with why.
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// The model of a blueprint (a prop's path or a unit's id) in <paramref name="catalog"/>; null
    /// while it is being read, and for one the workspace does not have (<paramref name="missing"/>).
    /// Another catalog (the assets read again) reads the models again.
    /// </summary>
    public Model? Get(string blueprint, AssetCatalog catalog, out bool missing)
    {
        if (!ReferenceEquals(catalog, _catalog))
        {
            _entries.Clear();
            _catalog = catalog;
        }
        string key = AssetCatalog.KeyOf(blueprint);
        if (!_entries.TryGetValue(key, out Entry? entry))
        {
            entry = new Entry();
            _entries[key] = entry;
            Interlocked.Increment(ref _loading);
            _ = Task.Run(() =>
            {
                try
                {
                    entry.Model = Read(key, catalog);
                    entry.Missing = entry.Model is null;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or GlbFormatException or FormatException or JsonException or InvalidOperationException)
                {
                    entry.Missing = true;
                    LastError = $"{key}: {exception.Message}";
                }
                finally
                {
                    Interlocked.Decrement(ref _loading);
                }
                loaded();
            });
        }
        missing = entry.Missing;
        return entry.Model;
    }

    /// <summary>
    /// The bounds of a blueprint's model, or <see cref="ObjectBounds.Box"/> while there is none.
    /// </summary>
    public ObjectBounds BoundsOf(string blueprint) =>
        _entries.TryGetValue(AssetCatalog.KeyOf(blueprint), out Entry? entry) && entry.Model is Model model ? model.Bounds : ObjectBounds.Box;

    private static Model? Read(string key, AssetCatalog catalog)
    {
        using Performance.Span measure = Performance.Measure("3d.read-model");
        if (catalog.Find(key) is not { Model: AssetModel first } entry || !File.Exists(first.Mesh))
        {
            return null;
        }
        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(entry.File));
        JsonElement blueprint = json.RootElement;
        float scale = blueprint.TryGetProperty("uniformScale", out JsonElement uniform) && uniform.ValueKind == JsonValueKind.Number && uniform.GetSingle() > 0
            ? uniform.GetSingle()
            : 1;

        // the levels' paths are in the assets folder, where the catalog found the first one
        List<(float Cutoff, string Mesh, string? Albedo, string? Shader)> levels = [];
        if (blueprint.TryGetProperty("lods", out JsonElement lods) && lods.ValueKind == JsonValueKind.Array)
        {
            string? root = null;
            foreach (JsonElement lod in lods.EnumerateArray())
            {
                string? mesh = Text(lod, "mesh");
                if (mesh is null)
                {
                    continue;
                }
                root ??= RootOf(first.Mesh, mesh);
                if (root is null)
                {
                    break;
                }
                float cutoff = lod.TryGetProperty("cutoff", out JsonElement c) && c.ValueKind == JsonValueKind.Number ? c.GetSingle() : float.PositiveInfinity;
                levels.Add((cutoff, InAssets(root, mesh), Text(lod, "albedo") is string albedo ? InAssets(root, albedo) : null, Text(lod, "shader")));
            }
        }
        if (levels.Count == 0)
        {
            levels.Add((float.PositiveInfinity, first.Mesh, first.Albedo, first.Shader));
        }

        List<Lod> read = [];
        ObjectBounds bounds = default;
        foreach ((float cutoff, string meshFile, string? albedoFile, string? shaderName) in levels)
        {
            if (!File.Exists(meshFile))
            {
                // a level that is not there: the one before it shows further
                continue;
            }
            Scm mesh = GlbReader.Read(File.ReadAllBytes(meshFile));
            if (mesh.Vertices.Count == 0 || mesh.Indices.Length == 0)
            {
                continue;
            }
            (float[] vertices, ObjectBounds box) = VerticesOf(mesh, scale);
            if (read.Count == 0)
            {
                bounds = box;
            }
            TextureSource? albedo = albedoFile is not null && File.Exists(albedoFile)
                ? new TextureSource(albedoFile.ToLowerInvariant(), File.GetLastWriteTimeUtc(albedoFile), () => GameTextures.Decode(File.ReadAllBytes(albedoFile)))
                : null;
            (PreviewShader shader, float? alphaTest) = MeshPreview.ShaderOf(shaderName);
            read.Add(new Lod(cutoff, vertices, mesh.Indices, albedo, alphaTest, shader));
        }
        return read.Count == 0 ? null : new Model(key, [.. read], bounds);
    }

    /// <summary>
    /// Eight floats a vertex (position scaled to ogrids, normal, texture coordinates), and the box around them.
    /// </summary>
    private static (float[] Vertices, ObjectBounds Bounds) VerticesOf(Scm mesh, float scale)
    {
        float[] vertices = new float[mesh.Vertices.Count * 8];
        Vector3 min = new Vector3(float.PositiveInfinity);
        Vector3 max = new Vector3(float.NegativeInfinity);
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            ScmVertex vertex = mesh.Vertices[i];
            Vector3 position = vertex.Position * scale;
            Vector3 normal = vertex.Normal.LengthSquared() > 0 ? Vector3.Normalize(vertex.Normal) : Vector3.UnitY;
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
            int at = i * 8;
            vertices[at] = position.X;
            vertices[at + 1] = position.Y;
            vertices[at + 2] = position.Z;
            vertices[at + 3] = normal.X;
            vertices[at + 4] = normal.Y;
            vertices[at + 5] = normal.Z;
            vertices[at + 6] = vertex.Uv0.X;
            vertices[at + 7] = vertex.Uv0.Y;
        }
        return (vertices, new ObjectBounds(min, max));
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// The assets folder: the file of the first level without the path the blueprint names it by.
    /// </summary>
    private static string? RootOf(string file, string gamePath)
    {
        string relative = gamePath.TrimStart('/').Replace('\\', '/');
        string normalized = file.Replace('\\', '/');
        return normalized.EndsWith(relative, StringComparison.OrdinalIgnoreCase) ? file[..^relative.Length] : null;
    }

    private static string InAssets(string root, string gamePath) =>
        Path.Combine(root, gamePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
}
