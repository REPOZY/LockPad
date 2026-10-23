using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace LockPad.App.Services
{
    /// <summary>The two Windows Error Reporting calls LockPad makes at startup. Test seam: tests use a fake, so they never
    /// write the real registry.</summary>
    internal interface IErrorReportingApi
    {
        /// <summary>WerAddExcludedApplication; returns its HRESULT.</summary>
        int AddExcludedApplication(string exeName, bool allUsers);

        /// <summary>WerSetFlags; returns its HRESULT.</summary>
        int SetFlags(uint flags);
    }

    /// <summary>
    /// Keeps note text out of Windows Error Reporting (user decision 2026-10-18). A crash report can carry process
    /// memory, which may hold notes, so LockPad:
    /// <list type="bullet">
    /// <item>excludes itself from WER for the current user: WerAddExcludedApplication(exe name, bAllUsers = FALSE) writes
    /// the value <c>LockPad.exe</c> under <c>HKCU\Software\Microsoft\Windows\Windows Error Reporting\ExcludedApplications</c>;</item>
    /// <item>sets WER_FAULT_REPORTING_FLAG_NOHEAP for this process, so any report that is still made leaves out the heap.</item>
    /// </list>
    /// To undo the exclusion, delete that registry value (e.g. <c>reg delete "HKCU\Software\Microsoft\Windows\Windows
    /// Error Reporting\ExcludedApplications" /v LockPad.exe /f</c>). The flag lasts only for the running process.
    /// Best effort: a failure HRESULT or a missing entry point is ignored and never blocks startup.
    /// </summary>
    internal static class ErrorReporting
    {
        /// <summary>WER_FAULT_REPORTING_FLAG_NOHEAP (werapi.h).</summary>
        internal const uint WerFaultReportingFlagNoHeap = 1;

        /// <summary>Used only when the running module's file name cannot be read.</summary>
        private const string FallbackExeName = "LockPad.exe";

        /// <summary>Calls both functions; each failure (HRESULT or exception) is ignored and never skips the other call.</summary>
        internal static void Apply(IErrorReportingApi wer, string exeName)
        {
            if (wer == null) throw new ArgumentNullException(nameof(wer));
            if (!string.IsNullOrEmpty(exeName))
            {
                try { wer.AddExcludedApplication(exeName, false); }
                catch (Exception) { }
            }
            try { wer.SetFlags(WerFaultReportingFlagNoHeap); }
            catch (Exception) { }
        }

        /// <summary>The file name (no folder) of the running executable, e.g. <c>LockPad.exe</c>.</summary>
        internal static string CurrentExeName()
        {
            try
            {
                using (Process p = Process.GetCurrentProcess())
                {
                    string name = Path.GetFileName(p.MainModule.FileName);
                    return string.IsNullOrEmpty(name) ? FallbackExeName : name;
                }
            }
            catch (Exception)
            {
                return FallbackExeName;
            }
        }
    }

    /// <summary>The real calls. Loaded from System32 only, so a planted wer.dll next to the exe is never used.</summary>
    internal sealed class NativeErrorReportingApi : IErrorReportingApi
    {
        [DllImport("wer.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int WerAddExcludedApplication(string pwzExeName, [MarshalAs(UnmanagedType.Bool)] bool bAllUsers);

        // WerSetFlags is exported by kernel32.dll (not wer.dll).
        [DllImport("kernel32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int WerSetFlags(uint dwFlags);

        public int AddExcludedApplication(string exeName, bool allUsers)
        {
            return WerAddExcludedApplication(exeName, allUsers);
        }

        public int SetFlags(uint flags)
        {
            return WerSetFlags(flags);
        }
    }
}
