using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using LockPad.App.ViewModels;
using LockPad.Core;
using KeePassLib.Cryptography.KeyDerivation;
using NUnit.Framework;

namespace LockPad.Tests
{
    [TestFixture]
    public class CreateNotebookViewModelTests
    {
        private const string StrongPassword = "q7#Vt9!mZ2@xLp4$Rw8&";
        private const string WeakPassword = "abc";

        private TempDir _dir;
        private FakeDialogService _dialogs;
        private List<Notebook> _created;
        private int _cancelCalls;

        [SetUp]
        public void SetUp()
        {
            _dir = new TempDir();
            _dialogs = new FakeDialogService();
            _created = new List<Notebook>();
            _cancelCalls = 0;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Notebook nb in _created) nb.Dispose();
            _dir.Dispose();
        }

        private static SecureString Secure(string s)
        {
            return UnlockViewModelTests.Secure(s);
        }

        private CreateNotebookViewModel NewVm(Func<KdfParameters> kdf = null)
        {
            return new CreateNotebookViewModel(new NotebookStore(), _dialogs, kdf ?? TestKdf.Fast,
                nb => _created.Add(nb), () => _cancelCalls++);
        }

        private static async Task Create(CreateNotebookViewModel vm, string password, string confirm)
        {
            using (SecureString a = Secure(password))
            using (SecureString b = Secure(confirm))
                await vm.CreateAsync(a, b);
        }

        private static Credentials Pw(string password, string keyFile = null)
        {
            return new Credentials(Encoding.UTF8.GetBytes(password), keyFile);
        }

