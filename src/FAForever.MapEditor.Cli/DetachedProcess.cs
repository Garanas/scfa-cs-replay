using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FAForever.MapEditor.Cli
{
    /// <summary>
    /// Starts a process that outlives this one without holding on to its output: the child's
    /// standard output and error go to a log file and its input reads nothing, and none of this
    /// tool's own standard handles is passed on. A caller that reads this tool's output to its end
    /// (an agent's shell, a pipe) then sees it end when this tool exits, not when the child does.
    /// </summary>
    public static class DetachedProcess
    {
        /// <summary>
        /// Starts <paramref name="start"/> (which must not redirect anything) with its output in
        /// <paramref name="log"/>, which is made again.
        /// </summary>
        public static Process Start(ProcessStartInfo start, string log)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(log))!);
            if (!OperatingSystem.IsWindows())
            {
                // the shell points the output at the log and then becomes the program (same process)
                ProcessStartInfo shell = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
                shell.ArgumentList.Add("-c");
                shell.ArgumentList.Add("exec \"$0\" \"$@\" > \"$FAF_DETACHED_LOG\" 2>&1 < /dev/null");
                shell.ArgumentList.Add(start.FileName);
                foreach (string argument in start.ArgumentList)
                {
                    shell.ArgumentList.Add(argument);
                }
                foreach ((string key, string? value) in start.Environment)
                {
                    shell.Environment[key] = value;
                }
                shell.Environment["FAF_DETACHED_LOG"] = Path.GetFullPath(log);
                return Process.Start(shell) ?? throw new InvalidOperationException($"Could not start {start.FileName}");
            }
            return StartOnWindows(start, log);
        }

        /// <summary>
        /// .NET hands a child this process's standard handles (<c>GetStdHandle</c>) when it
        /// redirects nothing, and Windows passes every inheritable handle on. So for the moment of
        /// the start the standard handles are the log and NUL (inheritable), and this tool's own
        /// are made not inheritable; both are put back after.
        /// </summary>
        private static Process StartOnWindows(ProcessStartInfo start, string log)
        {
            using FileStream output = new FileStream(log, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            using FileStream input = new FileStream("NUL", FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            SetInheritable(output.SafeFileHandle.DangerousGetHandle(), true);
            SetInheritable(input.SafeFileHandle.DangerousGetHandle(), true);

            int[] kinds = [StdInput, StdOutput, StdError];
            IntPtr[] own = [.. kinds.Select(GetStdHandle)];
            bool[] ownInheritable = [.. own.Select(IsInheritable)];
            try
            {
                foreach (IntPtr handle in own)
                {
                    SetInheritable(handle, false);
                }
                SetStdHandle(StdInput, input.SafeFileHandle.DangerousGetHandle());
                SetStdHandle(StdOutput, output.SafeFileHandle.DangerousGetHandle());
                SetStdHandle(StdError, output.SafeFileHandle.DangerousGetHandle());
                return Process.Start(start) ?? throw new InvalidOperationException($"Could not start {start.FileName}");
            }
            finally
            {
                for (int i = 0; i < kinds.Length; i++)
                {
                    SetStdHandle(kinds[i], own[i]);
                    if (ownInheritable[i])
                    {
                        SetInheritable(own[i], true);
                    }
                }
            }
        }

        private const int StdInput = -10;
        private const int StdOutput = -11;
        private const int StdError = -12;
        private const uint HandleFlagInherit = 1;

        private static bool IsInheritable(IntPtr handle) =>
            handle != IntPtr.Zero && handle != new IntPtr(-1) && GetHandleInformation(handle, out uint flags) && (flags & HandleFlagInherit) != 0;

        /// <summary>
        /// Sets whether a child inherits the handle; a handle that takes no flags (none, a console's
        /// on old Windows) is left as it is.
        /// </summary>
        private static void SetInheritable(IntPtr handle, bool inheritable)
        {
            if (handle != IntPtr.Zero && handle != new IntPtr(-1))
            {
                SetHandleInformation(handle, HandleFlagInherit, inheritable ? HandleFlagInherit : 0);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int kind);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetStdHandle(int kind, IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetHandleInformation(IntPtr handle, out uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
    }
}
