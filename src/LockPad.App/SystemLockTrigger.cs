namespace LockPad.App
{
    /// <summary>What a Windows session or power event asks of the main window (user decision 2026-10-19).</summary>
    internal enum SystemLockTrigger
    {
        /// <summary>Nothing (e.g. session unlock, power status change; logoff goes through the session-ending path).</summary>
        None,

        /// <summary>Windows locked the session, switched user or disconnected a remote session: lock like Ctrl+L.</summary>
        Lock,

        /// <summary>The PC is about to sleep or hibernate: obscure at once, then lock like Ctrl+L.</summary>
        Suspend,

        /// <summary>The PC woke up: lock if the idle timeout passed while asleep (unchanged behaviour).</summary>
        Resume
    }
}
