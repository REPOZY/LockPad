using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LockPad.App.Views;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>UX round 3, V4: the note-name prompt (New note and Rename) warns against secrets in titles.</summary>
    [TestFixture]
    public class PromptHintTests
    {
        private const string Hint =
            "Titles are visible in the notes list and in messages. Don't put passwords or other secrets in a title.";

        [TestCase("New note", "Name of the new note:", "New note")]
        [TestCase("Rename note", "Title:", "Ledger")]
        [Apartment(ApartmentState.STA)]
        public void NamePrompt_ShowsTheMutedWrappedTitleHint_BehaviourUnchanged(string title, string label, string initial)
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            var win = new PromptWindow(title, label, initial);
            try
            {
                var hint = win.FindName("HintText") as TextBlock;
                Assert.That(hint, Is.Not.Null, "a TextBlock named HintText");
                Assert.That(hint.Text, Is.EqualTo(Hint));
                Assert.That(hint.TextWrapping, Is.EqualTo(TextWrapping.Wrap));
                Assert.That(hint.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(ReadLocalExpressionKey(hint, TextBlock.ForegroundProperty), Is.EqualTo("MutedForeground"));

                // Below the text box, above the buttons.
                var panel = (Panel)hint.Parent;
                var box = (TextBox)win.FindName("ValueBox");
                Assert.That(panel.Children.IndexOf(hint), Is.EqualTo(panel.Children.IndexOf(box) + 1));

                // Unchanged prompt behaviour.
                Assert.That(win.Title, Is.EqualTo(title));
                Assert.That(((TextBlock)win.FindName("LabelText")).Text, Is.EqualTo(label));
                Assert.That(box.Text, Is.EqualTo(initial));
                Assert.That(win.Value, Is.Null);
            }
            finally
            {
                win.Close();
            }
        }

        /// <summary>The resource key of a {DynamicResource} set on <paramref name="p"/>.</summary>
        private static object ReadLocalExpressionKey(DependencyObject d, DependencyProperty p)
        {
            object local = d.ReadLocalValue(p);
            var expr = local as Expression;
            Assert.That(expr, Is.Not.Null, "a DynamicResource");
            // ResourceReferenceExpression is internal; its ResourceKey property is read by reflection.
            return expr.GetType().GetProperty("ResourceKey").GetValue(expr);
        }
    }
}
