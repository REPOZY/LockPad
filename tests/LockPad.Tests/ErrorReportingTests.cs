using System;
using System.Collections.Generic;
using LockPad.App.Services;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>C2 (user decision 2026-10-18): LockPad excludes itself from Windows Error Reporting. Only a fake is used
    /// here: the real calls would write HKCU\Software\Microsoft\Windows\Windows Error Reporting\ExcludedApplications.</summary>
    [TestFixture]
    public class ErrorReportingTests
    {
        private const int EFail = unchecked((int)0x80004005);

        private sealed class FakeWer : IErrorReportingApi
        {
            public readonly List<string> Calls = new List<string>();
            public int AddResult;
            public int FlagsResult;
            public Exception AddThrows;
            public Exception FlagsThrows;

            public int AddExcludedApplication(string exeName, bool allUsers)
            {
                Calls.Add("Add:" + exeName + ":" + allUsers);
                if (AddThrows != null) throw AddThrows;
                return AddResult;
            }

            public int SetFlags(uint flags)
            {
                Calls.Add("Flags:" + flags);
                if (FlagsThrows != null) throw FlagsThrows;
                return FlagsResult;
            }
        }

        [Test]
        public void Apply_ExcludesPerUser_AndSetsNoHeap()
        {
            var wer = new FakeWer();
            ErrorReporting.Apply(wer, "LockPad.exe");
            Assert.That(wer.Calls, Is.EqualTo(new[] { "Add:LockPad.exe:False", "Flags:1" }));
        }

        [Test]
        public void Apply_FailureHResults_DoNotThrow_BothCalled()
        {
            var wer = new FakeWer { AddResult = EFail, FlagsResult = EFail };
            Assert.DoesNotThrow(() => ErrorReporting.Apply(wer, "LockPad.exe"));
            Assert.That(wer.Calls, Is.EqualTo(new[] { "Add:LockPad.exe:False", "Flags:1" }));
        }

        [Test]
        public void Apply_MissingEntryPoints_DoNotThrow_BothCalled()
        {
            var wer = new FakeWer
            {
                AddThrows = new DllNotFoundException("wer.dll"),
                FlagsThrows = new EntryPointNotFoundException("WerSetFlags")
            };
            Assert.DoesNotThrow(() => ErrorReporting.Apply(wer, "LockPad.exe"));
            Assert.That(wer.Calls.Count, Is.EqualTo(2), "a failing first call does not skip the second");
        }

        [Test]
        public void Apply_NoExeName_StillSetsNoHeap()
        {
            var wer = new FakeWer();
            Assert.DoesNotThrow(() => ErrorReporting.Apply(wer, null));
            Assert.That(wer.Calls, Is.EqualTo(new[] { "Flags:1" }));
        }

        [Test]
        public void CurrentExeName_IsTheRunningModuleFileName()
        {
            string expected = System.IO.Path.GetFileName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
            Assert.That(ErrorReporting.CurrentExeName(), Is.EqualTo(expected));
            Assert.That(ErrorReporting.CurrentExeName(), Does.Not.Contain("\\"));
        }

        [Test]
        public void NoHeapFlag_IsOne()
        {
            Assert.That(ErrorReporting.WerFaultReportingFlagNoHeap, Is.EqualTo(1u));
        }
    }
}
