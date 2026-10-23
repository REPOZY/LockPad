using System;
using System.Diagnostics;

namespace LockPad.Core
{
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    /// <summary>
    /// Monotonic clock: a DateTime.UtcNow captured once at construction plus a Stopwatch,
    /// so changing the Windows clock neither extends nor shortens the idle timeout.
    /// </summary>
    public sealed class SystemClock : IClock
    {
        private readonly DateTime _start = DateTime.UtcNow;
        private readonly Stopwatch _watch = Stopwatch.StartNew();

        public DateTime UtcNow
        {
            get { return _start + _watch.Elapsed; }
        }
    }
}
