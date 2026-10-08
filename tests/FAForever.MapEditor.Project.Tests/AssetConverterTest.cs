using FAForever.FileFormats.Blueprints;
using FAForever.MapEditor.Project.Workspaces;
using FAForever.MapEditor.Project.Workspaces.Serialized;
using ImageMagick;

namespace FAForever.MapEditor.Project.Tests
{
    [TestClass]
    public sealed class AssetConverterTest
    {
        [ClassCleanup]
        public static void Cleanup() => ProjectFolders.Clean();

        private static (GameFiles Game, Workspace Workspace) Setup()
        {
            string root = ProjectFolders.New("game");
            GameFiles game = new GameFiles(root);
            return (game, Workspace.Create(Path.Combine(root, "workspace"), game.GameFolder, game.FafDataFolder, game.VaultFolder));
        }

        private static byte[] Rgba(MagickImage image)
        {
            using IPixelCollection<ushort> pixels = image.GetPixels();
            return pixels.ToByteArray(PixelMapping.RGBA)!;
        }

        private static T Read<T>(Workspace workspace, string asset) => ProjectJson.Read<T>(Path.Combine(workspace.AssetsFolder, asset));

        [TestMethod]
        public void ConvertsTexturesToPngWithAlpha()
        {
            (_, Workspace workspace) = Setup();
            AssetConverter.Convert(workspace);

            string png = workspace.FindAsset("/env/Evergreen/layers/Eg_Grass001_albedo.dds")!;
            Assert.AreEqual(Path.Combine(workspace.AssetsFolder, "env", "evergreen", "layers", "eg_grass001_albedo.png"), png);
            using MagickImage image = new MagickImage(File.ReadAllBytes(png));
            Assert.AreEqual(MagickFormat.Png, image.Format);
            Assert.IsTrue(image.HasAlpha);
            CollectionAssert.AreEqual(new byte[] { 0x20, 0, 0, 0x80 }, Rgba(image)[..4]);
        }

        [TestMethod]
        public void FafArchivesWinFromTheGames()
        {
            (_, Workspace workspace) = Setup();
            AssetConverter.Convert(workspace);

            using MagickImage image = new MagickImage(File.ReadAllBytes(workspace.FindAsset("/env/evergreen/props/pine_albedo.dds")!));
            Assert.AreEqual(0x80, Rgba(image)[0], "the albedo of env.nx2");
        }

        [TestMethod]
        public void ReducesPropBlueprintsAndFillsInTheirMeshes()
        {
            (_, Workspace workspace) = Setup();
            AssetConversion conversion = AssetConverter.Convert(workspace);

            PropBlueprintDto pine = Read<PropBlueprintDto>(workspace, "env/evergreen/props/pine_prop.json");
            Assert.AreEqual("/env/evergreen/props/pine_prop.bp", pine.Id);
            Assert.AreEqual("Pine tree", pine.Description);
            Assert.AreEqual(4f, pine.Size!.Y);
            Assert.AreEqual(10, pine.ReclaimEnergy);
            Assert.AreEqual(0.5, pine.UniformScale);
            Assert.AreEqual(2, pine.Lods.Count);
            Assert.AreEqual("/env/evergreen/props/pine_lod0.scm", pine.Lods[0].Mesh, "the default name of the first level");
            Assert.AreEqual("/env/evergreen/props/pine_albedo.png", pine.Lods[0].Albedo);
            Assert.AreEqual("TMeshAlpha", pine.Lods[0].Shader);
            Assert.IsNull(pine.Lods[0].Normals, "the archives have no normals");
            Assert.AreEqual("/env/evergreen/props/pine_far.scm", pine.Lods[1].Mesh);
            Assert.IsNull(pine.Lods[1].Albedo, "a texture the archives lack is left out");
            Assert.AreEqual("game far", File.ReadAllText(workspace.FindAsset("/env/evergreen/props/pine_far.scm")!));

            Assert.AreEqual(1, conversion.Skipped.Count, string.Join(Environment.NewLine, conversion.Skipped));
            StringAssert.StartsWith(conversion.Skipped[0], "env/evergreen/props/broken_prop.bp");
        }

