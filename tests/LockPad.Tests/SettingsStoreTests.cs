using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    [TestFixture]
    public class SettingsStoreTests
    {
        private static void AssertDefaults(AppSettings s)
        {
            Assert.That(s.FontSize, Is.EqualTo(14.0));
            Assert.That(s.WordWrap, Is.True);
            Assert.That(s.Theme, Is.EqualTo("System"));
            Assert.That(s.IdleLockMinutes, Is.EqualTo(5));
            Assert.That(s.ClipboardClearSeconds, Is.EqualTo(20));
            Assert.That(s.LastNotebookPath, Is.Null);
        }

        [Test]
        public void Load_Missing_ReturnsDefaults()
        {
            using (var d = new TempDir())
                AssertDefaults(new SettingsStore(d.Path).Load());
        }

        [Test]
        public void Load_Corrupt_ReturnsDefaults()
        {
            using (var d = new TempDir())
            {
                File.WriteAllText(d.File("settings.json"), "{not json");
                AssertDefaults(new SettingsStore(d.Path).Load());
            }
        }

        [Test]
        public void Load_WrongType_ReturnsDefaults()
        {
            using (var d = new TempDir())
            {
                File.WriteAllText(d.File("settings.json"), "{\"IdleLockMinutes\":\"abc\"}");
                AssertDefaults(new SettingsStore(d.Path).Load());
            }
        }

        [Test]
        public void Load_DirectoryNamedSettingsJson_ReturnsDefaults()
        {
            using (var d = new TempDir())
            {
                Directory.CreateDirectory(d.File("settings.json"));
                AssertDefaults(new SettingsStore(d.Path).Load());
            }
        }

        [Test]
        public void Load_PartialFile_FillsDefaults()
        {
            using (var d = new TempDir())
            {
                File.WriteAllText(d.File("settings.json"), "{\"WordWrap\":false}");
                AppSettings s = new SettingsStore(d.Path).Load();
                Assert.That(s.WordWrap, Is.False);
                Assert.That(s.FontSize, Is.EqualTo(14.0));
                Assert.That(s.IdleLockMinutes, Is.EqualTo(5));
                Assert.That(s.ClipboardClearSeconds, Is.EqualTo(20));
                Assert.That(s.Theme, Is.EqualTo("System"));
                Assert.That(s.LastNotebookPath, Is.Null);
            }
        }

        [Test]
        public void SaveThenLoad_RoundTrips()
        {
            using (var d = new TempDir())
            {
                var store = new SettingsStore(d.Path);
                store.Save(new AppSettings
                {
                    LastNotebookPath = @"C:\notes\a.kdbx",
                    FontSize = 20,
                    WordWrap = false,
                    Theme = "Dark",
                    IdleLockMinutes = 10,
                    ClipboardClearSeconds = 30
                });
                AppSettings s = store.Load();
                Assert.That(s.LastNotebookPath, Is.EqualTo(@"C:\notes\a.kdbx"));
                Assert.That(s.FontSize, Is.EqualTo(20.0));
                Assert.That(s.WordWrap, Is.False);
                Assert.That(s.Theme, Is.EqualTo("Dark"));
                Assert.That(s.IdleLockMinutes, Is.EqualTo(10));
                Assert.That(s.ClipboardClearSeconds, Is.EqualTo(30));
            }
        }

        [Test]
        public void Save_Twice_OverwritesAndLeavesNoTempFiles()
        {
            using (var d = new TempDir())
            {
                var store = new SettingsStore(d.Path);
                store.Save(new AppSettings { FontSize = 10 });
                store.Save(new AppSettings { FontSize = 12 });
                Assert.That(store.Load().FontSize, Is.EqualTo(12.0));
                Assert.That(Directory.GetFiles(d.Path, "*.tmp"), Is.Empty);
            }
        }

        [Test]
        public void Save_CreatesMissingDirectory()
        {
            using (var d = new TempDir())
            {
                string sub = Path.Combine(d.Path, "a", "b");
                var store = new SettingsStore(sub);
                store.Save(new AppSettings { FontSize = 11 });
                Assert.That(store.Load().FontSize, Is.EqualTo(11.0));
            }
        }

        [Test]
        public void Save_WritesNormalizedValues()
        {
            using (var d = new TempDir())
            {
                var store = new SettingsStore(d.Path);
                store.Save(new AppSettings { IdleLockMinutes = 999, Theme = "Purple" });
                string json = File.ReadAllText(d.File("settings.json"));
                AppSettings s = store.Load();
                Assert.That(s.IdleLockMinutes, Is.EqualTo(60));
                Assert.That(s.Theme, Is.EqualTo("System"));
                Assert.That(json, Does.Not.Contain("999"));
            }
        }

        [Test]
        public void Save_DirectoryPathIsAFile_DoesNotThrow()
        {
            using (var d = new TempDir())
            {
                string p = d.File("occupied");
                File.WriteAllText(p, "x");
                var store = new SettingsStore(p);
                Assert.DoesNotThrow(() => store.Save(new AppSettings()));
                AssertDefaults(store.Load());
            }
        }

        [Test]
        public void Save_Null_DoesNotThrow()
        {
            using (var d = new TempDir())
                Assert.DoesNotThrow(() => new SettingsStore(d.Path).Save(null));
        }

        [Test]
        public void Serialized_MemberNames_AreExactlyTheSevenDocumentedOnes()
        {
            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(ms, new AppSettings());
                string json = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                var names = System.Text.RegularExpressions.Regex.Matches(json, "\"([A-Za-z]+)\":")
                    .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).OrderBy(x => x).ToArray();
                Assert.That(names, Is.EqualTo(new[]
                {
                    "ClipboardClearSeconds", "FontSize", "HideFromScreenCapture", "IdleLockMinutes", "LastNotebookPath", "Theme",
                    "WordWrap" // HideFromScreenCapture: an on/off flag, not a secret (2026-10-21)
                }));
            }
        }

        [Test]
        public void DefaultDirectory_IsAppDataLockPad()
        {
            Assert.That(SettingsStore.DefaultDirectory,
                Is.EqualTo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LockPad")));
        }

        [TestCase(0, 1)]
        [TestCase(999, 60)]
        public void Normalized_ClampsIdle(int input, int expected)
        {
            Assert.That(new AppSettings { IdleLockMinutes = input }.Normalized().IdleLockMinutes, Is.EqualTo(expected));
        }

        [TestCase(1, 5)]
        [TestCase(1000, 120)]
        public void Normalized_ClampsClipboard(int input, int expected)
        {
            Assert.That(new AppSettings { ClipboardClearSeconds = input }.Normalized().ClipboardClearSeconds, Is.EqualTo(expected));
        }

        [TestCase(2.0, 8.0)]
        [TestCase(100.0, 48.0)]
        public void Normalized_ClampsFont(double input, double expected)
        {
            Assert.That(new AppSettings { FontSize = input }.Normalized().FontSize, Is.EqualTo(expected));
        }

        [Test]
        public void Normalized_NonFiniteFont_IsDefault()
        {
            Assert.That(new AppSettings { FontSize = double.NaN }.Normalized().FontSize, Is.EqualTo(14.0));
            Assert.That(new AppSettings { FontSize = double.PositiveInfinity }.Normalized().FontSize, Is.EqualTo(14.0));
            Assert.That(new AppSettings { FontSize = double.NegativeInfinity }.Normalized().FontSize, Is.EqualTo(14.0));
        }

        [Test]
        public void Normalized_UnknownTheme_IsSystem()
        {
            Assert.That(new AppSettings { Theme = "Purple" }.Normalized().Theme, Is.EqualTo("System"));
            Assert.That(new AppSettings { Theme = null }.Normalized().Theme, Is.EqualTo("System"));
        }

        [Test]
        public void Normalized_Theme_CaseInsensitiveCanonical()
        {
            Assert.That(new AppSettings { Theme = "dark" }.Normalized().Theme, Is.EqualTo("Dark"));
            Assert.That(new AppSettings { Theme = "LIGHT" }.Normalized().Theme, Is.EqualTo("Light"));
            Assert.That(new AppSettings { Theme = "system" }.Normalized().Theme, Is.EqualTo("System"));
        }

        [Test]
        public void Normalized_DoesNotMutateOriginal_AndKeepsPathAndWrap()
        {
            var s = new AppSettings { IdleLockMinutes = 999, LastNotebookPath = "x", WordWrap = false };
            AppSettings n = s.Normalized();
            Assert.That(s.IdleLockMinutes, Is.EqualTo(999));
            Assert.That(n.LastNotebookPath, Is.EqualTo("x"));
            Assert.That(n.WordWrap, Is.False);
        }
    }
}
