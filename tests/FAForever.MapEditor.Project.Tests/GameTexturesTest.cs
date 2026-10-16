using System.Collections.Immutable;
using FAForever.MapEditor.Project.Files.InMemory;
using FAForever.MapEditor.Project.Workspaces;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public sealed class GameTexturesTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static MapDocument Map() => EditedMap.ImportMap(ProjectFolders.HardFfa).Document;

        [TestMethod]
        public void FindsTheMapsOwnFilesWhateverTheFolderIsCalled()
        {
            MapDocument document = Map() with
            {
                Files = ImmutableSortedDictionary.Create<string, ImmutableArray<byte>>(StringComparer.Ordinal)
                    .Add("env/Rock.dds", [.. GameFiles.Texture(0x40)]),
            };

            TextureSource source = GameTextures.Find("/maps/hardffa.v0003/ENV/rock.dds", document, null)!;
            DecodedTexture texture = source.Read();

            Assert.AreEqual("map:env/rock.dds", source.Key);
            Assert.AreEqual((4, 4), (texture.Width, texture.Height));
            CollectionAssert.AreEqual(new byte[] { 0x40, 0, 0, 0x80 }, texture.Rgba[..4]);
            Assert.IsNull(GameTextures.Find("/maps/hardffa.v0003/env/missing.dds", document, null));
        }

        [TestMethod]
        public void FindsTheGamesTexturesInTheWorkspace()
        {
            Workspace workspace = Workspace.Create(ProjectFolders.New("workspace"), "C:/game", null, "D:/vault");
            string png = Path.Combine(workspace.AssetsFolder, "env", "evergreen", "layers", "grass.png");
            Directory.CreateDirectory(Path.GetDirectoryName(png)!);
            ProjectImages.WriteRgba(png, 2, 2, [1, 2, 3, 255, 1, 2, 3, 255, 1, 2, 3, 255, 1, 2, 3, 255]);

            TextureSource source = GameTextures.Find("/env/Evergreen/layers/Grass.dds", Map(), workspace)!;

            Assert.AreEqual("env/evergreen/layers/grass.dds", source.Key);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 255 }, source.Read().Rgba[..4]);

            File.SetLastWriteTimeUtc(png, new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            Assert.AreNotEqual(source.Version, GameTextures.Find("/env/Evergreen/layers/Grass.dds", Map(), workspace)!.Version,
                "a tool that saves the texture gives it another version, which the view loads again");
            Assert.IsNull(GameTextures.Find("/env/Evergreen/layers/Grass.dds", Map(), null), "no workspace, no game textures");
        }
    }
}
