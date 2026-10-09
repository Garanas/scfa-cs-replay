namespace FAForever.MapEditor.Project.Tests
{
    /// <summary>
    /// The test maps and fresh folders to unpack and pack them in, next to the test assembly.
    /// </summary>
    internal static class ProjectFolders
    {
        public const string ThetaPassage = "assets/maps/theta_passage_-_faf_version.v0001";
        public const string HardFfa = "assets/maps/HardFFA.v0001";
        public const string CoopR01 = "assets/maps/SCCA_Coop_R01.v0020";

        /// <summary>
        /// A path to a folder that does not exist yet; <see cref="Clean"/> removes them all.
        /// </summary>
        public static string New(string name) => Path.Combine(Root, $"{name}-{Guid.NewGuid():N}");

        public static void Clean()
        {
            if (Directory.Exists(Root))
            {
                // git's objects are read-only, which stops a delete on Windows
                foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(Root, recursive: true);
            }
        }

        private static string Root => Path.Combine(AppContext.BaseDirectory, "projects");
    }
}
