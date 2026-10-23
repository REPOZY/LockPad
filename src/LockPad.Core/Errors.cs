using System;

namespace LockPad.Core
{
    public sealed class WrongCredentialsException : Exception
    {
        public WrongCredentialsException() : base("Wrong password or key file.") { }
    }

    public sealed class NotebookFormatException : Exception
    {
        public NotebookFormatException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>The path is refused: not a file on a local drive (URL, UNC, network drive, device namespace), or, as
    /// the derived <see cref="InvalidPathException"/>, not a usable path at all.</summary>
    public class NonLocalPathException : ArgumentException
    {
        public NonLocalPathException(string message) : base(message) { }
    }

    /// <summary>
    /// An empty, malformed (invalid characters) or too-long path (review M8). The unlock and create pages show a
    /// per-box invalid-path message instead of the non-local message; the App's generic invalid-path string is only the
    /// fallback of its error-text mapping (NotebookErrorText.Describe). It derives from
    /// <see cref="NonLocalPathException"/> so that every existing rejection of non-local paths refuses it too.
    /// </summary>
    public sealed class InvalidPathException : NonLocalPathException
    {
        public InvalidPathException(string message) : base(message) { }
    }
}
