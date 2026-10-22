using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace KillerNotes.Tests
{
    [CollectionDefinition("Application resources", DisableParallelization = true)]
    public sealed class ApplicationResourcesCollection { }

    /// <summary>The picker's R, G, B and Hex boxes show only short labels, so each needs its own
    /// spoken name for screen readers.</summary>
    [Collection("Application resources")]
    public sealed class ColorPickerFieldNameTests
    {
        [Fact]
        public void PrecisionColorFieldsHaveSpokenNames() => Sta.RunApplication(() =>
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var previous = app.Resources.MergedDictionaries.ToArray();
            KillerNotes.Controls.ColorPickerDialog? dialog = null;
            try
            {
                foreach (string resource in new[] { "Themes/Dark.xaml", "Controls/Controls.xaml", "Strings/en-US.xaml" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("/KillerNotes;component/" + resource, UriKind.Relative)
                    });
                dialog = new KillerNotes.Controls.ColorPickerDialog(null, Colors.SteelBlue);
                var boxes = Descendants(dialog).OfType<TextBox>().ToList();
                var names = boxes.Select(AutomationProperties.GetName).ToList();
                foreach (string expected in new[] { "Red", "Green", "Blue", "Hex color" })
                    Assert.Contains(expected, names);
            }
            finally
            {
                dialog?.Close();
                app.Resources.MergedDictionaries.Clear();
                foreach (var dictionary in previous)
                    app.Resources.MergedDictionaries.Add(dictionary);
            }
        });

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
