using System.IO.Compression;
using System.Numerics;
using System.Text;
using FAForever.FileFormats.Mesh;
using FAForever.MapEditor.Project.Files;

namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// A small stand-in for the game's installation and FAF's data: archives with a prop, a unit
    /// and their meshes and textures, made on the spot (the game's own files are not ours to copy).
    /// </summary>
    internal sealed class GameFiles
    {
        public const string PropBlueprint = """
            PropBlueprint {
                Description = '<LOC pine_desc>Pine tree',
                Categories = { 'RECLAIMABLE', 'OBSTRUCTSBUILDING' },
                Defense = { Health = 50, MaxHealth = 50 },
                Display = {
                    Mesh = { LODs = { { LODCutoff = 100, ShaderName = 'TMeshAlpha' }, { LODCutoff = 300, MeshName = 'pine_far.scm', AlbedoName = 'missing_albedo.dds', NormalsName = './../Shared_normalsTS.dds' } } },
                    UniformScale = 0.5,
                },
                Economy = { ReclaimEnergyMax = 10, ReclaimMassMax = 0, ReclaimTime = 5 },
                SizeX = 1, SizeY = 4, SizeZ = 1,
            }
            """;

        public const string UnitBlueprint = """
            UnitBlueprint {
                Description = '<LOC uel0001_desc>Armored Command Unit',
                Categories = { 'UEF', 'COMMAND', 'LAND' },
                Defense = { Health = 12000, MaxHealth = 12000 },
                Display = { Mesh = { LODs = { { LODCutoff = 215, ShaderName = 'Unit' } } }, UniformScale = 0.05 },
                Economy = { BuildCostEnergy = 5000000, BuildCostMass = 18000 },
                Footprint = { SizeX = 1, SizeZ = 1 },
                General = { FactionName = 'UEF' },
                StrategicIconName = 'icon_commander_generic',
                Wreckage = { MassMult = 0.9 },
                SizeX = 0.6, SizeY = 1.5, SizeZ = 0.6,
            }
            """;

        public GameFiles(string root)
        {
            GameFolder = Path.Combine(root, "game");
            FafDataFolder = Path.Combine(root, "faf", "gamedata");
            VaultFolder = Path.Combine(root, "vault");

            WriteArchive(Path.Combine(GameFolder, "gamedata", "env.scd"), new Dictionary<string, byte[]>
            {
                ["env/Evergreen/props/Pine_prop.bp"] = Encoding.UTF8.GetBytes(PropBlueprint),
                ["env/Evergreen/props/Pine_lod0.scm"] = Mesh("game lod0"),
                ["env/Evergreen/props/pine_far.scm"] = Mesh("game far"),
                ["env/Evergreen/props/Pine_albedo.dds"] = Texture(0x10),
                ["env/Evergreen/Shared_normalsTS.dds"] = Texture(0x50),
                ["env/Evergreen/layers/Eg_Grass001_albedo.dds"] = Texture(0x20),
                ["env/Evergreen/props/Broken_prop.bp"] = Encoding.UTF8.GetBytes("PropBlueprint { Description = "),
                ["env/Evergreen/props/Broken_lod0.scm"] = Encoding.UTF8.GetBytes("not a mesh"),
            });
            WriteArchive(Path.Combine(GameFolder, "gamedata", "units.scd"), new Dictionary<string, byte[]>
            {
                ["units/UEL0001/UEL0001_unit.bp"] = Encoding.UTF8.GetBytes(UnitBlueprint),
                ["units/UEL0001/UEL0001_lod0.scm"] = Mesh("unit lod0"),
                ["units/UEL0001/UEL0001_Albedo.dds"] = Texture(0x30),
                ["units/UEL0001/UEL0001_NormalsTS.dds"] = Texture(0x40),
                ["units/UEL0001/UEL0001_script.lua"] = Encoding.UTF8.GetBytes("-- not converted"),
            });
            WriteFafEnv(withExtraFile: false);
            Directory.CreateDirectory(VaultFolder);
        }

        public string GameFolder { get; }

        public string FafDataFolder { get; }

        public string VaultFolder { get; }

        public string FafEnv => Path.Combine(FafDataFolder, "env.nx2");

        /// <summary>
        /// FAF's <c>env.nx2</c>, which replaces the pine's albedo; a release that changes it adds a file.
        /// </summary>
        public void WriteFafEnv(bool withExtraFile)
        {
            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>
            {
                ["env/evergreen/props/pine_albedo.dds"] = Texture(0x80),
            };
            if (withExtraFile)
            {
                files["env/evergreen/props/new_lod0.scm"] = Mesh("new");
            }
            WriteArchive(FafEnv, files);
        }

        /// <summary>
        /// A mesh of one triangle, with one bone that is named to tell the meshes apart.
        /// </summary>
        public static byte[] Mesh(string bone) => ScmWriter.Write(new Scm
        {
            Bones = [new ScmBone(bone, Matrix4x4.Identity, Vector3.Zero, Quaternion.Identity, -1)],
            Vertices =
            [
                new ScmVertex(Vector3.Zero, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, Vector2.Zero, Vector2.Zero, 0),
                new ScmVertex(Vector3.UnitX, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, Vector2.UnitX, Vector2.Zero, 0),
                new ScmVertex(Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, Vector2.UnitY, Vector2.Zero, 0),
            ],
            Indices = [0, 2, 1],
            Information = [],
        });

        /// <summary>
        /// A 4 by 4 texture of one colour, half transparent.
        /// </summary>
        public static byte[] Texture(byte red)
        {
            byte[] rgba = new byte[4 * 4 * 4];
            for (int i = 0; i < rgba.Length; i += 4)
            {
                rgba[i] = red;
                rgba[i + 3] = 0x80;
            }
            return DdsTexture.Write(4, 4, TextureFormat.A8R8G8B8, rgba);
        }

        private static void WriteArchive(string path, Dictionary<string, byte[]> files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using FileStream stream = File.Create(path);
            using ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create);
            foreach ((string name, byte[] bytes) in files)
            {
                using Stream entry = zip.CreateEntry(name).Open();
                entry.Write(bytes);
            }
        }
    }
}
