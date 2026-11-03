using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using LockPad.App.ViewModels;
using LockPad.Core;
using KeePassLib;
using KeePassLib.Security;
using KeePassLib.Serialization;
using NUnit.Framework;

namespace LockPad.Tests
{
    [TestFixture]
    public class UnlockViewModelTests
    {
        private const string WrongMessage = "Wrong password or key file.";

        /// <summary>The path-box messages name the box by its label and its Browse button (both pages use the same).</summary>
        internal static readonly string NotebookNotFull =
            "Enter a full path in \"Notebook file\" or use Browse notebook" + (char)0x2026;
        internal static readonly string KeyFileNotFull =
            "Enter a full path in \"Key file (optional)\" or use Browse key file" + (char)0x2026;
        private const string ChooseNotebook = "Choose the notebook to open in \"Notebook file\".";

        private TempDir _dir;
        private FakeDialogService _dialogs;
        private SettingsStore _settingsStore;
        private AppSettings _settings;
        private List<Notebook> _opened;
        private int _showCreateCalls;

        [SetUp]
        public void SetUp()
        {
            _dir = new TempDir();
            _dialogs = new FakeDialogService();
            _settingsStore = new SettingsStore(_dir.File("settings"));
            _settings = new AppSettings();
            _opened = new List<Notebook>();
            _showCreateCalls = 0;
            _onceNotebook = null; // the fixture instance is shared across tests; each test has its own TempDir
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Notebook nb in _opened) nb.Dispose();
            _dir.Dispose();
        }

        internal static SecureString Secure(string s)
        {
            var ss = new SecureString();
            foreach (char c in s) ss.AppendChar(c);
            ss.MakeReadOnly();
            return ss;
        }

        private static Credentials Pw(string password)
        {
            return new Credentials(Encoding.UTF8.GetBytes(password), null);
        }

        private string CreateNotebook(string name, string password = "pw")
        {
            string p = _dir.File(name);
            using (new NotebookStore().Create(p, Pw(password), TestKdf.Fast())) { }
            return p;
        }

        private UnlockViewModel NewVm(Notebook held = null, string heldWarning = null)
        {
            var vm = new UnlockViewModel(new NotebookStore(), _settingsStore, _settings, _dialogs,
                nb => _opened.Add(nb), () => _showCreateCalls++, held, heldWarning);
            vm.DelayAsync = t => Task.FromResult(0); // never wait out the held-notebook back-off in real time
            return vm;
        }

        private static async Task Unlock(UnlockViewModel vm, string password)
        {
            using (SecureString ss = Secure(password)) await vm.UnlockAsync(ss);
        }

        [Test]
        public void Unlock_PrefillsExistingLastPath_IgnoresMissing()
        {
            string p = CreateNotebook("a.kdbx");
            _settings.LastNotebookPath = p;
            Assert.That(NewVm().NotebookPath, Is.EqualTo(p));

            _settings.LastNotebookPath = _dir.File("missing.kdbx");
            Assert.That(NewVm().NotebookPath, Is.EqualTo(""));

            _settings.LastNotebookPath = null;
            Assert.That(NewVm().NotebookPath, Is.EqualTo(""));
        }

        // ---- Bug fix 2026-10-21: a notebook path from the command line ("Open with" / double-click) ----

        private static string RejectedStartup(string shown)
        {
            return "The file \"" + shown + "\" cannot be opened. Notebooks must be existing files on a local drive.";
        }

        private UnlockViewModel NewStartupVm(string startupPath)
        {
            return new UnlockViewModel(new NotebookStore(), _settingsStore, _settings, _dialogs,
                nb => _opened.Add(nb), () => _showCreateCalls++, null, null, startupPath);
        }

        private UnlockViewModel NewStartupVm(string startupPath, Func<string, bool> fileExists,
            Func<string, string> requireLocalFile)
        {
            return new UnlockViewModel(new NotebookStore(), _settingsStore, _settings, _dialogs,
                nb => _opened.Add(nb), () => _showCreateCalls++, null, null, startupPath, fileExists, requireLocalFile);
        }

