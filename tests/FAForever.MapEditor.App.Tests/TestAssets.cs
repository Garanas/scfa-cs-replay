using System.Numerics;
using FAForever.FileFormats.Mesh;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.App.Tests
{
    /// <summary>
    /// A workspace's assets folder with a few of each kind, as the conversion writes them, and a
    /// brushes folder beside it, for the asset shelf.
    /// </summary>
    internal static class TestAssets
    {
        /// <summary>
        /// A PNG of one grey pixel.
        /// </summary>
        private static readonly byte[] Pixel = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAAAAAA6fptVAAAACklEQVR4nGNgAAAAAgABSK+kcQAAAABJRU5ErkJggg==");

        private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "assets-shelf");

        public static AssetCatalog Catalog()
        {
            string assets = Folder();
            return AssetCatalog.Load(assets, Path.Combine(assets, "..", Path.GetFileName(assets) + "-brushes"));
        }

        /// <summary>
        /// The assets without brushes.
        /// </summary>
        public static AssetCatalog WithoutBrushes() => AssetCatalog.Load(Folder());

        public static string Folder()
        {
            string assets = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            void Write(string path, string? text = null)
            {
                string file = Path.Combine(assets, path);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                if (text is null)
                {
                    File.WriteAllBytes(file, Pixel);
                }
                else
                {
                    File.WriteAllText(file, text);
                }
            }
            const string tree = """{ "id": "x", "size": { "x": 1, "y": 9, "z": 1 }, "reclaimEnergy": 30 }""";
            Write("env/evergreen/props/trees/pine06_prop.json", tree);
            Write("env/evergreen/props/trees/pine06_s2_prop.json", tree);
            Write("env/evergreen/props/trees/pine06_v1_prop.json", tree);
            Write("env/evergreen/props/trees/oak01_prop.json", tree);
            Write("env/evergreen/props/rocks/rock01_prop.json", """
                { "id": "x", "reclaimMass": 20, "lods": [
                    { "mesh": "/env/evergreen/props/rocks/rock01_lod0.glb", "shader": "NormalMappedTerrain", "albedo": "/env/evergreen/props/rocks/rock01_albedo.png" } ] }
                """);
            Write("env/evergreen/props/rocks/rock01_albedo.png");
            File.WriteAllBytes(Path.Combine(assets, "env/evergreen/props/rocks/rock01_lod0.glb"), GlbWriter.Write(Pyramid()));
            Write("env/tundra/props/rocks/tund_rock01_prop.json", """{ "id": "x", "reclaimMass": 20 }""");
            Write("env/evergreen/decals/e_mud001_albedo.png");
            Write("env/evergreen/decals/e_mud002_albedo.png");
            Write("env/tundra/layers/tund_rock_albedo.png");
            Write("env/tundra/layers/tund_rock_normal.png");
            Write("env/evergreen/layers/eg_grass001_albedo.png");
            Write("units/uec1101/uec1101_unit.json", """{ "id": "UEC1101", "name": "UEF Residential Building", "faction": "Uef", "techLevel": 1, "categories": ["UEF", "STRUCTURE", "CIVILIAN"] }""");
            Write("units/uel0101/uel0101_unit.json", """{ "id": "UEL0101", "name": "Snoop", "faction": "Uef", "techLevel": 1, "categories": ["UEF", "LAND"] }""");
            Write($"../{Path.GetFileName(assets)}-brushes/craters/crater01.png");
            Write($"../{Path.GetFileName(assets)}-brushes/ridge.png");
            return assets;
        }

        /// <summary>
        /// A rock's mesh: a pyramid of four sides on a square of 2 by 2, 1 high.
        /// </summary>
        private static Scm Pyramid()
        {
            Vector3[] corners = [new(-1, 0, -1), new(1, 0, -1), new(1, 0, 1), new(-1, 0, 1), new(0, 1, 0)];
            return new Scm
            {
                Bones = [],
                Vertices = [.. corners.Select(corner => new ScmVertex(corner, Vector3.Normalize(corner + Vector3.UnitY), Vector3.UnitX, Vector3.UnitZ, new Vector2(corner.X, corner.Z), Vector2.Zero, 0))],
                Indices = [0, 4, 1, 1, 4, 2, 2, 4, 3, 3, 4, 0],
                Information = [],
            };
        }
    }
}
