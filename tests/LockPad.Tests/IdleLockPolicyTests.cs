using System;
using LockPad.Core;
using NUnit.Framework;

namespace LockPad.Tests
{
    [TestFixture]
    public class IdleLockPolicyTests
    {
        private static readonly TimeSpan Five = TimeSpan.FromMinutes(5);

        [Test]
        public void Idle_NotExpiredJustBeforeTimeout()
        {
            var c = new FakeClock();
            var p = new IdleLockPolicy(c, Five);
            c.Advance(new TimeSpan(0, 4, 59));
            Assert.That(p.IsExpired, Is.False);
            Assert.That(p.Remaining, Is.EqualTo(TimeSpan.FromSeconds(1)));
        }

        [Test]
        public void Idle_ExpiredAtTimeout()
        {
            var c = new FakeClock();
            var p = new IdleLockPolicy(c, Five);
            c.Advance(Five);
            Assert.That(p.IsExpired, Is.True);
        }

        [Test]
        public void Idle_RecordActivityResets()
        {
            var c = new FakeClock();
            var p = new IdleLockPolicy(c, Five);
            c.Advance(new TimeSpan(0, 4, 59));
            p.RecordActivity();
            c.Advance(new TimeSpan(0, 4, 59));
            Assert.That(p.IsExpired, Is.False);
            Assert.That(p.Remaining, Is.EqualTo(TimeSpan.FromSeconds(1)));
        }

        [Test]
        public void Idle_RemainingNeverNegative()
        {
            var c = new FakeClock();
            var p = new IdleLockPolicy(c, Five);
            c.Advance(TimeSpan.FromHours(1));
            Assert.That(p.Remaining, Is.EqualTo(TimeSpan.Zero));
            Assert.That(p.IsExpired, Is.True);
        }

        [Test]
        public void Idle_ClockGoesBackwards_TreatedAsZeroElapsed()
        {
            var c = new FakeClock();
            var p = new IdleLockPolicy(c, Five);
            c.Advance(TimeSpan.FromMinutes(-10));
            Assert.That(p.Remaining, Is.EqualTo(Five));
            Assert.That(p.IsExpired, Is.False);
        }

        [Test]
        public void Idle_TimeoutShortened_KeepsLastActivity()
        {
            var c = new FakeClock();
            var p = new IdleLockPolicy(c, Five);
            c.Advance(TimeSpan.FromMinutes(2));
            p.Timeout = TimeSpan.FromMinutes(1);
            Assert.That(p.IsExpired, Is.True);
            Assert.That(p.Timeout, Is.EqualTo(TimeSpan.FromMinutes(1)));
        }

        [Test]
        public void Constructor_RejectsNonPositiveTimeout()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new IdleLockPolicy(new FakeClock(), TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new IdleLockPolicy(new FakeClock(), TimeSpan.FromSeconds(-1)));
        }

        [Test]
        public void Timeout_Setter_RejectsNonPositive()
        {
            var p = new IdleLockPolicy(new FakeClock(), Five);
            Assert.Throws<ArgumentOutOfRangeException>(() => p.Timeout = TimeSpan.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => p.Timeout = TimeSpan.FromSeconds(-1));
        }

        [Test]
        public void Constructor_NullClock_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new IdleLockPolicy(null, Five));
        }

        [Test]
        public void SystemClock_IsUtcAndMonotonicallyNonDecreasing()
        {
            var c = new SystemClock();
            DateTime a = c.UtcNow;
            DateTime b = c.UtcNow;
            Assert.That(a.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(b, Is.GreaterThanOrEqualTo(a));
            Assert.That(Math.Abs((a - DateTime.UtcNow).TotalSeconds), Is.LessThan(5));
        }
    }
}