        [Test]
        public void StartupPath_ValidLocalFile_BeatsLastPath()
        {
            string last = CreateNotebook("last.kdbx");
            string arg = CreateNotebook("arg.kdbx");
            _settings.LastNotebookPath = last;

            UnlockViewModel vm = NewStartupVm(arg);

            Assert.That(vm.NotebookPath, Is.EqualTo(arg));
            Assert.That(vm.ErrorMessage, Is.Null);
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.None));
        }

        [Test]
        public void StartupPath_NullOrBlank_KeepsTheLastPath()
        {
            string last = CreateNotebook("last.kdbx");
            _settings.LastNotebookPath = last;
            foreach (string none in new[] { null, "", "   " })
            {
                UnlockViewModel vm = NewStartupVm(none);
                Assert.That(vm.NotebookPath, Is.EqualTo(last), "[" + none + "]");
                Assert.That(vm.ErrorMessage, Is.Null);
            }
        }

        /// <summary>A backup name is pre-filled as usual; opening it follows the existing read-only rules.</summary>
        [Test]
        public void StartupPath_BackupName_IsPrefilled()
        {
            string bak = _dir.File("nb.kdbx.bak");
            File.Move(CreateNotebook("nb.kdbx"), bak);
            UnlockViewModel vm = NewStartupVm(bak);
            Assert.That(vm.NotebookPath, Is.EqualTo(bak));
            Assert.That(vm.ErrorMessage, Is.Null);
        }

        [Test]
        public void StartupPath_Rejected_EmptyField_ExactError_NoFallbackToLastPath()
        {
            string last = CreateNotebook("last.kdbx");
            _settings.LastNotebookPath = last;
            string[] rejected =
            {
                _dir.File("missing.kdbx"),
                _dir.Path, // a folder, not a file
                @"\\server\share\n.kdbx",
                "//server/share/n.kdbx",
                @"\\?\C:\n.kdbx",
                @"\\.\C:\n.kdbx",
                @"\??\C:\n.kdbx",
                "n.kdbx", // relative
                "http://x/n.kdbx",
                @"C:\bad|name?.kdbx",
            };
            foreach (string arg in rejected)
            {
                UnlockViewModel vm = NewStartupVm(arg);
                Assert.That(vm.NotebookPath, Is.EqualTo(""), arg);
                Assert.That(vm.ErrorMessage, Is.EqualTo(RejectedStartup(arg)), arg);
                Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.NotebookPath), arg);
            }
            Assert.That(_settings.LastNotebookPath, Is.EqualTo(last), "never changed by the argument");
        }

        /// <summary>A mapped network drive, through LocalPath's drive-type seam: refused before File.Exists, and no
        /// fallback (the remembered path is never probed either). The control proves the seam is the one consulted.</summary>
        [Test]
        public void StartupPath_NetworkDrive_Rejected_NeverProbed()
        {
            _settings.LastNotebookPath = CreateNotebook("last.kdbx");
            const string arg = @"Z:\notes\n.kdbx";
            var probed = new List<string>();
            var roots = new List<string>();
            UnlockViewModel vm = NewStartupVm(arg, p => { probed.Add(p); return true; },
                p => LocalPath.RequireLocalFile(p, root =>
                {
                    roots.Add(root);
                    return string.Equals(root, @"Z:\", StringComparison.OrdinalIgnoreCase) ? DriveType.Network : DriveType.Fixed;
                }));

            Assert.That(vm.NotebookPath, Is.EqualTo(""));
            Assert.That(vm.ErrorMessage, Is.EqualTo(RejectedStartup(arg)));
            Assert.That(roots, Does.Contain(@"Z:\"));
            Assert.That(probed, Is.Empty, "neither the argument nor the remembered path reached File.Exists");

            UnlockViewModel control = NewStartupVm(arg, p => { probed.Add(p); return true; },
                p => LocalPath.RequireLocalFile(p, root => DriveType.Fixed));
            Assert.That(control.NotebookPath, Is.EqualTo(arg));
            Assert.That(control.ErrorMessage, Is.Null);
            Assert.That(probed, Is.EqualTo(new[] { arg }));
        }

        [TestCase(@"\\server\share\n.kdbx")]
        [TestCase("//server/share/n.kdbx")]
        [TestCase(@"\\?\C:\n.kdbx")]
        public void StartupPath_UncOrDevicePath_NeverReachesFileExists(string arg)
        {
            _settings.LastNotebookPath = CreateNotebook("last.kdbx");
            var probed = new List<string>();
            UnlockViewModel vm = NewStartupVm(arg, p => { probed.Add(p); return true; }, LocalPath.RequireLocalFile);

            Assert.That(vm.NotebookPath, Is.EqualTo(""));
            Assert.That(vm.ErrorMessage, Is.EqualTo(RejectedStartup(arg)));
            Assert.That(probed, Is.Empty);
        }

        [Test]
        public void StartupPath_HugeArgument_ShownCappedAt260Characters()
        {
            string huge = @"C:\" + new string('a', 997);
            Assert.That(huge.Length, Is.EqualTo(1000));
            UnlockViewModel vm = NewStartupVm(huge);
            Assert.That(vm.NotebookPath, Is.EqualTo(""));
            Assert.That(vm.ErrorMessage, Is.EqualTo(RejectedStartup(huge.Substring(0, 260) + "...")));
            Assert.That(vm.ErrorMessage, Does.Not.Contain(huge.Substring(0, 261)));

            string exact = @"C:\missing\" + new string('b', 249);
            Assert.That(exact.Length, Is.EqualTo(260));
            Assert.That(NewStartupVm(exact).ErrorMessage, Is.EqualTo(RejectedStartup(exact)), "260 characters: not cut");

            // Never cut between the two halves of a surrogate pair (positions 259 and 260).
            string pair = @"C:\" + new string('c', 256) + "\uD83D\uDE00" + new string('d', 50);
            Assert.That(NewStartupVm(pair).ErrorMessage, Is.EqualTo(RejectedStartup(pair.Substring(0, 259) + "...")));
        }

        /// <summary>Pre-fill only: a valid argument opens nothing, shows no dialog, writes no settings and leaves the
        /// file untouched. LastNotebookPath changes only after a successful unlock.</summary>
        [Test]
        public void StartupPath_Valid_OpensNothing_WritesNoSettings()
        {
            string arg = CreateNotebook("arg.kdbx");
            byte[] before = File.ReadAllBytes(arg);

            UnlockViewModel vm = NewStartupVm(arg);

            Assert.That(vm.NotebookPath, Is.EqualTo(arg));
            Assert.That(vm.IsBusy, Is.False);
            Assert.That(_opened, Is.Empty);
            Assert.That(_dialogs.Calls, Is.Empty);
            Assert.That(_settings.LastNotebookPath, Is.Null);
            Assert.That(File.Exists(Path.Combine(_dir.File("settings"), "settings.json")), Is.False, "settings not saved");
            Assert.That(File.ReadAllBytes(arg), Is.EqualTo(before));
            Assert.That(File.Exists(NotebookStore.BackupPath(arg)), Is.False);
        }

        [Test]
        public async Task StartupPath_UnlockSucceeds_ThenRemembered()
        {
            _settings.LastNotebookPath = CreateNotebook("last.kdbx");
            string arg = CreateNotebook("arg.kdbx");
            UnlockViewModel vm = NewStartupVm(arg);

            await Unlock(vm, "pw");

            Assert.That(_opened.Count, Is.EqualTo(1));
            Assert.That(_settings.LastNotebookPath, Is.EqualTo(Path.GetFullPath(arg)));
        }

        [Test]
        public void Unlock_UrlLastPath_PrefillsEmpty()
        {
            _settings.LastNotebookPath = "http://x/y.kdbx";
            Assert.That(NewVm().NotebookPath, Is.EqualTo(""));

            _settings.LastNotebookPath = "file:///C:/y.kdbx";
            Assert.That(NewVm().NotebookPath, Is.EqualTo(""));

            _settings.LastNotebookPath = "C:\\bad|name?.kdbx";
            Assert.That(NewVm().NotebookPath, Is.EqualTo(""));
        }

        /// <summary>C1: a UNC LastNotebookPath from settings.json is discarded before any file-system call. Counting fake
        /// for the existence probe; the local path control proves the fake is the one consulted.</summary>
        [TestCase(@"\\nonexistent-host\share\nb.kdbx")]
        [TestCase(@"//nonexistent-host/share/nb.kdbx")]
        public void Prefill_UncLastPath_NeverProbed(string unc)
        {
            var probed = new List<string>();
            string result = UnlockViewModel.ExistingLocalPathOrEmpty(unc, p => { probed.Add(p); return true; });
            Assert.That(result, Is.EqualTo(""));
            Assert.That(probed, Is.Empty, "the UNC path never reached File.Exists");

            string local = _dir.File("a.kdbx");
            Assert.That(UnlockViewModel.ExistingLocalPathOrEmpty(local, p => { probed.Add(p); return true; }), Is.EqualTo(local));
            Assert.That(probed, Is.EqualTo(new[] { local }));
        }

        [Test]
        public async Task Unlock_UncPath_SetsLocalOnlyError_NothingOpened()
        {
            _settings.LastNotebookPath = @"\\nonexistent-host\share\nb.kdbx";
            UnlockViewModel vm = NewVm();
            Assert.That(vm.NotebookPath, Is.EqualTo(""), "not pre-filled");

            foreach (string unc in new[] { @"\\nonexistent-host\share\nb.kdbx", "//nonexistent-host/share/nb.kdbx" })
            {
                vm.NotebookPath = unc;
                await Unlock(vm, "pw");
                Assert.That(vm.ErrorMessage, Is.EqualTo("Only files on this PC can be opened."), unc);
                Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.NotebookPath), unc);
            }
            Assert.That(_opened, Is.Empty);
        }

        /// <summary>C8 (review M8): malformed and too-long paths get the invalid-path message (naming the box), not "Only
        /// files on this PC". An empty notebook path keeps its own, more specific message (checked first).</summary>
        [Test]
        public async Task Unlock_InvalidPaths_ShowInvalidPathMessage()
        {
            UnlockViewModel vm = NewVm();
            foreach (string bad in new[] { @"C:\a<b", LocalPathTests.TooLongPath })
            {
                vm.NotebookPath = bad;
                vm.KeyFilePath = null;
                await Unlock(vm, "pw");
                Assert.That(vm.ErrorMessage, Is.EqualTo(NotebookNotFull), "notebook");
                Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.NotebookPath));

                vm.NotebookPath = CreateNotebookOnce();
                vm.KeyFilePath = bad;
                await Unlock(vm, "pw");
                Assert.That(vm.ErrorMessage, Is.EqualTo(KeyFileNotFull), "key file");
                Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.KeyFilePath));
            }

            vm.KeyFilePath = null;
            vm.NotebookPath = "";
            await Unlock(vm, "pw");
            Assert.That(vm.ErrorMessage, Is.EqualTo(ChooseNotebook));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.NotebookPath));

            vm.NotebookPath = @"\\server\share\a.kdbx";
            await Unlock(vm, "pw");
            Assert.That(vm.ErrorMessage, Is.EqualTo("Only files on this PC can be opened."), "non-local keeps its message");
            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task Unlock_ErrorField_ResetsOnlyWhenTheUserEditsThatBox()
        {
            UnlockViewModel vm = NewVm();
            vm.NotebookPath = "a.kdbx";
            await Unlock(vm, "pw");
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.NotebookPath));

            vm.KeyFilePath = "k";
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.NotebookPath), "another box");
            vm.NotebookPath = "a.kdbx2";
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.None));

            vm.NotebookPath = CreateNotebookOnce();
            vm.KeyFilePath = "k.keyx";
            await Unlock(vm, "pw");
            Assert.That(vm.ErrorMessage, Is.EqualTo(KeyFileNotFull));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.KeyFilePath));
            vm.NotebookPath = CreateNotebookOnce() + " ";
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.KeyFilePath), "another box");
            vm.KeyFilePath = null;
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.None));

            vm.NotebookPath = CreateNotebookOnce();
            await Unlock(vm, "pw");
            Assert.That(vm.ErrorMessage, Is.Null);
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.None), "success");
        }

        [Test]
        public async Task Unlock_PasswordErrors_BelongToThePasswordBox_BrowseClears()
        {
            UnlockViewModel vm = NewVm();
            vm.NotebookPath = CreateNotebookOnce();
            await Unlock(vm, "");
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.Password));
            await Unlock(vm, "nope");
            Assert.That(vm.ErrorMessage, Is.EqualTo(WrongMessage));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.Password));

            vm.NotebookPath = "";
            await Unlock(vm, "pw");
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.NotebookPath));
            _dialogs.OpenPaths.Enqueue(CreateNotebookOnce());
            vm.BrowseNotebookCommand.Execute(null);
            Assert.That(vm.ErrorMessage, Is.Null);
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.None));
        }

        [Test]
        public async Task Unlock_MissingNotebook_BelongsToTheNotebookBox()
        {
            UnlockViewModel vm = NewVm();
            vm.NotebookPath = _dir.File("missing.kdbx");
            await Unlock(vm, "pw");
            Assert.That(vm.ErrorMessage, Is.EqualTo("File not found."));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.NotebookPath));
        }

        /// <summary>
        /// Which box an open failure belongs to. A held notebook is matched in memory and its notebook box is read-only,
        /// so a FileNotFoundException there (only possible if the key file vanishes between its check and its read, a
        /// race the end-to-end tests cannot provoke) belongs to no box.
        /// </summary>
        [Test]
        public void OpenFailureField_PerExceptionAndHeldState()
        {
            var notFound = new FileNotFoundException("File not found.");
            var format = new NotebookFormatException("bad", null);
            Assert.That(UnlockViewModel.ErrorFieldForOpenFailure(notFound, false), Is.EqualTo(ErrorField.NotebookPath));
            Assert.That(UnlockViewModel.ErrorFieldForOpenFailure(notFound, true), Is.EqualTo(ErrorField.None));
            Assert.That(UnlockViewModel.ErrorFieldForOpenFailure(format, false), Is.EqualTo(ErrorField.NotebookPath));
            Assert.That(UnlockViewModel.ErrorFieldForOpenFailure(format, true), Is.EqualTo(ErrorField.None));
            Assert.That(UnlockViewModel.ErrorFieldForOpenFailure(new WrongCredentialsException(), false),
                Is.EqualTo(ErrorField.Password));
            Assert.That(UnlockViewModel.ErrorFieldForOpenFailure(new WrongCredentialsException(), true),
                Is.EqualTo(ErrorField.Password));
            Assert.That(UnlockViewModel.ErrorFieldForOpenFailure(new IOException("x"), false), Is.EqualTo(ErrorField.None));
        }

        [Test]
        public void ErrorText_InvalidPath_IsTheInvalidPathMessage()
        {
            Assert.That(NotebookErrorText.Describe(new InvalidPathException("Path is empty."), "x: "),
                Is.EqualTo("Enter a full path or use Browse."));
            Assert.That(NotebookErrorText.Describe(new NonLocalPathException("Only local file paths are allowed."), "x: "),
                Is.EqualTo("Only files on this PC can be opened."));
        }

        private string _onceNotebook;

        private string CreateNotebookOnce()
        {
            return _onceNotebook ?? (_onceNotebook = CreateNotebook("once.kdbx"));
        }

        [Test]
        public async Task Unlock_UncKeyFile_SetsLocalOnlyError()
        {
            string p = CreateNotebook("a.kdbx");
            UnlockViewModel vm = NewVm();
            vm.NotebookPath = p;
            foreach (string unc in new[] { @"\\nonexistent-host\share\k.keyx", "//nonexistent-host/share/k.keyx" })
            {
                vm.KeyFilePath = unc;
                await Unlock(vm, "pw");
                Assert.That(vm.ErrorMessage, Is.EqualTo("Only files on this PC can be opened."), unc);
                Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.KeyFilePath), unc);
            }
            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task Unlock_Correct_CallsOnOpened_SavesLastPath()
        {
            string p = CreateNotebook("a.kdbx");
            AppSettings shared = _settings;
            UnlockViewModel vm = NewVm();
            vm.NotebookPath = p;

            await Unlock(vm, "pw");

            Assert.That(_opened.Count, Is.EqualTo(1));
            Assert.That(_opened[0].FilePath, Is.EqualTo(Path.GetFullPath(p)));
            Assert.That(_opened[0].IsReadOnly, Is.False);
            Assert.That(vm.ErrorMessage, Is.Null);
            Assert.That(vm.IsBusy, Is.False);
            Assert.That(_settings, Is.SameAs(shared));
            Assert.That(_settings.LastNotebookPath, Is.EqualTo(Path.GetFullPath(p)));
            Assert.That(_settingsStore.Load().LastNotebookPath, Is.EqualTo(Path.GetFullPath(p)));
            Assert.That(_dialogs.Calls, Is.Empty);
        }

        [Test]
        public async Task Unlock_Wrong_SetsError_NoCallback()
        {
            string p = CreateNotebook("a.kdbx");
            UnlockViewModel vm = NewVm();
            vm.NotebookPath = p;

            await Unlock(vm, "nope");

            Assert.That(vm.ErrorMessage, Is.EqualTo(WrongMessage));
            Assert.That(_opened, Is.Empty);
            Assert.That(vm.IsBusy, Is.False);
            Assert.That(_settings.LastNotebookPath, Is.Null);
        }

        [Test]
        public async Task Unlock_Wrong_ThenCorrect_ClearsError()
        {
            string p = CreateNotebook("a.kdbx");
            UnlockViewModel vm = NewVm();
            vm.NotebookPath = p;

            await Unlock(vm, "nope");
            await Unlock(vm, "pw");

            Assert.That(vm.ErrorMessage, Is.Null);
            Assert.That(_opened.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task Unlock_Missing_SetsFileNotFound()
        {
            UnlockViewModel vm = NewVm();
            vm.NotebookPath = _dir.File("missing.kdbx");

            await Unlock(vm, "pw");

            Assert.That(vm.ErrorMessage, Is.EqualTo("File not found."));
            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task Unlock_GarbageFile_SetsFormatError()
        {
            string p = _dir.File("junk.kdbx");
            File.WriteAllBytes(p, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 });
            UnlockViewModel vm = NewVm();
            vm.NotebookPath = p;

            await Unlock(vm, "pw");

            Assert.That(vm.ErrorMessage, Is.EqualTo("This file is not a valid notebook or KeePass database."));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.NotebookPath));
            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task Unlock_UrlPath_SetsLocalOnlyError()
        {
            UnlockViewModel vm = NewVm();
            vm.NotebookPath = "http://x/y.kdbx";

            await Unlock(vm, "pw");

            Assert.That(vm.ErrorMessage, Is.EqualTo("Only files on this PC can be opened."));
            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task Unlock_EmptyPasswordOrPath_SetsError_NoCallback()
        {
            string p = CreateNotebook("a.kdbx");
            UnlockViewModel vm = NewVm();
            vm.NotebookPath = p;

            await Unlock(vm, "");
            Assert.That(vm.ErrorMessage, Is.EqualTo("Enter a password."));

            await vm.UnlockAsync(null);
            Assert.That(vm.ErrorMessage, Is.EqualTo("Enter a password."));

            vm.NotebookPath = "  ";
            await Unlock(vm, "pw");
            Assert.That(vm.ErrorMessage, Is.EqualTo(ChooseNotebook));

            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task Unlock_Unmarked_OpensReadOnly_Warns()
        {
            string p = _dir.File("foreign.kdbx");
            using (Credentials c = Pw("pw"))
            {
                var db = new PwDatabase();
                db.New(IOConnectionInfo.FromPath(p), c.CompositeKey);
                db.KdfParameters = TestKdf.Fast();
                var e = new PwEntry(true, true);
                e.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "t"));
                e.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "SECRET-PW"));
                db.RootGroup.AddEntry(e, true);
                db.SaveAs(IOConnectionInfo.FromPath(p), false, null);
                db.Close();
            }
            UnlockViewModel vm = NewVm();
            vm.NotebookPath = p;

            await Unlock(vm, "pw");

            Assert.That(_dialogs.Calls, Does.Contain("ShowWarning"));
            Assert.That(_dialogs.LastWarningTitle, Is.EqualTo("Read-only"));
            Assert.That(_dialogs.LastWarningMessage, Is.EqualTo(
                "This file was not created by LockPad " + (char)0x2014
                + " it may be your password database. It is opened read-only."));
            Assert.That(_opened.Count, Is.EqualTo(1));
            Assert.That(_opened[0].IsReadOnly, Is.True);
        }

        [Test]
        public async Task Unlock_BackupCopy_OpensReadOnly_WarnsWithBackupMessage()
        {
            string p = _dir.File("nb.kdbx.bak2");
            File.Move(CreateNotebook("nb.kdbx"), p); // Create refuses backup names
            UnlockViewModel vm = NewVm();
            vm.NotebookPath = p;

            await Unlock(vm, "pw");

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ShowWarning" }));
            Assert.That(_dialogs.LastWarningTitle, Is.EqualTo("Read-only"));
            Assert.That(_dialogs.LastWarningMessage, Is.EqualTo(
                "This is a backup copy. It is opened read-only. To restore it, close LockPad and rename the file."));
            Assert.That(_opened.Count, Is.EqualTo(1));
            Assert.That(_opened[0].IsReadOnly, Is.True);
        }

        [Test]
        public async Task Unlock_ThroughSymbolicLink_OpensReadOnly_WarnsWithLinkMessage()
        {
            string real = CreateNotebook("real.kdbx");
            string link = _dir.File("link.kdbx");
            NotebookStoreOpenTests.SymlinkOrIgnore(link, real);
            UnlockViewModel vm = NewVm();
            vm.NotebookPath = link;

            await Unlock(vm, "pw");

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ShowWarning" }));
            Assert.That(_dialogs.LastWarningTitle, Is.EqualTo("Read-only"));
            Assert.That(_dialogs.LastWarningMessage, Is.EqualTo(
                "This notebook is a link to another file. It is opened read-only. Open the real file instead."));
            Assert.That(_opened[0].IsReadOnly, Is.True);
        }

        [Test]
        public async Task Unlock_Held_RightPassword_ReturnsSameInstance()
        {
            string p = CreateNotebook("a.kdbx");
            Notebook held = new NotebookStore().Open(p, Pw("pw"));
            try
            {
                UnlockViewModel vm = NewVm(held, "held warning");
                Assert.That(vm.HeldWarning, Is.EqualTo("held warning"));
                Assert.That(vm.NotebookPath, Is.EqualTo(held.FilePath));

                await Unlock(vm, "pw");

                Assert.That(_opened.Count, Is.EqualTo(1));
                Assert.That(_opened[0], Is.SameAs(held));
                Assert.That(vm.ErrorMessage, Is.Null);
                Assert.That(held.Credentials, Is.Not.Null);
                Assert.That(held.CredentialsMatch(Pw("pw")), Is.True);
            }
            finally
            {
                _opened.Remove(held);
                held.Dispose();
            }
        }

        [Test]
        public async Task Unlock_Held_WrongPassword_SetsError()
        {
            string p = CreateNotebook("a.kdbx");
            using (Notebook held = new NotebookStore().Open(p, Pw("pw")))
            {
                UnlockViewModel vm = NewVm(held, "w");

                await Unlock(vm, "nope");

                Assert.That(vm.ErrorMessage, Is.EqualTo(WrongMessage));
                Assert.That(_opened, Is.Empty);
            }
        }

        [Test]
        public async Task Unlock_Wrong_DisposesCredentials()
        {
            // The attempt's credentials are disposed; the held notebook keeps its own and stays usable.
            string p = CreateNotebook("a.kdbx");
            var store = new NotebookStore();
            using (Notebook held = store.Open(p, Pw("pw")))
            {
                held.AddNote("n");
                UnlockViewModel vm = NewVm(held, "w");

                await Unlock(vm, "nope");
                await Unlock(vm, "also wrong");

                Assert.That(vm.ErrorMessage, Is.EqualTo(WrongMessage));
                Assert.That(held.Credentials, Is.Not.Null);
                Assert.That(held.CredentialsMatch(Pw("pw")), Is.True);
                Assert.That(store.Save(held, false).Status, Is.EqualTo(SaveStatus.Saved));

                await Unlock(vm, "pw");
                Assert.That(_opened.Count, Is.EqualTo(1));
                Assert.That(_opened[0], Is.SameAs(held));
                _opened.Clear();
            }
        }

        [Test]
        public void Unlock_Held_NewNotebookAndBrowseNotebookDisabled()
        {
            string p = CreateNotebook("a.kdbx");
            using (Notebook held = new NotebookStore().Open(p, Pw("pw")))
            {
                UnlockViewModel vm = NewVm(held, "w");
                Assert.That(vm.NewNotebookCommand.CanExecute(null), Is.False);
                Assert.That(vm.BrowseNotebookCommand.CanExecute(null), Is.False);
                Assert.That(vm.BrowseKeyFileCommand.CanExecute(null), Is.True);
            }
        }

        [Test]
        public async Task Unlock_DoubleInvoke_CallsOnOpenedOnce()
        {
            string p = CreateNotebook("a.kdbx");
            UnlockViewModel vm = NewVm();
            vm.NotebookPath = p;

            using (SecureString a = Secure("pw"))
            using (SecureString b = Secure("pw"))
            {
                Task first = vm.UnlockAsync(a);
                Task second = vm.UnlockAsync(b);
                await Task.WhenAll(first, second);
            }

            Assert.That(_opened.Count, Is.EqualTo(1));
            Assert.That(vm.IsBusy, Is.False);
        }

        [Test]
        public async Task Unlock_BusyDuringOpen_DisablesCommands()
        {
            string p = CreateNotebook("a.kdbx");
            bool busyInCallback = false, canBrowse = true, canBrowseKey = true, canNew = true;
            UnlockViewModel vm = null;
            int canExecuteChanged = 0;
            vm = new UnlockViewModel(new NotebookStore(), _settingsStore, _settings, _dialogs, nb =>
            {
                _opened.Add(nb);
                busyInCallback = vm.IsBusy;
                canBrowse = vm.BrowseNotebookCommand.CanExecute(null);
                canBrowseKey = vm.BrowseKeyFileCommand.CanExecute(null);
                canNew = vm.NewNotebookCommand.CanExecute(null);
            }, () => { });
            vm.BrowseNotebookCommand.CanExecuteChanged += (s, e) => canExecuteChanged++;
            vm.NotebookPath = p;
            Assert.That(vm.NewNotebookCommand.CanExecute(null), Is.True);

            await Unlock(vm, "pw");

            Assert.That(busyInCallback, Is.True);
            Assert.That(canBrowse || canBrowseKey || canNew, Is.False);
            Assert.That(canExecuteChanged, Is.GreaterThanOrEqualTo(2));
            Assert.That(vm.BrowseNotebookCommand.CanExecute(null), Is.True);
        }

        [Test]
        public void Commands_BrowseAndNewNotebook()
        {
            UnlockViewModel vm = NewVm();
            _dialogs.OpenPaths.Enqueue(_dir.File("picked.kdbx"));
            _dialogs.KeyFiles.Enqueue(_dir.File("k.keyx"));

            vm.BrowseNotebookCommand.Execute(null);
            vm.BrowseKeyFileCommand.Execute(null);
            Assert.That(vm.NotebookPath, Is.EqualTo(_dir.File("picked.kdbx")));
            Assert.That(vm.KeyFilePath, Is.EqualTo(_dir.File("k.keyx")));

            // Cancelled pickers leave the values unchanged.
            vm.BrowseNotebookCommand.Execute(null);
            vm.BrowseKeyFileCommand.Execute(null);
            Assert.That(vm.NotebookPath, Is.EqualTo(_dir.File("picked.kdbx")));
            Assert.That(vm.KeyFilePath, Is.EqualTo(_dir.File("k.keyx")));

            vm.NewNotebookCommand.Execute(null);
            Assert.That(_showCreateCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task Unlock_Held_WrongAttempts_BackOff_1_2_4_8_8_ThenResetAfterMatch()
        {
            string p = CreateNotebook("a.kdbx");
            using (Notebook held = new NotebookStore().Open(p, Pw("pw")))
            {
                UnlockViewModel vm = NewVm(held, "w");
                var delays = new List<double>();
                var busyDuringDelay = new List<bool>();
                vm.DelayAsync = t =>
                {
                    delays.Add(t.TotalSeconds);
                    busyDuringDelay.Add(vm.IsBusy);
                    return Task.FromResult(0);
                };

                for (int i = 0; i < 5; i++) await Unlock(vm, "nope");
                Assert.That(delays, Is.EqualTo(new[] { 1.0, 2.0, 4.0, 8.0, 8.0 }));
                Assert.That(busyDuringDelay, Is.All.True);
                Assert.That(vm.ErrorMessage, Is.EqualTo(WrongMessage));
                Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.Password));

                await Unlock(vm, "pw");
                Assert.That(delays.Count, Is.EqualTo(5), "a correct password is never delayed");
                Assert.That(_opened, Is.EqualTo(new[] { held }));

                await Unlock(vm, "nope");
                Assert.That(delays[5], Is.EqualTo(1.0), "the back-off restarts after a match");
                _opened.Clear();
            }
        }

        [Test]
        public async Task Unlock_Held_Wrong_ShowsCountdownDuringDelay_ThenPlainError()
        {
            string p = CreateNotebook("a.kdbx");
            using (Notebook held = new NotebookStore().Open(p, Pw("pw")))
            {
                UnlockViewModel vm = NewVm(held, "w");
                var duringDelay = new List<string>();
                var fieldDuringDelay = new List<ErrorField>();
                vm.DelayAsync = t =>
                {
                    duringDelay.Add(vm.ErrorMessage + "|" + vm.IsBusy);
                    fieldDuringDelay.Add(vm.ErrorField);
                    return Task.FromResult(0);
                };

                await Unlock(vm, "nope");
                Assert.That(vm.ErrorMessage, Is.EqualTo(WrongMessage));
                await Unlock(vm, "nope");
                Assert.That(vm.ErrorMessage, Is.EqualTo(WrongMessage));
                await Unlock(vm, "nope");
                Assert.That(vm.IsBusy, Is.False);

                Assert.That(duringDelay, Is.EqualTo(new[]
                {
                    "Wrong password or key file. Try again in 1 s.|True",
                    "Wrong password or key file. Try again in 2 s.|True",
                    "Wrong password or key file. Try again in 4 s.|True"
                }));
                Assert.That(fieldDuringDelay, Is.EqualTo(new[] { ErrorField.Password, ErrorField.Password, ErrorField.Password }));
                Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.Password));
            }
        }

        [Test]
        public async Task Unlock_Held_DelayInProgress_SecondCallIgnored()
        {
            string p = CreateNotebook("a.kdbx");
            using (Notebook held = new NotebookStore().Open(p, Pw("pw")))
            {
                UnlockViewModel vm = NewVm(held, "w");
                var gate = new TaskCompletionSource<int>();
                vm.DelayAsync = t => gate.Task;

                // The SecureString stays alive until the call has finished: the background match reads it.
                using (SecureString wrong = Secure("nope"))
                {
                    Task first = vm.UnlockAsync(wrong);
                    // The delay is pending: the countdown is shown and the page stays busy.
                    for (int i = 0; i < 200 && vm.ErrorMessage == null; i++) await Task.Delay(10);
                    Assert.That(vm.ErrorMessage, Is.EqualTo("Wrong password or key file. Try again in 1 s."));
                    Assert.That(vm.IsBusy, Is.True);
                    Assert.That(vm.IsWaiting, Is.True);

                    await Unlock(vm, "pw"); // ignored while busy
                    Assert.That(_opened, Is.Empty);

                    gate.SetResult(0);
                    await first;
                }
                Assert.That(vm.ErrorMessage, Is.EqualTo(WrongMessage));
                Assert.That(vm.IsBusy, Is.False);
                Assert.That(vm.IsWaiting, Is.False);
            }
        }

        [Test]
        public async Task Unlock_Held_IsWaiting_TrueOnlyDuringBackOffDelay()
        {
            string p = CreateNotebook("a.kdbx");
            using (Notebook held = new NotebookStore().Open(p, Pw("pw")))
            {
                var waitingDuringDelay = new List<bool>();
                var changes = new List<bool>();
                bool waitingInCallback = true;
                UnlockViewModel vm = null;
                vm = new UnlockViewModel(new NotebookStore(), _settingsStore, _settings, _dialogs,
                    nb => waitingInCallback = vm.IsWaiting, () => { }, held, "w");
                vm.DelayAsync = t =>
                {
                    waitingDuringDelay.Add(vm.IsWaiting && vm.IsBusy);
                    return Task.FromResult(0);
                };
                vm.PropertyChanged += (s, e) => { if (e.PropertyName == "IsWaiting") changes.Add(vm.IsWaiting); };
                Assert.That(vm.IsWaiting, Is.False);

                await Unlock(vm, "nope");
                Assert.That(waitingDuringDelay, Is.EqualTo(new[] { true }));
                Assert.That(vm.IsWaiting, Is.False);
                Assert.That(changes, Is.EqualTo(new[] { true, false }));

                await Unlock(vm, "pw");
                Assert.That(waitingInCallback, Is.False, "not waiting while matching or handing off");
                Assert.That(vm.IsWaiting, Is.False);
                Assert.That(changes, Is.EqualTo(new[] { true, false }));
            }
        }

        [Test]
        public async Task Unlock_FileWrongPassword_NoBackOffDelay()
        {
            // Opening a file already costs an Argon2 run per guess; the back-off is only for held notebooks.
            string p = CreateNotebook("a.kdbx");
            UnlockViewModel vm = NewVm();
            int delays = 0;
            vm.DelayAsync = t => { delays++; return Task.FromResult(0); };
            vm.NotebookPath = p;

            await Unlock(vm, "nope");

            Assert.That(vm.ErrorMessage, Is.EqualTo(WrongMessage));
            Assert.That(delays, Is.EqualTo(0));
        }

        [Test]
        public async Task Unlock_MissingKeyFile_SaysKeyFileNotFound()
        {
            string p = CreateNotebook("a.kdbx");
            UnlockViewModel vm = NewVm();
            vm.NotebookPath = p;
            vm.KeyFilePath = _dir.File("missing.keyx");

            await Unlock(vm, "pw");

            Assert.That(vm.ErrorMessage, Is.EqualTo("Key file not found."));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.KeyFilePath));
            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task Unlock_Held_MissingKeyFile_SaysKeyFileNotFound()
        {
            string p = CreateNotebook("a.kdbx");
            using (Notebook held = new NotebookStore().Open(p, Pw("pw")))
            {
                UnlockViewModel vm = NewVm(held, "w");
                vm.KeyFilePath = _dir.File("missing.keyx");

                await Unlock(vm, "pw");

                Assert.That(vm.ErrorMessage, Is.EqualTo("Key file not found."));
                Assert.That(_opened, Is.Empty);
            }
        }

        [TestCase("a.kdbx")]
        [TestCase("sub\\a.kdbx")]
        [TestCase("C:a.kdbx")]
        [TestCase("\\a.kdbx")]
        public async Task Unlock_RelativeNotebookPath_Rejected(string path)
        {
            UnlockViewModel vm = NewVm();
            vm.NotebookPath = path;

            await Unlock(vm, "pw");

            Assert.That(vm.ErrorMessage, Is.EqualTo(NotebookNotFull));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.NotebookPath));
            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task Unlock_RelativeKeyFilePath_Rejected()
        {
            string p = CreateNotebook("a.kdbx");
            UnlockViewModel vm = NewVm();
            vm.NotebookPath = p;
            vm.KeyFilePath = "k.keyx";

            await Unlock(vm, "pw");

            Assert.That(vm.ErrorMessage, Is.EqualTo(KeyFileNotFull));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.KeyFilePath));
            Assert.That(_opened, Is.Empty);
        }

        /// <summary>C11 (review M11): a different, existing key file (not a missing one) is a wrong credential.</summary>
        [Test]
        public async Task Unlock_WithADifferentValidKeyFile_SaysWrongPasswordOrKeyFile()
        {
            string rightKey = _dir.File("right.keyx");
            string otherKey = _dir.File("other.keyx");
            Credentials.CreateKeyFile(rightKey);
            Credentials.CreateKeyFile(otherKey);
            string p = _dir.File("a.kdbx");
            using (new NotebookStore().Create(p, new Credentials(Encoding.UTF8.GetBytes("pw"), rightKey), TestKdf.Fast())) { }

            UnlockViewModel vm = NewVm();
            vm.NotebookPath = p;
            vm.KeyFilePath = otherKey;
            await Unlock(vm, "pw");

            Assert.That(vm.ErrorMessage, Is.EqualTo(WrongMessage));
            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task Unlock_WithKeyFile_RequiresKeyFile()
        {
            string key = _dir.File("k.keyx");
            Credentials.CreateKeyFile(key);
            string p = _dir.File("a.kdbx");
            using (new NotebookStore().Create(p, new Credentials(Encoding.UTF8.GetBytes("pw"), key), TestKdf.Fast())) { }

            UnlockViewModel vm = NewVm();
            vm.NotebookPath = p;
            await Unlock(vm, "pw");
            Assert.That(vm.ErrorMessage, Is.EqualTo(WrongMessage));

            vm.KeyFilePath = key;
            await Unlock(vm, "pw");
            Assert.That(vm.ErrorMessage, Is.Null);
            Assert.That(_opened.Count, Is.EqualTo(1));
            Assert.That(_settingsStore.Load().LastNotebookPath, Is.EqualTo(Path.GetFullPath(p)));
            Assert.That(File.ReadAllText(Path.Combine(_dir.File("settings"), "settings.json")), Does.Not.Contain("k.keyx"));
        }
    }
}
