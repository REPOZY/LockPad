using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using KeePassLib.Cryptography.KeyDerivation;
using NUnit.Framework;

namespace LockPad.Tests
{
    [TestFixture]
    public class RepositoryGuardTests
    {
        private static string RepoRoot()
        {
            DirectoryInfo d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "LockPad.sln"))) d = d.Parent;
            Assert.That(d, Is.Not.Null, "LockPad.sln not found");
            return d.FullName;
        }

        private static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
        }

        [Test]
        public void VendoredKeePassLib_MatchesManifest()
        {
            string root = RepoRoot();
            string libDir = Path.Combine(root, "lib", "KeePassLib");
            var mismatches = new List<string>();
            var listed = new HashSet<string>(StringComparer.Ordinal);

            foreach (string line in File.ReadAllLines(Path.Combine(root, "lib", "keepasslib.sha256")))
            {
                if (line.Trim().Length == 0) continue;
                string hash = line.Substring(0, 64);
                string rel = line.Substring(66);
                listed.Add(rel);
                string full = Path.Combine(libDir, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full)) mismatches.Add("missing: " + rel);
                else if (!string.Equals(Sha256(full), hash, StringComparison.OrdinalIgnoreCase))
                    mismatches.Add("hash differs: " + rel);
            }

            foreach (string f in Directory.GetFiles(libDir, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(libDir.Length + 1).Replace('\\', '/');
                if (!listed.Contains(rel)) mismatches.Add("not in manifest: " + rel);
            }

            Assert.That(mismatches, Is.Empty);
        }

        [Test]
        public void VendoredKeePassData_MatchesManifest()
        {
            string root = RepoRoot();
            string dir = Path.Combine(root, "lib", "KeePassData");
            var mismatches = new List<string>();
            var listed = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(Path.Combine(root, "lib", "keepassdata.sha256")))
            {
                if (line.Trim().Length == 0) continue;
                string rel = line.Substring(66);
                listed.Add(rel);
                string full = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full)) mismatches.Add("missing: " + rel);
                else if (!string.Equals(Sha256(full), line.Substring(0, 64), StringComparison.OrdinalIgnoreCase))
                    mismatches.Add("hash differs: " + rel);
            }
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(dir.Length + 1).Replace('\\', '/');
                if (!listed.Contains(rel)) mismatches.Add("not in manifest: " + rel);
            }
            Assert.That(mismatches, Is.Empty);
        }

        /// <summary>
        /// SEC-M2: everything compiled into KeePassLib.dll must be covered by keepasslib.sha256, which only lists files
        /// under lib/KeePassLib/. Build output (lib/bin, lib/obj, e.g. the SDK's generated AssemblyAttributes.cs) is
        /// not source and is skipped.
        /// </summary>
        [Test]
        public void Lib_HasNoCSharpSourceOutsideVendoredFolder()
        {
            string root = RepoRoot();
            string lib = Path.Combine(root, "lib");
            var stray = new List<string>();
            int vendored = 0;
            foreach (string f in Directory.GetFiles(lib, "*.cs", SearchOption.AllDirectories))
            {
                string rel = RepoRelative(root, f); // lib/...
                string[] parts = rel.Split('/');
                if (parts.Length > 1 && (parts[1] == "bin" || parts[1] == "obj")) continue;
                if (parts.Length > 2 && parts[1] == "KeePassLib") { vendored++; continue; }
                stray.Add(rel);
            }
            Assert.That(vendored, Is.GreaterThan(100), "the vendored sources were scanned");
            Assert.That(stray, Is.Empty);
        }

        /// <summary>
        /// SEC-M2: lib/KeePassLib.csproj compiles exactly lib/KeePassLib/**/*.cs. SDK default globbing would compile
        /// every .cs under lib/, so it must be off, with one explicit include and no other Compile items.
        /// </summary>
        [Test]
        public void KeePassLibCsproj_CompilesOnlyTheVendoredFolder()
        {
            var doc = System.Xml.Linq.XDocument.Load(Path.Combine(RepoRoot(), "lib", "KeePassLib.csproj"));
            var props = doc.Descendants("EnableDefaultCompileItems").Select(e => e.Value.Trim()).ToList();
            Assert.That(props, Is.EqualTo(new[] { "false" }), "EnableDefaultCompileItems must be set to false once");

            var compile = doc.Descendants("Compile")
                .Select(e => new
                {
                    Include = (string)e.Attribute("Include"),
                    Other = e.Attributes().Where(a => a.Name.LocalName != "Include").Select(a => a.Name.LocalName).ToList()
                })
                .ToList();
            Assert.That(compile.Count, Is.EqualTo(1), "exactly one Compile item");
            Assert.That(compile[0].Include, Is.EqualTo(@"KeePassLib\**\*.cs"));
            Assert.That(compile[0].Other, Is.Empty, "no Remove/Update/Condition on it");
        }

        /// <summary>
        /// Full check 2026-10-22, L4: MSBuild imports Directory.Build.props/.targets, Directory.Packages.props and
        /// Directory.Build.rsp from a project's folder or any folder above it, and any of them could add Compile items
        /// or analyzers to KeePassLib.dll without touching lib/KeePassLib.csproj. Only the root files may exist (the
        /// .targets and .rsp ones stop MSBuild from searching folders above the repository), and none of them may
        /// add code, imports or targets.
        /// </summary>
        [Test]
        public void MsBuildImportFiles_OnlyTheReviewedRootFiles()
        {
            string root = RepoRoot();
            var found = new List<string>();
            foreach (string f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(root.Length + 1).Replace('\\', '/');
                string[] parts = rel.Split('/');
                if (parts.Any(p => p == ".git" || p == "bin" || p == "obj" || p == "node_modules")) continue;
                string name = parts[parts.Length - 1];
                if (name.StartsWith("Directory.", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".rsp", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".props", StringComparison.OrdinalIgnoreCase))
                    found.Add(rel);
            }
            found.Sort(StringComparer.Ordinal);
            Assert.That(found, Is.EqualTo(new[] { "Directory.Build.props", "Directory.Build.rsp", "Directory.Build.targets" }));

            string[] forbidden = { "Compile", "Analyzer", "Import", "Target", "UsingTask", "Reference", "ProjectReference" };
            foreach (string file in new[] { "Directory.Build.props", "Directory.Build.targets" })
            {
                var doc = System.Xml.Linq.XDocument.Load(Path.Combine(root, file));
                Assert.That(doc.Descendants().Select(e => e.Name.LocalName).Where(forbidden.Contains), Is.Empty, file);
            }
            string[] rsp = File.ReadAllLines(Path.Combine(root, "Directory.Build.rsp"));
            Assert.That(rsp.Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("#")), Is.Empty,
                "Directory.Build.rsp holds comments only");
        }

        /// <summary>Full check 2026-10-22, L3: every NuGet package is pinned to one exact version, and restore checks
        /// each package's content hash against the committed lock files (locked mode).</summary>
        [Test]
        public void NuGetPackages_ExactVersions_LockFilesInLockedMode()
        {
            string root = RepoRoot();
            var props = System.Xml.Linq.XDocument.Load(Path.Combine(root, "Directory.Build.props"));
            Assert.That(props.Descendants("RestorePackagesWithLockFile").Select(e => e.Value.Trim()), Is.EqualTo(new[] { "true" }));
            Assert.That(props.Descendants("RestoreLockedMode").Select(e => e.Value.Trim()), Is.EqualTo(new[] { "true" }));

            string[] projects =
            {
                "Directory.Build.props", "lib/KeePassLib.csproj", "src/LockPad.Core/LockPad.Core.csproj",
                "src/LockPad.App/LockPad.App.csproj", "tests/LockPad.Tests/LockPad.Tests.csproj",
            };
            foreach (string p in projects)
            {
                var doc = System.Xml.Linq.XDocument.Load(Path.Combine(root, p));
                foreach (var r in doc.Descendants("PackageReference"))
                    Assert.That((string)r.Attribute("Version"), Does.Match(@"^\[\d+(\.\d+){2,3}\]$"),
                        p + ": " + (string)r.Attribute("Include"));
                if (p.EndsWith(".csproj", StringComparison.Ordinal))
                    Assert.That(File.Exists(Path.Combine(root, Path.GetDirectoryName(p), "packages.lock.json")), Is.True,
                        p + " has a committed lock file");
            }
        }

        [Test]
        public void SourceFiles_ContainNoMojibake()
        {
            string root = RepoRoot();
            // Built from numeric char codes so this file never contains the markers itself.
            string c1 = "[" + (char)0x80 + "-" + (char)0xBF + "]";
            string A = ((char)0xC3).ToString(), Ac = ((char)0xC2).ToString();
            string a = ((char)0xE2).ToString();
            string D = "[" + (char)0xD0 + (char)0xD1 + "]";
            var marker = new Regex(
                A + c1 + "|" + a + (char)0x20AC + "|" + a + (char)0x201E + "|" + Ac + c1 + "|" + D + c1);
            var strict = new UTF8Encoding(false, true);
            var hits = new List<string>();
            var scanned = new List<string>();
            string sep = Path.DirectorySeparatorChar.ToString();
            foreach (string dir in new[] { "src", "tests" })
            {
                foreach (string f in Directory.GetFiles(Path.Combine(root, dir), "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".cs" && ext != ".xaml" && ext != ".csproj" && ext != ".md") continue;
                    if (f.Contains(sep + "obj" + sep) || f.Contains(sep + "bin" + sep)) continue;
                    scanned.Add(RepoRelative(root, f));
                    string text;
                    try { text = strict.GetString(File.ReadAllBytes(f)); }
                    catch (DecoderFallbackException) { hits.Add(f + ": invalid UTF-8"); continue; }
                    string[] lines = text.Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                        if (marker.IsMatch(lines[i])) hits.Add(f + ":" + (i + 1));
                }
            }
            AssertScannedIncludesKnownFiles(scanned);
            Assert.That(hits, Is.Empty);
        }

        /// <summary>Files every source scan must cover; guards against a filter that silently skips real sources.</summary>
        private static readonly string[] MustBeScanned =
        {
            "src/LockPad.Core/NotebookStore.cs",
            "tests/LockPad.Tests/RepositoryGuardTests.cs",
        };

        private static string RepoRelative(string root, string fullPath)
        {
            return fullPath.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
        }

        private static void AssertScannedIncludesKnownFiles(IEnumerable<string> scannedRelativePaths)
        {
            var set = new HashSet<string>(scannedRelativePaths, StringComparer.OrdinalIgnoreCase);
            foreach (string required in MustBeScanned)
                Assert.That(set.Contains(required), Is.True, "not scanned: " + required);
        }

        /// <summary>
        /// C# source must be pure ASCII (non-ASCII text is built from numeric char codes). A single leading UTF-8
        /// BOM is tolerated: it is encoding metadata, not source text, and several earlier files carry one.
        /// </summary>
        [Test]
        public void CSharpSource_IsPureAscii()
        {
            string root = RepoRoot();
            var hits = new List<string>();
            var scanned = new List<string>();
            foreach (string dir in new[] { "src", "tests" })
            {
                foreach (string f in Directory.GetFiles(Path.Combine(root, dir), "*.cs", SearchOption.AllDirectories))
                {
                    // Repo-relative, so a checkout below a folder named "bin" or "obj" is still scanned.
                    string rel = RepoRelative(root, f);
                    string[] parts = rel.Split('/');
                    if (parts.Contains("bin") || parts.Contains("obj")) continue;
                    if (!rel.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
                    scanned.Add(rel);

                    byte[] bytes = File.ReadAllBytes(f);
                    int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                    int line = 1;
                    int lastHitLine = 0;
                    for (int i = start; i < bytes.Length; i++)
                    {
                        if (bytes[i] == (byte)'\n') line++;
                        else if (bytes[i] > 0x7F && line != lastHitLine)
                        {
                            hits.Add(rel + ":" + line);
                            lastHitLine = line;
                        }
                    }
                }
            }
            Assert.That(scanned, Is.Not.Empty, "no C# files were scanned");
            AssertScannedIncludesKnownFiles(scanned);
            Assert.That(hits, Is.Empty);
        }

        private static readonly Regex[] BannedNetworkPatterns =
        {
            new Regex(@"System\s*\.\s*Net\b"),
            new Regex(@"HttpClient"),
            new Regex(@"WebClient"),
            new Regex(@"WebRequest"),
            new Regex(@"Socket"),
            new Regex(@"\bDns\b"),
            new Regex(@"TcpClient"),
            new Regex(@"UdpClient"),
            new Regex(@"SmtpClient"),
            new Regex(@"NetworkInformation"),
            new Regex(@"\bIOConnection\s*\."),
        };

        private static List<string> FindNetworkApis(string text)
        {
            return BannedNetworkPatterns.Where(r => r.IsMatch(text)).Select(r => r.ToString()).ToList();
        }

        [Test]
        public void Source_DoesNotReferenceNetworkApis()
        {
            string src = Path.Combine(RepoRoot(), "src");
            var hits = new List<string>();
            foreach (string f in Directory.GetFiles(src, "*.*", SearchOption.AllDirectories)
                .Where(p => (p.EndsWith(".cs") || p.EndsWith(".xaml"))
                    && !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                    && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)))
            {
                foreach (string pattern in FindNetworkApis(File.ReadAllText(f)))
                    hits.Add(f + ": " + pattern);
            }
            Assert.That(hits, Is.Empty);
        }

        [TestCase("System . Net.Dns")]
        [TestCase("S.Net.Mail.SmtpClient")]
        [TestCase("IOConnection.OpenRead(x)")]
        [TestCase("using System.Net;")]
        [TestCase("new TcpClient()")]
        [TestCase("System.Net\r\n.Sockets")]
        public void NetworkMatcher_FlagsKnownBypasses(string sample)
        {
            Assert.That(FindNetworkApis(sample), Is.Not.Empty);
        }

        [Test]
        public void NetworkMatcher_DoesNotFlagIOConnectionInfo()
        {
            Assert.That(FindNetworkApis("IOConnectionInfo.FromPath(p)"), Is.Empty);
        }

        /// <summary>
        /// SEC-H2: NativeLib.IsLibraryInstalled temporarily forces AllowNative to true and loads KeePassLibN, which
        /// LockPad never ships (a found copy can only be planted). Nothing in src may call it.
        /// </summary>
        [Test]
        public void Source_NeverCallsNativeLibIsLibraryInstalled()
        {
            string src = Path.Combine(RepoRoot(), "src");
            var hits = new List<string>();
            var scanned = new List<string>();
            foreach (string f in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories))
            {
                string[] parts = RepoRelative(RepoRoot(), f).Split('/');
                if (parts.Contains("bin") || parts.Contains("obj")) continue;
                scanned.Add(RepoRelative(RepoRoot(), f));
                if (File.ReadAllText(f).Contains("IsLibraryInstalled")) hits.Add(f);
            }
            Assert.That(scanned, Has.Some.EqualTo("src/LockPad.Core/NotebookStore.cs"));
            Assert.That(hits, Is.Empty);
        }
        /// <summary>
        /// C11 (review M11): the test seams that weaken the KDF floor or hook into the save pipeline are only ever set by
        /// the test suite. An assignment is the name, optional whitespace, then "=" (or a compound "+=" / "-=") not
        /// followed by another "=". The declarations (<c>TestHook { get; set; }</c>, <c>AcceptWeakKdfForTests { get; set;
        /// }</c>, <c>FreshKdf { get; set; } = ...</c>) do not match: the name is followed by "{", not "=".
        /// </summary>
        private static readonly Regex TestSeamAssignment =
            new Regex(@"\b(AcceptWeakKdfForTests|FreshKdf|TestHook)\s*[+-]?=(?!=)");

        [Test]
        public void Source_NeverAssignsTestSeams()
        {
            string root = RepoRoot();
            var hits = new List<string>();
            var scanned = new List<string>();
            foreach (string f in Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
            {
                string rel = RepoRelative(root, f);
                string[] parts = rel.Split('/');
                if (parts.Contains("bin") || parts.Contains("obj")) continue;
                scanned.Add(rel);
                string[] lines = File.ReadAllLines(f);
                for (int i = 0; i < lines.Length; i++)
                    if (TestSeamAssignment.IsMatch(lines[i])) hits.Add(rel + ":" + (i + 1) + ": " + lines[i].Trim());
            }
            Assert.That(scanned, Has.Some.EqualTo("src/LockPad.Core/SaveFormatPolicy.cs"));
            Assert.That(scanned, Has.Some.EqualTo("src/LockPad.Core/NotebookStore.cs"));
            Assert.That(hits, Is.Empty);
        }

        [TestCase("SaveFormatPolicy.AcceptWeakKdfForTests = true;")]
        [TestCase("SaveFormatPolicy.AcceptWeakKdfForTests=true;")]
        [TestCase("LockPad.Core.SaveFormatPolicy.FreshKdf = () => null;")]
        [TestCase("store.TestHook = (s, t) => { };")]
        [TestCase("new NotebookStore { TestHook = null }")]
        [TestCase("store.TestHook += (s, t) => { };")]
        public void TestSeamMatcher_FlagsAssignments(string sample)
        {
            Assert.That(TestSeamAssignment.IsMatch(sample), Is.True);
        }

        [TestCase("internal static bool AcceptWeakKdfForTests { get; set; }")]
        [TestCase("internal static Func<KdfParameters> FreshKdf { get; set; } =")]
        [TestCase("internal Action<string, string> TestHook { get; set; }")]
        [TestCase("TestHook?.Invoke(\"after-temp-write\", temp);")]
        [TestCase("if (AcceptWeakKdfForTests) return true;")]
        [TestCase("if (store.TestHook == null) return;")]
        [TestCase("KdfParameters fresh = FreshKdf();")]
        public void TestSeamMatcher_IgnoresDeclarationsReadsAndComparisons(string sample)
        {
            Assert.That(TestSeamAssignment.IsMatch(sample), Is.False);
        }

        // The old product name, assembled so that this file itself carries no literal copy of it.
        private static readonly string OldName = "Kdbx" + "Pad";

        /// <summary>
        /// The only places the old name may still appear in src/ and tests/: (file, exact substring). A substring is
        /// removed from a line before the line is checked, so nothing else on that line is excused.
        /// </summary>
        private static readonly KeyValuePair<string, string>[] OldNameAllowList =
        {
            // Real leftover file names from old builds, which saves must leave untouched.
            new KeyValuePair<string, string>("tests/LockPad.Tests/SingleBackupTests.cs", OldName.ToLowerInvariant() + "-rotation"),
            // Old notebook labels used as test data: such files must open read-only and never be written or replaced.
            new KeyValuePair<string, string>("tests/LockPad.Tests/NotebookStoreOpenTests.cs", OldName + ".Notebook"),
            new KeyValuePair<string, string>("tests/LockPad.Tests/BakSafetyTests.cs", OldName + ".Notebook"),
            // This test's own name and the pattern above.
            new KeyValuePair<string, string>("tests/LockPad.Tests/RepositoryGuardTests.cs", "No" + OldName + "NameLeft"),
        };

        [Test]
        public void NoKdbxPadNameLeft()
        {
            string root = RepoRoot();
            var hits = new List<string>();
            foreach (string top in new[] { "src", "tests" })
            {
                foreach (string f in Directory.GetFileSystemEntries(Path.Combine(root, top), "*", SearchOption.AllDirectories))
                {
                    string rel = RepoRelative(root, f);
                    string[] parts = rel.Split('/');
                    if (parts.Contains("bin") || parts.Contains("obj")) continue;
                    if (rel.IndexOf(OldName, StringComparison.OrdinalIgnoreCase) >= 0) hits.Add("name: " + rel);
                    if (!File.Exists(f)) continue;

                    string[] allowed = OldNameAllowList.Where(a => a.Key == rel).Select(a => a.Value).ToArray();
                    string[] lines = File.ReadAllText(f, Encoding.GetEncoding(28591)).Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i];
                        foreach (string a in allowed) line = line.Replace(a, "");
                        if (line.IndexOf(OldName, StringComparison.OrdinalIgnoreCase) >= 0)
                            hits.Add(rel + ":" + (i + 1));
                    }
                }
            }
            Assert.That(hits, Is.Empty);

            // Nobody but the developer ever had a build under the old name (user decision 2026-10-21), so the public
            // documentation never mentions it, in any case. Every document under docs/ (specs, plans, review records,
            // the checklist) is public too (user decisions 2026-10-23).
            var docs = new List<string> { "README.md", "SECURITY.md", "CONTRIBUTING.md" };
            string docsRoot = Path.Combine(root, "docs");
            docs.AddRange(Directory.GetFiles(docsRoot, "*.md", SearchOption.AllDirectories)
                .Select(f => "docs/" + f.Substring(docsRoot.Length + 1).Replace('\\', '/')));
            Assert.That(docs, Has.Member("docs/user-guide.md").And.Member("docs/manual-test-checklist.md"));
            Assert.That(docs.Any(d => d.StartsWith("docs/specs/", StringComparison.Ordinal)), Is.True, "the specs were found");
            foreach (string doc in docs)
            {
                string text = File.ReadAllText(Path.Combine(root, doc));
                Assert.That(Regex.Matches(text, OldName, RegexOptions.IgnoreCase).Count, Is.EqualTo(0),
                    doc + " never names the old product");
            }
        }

        /// <summary>
        /// Bug fix 2026-10-21: WPF startup cannot be unit-tested, so this checks the source. App.OnStartup hands the
        /// command line to the shell only through StartupArguments.NotebookPathFrom, inside the ShellViewModel
        /// construction, and reads e.Args nowhere else.
        /// </summary>
        [Test]
        public void AppStartup_PassesTheCommandLineThroughNotebookPathFrom()
        {
            string app = File.ReadAllText(Path.Combine(RepoRoot(), "src", "LockPad.App", "App.xaml.cs"));
            int start = app.IndexOf("protected override void OnStartup(StartupEventArgs e)", StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), "OnStartup found");
            int shell = app.IndexOf("new ShellViewModel(", start, StringComparison.Ordinal);
            Assert.That(shell, Is.GreaterThan(start), "OnStartup builds the shell");
            int end = app.IndexOf(';', shell);
            string construction = app.Substring(shell, end - shell);
            Assert.That(construction, Does.Contain("StartupArguments.NotebookPathFrom(e.Args)"));
            Assert.That(Regex.Matches(app, @"\be\s*\.\s*Args\b").Count, Is.EqualTo(1), "e.Args is read only there");
            Assert.That(app, Does.Not.Contain("LastNotebookPath"), "the argument is never remembered at startup");
        }

        [Test]
        public void KeePassLib_SelfTestAndArgon2idAvailable()
        {
            KeePassLib.Cryptography.SelfTest.Perform();
            Assert.That(new Argon2Kdf(Argon2Type.ID).Name, Is.EqualTo("Argon2id"));
        }
    }
}