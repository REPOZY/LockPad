using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>
    /// Round-trips a LockPad notebook through the installed keepassxc-cli. Throwaway notebooks in a TempDir only;
    /// the password goes to keepassxc-cli on stdin and is never printed.
    /// </summary>
    [TestFixture]
    [Category("Interop")]
    public class KeePassXcInteropTests
    {
        private const string Cli = @"C:\Program Files\KeePassXC\keepassxc-cli.exe";
        private const string Password = "Interop-Test-Pw-1!";
        private const string Title = "Ledger seed";
        private const string EntryPath = "Notes/Ledger seed";
        private const int TimeoutMs = 30000;

        // C# source stays pure ASCII: the numero sign is built from its code point.
        private static readonly string NoteText = "alpha beta\ngamma " + (char)0x2116;

        private const string StdinBomHint =
            " (if the system code page is 65001, a BOM on stdin can corrupt the password)";

        // Kept undisposed until TearDown: the open process handle stops Windows from reusing the PID, so TearDown can
        // only ever kill a process this fixture started, never an unrelated keepassxc-cli the user is running.
        private readonly List<Process> _started = new List<Process>();
        private TempDir _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = new TempDir();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Process p in _started)
            {
                KillIfRunning(p);
                p.Dispose();
            }
            _started.Clear();
            _dir.Dispose();
        }

        private static void KillIfRunning(Process p)
        {
            try
            {
                if (!p.HasExited) p.Kill();
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            {
                // Exited between the check and the kill.
            }
        }

        private static Credentials Pw()
        {
            return new Credentials(Encoding.UTF8.GetBytes(Password), null);
        }

        private string CreateNotebookWithNote()
        {
            string path = _dir.File("interop.kdbx");
            var store = new NotebookStore();
            using (Notebook nb = store.Create(path, Pw(), TestKdf.Fast()))
            {
                Note note = nb.AddNote(Title);
                nb.UpdateNote(note.Id, Title, NoteText);
                SaveResult r = store.Save(nb, false);
                Assert.That(r.Status, Is.EqualTo(SaveStatus.Saved), r.Message);
            }
            return path;
        }

        private static string Normalize(string s)
        {
            return s.Replace("\r\n", "\n").TrimEnd('\r', '\n');
        }

        private static string Quote(string arg)
        {
            // MSVCRT/CommandLineToArgvW rules: backslashes are literal except before a quote.
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"') sb.Append('\\', backslashes * 2 + 1);
                else sb.Append('\\', backslashes);
                backslashes = 0;
                sb.Append(c);
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        private sealed class CliResult
        {
            public int ExitCode;
            public string StdOut;
            public string StdErr;
        }

        /// <summary>
        /// Runs keepassxc-cli with the notebook password on stdin. The whole run (exit plus reading all output) is
        /// bounded by one 30 s budget; on timeout the process is killed and the test fails.
        /// </summary>
        private CliResult RunCli(params string[] args)
        {
            Stopwatch budget = Stopwatch.StartNew();
            var psi = new ProcessStartInfo(Cli, string.Join(" ", args.Select(Quote)))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            var p = new Process { StartInfo = psi };
            try
            {
                p.Start();
            }
            catch
            {
                p.Dispose();
                throw;
            }
            _started.Add(p); // disposed in TearDown, not here (see _started)

            Task<string> stdout = p.StandardOutput.ReadToEndAsync();
            Task<string> stderr = p.StandardError.ReadToEndAsync();

            // The password is written as raw UTF-8 bytes to the base stream, so this code adds no preamble itself.
            // It cannot undo one written earlier: on .NET Framework, Process.Start wraps stdin in a StreamWriter using
            // Console.InputEncoding with AutoFlush, which may emit that encoding's preamble before this point.
            byte[] pw = Encoding.UTF8.GetBytes(Password + "\n");
            try
            {
                p.StandardInput.BaseStream.Write(pw, 0, pw.Length);
                p.StandardInput.BaseStream.Flush();
            }
            catch (IOException)
            {
                // keepassxc-cli exited before reading stdin; the exit code and stderr below report why.
            }
            finally
            {
                Array.Clear(pw, 0, pw.Length);
                try { p.StandardInput.Close(); }
                catch (IOException) { }
            }

            if (!p.WaitForExit(Remaining(budget)))
                FailTimedOut(p, args[0], "did not exit", stderr);

            // WaitForExit(int) does not wait for the ReadToEndAsync tasks; wait for them within the same budget.
            if (!Task.WaitAll(new Task[] { stdout, stderr }, Remaining(budget)))
                FailTimedOut(p, args[0], "exited but its output was not fully read", stderr);

            return new CliResult { ExitCode = p.ExitCode, StdOut = stdout.Result, StdErr = stderr.Result };
        }

        private static int Remaining(Stopwatch budget)
        {
            return (int)Math.Max(0, TimeoutMs - budget.ElapsedMilliseconds);
        }

        private static void FailTimedOut(Process p, string command, string what, Task<string> stderr)
        {
            KillIfRunning(p);
            // Bounded: after the kill the pipe closes and the reader completes; never block indefinitely.
            string err = stderr.Wait(5000) ? stderr.Result : "(stderr unavailable)";
            Assert.Fail("keepassxc-cli " + command + " " + what + " within " + TimeoutMs / 1000 + " s. stderr: " + err);
        }

        private CliResult ShowNotes(string db)
        {
            CliResult r = RunCli("show", "-q", "-s", "-a", "Notes", db, EntryPath);
            Assert.That(r.ExitCode, Is.EqualTo(0), "keepassxc-cli show failed. stderr: " + r.StdErr + StdinBomHint);
            return r;
        }

        private void EditNotesInKeePassXc(string db, string notes)
        {
            CliResult r = RunCli("edit", "-q", "--notes", notes, db, EntryPath);
            Assert.That(r.ExitCode, Is.EqualTo(0), "keepassxc-cli edit failed. stderr: " + r.StdErr + StdinBomHint);
        }

        private static Note FindByTitle(Notebook nb)
        {
            Note note = nb.Notes.SingleOrDefault(n => n.Title == Title);
            Assert.That(note, Is.Not.Null, "note '" + Title + "' not found");
            return note;
        }

        [Test]
        public void KeePassXc_ReadsLockPadNote()
        {
            if (!File.Exists(Cli)) Assert.Ignore("keepassxc-cli not installed");
            string db = CreateNotebookWithNote();

            CliResult r = ShowNotes(db);

            Assert.That(Normalize(r.StdOut), Is.EqualTo(NoteText));
        }

        [Test]
        public void KeePassXc_Edit_ThenLockPadOpens_StillMarked()
        {
            if (!File.Exists(Cli)) Assert.Ignore("keepassxc-cli not installed");
            string db = CreateNotebookWithNote();
            string id;
            using (Notebook nb = new NotebookStore().Open(db, Pw()))
                id = nb.Database.CustomData.Get(NotebookStore.NotebookIdKey);
            Assert.That(id, Does.Match("^[0-9a-f]{32}$"), "Create wrote a notebook id");

            EditNotesInKeePassXc(db, "changed by kpxc");

            using (Notebook nb = new NotebookStore().Open(db, Pw()))
            {
                Assert.That(nb.IsReadOnly, Is.False,
                    "KeePassXC dropped the LockPad.Notebook CustomData marker (design assumption broken)");
                Assert.That(nb.Database.CustomData.Get(NotebookStore.NotebookIdKey), Is.EqualTo(id),
                    "KeePassXC kept the LockPad.NotebookId CustomData entry");
                Assert.That(FindByTitle(nb).Text, Is.EqualTo("changed by kpxc"));
            }
        }

        [Test]
        public void KeePassXc_EditedFile_LockPadSavesAndKeePassXcReadsAgain()
        {
            if (!File.Exists(Cli)) Assert.Ignore("keepassxc-cli not installed");
            string db = CreateNotebookWithNote();
            EditNotesInKeePassXc(db, "changed by kpxc");
            string newText = "changed by lockpad\nsecond line " + (char)0x2116;

            var store = new NotebookStore();
            using (Notebook nb = store.Open(db, Pw()))
            {
                Assert.That(nb.IsReadOnly, Is.False);
                nb.UpdateNote(FindByTitle(nb).Id, Title, newText);
                SaveResult saved = store.Save(nb, false);
                Assert.That(saved.Status, Is.EqualTo(SaveStatus.Saved), saved.Message);
            }

            CliResult r = ShowNotes(db);

            Assert.That(Normalize(r.StdOut), Is.EqualTo(newText));
        }

        [Test]
        public void NoteDeletedSavedThenRestored_KeePassXcReadsIt()
        {
            // UX round 2, U3: Undo delete after the deletion was saved restores the same entry id.
            if (!File.Exists(Cli)) Assert.Ignore("keepassxc-cli not installed");
            string db = CreateNotebookWithNote();

            var store = new NotebookStore();
            using (Notebook nb = store.Open(db, Pw()))
            {
                Note note = FindByTitle(nb);
                nb.RemoveNote(note.Id);
                SaveResult deleted = store.Save(nb, false);
                Assert.That(deleted.Status, Is.EqualTo(SaveStatus.Saved), deleted.Message);
                nb.RestoreNote(note, 0);
                SaveResult restored = store.Save(nb, false);
                Assert.That(restored.Status, Is.EqualTo(SaveStatus.Saved), restored.Message);
            }

            CliResult r = ShowNotes(db);

            Assert.That(Normalize(r.StdOut), Is.EqualTo(NoteText));
        }
    }
}
