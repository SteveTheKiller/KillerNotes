using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Services;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests.Perf
{
    /// <summary>
    /// How long one F3 keeps the window busy looking for the next note with a real match, in both
    /// searches. Both walk the sidebar's results on the UI thread, so this time is also how long
    /// the next key waits. Writes results-search-navigation.json to KN_PERF_OUT. The notes are the
    /// worst cases for the walk: a match only in the title, only in a sketch label, or nowhere the
    /// editor can show, and notes full of pictures. Median of five after one warm-up.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class SearchNavigationBenchmarks
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Term = "cat";

        [PerfFact]
        public void Run() => Sta.Run(() =>
        {
            var lines = new List<string>();
            lines.Add(Measure("next note is the neighbor, 60 notes", 60, _ => ("Body", "a cat here", Package(new FlowDocument(new Paragraph(new Run("a cat here")))))));
            lines.Add(Measure("title-only, 1000 notes", 1000, _ => (Term + " title", "nothing", Small())));
            lines.Add(Measure("sketch-label-only, 60 notes", 60, _ => ("Sketch", "nothing\n" + Term, Small())));
            lines.Add(Measure("sketch-label-only, 1000 notes", 1000, _ => ("Sketch", "nothing\n" + Term, Small())));
            lines.Add(Measure("title-only with 20 pictures, 60 notes", 60, _ => (Term + " title", "nothing", Pictures())));
            lines.Add(Measure("sketch-label-only with 20 pictures, 60 notes", 60, _ => ("Sketch", "nothing\n" + Term, Pictures())));

            string outDir = Environment.GetEnvironmentVariable("KN_PERF_OUT") ?? Path.Combine(Path.GetTempPath(), "kn-perf");
            Directory.CreateDirectory(outDir);
            File.WriteAllLines(Path.Combine(outDir, "results-search-navigation.json"), lines);
        });

        private static byte[]? _small, _pictures;
        private static byte[] Small() => _small ??= Package(new FlowDocument(new Paragraph(new Run("nothing"))));

        private static byte[] Pictures()
        {
            if (_pictures != null) return _pictures;
            var doc = new FlowDocument();
            for (int n = 0; n < 20; n++)
            {
                int w = 400, h = 250;
                var pixels = new byte[w * h * 4];
                for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = (byte)(i * 13 + n); pixels[i + 1] = (byte)(i >> 3); pixels[i + 2] = 90; pixels[i + 3] = 255; }
                var image = new Image { Source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4) };
                doc.Blocks.Add(new Paragraph(new InlineUIContainer(image)));
            }
            return _pictures = Package(doc);
        }

        private static byte[] Package(FlowDocument doc)
        {
            using var bytes = new MemoryStream();
            new TextRange(doc.ContentStart, doc.ContentEnd).Save(bytes, DataFormats.XamlPackage);
            return bytes.ToArray();
        }

        private static string Measure(string name, int count, Func<int, (string Title, string Plain, byte[] Content)> note)
        {
            using var store = new TempStore();
            long first = Add("First " + Term, "a cat here", Small());
            for (int i = 0; i < count; i++)
            {
                var (title, plain, content) = note(i);
                Add(title, plain, content);
            }
            var window = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
            Set(window, "_findTerm", Term);
            Set(window, "_currentId", first);
            Set(window, "_notes", NoteStore.List(Term, "custom"));
            Set(window, "SearchBox", new TextBox { Text = Term });
            var global = Time(() => window.FindNextGlobalNoteIndex(1));
            var local = Time(() => window.FindNextNoteIndex(1));
            return $"{{\"scenario\":\"{name}\",\"blobBytes\":{note(0).Content.Length},\"globalMs\":{global:0.0},\"localMs\":{local:0.0}}}";
        }

        private static long Add(string title, string plain, byte[] content)
        {
            long id = NoteStore.Create(title);
            NoteStore.Save(id, title, content, plain);
            return id;
        }

        private static double Time(Action run)
        {
            run();
            var times = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                var watch = Stopwatch.StartNew();
                run();
                times.Add(watch.Elapsed.TotalMilliseconds);
            }
            return times.OrderBy(t => t).ElementAt(2);
        }

        private static void Set(MainWindow window, string name, object value) =>
            typeof(MainWindow).GetField(name, Fields)!.SetValue(window, value);
    }
}
