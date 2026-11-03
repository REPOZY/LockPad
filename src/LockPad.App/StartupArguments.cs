namespace LockPad.App
{
    /// <summary>
    /// The command line LockPad was started with. "Open with" (double-clicking a .kdbx file) passes the file as the
    /// first argument. The result is untrusted (anyone can make a shortcut with any path): the unlock page validates it
    /// like the remembered path and only pre-fills it.
    /// </summary>
    internal static class StartupArguments
    {
        /// <summary>The first argument without surrounding whitespace and quotes; null when there is none or it is
        /// empty after trimming. Further arguments are ignored.</summary>
        internal static string NotebookPathFrom(string[] args)
        {
            if (args == null || args.Length == 0 || args[0] == null) return null;
            string path = args[0].Trim().Trim('"').Trim();
            return path.Length == 0 ? null : path;
        }
    }
}
