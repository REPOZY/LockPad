using System.Diagnostics;
using NUnit.Framework;

[SetUpFixture]
public class AssemblySetup
{
    [OneTimeSetUp]
    public void DisableAssertDialogs()
    {
        foreach (TraceListener l in Trace.Listeners)
        {
            var d = l as DefaultTraceListener;
            if (d != null) d.AssertUiEnabled = false;
        }
        // Tests that build databases with KeePassLib directly (before any Core call) also use managed code only.
        LockPad.Core.KeePassLibPolicy.Apply();
        // Notebooks in tests use TestKdf.Fast (1 MiB, 1 iteration) so the suite stays fast. The save pipeline would
        // replace such parameters with a fresh ~1 s KdfTuner result on every first save; accept any Argon2id
        // parameters here instead. NotebookStoreSaveTests switch the production floor back on to test it.
        LockPad.Core.SaveFormatPolicy.AcceptWeakKdfForTests = true;
        LockPad.Core.SaveFormatPolicy.FreshKdf = LockPad.Tests.TestKdf.Fast;
    }
}
