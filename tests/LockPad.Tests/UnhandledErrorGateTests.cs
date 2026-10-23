using System;
using LockPad.App;
using NUnit.Framework;

namespace LockPad.Tests
{
    [TestFixture]
    public class UnhandledErrorGateTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void ShowsFirstError()
        {
            Assert.That(new UnhandledErrorGate().ShouldShow(T0), Is.True);
        }

        [Test]
        public void WhileShowing_NestedErrorsAreSuppressed()
        {
            var g = new UnhandledErrorGate();
            Assert.That(g.ShouldShow(T0), Is.True);
            g.Enter();
            Assert.That(g.ShouldShow(T0.AddMilliseconds(10)), Is.False);
            g.Exit();
            Assert.That(g.ShouldShow(T0.AddSeconds(1)), Is.True);
        }

        [Test]
        public void MoreThanFiveErrorsWithinTenSeconds_SuppressesUntilQuiet()
        {
            var g = new UnhandledErrorGate();
            for (int i = 0; i < 5; i++)
                Assert.That(g.ShouldShow(T0.AddSeconds(i)), Is.True, "error " + (i + 1));

            Assert.That(g.ShouldShow(T0.AddSeconds(5)), Is.False);
            // Still erroring: stays suppressed even though >10 s since the first error.
            Assert.That(g.ShouldShow(T0.AddSeconds(12)), Is.False);
            Assert.That(g.ShouldShow(T0.AddSeconds(20)), Is.False);
            // 10 s with no error: shows again.
            Assert.That(g.ShouldShow(T0.AddSeconds(30)), Is.True);
        }

        [Test]
        public void SpreadOutErrors_AreAlwaysShown()
        {
            var g = new UnhandledErrorGate();
            for (int i = 0; i < 20; i++)
                Assert.That(g.ShouldShow(T0.AddSeconds(i * 11)), Is.True);
        }
    }
}
