using System.IO.Compression;
using FAForever.FileFormats.Map;

namespace FAForever.FileFormats.Map.Tests
{
    [TestClass]
    public class MapArchiveTest
    {
        private const string Folder = "theta_passage_-_faf_version.v0001";

        /// <summary>
        /// An archive like the vault's: the map folder with its files, a variant in a subfolder and a
        /// large file the reader should not need.
        /// </summary>
        private static byte[] BuildArchive()
        {
            using MemoryStream stream = new MemoryStream();
            using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                void Add(string name, byte[] content, CompressionLevel level = CompressionLevel.Optimal)
                {
                    using Stream entry = zip.CreateEntry(name, level).Open();
                    entry.Write(content);
                }

                Add($"{Folder}/flop_and_rotate_90/theta_passage_-_faf_version_scenario.lua", "version = 3"u8.ToArray());
                Add($"{Folder}/env/huge_texture.dds", new byte[300_000]);
                foreach (string suffix in new[] { ".scmap", "_save.lua", "_scenario.lua" })
                {
                    Add($"{Folder}/theta_passage_-_faf_version{suffix}", File.ReadAllBytes(ScmapParserTest.ThetaPassage + suffix));
                }
                Add($"{Folder}/stored.txt", "kept as is"u8.ToArray(), CompressionLevel.NoCompression);
            }
            return stream.ToArray();
        }

        /// <summary>Reads files the way the Viewer does: the tail first, then only the entries it wants.</summary>
        private static (byte[] Archive, IReadOnlyList<MapArchiveEntry> Entries) ReadDirectory()
        {
            byte[] archive = BuildArchive();
            int tailStart = Math.Max(0, archive.Length - MapArchive.TailLength);
            (long offset, long length) = MapArchive.FindDirectory(archive.AsSpan(tailStart), tailStart);
            return (archive, MapArchive.ReadDirectory(archive.AsSpan((int)offset, (int)length)));
        }

        private static byte[] Read(byte[] archive, MapArchiveEntry entry)
        {
            int length = (int)Math.Min(entry.SpanLength(), archive.Length - entry.LocalHeaderOffset);
            return MapArchive.ReadEntry(archive.AsSpan((int)entry.LocalHeaderOffset, length), entry);
        }

        [TestMethod]
        public void ListsTheFiles()
        {
            (_, IReadOnlyList<MapArchiveEntry> entries) = ReadDirectory();

            Assert.AreEqual(6, entries.Count);
            MapArchiveEntry texture = entries.Single(entry => entry.Name.EndsWith(".dds", StringComparison.Ordinal));
            Assert.AreEqual(300_000, texture.UncompressedSize);
        }

        [TestMethod]
        public void FindsTheMapFilesAndUnpacksThem()
        {
            (byte[] archive, IReadOnlyList<MapArchiveEntry> entries) = ReadDirectory();

            MapArchiveEntry? scenarioEntry = MapArchive.FindScenario(entries);
            Assert.AreEqual($"{Folder}/theta_passage_-_faf_version_scenario.lua", scenarioEntry?.Name, "the scenario at the top, not the variant");

            MapScenario scenario = MapScenarioParser.Parse(System.Text.Encoding.UTF8.GetString(Read(archive, scenarioEntry!)));
            MapArchiveEntry? scmap = MapArchive.FindByGamePath(entries, scenario.Map!, scenarioEntry!);
            MapArchiveEntry? save = MapArchive.FindByGamePath(entries, scenario.Save!, scenarioEntry!);

            CollectionAssert.AreEqual(File.ReadAllBytes(ScmapParserTest.ThetaPassage + ".scmap"), Read(archive, scmap!));
            CollectionAssert.AreEqual(File.ReadAllBytes(ScmapParserTest.ThetaPassage + "_save.lua"), Read(archive, save!));
        }

        [TestMethod]
        public void UnpacksStoredFiles()
        {
            (byte[] archive, IReadOnlyList<MapArchiveEntry> entries) = ReadDirectory();

            MapArchiveEntry stored = entries.Single(entry => entry.Name.EndsWith("stored.txt", StringComparison.Ordinal));

            Assert.AreEqual(0, stored.Method);
            Assert.AreEqual("kept as is", System.Text.Encoding.UTF8.GetString(Read(archive, stored)));
        }

        [TestMethod]
        public void FindsAFileOfARenamedFolder()
        {
            (_, IReadOnlyList<MapArchiveEntry> entries) = ReadDirectory();
            MapArchiveEntry scenario = MapArchive.FindScenario(entries)!;

            MapArchiveEntry? save = MapArchive.FindByGamePath(entries, "/maps/old_name.v0001/THETA_PASSAGE_-_FAF_VERSION_save.lua", scenario);

            Assert.AreEqual($"{Folder}/theta_passage_-_faf_version_save.lua", save?.Name);
        }

        [TestMethod]
        public void RejectsOtherFiles()
        {
            Assert.ThrowsException<FormatException>(() => MapArchive.FindDirectory(new byte[100], 0));
        }
    }
}
