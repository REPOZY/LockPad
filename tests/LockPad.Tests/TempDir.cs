using System;
using System.IO;

namespace LockPad.Tests
{
    internal sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LockPadTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string File(string name)
        {
            return System.IO.Path.Combine(Path, name);
        }

        public void Dispose()
        {
            try
            {
                if (!Directory.Exists(Path)) return;
                foreach (string f in Directory.GetFiles(Path, "*", SearchOption.AllDirectories))
                    System.IO.File.SetAttributes(f, FileAttributes.Normal);
                foreach (string dir in Directory.GetDirectories(Path, "*", SearchOption.AllDirectories))
                    new DirectoryInfo(dir).Attributes = FileAttributes.Normal;
                new DirectoryInfo(Path).Attributes = FileAttributes.Normal;
                Directory.Delete(Path, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                NUnit.Framework.TestContext.Progress.WriteLine("WARNING: could not delete temp dir " + Path + ": " + ex.Message);
            }
        }
    }
}