        [Test]
        public async Task Create_Mismatch_SetsError_NoFile()
        {
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = _dir.File("a.kdbx");

            await Create(vm, StrongPassword, StrongPassword + "x");
            Assert.That(vm.ErrorMessage, Is.EqualTo("Passwords do not match."));

            await Create(vm, StrongPassword, "q7#Vt9!mZ2@xLp4$Rw8*"); // same length, last char differs
            Assert.That(vm.ErrorMessage, Is.EqualTo("Passwords do not match."));

            using (SecureString a = Secure(StrongPassword)) await vm.CreateAsync(a, null);
            Assert.That(vm.ErrorMessage, Is.EqualTo("Passwords do not match."));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.Password));

            Assert.That(File.Exists(vm.NotebookPath), Is.False);
            Assert.That(_created, Is.Empty);
            Assert.That(_dialogs.Calls, Is.Empty);
        }

        [Test]
        public async Task Create_EmptyPassword_SetsError()
        {
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = _dir.File("a.kdbx");

            await Create(vm, "", "");
            Assert.That(vm.ErrorMessage, Is.EqualTo("Enter a password."));

            await vm.CreateAsync(null, null);
            Assert.That(vm.ErrorMessage, Is.EqualTo("Enter a password."));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.Password));

            Assert.That(File.Exists(vm.NotebookPath), Is.False);
            Assert.That(_created, Is.Empty);
        }

        [Test]
        public async Task Create_EmptyPath_SetsError()
        {
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = " ";

            await Create(vm, StrongPassword, StrongPassword);

            Assert.That(vm.ErrorMessage, Is.EqualTo("Choose where to save the notebook in \"Notebook file\"."));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.NotebookPath));
            Assert.That(_created, Is.Empty);
        }

        /// <summary>UX fix (manual test): only the key-file box filled, through its Browse button. The error names the
        /// notebook box, and belongs to it.</summary>
        [Test]
        public async Task Create_OnlyKeyFileFilled_ErrorNamesTheNotebookBox()
        {
            CreateNotebookViewModel vm = NewVm();
            _dialogs.KeyFiles.Enqueue(_dir.File("k.keyx"));
            vm.BrowseKeyFileCommand.Execute(null);

            await Create(vm, StrongPassword, StrongPassword);

            Assert.That(vm.ErrorMessage, Is.EqualTo("Choose where to save the notebook in \"Notebook file\"."));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.NotebookPath));
            Assert.That(Directory.GetFiles(_dir.Path), Is.Empty);
        }

        [Test]
        public async Task Create_ErrorField_ResetsOnlyWhenTheUserEditsThatBox()
        {
            CreateNotebookViewModel vm = NewVm();
            await Create(vm, StrongPassword, StrongPassword);
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.NotebookPath));
            var changes = new List<string>();
            vm.PropertyChanged += (s, e) => changes.Add(e.PropertyName);

            vm.KeyFilePath = "x";
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.NotebookPath), "another box");
            vm.NotebookPath = "C";
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.None));
            Assert.That(changes, Does.Contain("ErrorField"));

            vm.NotebookPath = _dir.File("a.kdbx");
            vm.KeyFilePath = "k.keyx";
            await Create(vm, StrongPassword, StrongPassword);
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.KeyFilePath));
            vm.NotebookPath = _dir.File("b.kdbx");
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.KeyFilePath), "another box");
            vm.KeyFilePath = "";
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.None));

            await Create(vm, StrongPassword, StrongPassword);
            Assert.That(vm.ErrorMessage, Is.Null);
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.None), "success");
        }

        [Test]
        public async Task Create_Browse_ClearsTheErrorAndItsField()
        {
            CreateNotebookViewModel vm = NewVm();
            await Create(vm, StrongPassword, StrongPassword);
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.NotebookPath));

            _dialogs.NewPaths.Enqueue(_dir.File("new.kdbx"));
            vm.BrowseNotebookCommand.Execute(null);

            Assert.That(vm.ErrorMessage, Is.Null);
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.None));
        }

        [Test]
        public async Task Create_Weak_Declined_NoFile()
        {
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = _dir.File("a.kdbx");

            await Create(vm, WeakPassword, WeakPassword);

            // C7 (review M6): default No, so Enter or Esc never accepts a password under 60 bits.
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ConfirmDefaultNo" }));
            Assert.That(File.Exists(vm.NotebookPath), Is.False);
            Assert.That(Directory.GetFiles(_dir.Path), Is.Empty, "no file of any kind");
            Assert.That(_created, Is.Empty);
            Assert.That(vm.IsBusy, Is.False);
            // UX follow-up: the cursor goes back to the (cleared) password box; no message is needed.
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.Password));
            Assert.That(vm.ErrorMessage, Is.Null);
        }

        private static readonly string NotebookFolderMissing = "The folder in \"Notebook file\" does not exist. "
            + "Choose an existing folder or use Browse notebook" + (char)0x2026;
        private static readonly string KeyFileFolderMissing = "The folder in \"Key file (optional)\" does not exist. "
            + "Choose an existing folder or use Browse key file" + (char)0x2026;

        [Test]
        public async Task Create_NotebookFolderMissing_NamesTheBox_NothingCreated()
        {
            string missing = Path.Combine(_dir.Path, "no-such-folder");
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = Path.Combine(missing, "a.kdbx");
            vm.KeyFilePath = _dir.File("k.keyx");
            vm.CreateNewKeyFile = true;

            // A weak password: the folder check must refuse before the weak-password prompt is ever shown.
            await Create(vm, WeakPassword, WeakPassword);

            Assert.That(vm.ErrorMessage, Is.EqualTo(NotebookFolderMissing));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.NotebookPath));
            Assert.That(_dialogs.Calls, Is.Empty, "refused before the weak-password prompt");
            Assert.That(Directory.Exists(missing), Is.False);
            Assert.That(File.Exists(vm.NotebookPath), Is.False);
            Assert.That(File.Exists(vm.KeyFilePath), Is.False);
            Assert.That(Directory.GetFiles(_dir.Path), Is.Empty, "no file of any kind");
            Assert.That(_created, Is.Empty);
        }

        [Test]
        public async Task Create_NewKeyFileFolderMissing_NamesTheBox_NothingCreated()
        {
            string missing = Path.Combine(_dir.Path, "no-such-folder");
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = _dir.File("a.kdbx");
            vm.KeyFilePath = Path.Combine(missing, "k.keyx");
            vm.CreateNewKeyFile = true;

            // A weak password: the folder check must refuse before the weak-password prompt is ever shown.
            await Create(vm, WeakPassword, WeakPassword);

            Assert.That(vm.ErrorMessage, Is.EqualTo(KeyFileFolderMissing));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.KeyFilePath));
            Assert.That(_dialogs.Calls, Is.Empty, "refused before the weak-password prompt");
            Assert.That(Directory.Exists(missing), Is.False);
            Assert.That(File.Exists(vm.NotebookPath), Is.False);
            Assert.That(Directory.GetFiles(_dir.Path), Is.Empty, "no file of any kind");
            Assert.That(_created, Is.Empty);

            vm.CreateNewKeyFile = false; // an existing key file in a missing folder is simply not found
            await Create(vm, WeakPassword, WeakPassword);
            Assert.That(vm.ErrorMessage, Is.EqualTo("Key file not found."));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.KeyFilePath));
            Assert.That(_dialogs.Calls, Is.Empty);
            Assert.That(Directory.GetFiles(_dir.Path), Is.Empty);
        }

        [Test]
        public async Task Create_NotebookFolderRemovedDuringKdf_KeepsStoreMessage_BelongsToTheNotebookBox()
        {
            string folder = Path.Combine(_dir.Path, "sub");
            Directory.CreateDirectory(folder);
            CreateNotebookViewModel vm = NewVm(() =>
            {
                Directory.Delete(folder); // the folder disappears after validation, before the write
                return TestKdf.Fast();
            });
            vm.NotebookPath = Path.Combine(folder, "a.kdbx");

            await Create(vm, StrongPassword, StrongPassword);

            Assert.That(vm.ErrorMessage, Is.Not.Null.And.Not.Empty);
            Assert.That(vm.ErrorMessage, Is.Not.EqualTo(NotebookFolderMissing), "the store's own message is kept");
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.NotebookPath));
            Assert.That(_created, Is.Empty);
        }

        /// <summary>An IOException from the store while the notebook's folder still exists (here: a file appeared at the
        /// path during the KDF) is not a missing-folder case and belongs to no box.</summary>
        [Test]
        public async Task Create_FileAppearsDuringKdf_FolderExists_BelongsToNoBox()
        {
            string path = _dir.File("a.kdbx");
            CreateNotebookViewModel vm = NewVm(() =>
            {
                File.WriteAllText(path, "appeared"); // after validation, before the store's own checks
                return TestKdf.Fast();
            });
            vm.NotebookPath = path;

            await Create(vm, StrongPassword, StrongPassword);

            Assert.That(vm.ErrorMessage, Is.EqualTo("File already exists."));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.None));
            Assert.That(File.ReadAllText(path), Is.EqualTo("appeared"), "never replaced");
            Assert.That(_created, Is.Empty);
        }

        [Test]
        public async Task Create_Weak_NewKeyFile_Declined_NoKeyFileEither()
        {
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = _dir.File("a.kdbx");
            vm.KeyFilePath = _dir.File("a.keyx");
            vm.CreateNewKeyFile = true;
            _dialogs.Confirms.Enqueue(false);

            await Create(vm, WeakPassword, WeakPassword);

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ConfirmDefaultNo" }));
            Assert.That(Directory.GetFiles(_dir.Path), Is.Empty);
            Assert.That(_created, Is.Empty);
        }

        [Test]
        public async Task Create_Weak_Accepted_Creates()
        {
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = _dir.File("a.kdbx");
            _dialogs.Confirms.Enqueue(true);

            await Create(vm, WeakPassword, WeakPassword);

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ConfirmDefaultNo" }));
            Assert.That(File.Exists(vm.NotebookPath), Is.True);
            Assert.That(_created.Count, Is.EqualTo(1));
            Assert.That(vm.ErrorMessage, Is.Null);
        }

        [Test]
        public async Task Create_Weak_ConfirmMessageNamesBits()
        {
            var dialogs = new RecordingConfirmDialogs();
            var vm = new CreateNotebookViewModel(new NotebookStore(), dialogs, TestKdf.Fast, nb => _created.Add(nb), () => { });
            vm.NotebookPath = _dir.File("a.kdbx");

            await Create(vm, WeakPassword, WeakPassword);

            uint bits;
            using (SecureString s = Secure(WeakPassword)) bits = PasswordStrength.EstimateBits(s);
            Assert.That(dialogs.Title, Is.EqualTo("Weak password"));
            Assert.That(dialogs.Message, Is.EqualTo("This password is weak (" + bits
                + " bits). Anyone who gets this file could guess it. Use it anyway?"));
        }

        [Test]
        public async Task Create_Strong_CreatesAndCallsBack()
        {
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = _dir.File("a.kdbx");
            using (SecureString s = Secure(StrongPassword)) vm.UpdateStrength(s);
            Assert.That(vm.StrengthBits, Is.GreaterThanOrEqualTo(80u));
            Assert.That(vm.Strength, Is.EqualTo(StrengthRating.Strong));
            Assert.That(vm.StrengthWarning, Is.Null);

            await Create(vm, StrongPassword, StrongPassword);

            Assert.That(vm.ErrorMessage, Is.Null);
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.None));
            Assert.That(vm.IsBusy, Is.False);
            Assert.That(File.Exists(vm.NotebookPath), Is.True);
            Assert.That(_created.Count, Is.EqualTo(1));
            Assert.That(_created[0].FilePath, Is.EqualTo(Path.GetFullPath(vm.NotebookPath)));
            Assert.That(_created[0].IsReadOnly, Is.False);
            Assert.That(_dialogs.Calls, Is.Empty);

            using (Notebook reopened = new NotebookStore().Open(vm.NotebookPath, Pw(StrongPassword)))
                Assert.That(reopened.IsReadOnly, Is.False);
        }

        [Test]
        public async Task Create_ExistingPath_SetsError()
        {
            string p = _dir.File("a.kdbx");
            File.WriteAllText(p, "existing");
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = p;

            await Create(vm, StrongPassword, StrongPassword);

            Assert.That(vm.ErrorMessage, Is.EqualTo("File already exists."));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.NotebookPath));
            Assert.That(File.ReadAllText(p), Is.EqualTo("existing"));
            Assert.That(_created, Is.Empty);
        }

        [Test]
        public async Task Create_BackupCopyName_Refused_NothingCreated()
        {
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = _dir.File("nb.kdbx.bak3");
            vm.KeyFilePath = _dir.File("k.keyx");
            vm.CreateNewKeyFile = true;

            await Create(vm, StrongPassword, StrongPassword);

            Assert.That(vm.ErrorMessage, Is.EqualTo("This name is reserved for backup copies. Choose another name."));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.NotebookPath));
            Assert.That(Directory.GetFiles(_dir.Path), Is.Empty, "no notebook and no key file");
            Assert.That(_created, Is.Empty);
        }

        [Test]
        public async Task Create_UrlPath_SetsLocalOnlyError()
        {
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = "http://x/y.kdbx";

            await Create(vm, StrongPassword, StrongPassword);

            Assert.That(vm.ErrorMessage, Is.EqualTo("Only files on this PC can be opened."));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.NotebookPath));
            Assert.That(_created, Is.Empty);
        }

        /// <summary>C8 (review M8): malformed and too-long paths get the invalid-path message, naming the box; an empty
        /// notebook path keeps its own message (checked first, see Create_EmptyPath_SetsError).</summary>
        [Test]
        public async Task Create_InvalidPaths_ShowInvalidPathMessage_NothingCreated()
        {
            foreach (string bad in new[] { @"C:\a<b", LocalPathTests.TooLongPath })
            {
                CreateNotebookViewModel vm = NewVm();
                vm.NotebookPath = bad;
                await Create(vm, StrongPassword, StrongPassword);
                Assert.That(vm.ErrorMessage, Is.EqualTo(UnlockViewModelTests.NotebookNotFull), "notebook");
                Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.NotebookPath));

                vm = NewVm();
                vm.NotebookPath = _dir.File("a.kdbx");
                vm.KeyFilePath = bad;
                vm.CreateNewKeyFile = true;
                await Create(vm, StrongPassword, StrongPassword);
                Assert.That(vm.ErrorMessage, Is.EqualTo(UnlockViewModelTests.KeyFileNotFull), "key file");
                Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.KeyFilePath));
            }
            Assert.That(Directory.GetFiles(_dir.Path), Is.Empty);
            Assert.That(_created, Is.Empty);
        }

        [TestCase(@"\\nonexistent-host\share\nb.kdbx", null)]
        [TestCase("//nonexistent-host/share/nb.kdbx", null)]
        [TestCase(null, @"\\nonexistent-host\share\k.keyx")]
        [TestCase(null, "//nonexistent-host/share/k.keyx")]
        public async Task Create_UncPath_SetsLocalOnlyError_NothingCreated(string notebook, string keyFile)
        {
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = notebook ?? _dir.File("a.kdbx");
            vm.KeyFilePath = keyFile;
            vm.CreateNewKeyFile = keyFile != null;

            await Create(vm, StrongPassword, StrongPassword);

            Assert.That(vm.ErrorMessage, Is.EqualTo("Only files on this PC can be opened."));
            Assert.That(vm.ErrorField, Is.EqualTo(notebook != null ? ErrorField.NotebookPath : ErrorField.KeyFilePath));
            Assert.That(Directory.GetFiles(_dir.Path), Is.Empty);
            Assert.That(_created, Is.Empty);
        }

        [Test]
        public async Task Create_NewKeyFile_RequiredToReopen()
        {
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = _dir.File("a.kdbx");
            vm.KeyFilePath = _dir.File("a.keyx");
            vm.CreateNewKeyFile = true;

            await Create(vm, StrongPassword, StrongPassword);

            Assert.That(vm.ErrorMessage, Is.Null);
            Assert.That(File.Exists(vm.KeyFilePath), Is.True);
            Assert.That(_created.Count, Is.EqualTo(1));

            var store = new NotebookStore();
            Credentials noKey = Pw(StrongPassword);
            Assert.Throws<WrongCredentialsException>(() => store.Open(vm.NotebookPath, noKey));
            noKey.Dispose();
            using (Notebook nb = store.Open(vm.NotebookPath, Pw(StrongPassword, vm.KeyFilePath)))
                Assert.That(nb.IsReadOnly, Is.False);
        }

        [Test]
        public async Task Create_NewKeyFile_WarnsBackup()
        {
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = _dir.File("a.kdbx");
            vm.KeyFilePath = _dir.File("a.keyx");
            vm.CreateNewKeyFile = true;

            await Create(vm, StrongPassword, StrongPassword);

            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ShowWarning" }));
            Assert.That(_dialogs.LastWarningTitle, Is.EqualTo("Back up your key file"));
            Assert.That(_dialogs.LastWarningMessage, Is.EqualTo("Keep a copy of the key file ("
                + Path.GetFullPath(_dir.File("a.keyx")) + ") somewhere safe. Without it, "
                + "this notebook can never be opened again, even with the right password."));
            Assert.That(_created.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task Create_ExistingKeyFile_NoNewKeyFile_UsesIt()
        {
            string key = _dir.File("k.keyx");
            Credentials.CreateKeyFile(key);
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = _dir.File("a.kdbx");
            vm.KeyFilePath = key;

            await Create(vm, StrongPassword, StrongPassword);

            Assert.That(vm.ErrorMessage, Is.Null);
            Assert.That(_dialogs.Calls, Is.Empty);
            using (Notebook nb = new NotebookStore().Open(vm.NotebookPath, Pw(StrongPassword, key)))
                Assert.That(nb.IsReadOnly, Is.False);
        }

        [Test]
        public async Task Create_NewKeyFile_EmptyKeyPath_SetsError()
        {
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = _dir.File("a.kdbx");
            vm.CreateNewKeyFile = true;
            vm.KeyFilePath = "";

            await Create(vm, StrongPassword, StrongPassword);

            Assert.That(vm.ErrorMessage, Is.EqualTo("Choose where to save the new key file in \"Key file (optional)\"."));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.KeyFilePath));
            Assert.That(File.Exists(vm.NotebookPath), Is.False);
            Assert.That(_created, Is.Empty);
        }

        [Test]
        public async Task Create_NewKeyFile_ExistingKeyPath_SetsError_KeyFileUntouched()
        {
            string key = _dir.File("k.keyx");
            File.WriteAllText(key, "mine");
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = _dir.File("a.kdbx");
            vm.KeyFilePath = key;
            vm.CreateNewKeyFile = true;

            await Create(vm, StrongPassword, StrongPassword);

            Assert.That(vm.ErrorMessage, Is.EqualTo("File already exists."));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.KeyFilePath), "creating the new key file failed");
            Assert.That(File.ReadAllText(key), Is.EqualTo("mine"));
            Assert.That(File.Exists(vm.NotebookPath), Is.False);
            Assert.That(_created, Is.Empty);
        }

        [Test]
        public async Task Create_NewKeyFile_SamePathAsNotebook_SetsError_NothingWritten()
        {
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = _dir.File("a.kdbx");
            vm.KeyFilePath = _dir.File("A.KDBX");
            vm.CreateNewKeyFile = true;

            await Create(vm, StrongPassword, StrongPassword);

            Assert.That(vm.ErrorMessage, Is.EqualTo("The key file cannot be the notebook file itself. Choose a different key file."));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.KeyFilePath));
            Assert.That(File.Exists(vm.NotebookPath), Is.False);
            Assert.That(_created, Is.Empty);
        }

        [Test]
        public async Task Create_FailsAfterNewKeyFile_KeepsKeyFile_SaysSo()
        {
            CreateNotebookViewModel vm = NewVm(() => { throw new InvalidOperationException("boom"); });
            vm.NotebookPath = _dir.File("a.kdbx");
            vm.KeyFilePath = _dir.File("a.keyx");
            vm.CreateNewKeyFile = true;

            await Create(vm, StrongPassword, StrongPassword);

            Assert.That(vm.ErrorMessage, Is.EqualTo("Could not create the notebook: boom The new key file was kept at "
                + Path.GetFullPath(_dir.File("a.keyx")) + "."));
            Assert.That(File.Exists(vm.KeyFilePath), Is.True);
            Assert.That(File.Exists(vm.NotebookPath), Is.False);
            Assert.That(_created, Is.Empty);
            Assert.That(_dialogs.Calls, Is.Empty);
            Assert.That(vm.IsBusy, Is.False);
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.None), "not a box's error");
            Assert.That(vm.CreateNewKeyFile, Is.False, "a retry must use the kept key file, not try to create it again");
        }

        [Test]
        public async Task Create_RetryAfterKeptKeyFile_UsesItAndWarnsBackup()
        {
            int kdfCalls = 0;
            CreateNotebookViewModel vm = NewVm(() =>
            {
                if (kdfCalls++ == 0) throw new InvalidOperationException("boom");
                return TestKdf.Fast();
            });
            vm.NotebookPath = _dir.File("a.kdbx");
            vm.KeyFilePath = _dir.File("a.keyx");
            vm.CreateNewKeyFile = true;

            await Create(vm, StrongPassword, StrongPassword);
            Assert.That(vm.ErrorMessage, Does.StartWith("Could not create the notebook: boom"));
            byte[] keptKey = File.ReadAllBytes(vm.KeyFilePath);

            await Create(vm, StrongPassword, StrongPassword);

            Assert.That(vm.ErrorMessage, Is.Null);
            Assert.That(_created.Count, Is.EqualTo(1));
            Assert.That(File.ReadAllBytes(vm.KeyFilePath), Is.EqualTo(keptKey), "the kept key file is used unchanged");
            // The user never saw the backup warning for this new key file, so it is shown now.
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "ShowWarning" }));
            Assert.That(_dialogs.LastWarningTitle, Is.EqualTo("Back up your key file"));

            var store = new NotebookStore();
            Credentials noKey = Pw(StrongPassword);
            Assert.Throws<WrongCredentialsException>(() => store.Open(vm.NotebookPath, noKey));
            noKey.Dispose();
            using (Notebook nb = store.Open(vm.NotebookPath, Pw(StrongPassword, vm.KeyFilePath)))
                Assert.That(nb.IsReadOnly, Is.False);
        }

        [Test]
        public async Task Create_MissingExistingKeyFile_SaysKeyFileNotFound()
        {
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = _dir.File("a.kdbx");
            vm.KeyFilePath = _dir.File("missing.keyx");

            await Create(vm, StrongPassword, StrongPassword);

            Assert.That(vm.ErrorMessage, Is.EqualTo("Key file not found."));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.KeyFilePath));
            Assert.That(File.Exists(vm.NotebookPath), Is.False);
            Assert.That(_created, Is.Empty);
        }

        [TestCase("a.kdbx")]
        [TestCase("sub\\a.kdbx")]
        [TestCase("C:a.kdbx")]
        [TestCase("\\a.kdbx")]
        public async Task Create_RelativeNotebookPath_Rejected(string path)
        {
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = path;

            await Create(vm, StrongPassword, StrongPassword);

            Assert.That(vm.ErrorMessage, Is.EqualTo(UnlockViewModelTests.NotebookNotFull));
            Assert.That(vm.ErrorMessage, Is.EqualTo("Enter a full path in \"Notebook file\" or use Browse notebook" + (char)0x2026));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.NotebookPath));
            Assert.That(_created, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task Create_RelativeKeyFilePath_Rejected(bool createNew)
        {
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = _dir.File("a.kdbx");
            vm.KeyFilePath = "k.keyx";
            vm.CreateNewKeyFile = createNew;

            await Create(vm, StrongPassword, StrongPassword);

            Assert.That(vm.ErrorMessage, Is.EqualTo(UnlockViewModelTests.KeyFileNotFull));
            Assert.That(vm.ErrorField, Is.EqualTo(ErrorField.KeyFilePath));
            Assert.That(File.Exists(vm.NotebookPath), Is.False);
            Assert.That(File.Exists(Path.GetFullPath("k.keyx")), Is.False);
            Assert.That(_created, Is.Empty);
        }

        [Test]
        public async Task Create_DoubleInvoke_CreatesOnce()
        {
            CreateNotebookViewModel vm = NewVm();
            vm.NotebookPath = _dir.File("a.kdbx");

            using (SecureString a = Secure(StrongPassword))
            using (SecureString b = Secure(StrongPassword))
            {
                Task first = vm.CreateAsync(a, b);
                Task second = vm.CreateAsync(a, b);
                await Task.WhenAll(first, second);
            }

            Assert.That(_created.Count, Is.EqualTo(1));
            Assert.That(vm.ErrorMessage, Is.Null);
        }

        [Test]
        public async Task Create_BusyDuringCreate_DisablesCommands()
        {
            CreateNotebookViewModel vm = null;
            bool busy = false, canAny = true;
            vm = new CreateNotebookViewModel(new NotebookStore(), _dialogs, TestKdf.Fast, nb =>
            {
                _created.Add(nb);
                busy = vm.IsBusy;
                canAny = vm.BrowseNotebookCommand.CanExecute(null) || vm.BrowseKeyFileCommand.CanExecute(null)
                    || vm.CancelCommand.CanExecute(null);
            }, () => { });
            vm.NotebookPath = _dir.File("a.kdbx");

            await Create(vm, StrongPassword, StrongPassword);

            Assert.That(busy, Is.True);
            Assert.That(canAny, Is.False);
            Assert.That(vm.CancelCommand.CanExecute(null), Is.True);
        }

        [Test]
        public void Commands_BrowseAndCancel()
        {
            CreateNotebookViewModel vm = NewVm();
            _dialogs.NewPaths.Enqueue(_dir.File("new.kdbx"));
            _dialogs.KeyFiles.Enqueue(_dir.File("k.keyx"));

            vm.BrowseNotebookCommand.Execute(null);
            vm.BrowseKeyFileCommand.Execute(null);
            vm.BrowseNotebookCommand.Execute(null); // cancelled: unchanged
            Assert.That(vm.NotebookPath, Is.EqualTo(_dir.File("new.kdbx")));
            Assert.That(vm.KeyFilePath, Is.EqualTo(_dir.File("k.keyx")));
            Assert.That(_dialogs.Calls, Is.EqualTo(new[] { "PickNewNotebookPath", "PickKeyFile", "PickNewNotebookPath" }));

            vm.CancelCommand.Execute(null);
            Assert.That(_cancelCalls, Is.EqualTo(1));
        }

        [Test]
        public void UpdateStrength_Empty_NoWarning()
        {
            CreateNotebookViewModel vm = NewVm();
            using (SecureString s = Secure(StrongPassword)) vm.UpdateStrength(s);
            Assert.That(vm.StrengthBits, Is.GreaterThan(0u));

            using (SecureString s = Secure("")) vm.UpdateStrength(s);
            Assert.That(vm.StrengthBits, Is.EqualTo(0u));
            Assert.That(vm.Strength, Is.EqualTo(StrengthRating.Weak));
            Assert.That(vm.StrengthWarning, Is.Null);

            vm.UpdateStrength(null);
            Assert.That(vm.StrengthBits, Is.EqualTo(0u));
            Assert.That(vm.Strength, Is.EqualTo(StrengthRating.Weak));
            Assert.That(vm.StrengthWarning, Is.Null);
        }

        [Test]
        public void UpdateStrength_Weak_Warns()
        {
            CreateNotebookViewModel vm = NewVm();
            using (SecureString s = Secure(WeakPassword)) vm.UpdateStrength(s);
            Assert.That(vm.Strength, Is.EqualTo(StrengthRating.Weak));
            Assert.That(vm.StrengthWarning, Is.EqualTo("Weak password."));
        }

        [TestCase("abc")]
        [TestCase("Tr0ub4dor&3")]
        [TestCase("correct horse battery")]
        [TestCase("k9#Lm2$Pq")]
        [TestCase("q7#Vt9!mZ2@xLp4$Rw8&")]
        public void UpdateStrength_WarningMatchesRating(string password)
        {
            CreateNotebookViewModel vm = NewVm();
            uint bits;
            using (SecureString s = Secure(password))
            {
                bits = PasswordStrength.EstimateBits(s);
                vm.UpdateStrength(s);
            }
            Assert.That(vm.StrengthBits, Is.EqualTo(bits));
            Assert.That(vm.Strength, Is.EqualTo(PasswordStrength.Rate(bits)));
            string expected = vm.Strength == StrengthRating.Fair ? "Consider a longer password (aim for 80+ bits)."
                : vm.Strength == StrengthRating.Weak ? "Weak password." : null;
            Assert.That(vm.StrengthWarning, Is.EqualTo(expected));
        }

        [Test]
        public void UpdateStrength_Fair_SuggestsLonger()
        {
            CreateNotebookViewModel vm = NewVm();
            using (SecureString s = Secure(FairPassword)) vm.UpdateStrength(s);
            Assert.That(vm.Strength, Is.EqualTo(StrengthRating.Fair));
            Assert.That(vm.StrengthWarning, Is.EqualTo("Consider a longer password (aim for 80+ bits)."));
        }

        // 60-79 bits by KeePass's estimator (verified by UpdateStrength_Fair_SuggestsLonger).
        private const string FairPassword = "k9#Lm2$Pq7&w";

        private sealed class RecordingConfirmDialogs : LockPad.App.Services.IDialogService
        {
            public string Title, Message;
            public bool IsShowingDialog { get { return false; } }
            public void ShowError(string title, string message) { }
            public void ShowWarning(string title, string message) { }
            public void ShowInfo(string title, string message) { }
            public bool ShowChangePassword(LockPad.App.ViewModels.ChangePasswordViewModel vm) { return false; }
            public bool Confirm(string title, string message) { Title = "(Confirm used)"; return false; }
            public bool ConfirmDefaultNo(string title, string message) { Title = title; Message = message; return false; }
            public LockPad.App.Services.CloseSaveFailureChoice AskCloseAfterSaveFailure(string title, string message) { return LockPad.App.Services.CloseSaveFailureChoice.Cancel; }
            public LockPad.App.Services.ExternalChangeChoice AskExternalChange(string path) { return LockPad.App.Services.ExternalChangeChoice.Cancel; }
            public string PickOpenNotebookPath() { return null; }
            public string PickNewNotebookPath() { return null; }
            public string PickSaveAsPath(string currentPath) { return null; }
            public string PickKeyFile(bool forCreate) { return null; }
            public bool ShowSettings(LockPad.App.ViewModels.SettingsViewModel vm) { return false; }
            public void ShowShortcuts() { }
            public void ShowAbout() { }
            public string Prompt(string title, string label, string initialValue) { return null; }
        }
    }
}
