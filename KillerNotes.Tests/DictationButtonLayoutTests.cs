using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KillerNotes.Controls.Dictation;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class DictationButtonLayoutTests
    {
        public static IEnumerable<object[]> Languages()
            => Enum.GetValues(typeof(Locale)).Cast<Locale>().Select(locale => new object[] { locale.ToString() });

        [Theory]
        [MemberData(nameof(Languages))]
        public void LocalizedActionsFitAndKeepTheirNamesWhenEditingAudio(string language)
            => CardColorBehaviorTests.WithShell((_, _) =>
        {
            var previous = LocaleManager.Current;
            LocaleManager.Apply((Locale)Enum.Parse(typeof(Locale), language));
            var pad = new DictationWindow(null, _ => { }, (_, _, _, _, _) => null) { Width = 520, Height = 360 };
            try
            {
                pad.Show();
                CheckLayout();
                Assert.True(pad.LoadForEdit(DictationWorkflowTests.Silence(1000), "test", 1, null));
                CheckLayout();
                var save = Elements(pad).OfType<Button>().Single(b => AutomationProperties.GetAcceleratorKey(b) == "Alt+E");
                Assert.Equal((string)Application.Current.FindResource("Str_Dict_SaveChanges"), AutomationProperties.GetName(save));
            }
            finally { pad.Close(); LocaleManager.Apply(previous); }

            void CheckLayout()
            {
                pad.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                pad.UpdateLayout();
                var actions = Elements(pad).OfType<Button>().Where(b =>
                    new[] { "Alt+R", "Alt+P", "Alt+T", "Ctrl+Enter", "Alt+E" }.Contains(AutomationProperties.GetAcceleratorKey(b))).ToArray();
                Assert.Equal(5, actions.Length);
                Rect Bounds(FrameworkElement e) => e.TransformToAncestor(pad).TransformBounds(new Rect(e.RenderSize));
                var client = new Rect(pad.RenderSize);
                foreach (var button in actions)
                {
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
                    Assert.True(client.Contains(Bounds(button)), language + ": action extends outside the window.");
                    foreach (var label in Elements(button).OfType<TextBlock>())
                        Assert.True(Bounds(button).Contains(Bounds(label)), language + ": action caption is clipped.");
                }
                for (int i = 0; i < actions.Length; i++)
                    for (int j = i + 1; j < actions.Length; j++)
                        Assert.False(Bounds(actions[i]).IntersectsWith(Bounds(actions[j])), language + ": actions overlap.");
            }
        });

        private static IEnumerable<DependencyObject> Elements(DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Elements(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
    }
}
