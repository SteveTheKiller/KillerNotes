using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>The picker's R, G, B and Hex boxes show only short labels, so each needs its own
    /// spoken name for screen readers.</summary>
    public sealed class ColorPickerFieldNameTests
    {
        [Fact]
        public void PrecisionColorFieldsHaveSpokenNames()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    // The picker reads its brushes and words from the application, as in the app.
                    var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    foreach (string resource in new[] { "Themes/Dark.xaml", "Controls/Controls.xaml", "Strings/en-US.xaml" })
                        app.Resources.MergedDictionaries.Add(new ResourceDictionary
                        {
                            Source = new Uri("/KillerNotes;component/" + resource, UriKind.Relative)
                        });
                    var dialog = new KillerNotes.Controls.ColorPickerDialog(null, Colors.SteelBlue);
                    var boxes = Descendants(dialog).OfType<TextBox>().ToList();
                    var names = boxes.Select(AutomationProperties.GetName).ToList();
                    foreach (string expected in new[] { "Red", "Green", "Blue", "Hex color" })
                        Assert.Contains(expected, names);
                    dialog.Close();
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The color picker did not build.");
            if (failure != null) throw new InvalidOperationException(failure.ToString());
        }

        private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is not DependencyObject d) continue;
                yield return d;
                foreach (var nested in Descendants(d)) yield return nested;
            }
        }
    }
}
