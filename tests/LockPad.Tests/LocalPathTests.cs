using System.IO;
using System.Linq;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    [TestFixture]
    public class LocalPathTests
    {
        [Test]
        public void DrivePath_ReturnsFullPath()
        {
            Assert.That(LocalPath.RequireLocalFile(@"C:\x\a.kdbx"), Is.EqualTo(@"C:\x\a.kdbx"));
        }

        [Test]
        public void RelativePath_ReturnsFullPath()
        {
            Assert.That(LocalPath.RequireLocalFile("a.kdbx"), Is.EqualTo(Path.GetFullPath("a.kdbx")));
        }

        /// <summary>C1 (user decision 2026-10-18): only local drives. UNC paths in either slash form are refused with the
        /// existing local-path rejection. GetFullPath does not contact the server, and nothing else touches the path.</summary>
        [TestCase(@"\\server\share\a.kdbx")]
        [TestCase(@"//server/share/a.kdbx")]
        [TestCase(@"\\nonexistent-host\share\nb.kdbx")]
        [TestCase(@"/\server\share\a.kdbx")]
        [TestCase(@"  \\server\share\a.kdbx")]
        [TestCase(@"\\localhost\c$\a.kdbx")]
        public void UncPath_Rejected(string p)
        {
            var ex = Assert.Throws<NonLocalPathException>(() => LocalPath.RequireLocalFile(p));
            Assert.That(ex.Message, Is.EqualTo("Only local file paths are allowed."));
        }

        [Test]
        public void UncPath_DriveTypeLookupNeverCalled()
        {
            int lookups = 0;
            Assert.Throws<NonLocalPathException>(() => LocalPath.RequireLocalFile(@"\\server\share\a.kdbx",
                root => { lookups++; return DriveType.Fixed; }));
            Assert.That(lookups, Is.EqualTo(0));
        }

        [TestCase(DriveType.Network)]
        public void MappedNetworkDrive_Rejected(DriveType type)
        {
            var roots = new System.Collections.Generic.List<string>();
            var ex = Assert.Throws<NonLocalPathException>(() => LocalPath.RequireLocalFile(@"Z:\notes\nb.kdbx",
                root => { roots.Add(root); return type; }));
            Assert.That(ex.Message, Is.EqualTo("Only local file paths are allowed."));
            Assert.That(roots, Is.EqualTo(new[] { @"Z:\" }), "asked about the drive root only");
        }

        [TestCase(DriveType.Fixed)]
        [TestCase(DriveType.Removable)]
        [TestCase(DriveType.CDRom)]
        [TestCase(DriveType.Ram)]
        [TestCase(DriveType.NoRootDirectory)]
        [TestCase(DriveType.Unknown)]
        public void OtherDriveTypes_Allowed(DriveType type)
        {
            Assert.That(LocalPath.RequireLocalFile(@"Z:\notes\nb.kdbx", root => type), Is.EqualTo(@"Z:\notes\nb.kdbx"));
        }

        [Test]
        public void MappedNetworkDrive_ForwardSlashes_Rejected()
        {
            Assert.Throws<NonLocalPathException>(() => LocalPath.RequireLocalFile("z:/notes/nb.kdbx",
                root => string.Equals(root, @"z:\", System.StringComparison.OrdinalIgnoreCase) ? DriveType.Network : DriveType.Fixed));
        }

        // ---- Fix round 2, F2: the local-drive check runs before GetFullPath, which expands 8.3 names (file-system access) ----

        /// <summary>Calls LocalPath with every seam recorded in one ordered log: "drive:X:\" for a drive-type lookup,
        /// "cwd" for reading the current directory, "full:path" for the full-path expansion (the step that may touch the
        /// file system).</summary>
        private static string Recorded(string path, System.Collections.Generic.List<string> log,
            System.Func<string, DriveType> types, string cwd)
        {
            return LocalPath.RequireLocalFile(path,
                root => { log.Add("drive:" + root); return types(root); },
                () => { log.Add("cwd"); return cwd; },
                p => { log.Add("full:" + p); return p.StartsWith(@"\") ? @"C:" + p : p; });
        }

        [Test]
        public void ShortNameOnNetworkDrive_RefusedBeforeAnyFullPathExpansion()
        {
            var log = new System.Collections.Generic.List<string>();
            var ex = Assert.Throws<NonLocalPathException>(() => Recorded(@"Z:\NOTEBO~1\nb.kdbx", log,
                root => root == @"Z:\" ? DriveType.Network : DriveType.Fixed, @"C:\work"));
            Assert.That(ex.Message, Is.EqualTo("Only local file paths are allowed."));
            Assert.That(log, Is.EqualTo(new[] { @"drive:Z:\" }), "the drive type was asked and GetFullPath never ran");
        }

        [Test]
        public void LocalDrive_DriveTypeAskedBeforeTheFullPathExpansion()
        {
            var log = new System.Collections.Generic.List<string>();
            Assert.That(Recorded(@"  Z:\NOTEBO~1\nb.kdbx", log, root => DriveType.Fixed, @"C:\work"),
                Is.EqualTo(@"  Z:\NOTEBO~1\nb.kdbx"));
            Assert.That(log[0], Is.EqualTo(@"drive:Z:\"), "first the drive type");
            Assert.That(log.IndexOf(@"full:  Z:\NOTEBO~1\nb.kdbx"), Is.GreaterThan(0), "then the expansion");
            Assert.That(log, Has.No.Member("cwd"), "a drive-letter path does not depend on the current directory");
        }

        [TestCase("nb.kdbx")]
        [TestCase(@"sub\nb.kdbx")]
        [TestCase(@"\notes\nb.kdbx")]
        public void RelativePath_UncCurrentDirectory_Refused_BeforeExpansion(string relative)
        {
            var log = new System.Collections.Generic.List<string>();
            Assert.Throws<NonLocalPathException>(() => Recorded(relative, log, root => DriveType.Fixed, @"\\server\share\dir"));
            Assert.That(log, Is.EqualTo(new[] { "cwd" }), "refused on the current directory alone");
        }

        [TestCase("nb.kdbx")]
        [TestCase(@"\notes\nb.kdbx")]
        public void RelativePath_CurrentDirectoryOnNetworkDrive_Refused_BeforeExpansion(string relative)
        {
            var log = new System.Collections.Generic.List<string>();
            Assert.Throws<NonLocalPathException>(() => Recorded(relative, log,
                root => root == @"Y:\" ? DriveType.Network : DriveType.Fixed, @"Y:\work"));
            Assert.That(log, Is.EqualTo(new[] { "cwd", @"drive:Y:\" }));
        }

        [Test]
        public void RelativePath_LocalCurrentDirectory_Allowed()
        {
            var log = new System.Collections.Generic.List<string>();
            Assert.That(Recorded("nb.kdbx", log, root => DriveType.Fixed, @"C:\work"), Is.EqualTo("nb.kdbx"));
            Assert.That(log.Take(3), Is.EqualTo(new[] { "cwd", @"drive:C:\", "full:nb.kdbx" }));
        }

        [Test]
        public void RealDriveTypeLookup_SystemDriveIsAllowed()
        {
            string sys = Path.GetPathRoot(System.Environment.SystemDirectory);
            string p = Path.Combine(sys, "x", "a.kdbx");
            Assert.That(LocalPath.RequireLocalFile(p), Is.EqualTo(p));
        }

        [TestCase("https://evil.example/a.kdbx")]
        [TestCase("ftp://x/a.kdbx")]
        [TestCase("file:///C:/a.kdbx")]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void Rejected(string p)
        {
            // Catch, not Throws: empty paths now throw the derived InvalidPathException (C8).
            Assert.Catch<NonLocalPathException>(() => LocalPath.RequireLocalFile(p));
        }

        /// <summary>A path longer than any Windows path (32,767 characters).</summary>
        internal static readonly string TooLongPath = @"C:\" + new string('a', 40000) + ".kdbx";

        /// <summary>C8 (review M8): empty, malformed and too-long paths are invalid input, not non-local paths. The type
        /// derives from NonLocalPathException, so every existing rejection still applies.</summary>
        [Test]
        public void InvalidPaths_ThrowInvalidPathException()
        {
            foreach (string p in new[] { null, "", "   ", @"C:\a<b", "C:\\a\"b.kdbx", @"C:\a|b.kdbx", TooLongPath })
            {
                string label = p == null ? "null" : p.Length > 40 ? "too long" : p;
                Assert.Throws<InvalidPathException>(() => LocalPath.RequireLocalFile(p), label);
                Assert.That(() => LocalPath.RequireLocalFile(p), Throws.InstanceOf<NonLocalPathException>(), label);
            }
        }

        [TestCase("https://evil.example/a.kdbx")]
        [TestCase("file:///C:/a.kdbx")]
        [TestCase(@"\\server\share\a.kdbx")]
        [TestCase(@"\\?\C:\x\a.kdbx")]
        public void NonLocalPaths_AreNotInvalidPathException(string p)
        {
            var ex = Assert.Throws<NonLocalPathException>(() => LocalPath.RequireLocalFile(p));
            Assert.That(ex, Is.Not.InstanceOf<InvalidPathException>());
        }

        /// <summary>L1/L2: device-namespace prefixes bypass normalization (no 8.3 expansion, a literal '?'); no
        /// picker produces them, so they are refused with the existing local-path rejection.</summary>
        [TestCase(@"\\?\C:\x\a.kdbx")]
        [TestCase(@"\\.\C:\x\a.kdbx")]
        [TestCase(@"//?/C:/x/a.kdbx")]
        [TestCase(@"//./C:/x/a.kdbx")]
        [TestCase(@"\\?/C:\x\a.kdbx")]
        [TestCase(@"/\.\C:\x\a.kdbx")]
        [TestCase(@"\\?\UNC\server\share\a.kdbx")]
        [TestCase(@"\\.\GLOBALROOT\Device\HarddiskVolume1\a.kdbx")]
        [TestCase(@"  \\?\C:\x\a.kdbx")]
        // C6 (review M5): the NT object-manager prefix \??\, in either slash form.
        [TestCase(@"\??\C:\x\a.kdbx")]
        [TestCase(@"/??/C:/x/a.kdbx")]
        [TestCase(@"\??/C:\x\a.kdbx")]
        [TestCase(@"/??\C:\x\a.kdbx")]
        [TestCase(@"  \??\C:\x\a.kdbx")]
        [TestCase(@"\??\UNC\server\share\a.kdbx")]
        public void DeviceNamespacePrefix_Rejected(string p)
        {
            var ex = Assert.Throws<NonLocalPathException>(() => LocalPath.RequireLocalFile(p));
            Assert.That(ex.Message, Is.EqualTo("Only local file paths are allowed."));
        }
    }
}