        [TestMethod]
        public void ReducesUnitBlueprints()
        {
            (_, Workspace workspace) = Setup();
            AssetConversion conversion = AssetConverter.Convert(workspace);

            UnitBlueprintDto acu = Read<UnitBlueprintDto>(workspace, "units/uel0001/uel0001_unit.json");
            Assert.AreEqual("uel0001", acu.Id);
            Assert.AreEqual("Armored Command Unit", acu.Name);
            Assert.AreEqual(Faction.Uef, acu.Faction);
            Assert.IsNull(acu.TechLevel);
            Assert.AreEqual(18000, acu.MassCost);
            Assert.AreEqual(0.9, acu.WreckageMass);
            Assert.AreEqual(1, acu.Footprint!.SizeX);
            Assert.AreEqual("/units/uel0001/uel0001_lod0.scm", acu.Lods.Single().Mesh);
            Assert.AreEqual("/units/uel0001/uel0001_albedo.png", acu.Lods[0].Albedo);
            Assert.AreEqual("/units/uel0001/uel0001_normalsts.png", acu.Lods[0].Normals);
            Assert.IsFalse(File.Exists(Path.Combine(workspace.AssetsFolder, "units", "uel0001", "uel0001_script.lua")), "scripts are not needed");

            Assert.AreEqual(1, conversion.Manifest.Units);
            Assert.AreEqual(1, conversion.Manifest.Props);
            Assert.AreEqual(3, conversion.Manifest.Meshes, "the pine's two and the unit's");
        }

        [TestMethod]
        public void ReplacesTheAssetsOnlyWhenDone()
        {
            (_, Workspace workspace) = Setup();
            AssetConverter.Convert(workspace);
            File.WriteAllText(Path.Combine(workspace.AssetsFolder, "left-over.txt"), "from an older conversion");

            AssetConverter.Convert(workspace);

            Assert.IsFalse(File.Exists(Path.Combine(workspace.AssetsFolder, "left-over.txt")));
            Assert.IsFalse(Directory.Exists(Path.Combine(workspace.Folder, WorkspaceLayout.AssetsInProgress)));
            Assert.IsTrue(workspace.CheckAssets().IsCurrent);
        }

        [TestMethod]
        public void GoesOnWhereAStoppedConversionWas()
        {
            (_, Workspace workspace) = Setup();
            string done = Path.Combine(workspace.Folder, WorkspaceLayout.AssetsInProgress, "units", "uel0001", "uel0001_lod0.scm");
            Directory.CreateDirectory(Path.GetDirectoryName(done)!);
            File.WriteAllText(done, "converted before it stopped");
            Assert.AreEqual(AssetState.Missing, workspace.CheckAssets().State);
            StringAssert.Contains(workspace.CheckAssets().Reasons[0], "did not finish");

            AssetConverter.Convert(workspace);

            Assert.AreEqual("converted before it stopped", File.ReadAllText(workspace.FindAsset("/units/UEL0001/UEL0001_lod0.scm")!));
        }

        [TestMethod]
        public void StopsWhenCancelled()
        {
            (_, Workspace workspace) = Setup();
            using CancellationTokenSource cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            Assert.ThrowsException<OperationCanceledException>(() => AssetConverter.Convert(workspace, cancellation: cancelled.Token));
            Assert.AreEqual(AssetState.Missing, workspace.CheckAssets().State);
        }

        [TestMethod]
        public void NewFafReleaseOutdatesTheAssets()
        {
            (GameFiles game, Workspace workspace) = Setup();
            AssetConverter.Convert(workspace);
            Assert.IsTrue(workspace.CheckAssets().IsCurrent);

            game.WriteFafEnv(withExtraFile: true);

            AssetCheck check = workspace.CheckAssets();
            Assert.AreEqual(AssetState.Outdated, check.State);
            CollectionAssert.AreEqual(new[] { "FAF updated env.nx2." }, check.Reasons.ToArray());
        }

        [TestMethod]
        public void SameFafArchiveWithAnotherTimeIsCurrent()
        {
            (GameFiles game, Workspace workspace) = Setup();
            AssetConverter.Convert(workspace);

            File.SetLastWriteTimeUtc(game.FafEnv, new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            Assert.IsTrue(workspace.CheckAssets().IsCurrent, "the content is compared when only the time differs");
        }

        [TestMethod]
        public void NewEditorOutdatesTheAssets()
        {
            (_, Workspace workspace) = Setup();
            AssetConverter.Convert(workspace);
            string manifest = Path.Combine(workspace.AssetsFolder, WorkspaceLayout.Manifest);
            File.WriteAllText(manifest, File.ReadAllText(manifest).Replace($"\"editorVersion\": \"{AssetConverter.EditorVersion}\"", "\"editorVersion\": \"0.0.1\""));

            AssetCheck check = workspace.CheckAssets();

            Assert.AreEqual(AssetState.Outdated, check.State);
            StringAssert.Contains(check.Reasons.Single(), "updated from 0.0.1");
        }
    }
}
