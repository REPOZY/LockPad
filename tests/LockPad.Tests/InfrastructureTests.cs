using System;
using System.Collections.Generic;
using LockPad.App;
using LockPad.App.Infrastructure;
using NUnit.Framework;

namespace LockPad.Tests
{
    [TestFixture]
    public class InfrastructureTests
    {
        private sealed class Sample : ObservableObject
        {
            private int _value;
            public int Value
            {
                get { return _value; }
                set { SetProperty(ref _value, value); }
            }

            public bool Set(int v) { return SetProperty(ref _value, v, "Value"); }
        }

        [Test]
        public void SetProperty_RaisesOnlyOnChange()
        {
            var s = new Sample();
            var names = new List<string>();
            s.PropertyChanged += (o, e) => names.Add(e.PropertyName);

            Assert.That(s.Set(0), Is.False);
            Assert.That(names, Is.Empty);

            Assert.That(s.Set(5), Is.True);
            Assert.That(names, Is.EqualTo(new[] { "Value" }));

            s.Value = 5;
            Assert.That(names.Count, Is.EqualTo(1));

            s.Value = 6;
            Assert.That(names, Is.EqualTo(new[] { "Value", "Value" }));
        }

        [Test]
        public void RelayCommand_CanExecuteRespectsPredicate()
        {
            bool allowed = false;
            object seen = null;
            var cmd = new RelayCommand(p => seen = p, p => allowed);
            int raised = 0;
            cmd.CanExecuteChanged += (o, e) => raised++;

            Assert.That(cmd.CanExecute(null), Is.False);
            allowed = true;
            Assert.That(cmd.CanExecute(null), Is.True);

            cmd.Execute("arg");
            Assert.That(seen, Is.EqualTo("arg"));

            cmd.RaiseCanExecuteChanged();
            Assert.That(raised, Is.EqualTo(1));
        }

        [Test]
        public void RelayCommand_WithoutPredicate_AlwaysCanExecute()
        {
            Assert.That(new RelayCommand(p => { }).CanExecute(null), Is.True);
        }

        [Test]
        public void RelayCommand_NullExecute_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new RelayCommand(null));
        }

        [Test]
        public void FakeDialogService_UsesScriptedAnswersThenDefaults()
        {
            var d = new FakeDialogService();
            d.Confirms.Enqueue(true);
            d.OpenPaths.Enqueue("a.kdbx");
            Assert.That(d.Confirm("t", "m"), Is.True);
            Assert.That(d.Confirm("t", "m"), Is.False);
            Assert.That(d.PickOpenNotebookPath(), Is.EqualTo("a.kdbx"));
            Assert.That(d.PickOpenNotebookPath(), Is.Null);
            Assert.That(d.AskExternalChange("p"), Is.EqualTo(LockPad.App.Services.ExternalChangeChoice.Cancel));
            Assert.That(d.Calls, Is.EqualTo(new[]
            {
                "Confirm", "Confirm", "PickOpenNotebookPath", "PickOpenNotebookPath", "AskExternalChange"
            }));
        }

        [Test]
        public void DescribeUnhandled_ReturnsMessage_NeverStackTrace()
        {
            Exception ex;
            try { throw new InvalidOperationException("boom"); }
            catch (Exception e) { ex = e; }
            string text = App.App.DescribeUnhandled(ex);
            Assert.That(text, Is.EqualTo("boom"));
            Assert.That(text, Does.Not.Contain(" at "));
            Assert.That(text, Does.Not.Contain("InfrastructureTests"));
        }

        [Test]
        public void DescribeUnhandled_EmptyMessage_ReturnsTypeName()
        {
            Assert.That(App.App.DescribeUnhandled(new EmptyMessageException()), Is.EqualTo("EmptyMessageException"));
            Assert.That(App.App.DescribeUnhandled(null), Is.EqualTo("Unknown error"));
        }

        private sealed class EmptyMessageException : Exception
        {
            public override string Message { get { return ""; } }
        }
    }
}
