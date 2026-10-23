using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using LockPad.App.Services;
using LockPad.Core;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace LockPad.Tests
{
    // NOTE: tests in category "RealClipboard" use the real Windows clipboard and overwrite whatever
    // it held. They restore nothing; that is accepted. Everything they write carries the history /
    // cloud / monitor exclusion formats, so nothing enters Win+V history. Before each such test (and
    // before every test-side clipboard call) they wait for the clipboard to be free, and report
    // Inconclusive, naming the holder, if another process keeps it open for 20 s.
    // The fixture runs STA (OLE clipboard requirement) and non-parallel (the clipboard is global).
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public class ClipboardServiceTests
    {
        private const string RealClipboard = "RealClipboard";
        private static readonly TimeSpan ClipboardWait = TimeSpan.FromSeconds(20);
        private static readonly byte[] DwordZeroBytes = { 0, 0, 0, 0 };

        [DllImport("user32.dll")]
        private static extern IntPtr GetOpenClipboardWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [SetUp]
        public void WaitForClipboardBeforeRealClipboardTests()
        {
            if (IsRealClipboardTest()) WaitForFreeClipboard();
        }

        [TearDown]
        public void ReportClipboardHolderOnFailure()
        {
            if (!IsRealClipboardTest()) return;
            if (TestContext.CurrentContext.Result.Outcome.Status != TestStatus.Failed) return;
            IntPtr hwnd = GetOpenClipboardWindow();
            TestContext.Out.WriteLine("Clipboard holder at failure: "
                + (hwnd == IntPtr.Zero ? "none" : HolderName(hwnd)));
        }

        // Reads the attribute from the method so parameterized test cases are covered too.
        private bool IsRealClipboardTest()
        {
            MethodInfo m = GetType().GetMethod(TestContext.CurrentContext.Test.MethodName);
            return m != null && m.GetCustomAttributes(typeof(CategoryAttribute), true)
                .Cast<CategoryAttribute>().Any(c => c.Name == RealClipboard);
        }

        private static void WaitForFreeClipboard()
        {
            var sw = Stopwatch.StartNew();
            IntPtr hwnd;
            while ((hwnd = GetOpenClipboardWindow()) != IntPtr.Zero)
            {
                if (sw.Elapsed >= ClipboardWait)
                    Assert.Inconclusive("clipboard held by another process: " + HolderName(hwnd));
                Thread.Sleep(50);
            }
        }

        private static string HolderName(IntPtr hwnd)
        {
            try
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                using (Process p = Process.GetProcessById((int)pid))
                    return p.ProcessName + " (pid " + pid + ")";
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        private static ClipboardService NewService()
        {
            return new ClipboardService(() => 20);
        }

        // Waits for a free clipboard before every attempt; WPF retries about 1 s internally per call.
        private static T Retry<T>(Func<T> f)
        {
            for (int i = 0; ; i++)
            {
                WaitForFreeClipboard();
                try { return f(); }
                catch (ExternalException) { if (i >= 10) throw; Thread.Sleep(50); }
            }
        }

        private static void Retry(Action a)
        {
            Retry(() => { a(); return 0; });
        }

        // Puts "foreign" text on the clipboard without it entering Win+V history or cloud sync.
        private static void SetForeignText(string text)
        {
            Retry(() =>
            {
                var obj = new DataObject();
                obj.SetData(DataFormats.UnicodeText, text, false);
                obj.SetData(ClipboardFormats.ExcludeFromMonitor, new MemoryStream(ClipboardFormats.DwordZero()), false);
                obj.SetData(ClipboardFormats.CanIncludeInHistory, new MemoryStream(ClipboardFormats.DwordZero()), false);
                obj.SetData(ClipboardFormats.CanUploadToCloud, new MemoryStream(ClipboardFormats.DwordZero()), false);
                Clipboard.SetDataObject(obj, true);
            });
        }

        // Service calls against the real clipboard: wait until it is free first.
        private static void Copy(ClipboardService s, string text)
        {
            WaitForFreeClipboard();
            s.CopyProtected(text);
        }

        private static void ClearOwned(ClipboardService s)
        {
            WaitForFreeClipboard();
            s.ClearIfOwned();
        }

        private static void Elapse(ClipboardService s)
        {
            WaitForFreeClipboard();
            s.OnClearTimerElapsed();
        }

        private static byte[] ReadFormatBytes(string format)
        {
            object data = Retry(() => Clipboard.GetData(format));
            var stream = data as Stream;
            Assert.That(stream, Is.Not.Null, format + " is not a stream");
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        // ---- Real-clipboard tests ----

        [Test, Category(RealClipboard)]
        public void CopyProtected_SetsTextAndExclusionFormats()
        {
            Copy(NewService(), "secret-1");

            Assert.That(Retry(() => Clipboard.GetText()), Is.EqualTo("secret-1"));
            // Windows synthesizes CF_TEXT from CF_UNICODETEXT for legacy (ANSI) apps.
            Assert.That(Retry(() => Clipboard.ContainsText(TextDataFormat.Text)), Is.True);
            Assert.That(Retry(() => Clipboard.ContainsData(ClipboardFormats.ExcludeFromMonitor)), Is.True);
            Assert.That(Retry(() => Clipboard.ContainsData(ClipboardFormats.CanIncludeInHistory)), Is.True);
            Assert.That(Retry(() => Clipboard.ContainsData(ClipboardFormats.CanUploadToCloud)), Is.True);
            Assert.That(ReadFormatBytes(ClipboardFormats.CanIncludeInHistory), Is.EqualTo(DwordZeroBytes));
            Assert.That(ReadFormatBytes(ClipboardFormats.CanUploadToCloud), Is.EqualTo(DwordZeroBytes));
        }

        [Test, Category(RealClipboard)]
        public void ClearIfOwned_ClearsOwnText()
        {
            var s = NewService();
            Copy(s, "secret-2");
            ClearOwned(s);
            Assert.That(Retry(() => Clipboard.ContainsText()), Is.False);
            Assert.That(s.FingerprintForTest, Is.Null);
            Assert.That(s.IsClearPendingForTest, Is.False);
        }

        [Test, Category(RealClipboard)]
        public void ClearIfOwned_TextWithNul_StillClears()
        {
            // Full-check red team 2026-10-22 (L2): note text from another KeePass client can contain U+0000. Windows
            // reads clipboard text only up to the first NUL, so a fingerprint of the full string never matched and
            // the copy was never cleared.
            var s = NewService();
            Copy(s, "pin 1234\0rest");
            ClearOwned(s);
            Assert.That(Retry(() => Clipboard.ContainsText()), Is.False);
        }

        [Test, Category(RealClipboard)]
        public void ClearIfOwned_KeepsForeignText()
        {
            var s = NewService();
            Copy(s, "secret-3");
            SetForeignText("other");
            ClearOwned(s);
            Assert.That(Retry(() => Clipboard.GetText()), Is.EqualTo("other"));
            Assert.That(s.FingerprintForTest, Is.Null);
            Assert.That(s.IsClearPendingForTest, Is.False);
        }

        [Test, Category(RealClipboard)]
        public void TimerElapsed_ClearsOwnText()
        {
            var s = NewService();
            Copy(s, "secret-4");
            Assert.That(s.IsClearPendingForTest, Is.True);
            Elapse(s);
            Assert.That(Retry(() => Clipboard.ContainsText()), Is.False);
            Assert.That(s.FingerprintForTest, Is.Null);
            Assert.That(s.IsClearPendingForTest, Is.False);
        }

        [Test, Category(RealClipboard)]
        public void CopyProtected_Empty_NoOp()
        {
            SetForeignText("keep");
            var s = NewService();
            Copy(s, "");
            Copy(s, null);
            Assert.That(Retry(() => Clipboard.GetText()), Is.EqualTo("keep"));
            Assert.That(s.FingerprintForTest, Is.Null);
            Assert.That(s.IsClearPendingForTest, Is.False);
        }

        [Test, Category(RealClipboard)]
        public void CopyProtected_Empty_KeepsExistingFingerprintAndTimer()
        {
            var s = NewService();
            Copy(s, "secret-5");
            byte[] before = s.FingerprintForTest;
            Copy(s, "");
            Copy(s, null);
            Assert.That(s.FingerprintForTest, Is.EqualTo(before));
            Assert.That(s.IsClearPendingForTest, Is.True);
            Assert.That(Retry(() => Clipboard.GetText()), Is.EqualTo("secret-5"));
        }

        [Test, Category(RealClipboard)]
        public void ClearIfOwned_AfterSecondCopy_ClearsOnlyLatest()
        {
            var s = NewService();
            Copy(s, "a");
            Copy(s, "b");
            SetForeignText("c");
            ClearOwned(s);
            Assert.That(Retry(() => Clipboard.GetText()), Is.EqualTo("c"));
        }

        [Test, Category(RealClipboard)]
        public void ClearIfOwned_AfterSecondCopy_DoesNotTreatFirstAsOwned()
        {
            var s = NewService();
            Copy(s, "a");
            Copy(s, "b");
            SetForeignText("a");
            ClearOwned(s);
            Assert.That(Retry(() => Clipboard.GetText()), Is.EqualTo("a"));
        }

        [Test, Category(RealClipboard)]
        public void Fingerprint_IsKeyed()
        {
            var s1 = NewService();
            var s2 = NewService();
            Copy(s1, "same-text");
            Copy(s2, "same-text");
            byte[] f1 = s1.FingerprintForTest;
            byte[] f2 = s2.FingerprintForTest;
            Assert.That(f1, Has.Length.EqualTo(32));
            Assert.That(f2, Has.Length.EqualTo(32));
            Assert.That(f1, Is.Not.EqualTo(f2));
        }

        [Test, Category(RealClipboard)]
        public void FingerprintForTest_ReturnsCopy()
        {
            var s = NewService();
            Copy(s, "secret-6");
            byte[] f = s.FingerprintForTest;
            f[0] ^= 0xFF;
            Assert.That(s.FingerprintForTest, Is.Not.EqualTo(f));
        }

        [Test, Category(RealClipboard)]
        public void TimerElapsed_ForeignText_StopsAndForgets()
        {
            var s = NewService();
            Copy(s, "secret-7");
            SetForeignText("foreign");
            Elapse(s);
            Assert.That(Retry(() => Clipboard.GetText()), Is.EqualTo("foreign"));
            Assert.That(s.FingerprintForTest, Is.Null);
            Assert.That(s.IsClearPendingForTest, Is.False);
        }

        [Test, Category(RealClipboard)]
        public void ClearIfOwned_WithoutCopy_LeavesClipboard()
        {
            SetForeignText("untouched");
            ClearOwned(NewService());
            Assert.That(Retry(() => Clipboard.GetText()), Is.EqualTo("untouched"));
        }

        [TestCase(1, 5)]
        [TestCase(20, 20)]
        [TestCase(999, 120)]
        [Category(RealClipboard)]
        public void CopyProtected_StartsTimerWithClampedInterval(int provided, int expected)
        {
            var s = new ClipboardService(() => provided);
            Copy(s, "secret-8");
            Assert.That(s.ClearIntervalForTest, Is.EqualTo(TimeSpan.FromSeconds(expected)));
        }

        [Test]
        public void Constructor_NullProvider_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ClipboardService(null));
        }

        [Test]
        public void DwordZero_ReturnsFreshZeroedArray()
        {
            byte[] a = ClipboardFormats.DwordZero();
            byte[] b = ClipboardFormats.DwordZero();
            Assert.That(a, Is.EqualTo(DwordZeroBytes));
            Assert.That(a, Is.Not.SameAs(b));
        }

        // ---- Failure-path tests (fake backend; the real clipboard cannot be made to fail) ----
        // WPF's Clipboard already retries OleSetClipboard / OleFlushClipboard / OleGetClipboard /
        // Clear internally, so the service calls each backend method exactly once. Only GetData on
        // an already-obtained IDataObject is retried by the service (3 attempts).

        private static COMException Busy()
        {
            return new COMException("busy", unchecked((int)0x800401D0)); // CLIPBRD_E_CANT_OPEN
        }

        private sealed class FakeBackend : IClipboardBackend
        {
            public string Text;
            public int SetFailures;         // upcoming SetDataObject calls that throw
            public int GetObjectFailures;   // upcoming GetDataObject calls that throw
            public int ReadFailures;        // upcoming GetData calls on the returned object that throw
            public int ClearFailures;       // upcoming Clear calls that throw
            public int SetCalls, GetObjectCalls, ReadCalls, ClearCalls;
            public Exception SetLandsThenThrows; // data reaches the clipboard, then the call throws this

            public void SetDataObject(DataObject data)
            {
                SetCalls++;
                if (SetFailures > 0) { SetFailures--; throw Busy(); }
                Text = (string)data.GetData(DataFormats.UnicodeText);
                if (SetLandsThenThrows != null) throw SetLandsThenThrows;
            }

            public IDataObject GetDataObject()
            {
                GetObjectCalls++;
                if (GetObjectFailures > 0) { GetObjectFailures--; throw Busy(); }
                return new FakeDataObject(this, Text);
            }

            public void Clear()
            {
                ClearCalls++;
                if (ClearFailures > 0) { ClearFailures--; throw Busy(); }
                Text = null;
            }
        }

        private sealed class FakeDataObject : IDataObject
        {
            private readonly FakeBackend _owner;
            private readonly string _text;

            public FakeDataObject(FakeBackend owner, string text)
            {
                _owner = owner;
                _text = text;
            }

            public object GetData(string format, bool autoConvert)
            {
                _owner.ReadCalls++;
                if (_owner.ReadFailures > 0) { _owner.ReadFailures--; throw new ExternalException("busy"); }
                return format == DataFormats.UnicodeText ? _text : null;
            }

            public bool GetDataPresent(string format, bool autoConvert)
            {
                return format == DataFormats.UnicodeText && _text != null;
            }

            public object GetData(string format) { return GetData(format, true); }
            public object GetData(Type format) { throw new NotSupportedException(); }
            public bool GetDataPresent(string format) { return GetDataPresent(format, true); }
            public bool GetDataPresent(Type format) { throw new NotSupportedException(); }
            public string[] GetFormats() { throw new NotSupportedException(); }
            public string[] GetFormats(bool autoConvert) { throw new NotSupportedException(); }
            public void SetData(object data) { throw new NotSupportedException(); }
            public void SetData(string format, object data) { throw new NotSupportedException(); }
            public void SetData(Type format, object data) { throw new NotSupportedException(); }
            public void SetData(string format, object data, bool autoConvert) { throw new NotSupportedException(); }
        }

        [Test]
        public void OwnsClipboardContent_TrueWhileFingerprintHeld()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 20, b);
            Assert.That(s.OwnsClipboardContent, Is.False);

            s.CopyProtected("secret");
            Assert.That(s.OwnsClipboardContent, Is.True);

            b.ClearFailures = 1;
            s.ClearIfOwned();
            Assert.That(s.OwnsClipboardContent, Is.True, "the clear failed: still ours");

            s.ClearIfOwned();
            Assert.That(s.OwnsClipboardContent, Is.False);
            Assert.That(b.Text, Is.Null);
        }

        [Test]
        public void OwnsClipboardContent_FalseAfterForeignContentReplacedIt()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 20, b);
            s.CopyProtected("secret");
            b.Text = "someone else's";
            Assert.That(s.OwnsClipboardContent, Is.True, "not known until checked");

            s.ClearIfOwned();
            Assert.That(s.OwnsClipboardContent, Is.False);
            Assert.That(b.Text, Is.EqualTo("someone else's"));
        }

        [Test]
        public void OwnsClipboardContent_TrueForSecondaryFingerprintAfterInconclusiveFailure()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 20, b);
            s.CopyProtected("first");
            b.SetFailures = 1;
            b.GetObjectFailures = 1; // readback inconclusive: "first" may still be there
            Assert.Throws<COMException>(() => s.CopyProtected("second"));
            Assert.That(s.SecondaryFingerprintForTest, Is.Not.Null);
            Assert.That(s.OwnsClipboardContent, Is.True);
        }

        [Test]
        public void ClearWithRetry_RealService_ClearsAfterContention()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 20, b);
            s.CopyProtected("secret");
            b.ClearFailures = 2;
            int waits = 0;

            bool cleared = ClipboardExitHelper.ClearWithRetry(s, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(500),
                t => waits++);

            Assert.That(cleared, Is.True);
            Assert.That(b.ClearCalls, Is.EqualTo(3));
            Assert.That(waits, Is.EqualTo(2));
            Assert.That(b.Text, Is.Null);
            Assert.That(s.OwnsClipboardContent, Is.False);
        }

        [Test]
        public void CopyProtected_CallsSetDataObjectOnce_OnSuccess()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 20, b);
            s.CopyProtected("x");
            Assert.That(b.SetCalls, Is.EqualTo(1));
            Assert.That(b.Text, Is.EqualTo("x"));
            Assert.That(s.IsClearPendingForTest, Is.True);
        }

        [Test]
        public void CopyProtected_CallsSetDataObjectOnce_OnFailure_AndRethrows()
        {
            var b = new FakeBackend { SetFailures = 1 };
            var s = new ClipboardService(() => 20, b);
            Assert.Throws<COMException>(() => s.CopyProtected("x"));
            Assert.That(b.SetCalls, Is.EqualTo(1));
        }

        [Test]
        public void CopyProtected_Fails_ReadbackShowsOtherText_KeepsPreviousState()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 20, b);
            s.CopyProtected("first");
            byte[] before = s.FingerprintForTest;
            b.SetFailures = 1;
            Assert.Throws<COMException>(() => s.CopyProtected("second"));
            Assert.That(s.FingerprintForTest, Is.EqualTo(before));
            Assert.That(s.IsClearPendingForTest, Is.True);
        }

        [Test]
        public void CopyProtected_FailsAfterDataLanded_StillTracksForClear()
        {
            var b = new FakeBackend { SetLandsThenThrows = new COMException("flush failed", unchecked((int)0x800401D0)) };
            var s = new ClipboardService(() => 20, b);
            Assert.Throws<COMException>(() => s.CopyProtected("landed"));
            Assert.That(b.SetCalls, Is.EqualTo(1));
            Assert.That(b.Text, Is.EqualTo("landed"));
            Assert.That(s.FingerprintForTest, Is.Not.Null);
            Assert.That(s.IsClearPendingForTest, Is.True);
            s.ClearIfOwned();
            Assert.That(b.Text, Is.Null);
        }

        [Test]
        public void CopyProtected_NonComFailureAfterDataLanded_TrackedAndPropagates()
        {
            var b = new FakeBackend { SetLandsThenThrows = new UnauthorizedAccessException("denied") };
            var s = new ClipboardService(() => 20, b);
            Assert.Throws<UnauthorizedAccessException>(() => s.CopyProtected("landed"));
            Assert.That(b.Text, Is.EqualTo("landed"));
            Assert.That(s.FingerprintForTest, Is.Not.Null);
            Assert.That(s.IsClearPendingForTest, Is.True);
            s.OnClearTimerElapsed();
            Assert.That(b.Text, Is.Null);
        }

        [Test]
        public void CopyProtected_InconclusiveFailure_KeepsPreviousAsSecondary_TimerClearsIt()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 20, b);
            s.CopyProtected("A");
            byte[] fa = s.FingerprintForTest;

            b.SetFailures = 1;       // copy B fails...
            b.GetObjectFailures = 1; // ...and the readback is inconclusive
            Assert.Throws<COMException>(() => s.CopyProtected("B"));
            Assert.That(b.Text, Is.EqualTo("A"));
            Assert.That(s.SecondaryFingerprintForTest, Is.EqualTo(fa));
            Assert.That(s.FingerprintForTest, Is.Not.EqualTo(fa));

            s.OnClearTimerElapsed();
            Assert.That(b.Text, Is.Null);
            Assert.That(s.FingerprintForTest, Is.Null);
            Assert.That(s.SecondaryFingerprintForTest, Is.Null);
            Assert.That(s.IsClearPendingForTest, Is.False);
        }

        [Test]
        public void CopyProtected_InconclusiveThenConclusiveCopy_ForgetsSecondary()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 20, b);
            s.CopyProtected("A");
            b.SetFailures = 1;
            b.GetObjectFailures = 1;
            Assert.Throws<COMException>(() => s.CopyProtected("B"));
            Assert.That(s.SecondaryFingerprintForTest, Is.Not.Null);

            s.CopyProtected("C");
            Assert.That(s.SecondaryFingerprintForTest, Is.Null);

            b.Text = "A"; // A is no longer tracked
            s.ClearIfOwned();
            Assert.That(b.Text, Is.EqualTo("A"));
        }

        [Test]
        public void CopyProtected_InconclusiveFailure_ClearsAdoptedTextToo()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 20, b);
            s.CopyProtected("A");
            b.SetFailures = 1;
            b.GetObjectFailures = 1;
            Assert.Throws<COMException>(() => s.CopyProtected("B"));
            b.Text = "B";
            s.ClearIfOwned();
            Assert.That(b.Text, Is.Null);
            Assert.That(s.SecondaryFingerprintForTest, Is.Null);
        }

        [Test]
        public void CopyProtected_InconclusiveFailure_ForeignContentForgetsBoth()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 20, b);
            s.CopyProtected("A");
            b.SetFailures = 1;
            b.GetObjectFailures = 1;
            Assert.Throws<COMException>(() => s.CopyProtected("B"));
            b.Text = "foreign";
            s.OnClearTimerElapsed();
            Assert.That(b.Text, Is.EqualTo("foreign"));
            Assert.That(s.FingerprintForTest, Is.Null);
            Assert.That(s.SecondaryFingerprintForTest, Is.Null);
        }

        [Test]
        public void CopyProtected_RepeatedInconclusiveFailures_HoldAtMostTwo()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 20, b);
            s.CopyProtected("A");
            byte[] fa = s.FingerprintForTest;
            b.SetFailures = 1;
            b.GetObjectFailures = 1;
            Assert.Throws<COMException>(() => s.CopyProtected("B"));
            byte[] fb = s.FingerprintForTest;
            b.SetFailures = 1;
            b.GetObjectFailures = 1;
            Assert.Throws<COMException>(() => s.CopyProtected("C"));

            // Primary = C, secondary = A (oldest known-landed); the intermediate B is dropped.
            Assert.That(s.SecondaryFingerprintForTest, Is.EqualTo(fa));
            Assert.That(s.FingerprintForTest, Is.Not.EqualTo(fa));
            Assert.That(s.FingerprintForTest, Is.Not.EqualTo(fb));
            b.Text = "A"; // a sustained lock: none of the failed copies landed
            s.OnClearTimerElapsed();
            Assert.That(b.Text, Is.Null);
        }

        [Test]
        public void CopyProtected_SameTextRetriedAfterInconclusiveFailure_KeepsLandedSecondary()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 20, b);
            s.CopyProtected("A");
            byte[] fa = s.FingerprintForTest;
            b.SetFailures = 1;
            b.GetObjectFailures = 1;
            Assert.Throws<COMException>(() => s.CopyProtected("B"));
            byte[] fb = s.FingerprintForTest;
            b.SetFailures = 1;
            b.GetObjectFailures = 1;
            Assert.Throws<COMException>(() => s.CopyProtected("B")); // user retries the same copy

            Assert.That(s.SecondaryFingerprintForTest, Is.EqualTo(fa));
            Assert.That(s.FingerprintForTest, Is.EqualTo(fb));
            Assert.That(b.Text, Is.EqualTo("A"));
            s.OnClearTimerElapsed();
            Assert.That(b.Text, Is.Null);
        }

        [Test]
        public void CopyProtected_DuplicateOfPrimary_DoesNotShift_RestartsTimer()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 30, b);
            s.CopyProtected("A");
            b.SetFailures = 1;
            b.GetObjectFailures = 1;
            Assert.Throws<COMException>(() => s.CopyProtected("B"));
            byte[] fb = s.FingerprintForTest, fa = s.SecondaryFingerprintForTest;

            b.ClearFailures = 1; // put the timer into backoff
            b.Text = "B";
            s.OnClearTimerElapsed();
            Assert.That(s.ClearIntervalForTest, Is.EqualTo(TimeSpan.FromSeconds(1)));

            b.Text = "A";
            b.SetFailures = 1;
            b.GetObjectFailures = 1;
            Assert.Throws<COMException>(() => s.CopyProtected("B"));
            Assert.That(s.FingerprintForTest, Is.EqualTo(fb));
            Assert.That(s.SecondaryFingerprintForTest, Is.EqualTo(fa));
            Assert.That(s.IsClearPendingForTest, Is.True);
            Assert.That(s.ClearIntervalForTest, Is.EqualTo(TimeSpan.FromSeconds(30)));
        }

        [Test]
        public void CopyProtected_DuplicateOfSecondary_DoesNotFillBothSlots()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 20, b);
            s.CopyProtected("A");
            b.SetFailures = 1;
            b.GetObjectFailures = 1;
            Assert.Throws<COMException>(() => s.CopyProtected("B"));
            byte[] fb = s.FingerprintForTest, fa = s.SecondaryFingerprintForTest;
            b.SetFailures = 1;
            b.GetObjectFailures = 1;
            Assert.Throws<COMException>(() => s.CopyProtected("A")); // A is already tracked

            Assert.That(s.FingerprintForTest, Is.EqualTo(fb));
            Assert.That(s.SecondaryFingerprintForTest, Is.EqualTo(fa));
        }

        [Test]
        public void CopyProtected_InconclusiveFailureWithNothingTracked_HasNoSecondary()
        {
            var b = new FakeBackend { SetFailures = 1, GetObjectFailures = 1 };
            var s = new ClipboardService(() => 20, b);
            Assert.Throws<COMException>(() => s.CopyProtected("A"));
            Assert.That(s.FingerprintForTest, Is.Not.Null);
            Assert.That(s.SecondaryFingerprintForTest, Is.Null);
        }

        [Test]
        public void CopyProtected_FailedWithOtherTextReadback_KeepsSecondaryToo()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 20, b);
            s.CopyProtected("A");
            b.SetFailures = 1;
            b.GetObjectFailures = 1;
            Assert.Throws<COMException>(() => s.CopyProtected("B"));
            byte[] primary = s.FingerprintForTest, secondary = s.SecondaryFingerprintForTest;

            b.SetFailures = 1; // conclusive readback: clipboard holds A, not D
            Assert.Throws<COMException>(() => s.CopyProtected("D"));
            Assert.That(s.FingerprintForTest, Is.EqualTo(primary));
            Assert.That(s.SecondaryFingerprintForTest, Is.EqualTo(secondary));
        }

        [Test]
        public void CopyProtected_Fails_ReadbackAlsoFails_AdoptsNewFingerprint()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 20, b);
            s.CopyProtected("first");
            byte[] before = s.FingerprintForTest;
            b.SetFailures = 1;
            b.GetObjectFailures = 1;
            Assert.Throws<COMException>(() => s.CopyProtected("second"));
            Assert.That(s.FingerprintForTest, Is.Not.Null);
            Assert.That(s.FingerprintForTest, Is.Not.EqualTo(before));
            Assert.That(s.IsClearPendingForTest, Is.True);

            // The adopted fingerprint is the new text's: once "second" is on the clipboard it is cleared.
            b.Text = "second";
            s.ClearIfOwned();
            Assert.That(b.Text, Is.Null);
        }

        [Test]
        public void ClearIfOwned_NeverThrows_WhenGetDataObjectFails_NoOuterRetry()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 20, b);
            s.CopyProtected("x");
            b.GetObjectFailures = int.MaxValue;
            Assert.DoesNotThrow(() => s.ClearIfOwned());
            Assert.That(b.GetObjectCalls, Is.EqualTo(1));
            Assert.That(s.FingerprintForTest, Is.Not.Null); // still owned as far as we know
        }

        [Test]
        public void ClearIfOwned_NeverThrows_WhenClearFails_NoOuterRetry()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 20, b);
            s.CopyProtected("x");
            b.ClearFailures = int.MaxValue;
            Assert.DoesNotThrow(() => s.ClearIfOwned());
            Assert.That(b.ClearCalls, Is.EqualTo(1));
            Assert.That(b.Text, Is.EqualTo("x"));
            Assert.That(s.FingerprintForTest, Is.Not.Null);
        }

        [Test]
        public void ClearIfOwned_RetriesGetData_UpToThreeAttempts_ThenSucceeds()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 20, b);
            s.CopyProtected("x");
            b.ReadFailures = 2;
            s.ClearIfOwned();
            Assert.That(b.ReadCalls, Is.EqualTo(3));
            Assert.That(b.GetObjectCalls, Is.EqualTo(1));
            Assert.That(b.Text, Is.Null);
            Assert.That(s.FingerprintForTest, Is.Null);
        }

        [Test]
        public void ClearIfOwned_GetDataFailsThreeTimes_GivesUpWithoutThrowing()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 20, b);
            s.CopyProtected("x");
            b.ReadFailures = int.MaxValue;
            Assert.DoesNotThrow(() => s.ClearIfOwned());
            Assert.That(b.ReadCalls, Is.EqualTo(3));
            Assert.That(b.Text, Is.EqualTo("x"));
            Assert.That(s.FingerprintForTest, Is.Not.Null);
        }

        [Test]
        public void TimerElapsed_ClearFails_ReschedulesWithBackoff_FiveTimes()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 20, b);
            s.CopyProtected("x");
            b.ClearFailures = int.MaxValue;

            s.OnClearTimerElapsed(); // scheduled clear fails
            int[] backoff = { 1, 2, 4, 8, 16 };
            for (int i = 0; i < backoff.Length; i++)
            {
                Assert.That(s.IsClearPendingForTest, Is.True, "reschedule " + (i + 1));
                Assert.That(s.ClearIntervalForTest, Is.EqualTo(TimeSpan.FromSeconds(backoff[i])));
                s.OnClearTimerElapsed(); // rescheduled attempt fails
            }

            Assert.That(s.IsClearPendingForTest, Is.False);
            Assert.That(b.ClearCalls, Is.EqualTo(6)); // one call per elapsed, no outer retry
            // Gave up on the timer, but the fingerprint stays so lock/exit can still clear.
            Assert.That(s.FingerprintForTest, Is.Not.Null);
            b.ClearFailures = 0;
            s.ClearIfOwned();
            Assert.That(b.Text, Is.Null);
        }

        [Test]
        public void TimerElapsed_ClearRecovers_StopsAndForgets()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 20, b);
            s.CopyProtected("x");
            b.ClearFailures = 1;
            s.OnClearTimerElapsed();
            Assert.That(s.IsClearPendingForTest, Is.True);
            s.OnClearTimerElapsed();
            Assert.That(b.Text, Is.Null);
            Assert.That(s.IsClearPendingForTest, Is.False);
            Assert.That(s.FingerprintForTest, Is.Null);
        }

        [Test]
        public void CopyProtected_AfterFailedClears_RestartsFullIntervalAndBudget()
        {
            var b = new FakeBackend();
            var s = new ClipboardService(() => 30, b);
            s.CopyProtected("x");
            b.ClearFailures = int.MaxValue;
            s.OnClearTimerElapsed();
            s.OnClearTimerElapsed();
            Assert.That(s.ClearIntervalForTest, Is.EqualTo(TimeSpan.FromSeconds(2)));

            s.CopyProtected("y");
            Assert.That(s.ClearIntervalForTest, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(s.IsClearPendingForTest, Is.True);

            // Backoff restarts at 1 s and the budget is 5 reschedules again.
            s.OnClearTimerElapsed();
            Assert.That(s.ClearIntervalForTest, Is.EqualTo(TimeSpan.FromSeconds(1)));
            for (int i = 1; i <= 5; i++)
            {
                Assert.That(s.IsClearPendingForTest, Is.True, "reschedule " + i);
                s.OnClearTimerElapsed();
            }
            Assert.That(s.IsClearPendingForTest, Is.False);
        }
    }
}
