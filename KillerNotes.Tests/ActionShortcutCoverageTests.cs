using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class ActionShortcutCoverageTests
    {
        private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

        private static string Root()
        {
            for (var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "KillerNotes.csproj"))) return directory.FullName;
            throw new InvalidOperationException("Cannot locate the application sources");
        }

        [Fact]
        public void KeyboardMapInitializesWithoutConflictingPhysicalKeys()
        {
            var table = (Array)typeof(MainWindow).GetField("KsTable", StaticPrivate)!.GetValue(null)!;
            var map = (IDictionary)typeof(MainWindow).GetField("KbMap", StaticPrivate)!.GetValue(null)!;
            Assert.True(table.Length > 150);
            Assert.Equal(4, map.Count);
            foreach (DictionaryEntry layer in map) Assert.NotEmpty((IDictionary)layer.Value!);
        }

        [Fact]
        public void EveryMainWindowMenuActionAdvertisesKeyboardAccess()
        {
            var document = XDocument.Load(Path.Combine(Root(), "Shell", "MainWindow.xaml"));
            var actions = document.Descendants().Where(e => e.Name.LocalName == "MenuItem" && e.Attribute("Click") != null).ToArray();
            Assert.NotEmpty(actions);
            Assert.All(actions, action => Assert.False(string.IsNullOrWhiteSpace((string?)action.Attribute("InputGestureText")),
                "Missing menu gesture: " + action.Attribute("Click")!.Value));
        }

        [Fact]
        public void ActionBindingsHaveUniqueGesturesAndExistingLocalizedLabels()
        {
            var bindings = (Array)typeof(MainWindow).GetMethod("ActionShortcuts", StaticPrivate)!.Invoke(null, null)!;
            var seen = new HashSet<string>();
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
            var labels = XDocument.Load(Path.Combine(Root(), "Strings", "en-US.xaml")).Root!.Elements()
                .Select(e => (string?)e.Attribute(xaml + "Key")).ToHashSet();
            foreach (var binding in bindings)
            {
                var type = binding!.GetType();
                string gesture = (string)type.GetProperty("Gesture")!.GetValue(binding)!;
                string label = (string)type.GetProperty("Label")!.GetValue(binding)!;
                Assert.True(seen.Add(gesture), "Duplicate gesture: " + gesture);
                Assert.Contains(label, labels);
            }
        }
    }
}
