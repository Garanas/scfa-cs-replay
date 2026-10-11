namespace FAForever.MapEditor.Project.Workspaces
{
    /// <summary>
    /// The user's folders the editor keeps its files in, on every system. .NET gives an empty path
    /// for a folder the system does not know (Linux without a Documents folder, or without
    /// <c>HOME</c>); each falls back to the home folder, so no path is relative to wherever the editor
    /// was started.
    /// </summary>
    public static class UserFolders
    {
        /// <summary>
        /// The home folder (<c>C:\Users\name</c>, <c>/home/name</c>); the current folder when there is none.
        /// </summary>
        public static string Home => Known(Environment.SpecialFolder.UserProfile)
            ?? (Environment.GetEnvironmentVariable("HOME") is { Length: > 0 } home ? home : Environment.CurrentDirectory);

        /// <summary>
        /// Where a new workspace goes by default: the Documents folder (<c>~/Documents</c> on Linux
        /// when it is there), else the home folder.
        /// </summary>
        public static string Documents => Known(Environment.SpecialFolder.MyDocuments) is string documents && Directory.Exists(documents)
            ? documents
            : Home;

        /// <summary>
        /// Settings that follow the user: <c>%APPDATA%</c> on Windows, <c>~/.config</c> on Linux,
        /// <c>~/Library/Application Support</c> on macOS.
        /// </summary>
        public static string Settings => Known(Environment.SpecialFolder.ApplicationData)
            ?? Path.Combine(Home, OperatingSystem.IsMacOS() ? "Library/Application Support" : ".config");

        /// <summary>
        /// Data of this computer only: <c>%LOCALAPPDATA%</c> on Windows, <c>~/.local/share</c> on
        /// Linux, <c>~/Library/Application Support</c> on macOS.
        /// </summary>
        public static string LocalData => Known(Environment.SpecialFolder.LocalApplicationData)
            ?? Path.Combine(Home, OperatingSystem.IsMacOS() ? "Library/Application Support" : ".local/share");

        private static string? Known(Environment.SpecialFolder folder) =>
            Environment.GetFolderPath(folder) is { Length: > 0 } path && Path.IsPathRooted(path) ? path : null;
    }
}
