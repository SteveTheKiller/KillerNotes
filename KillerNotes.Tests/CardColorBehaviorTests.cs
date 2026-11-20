using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KillerFamily.Controls;
using KillerNotes.Controls;
using KillerNotes.Services;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class CardColorBehaviorTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReopenedCardColorButtonsUseTheCurrentLanguage(bool group) => WithShell((window, host) =>
        {
            long id = NoteStore.Create("Card note");
            NoteStore.AddGroup("Work");
            var target = new Button { Content = "Card target" };
            var targetWindow = new Window { Content = target, Width = 160, Height = 80, ShowInTaskbar = false };
            targetWindow.Show();
            var menu = (ContextMenu)window.FindName(group ? "GroupCard" : "NoteCard");
            void Open()
            {
                if (group) host.ShowGroupCard("Work", 0, target, false);
                else host.ShowNoteCard(new[] { id }, target, false);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            }
            var previous = LocaleManager.Current;
            try
            {
                Open();
                menu.IsOpen = false;
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                LocaleManager.Apply(Locale.De);
                Open();
                // 1.7.1: each open builds the palette afresh in the card's Color flyout.
                var buttons = CardTileHelper.Swatches(menu).Children.OfType<Button>().ToList();
                Assert.Equal("Keine Farbe", AutomationProperties.GetName(buttons.First()));
                Assert.StartsWith("Weitere Farben", AutomationProperties.GetName(buttons.Last()));
            }
            finally
            {
                menu.IsOpen = false;
                targetWindow.Close();
                LocaleManager.Apply(previous);
            }
        });

        [Fact]
        public void ManagerSingleNotePickerUsesTheCardsNoteRatherThanTheSidebarSelection() => WithShell((window, host) =>
        {
            long sidebarId = NoteStore.Create("Sidebar");
            long cardId = NoteStore.Create("Manager");
            NoteStore.SetTitleColor(sidebarId, "#112233");
            NoteStore.SetTitleColor(cardId, "#AABBCC");
            var list = (ListBox)window.FindName("NotesList");
            list.ItemsSource = NoteStore.List();
            list.SelectedIndex = 0;
            list.SelectedItem = list.Items.Cast<Models.Note>().Single(n => n.Id == sidebarId);
            var target = new Button { Content = "Card target" };
            var targetWindow = new Window { Content = target, Width = 160, Height = 80, ShowInTaskbar = false };
            targetWindow.Show();
            host.ShowNoteCard(new[] { cardId }, target, false);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var row = CardTileHelper.Swatches((ContextMenu)window.FindName("NoteCard"));
            Color? initial = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var picker = Application.Current.Windows.OfType<ColorPickerDialog>().FirstOrDefault();
                if (picker == null) return;
                initial = picker.SelectedColor;
                timer.Stop();
                picker.Close();
            };
            timer.Start();
            try { row.Children.OfType<Button>().Last().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            finally { timer.Stop(); targetWindow.Close(); }
            Assert.Equal(Color.FromRgb(0xAA, 0xBB, 0xCC), initial);
        });

        internal static void WithShell(Action<MainWindow, IGroupManagerHost> body) => Sta.RunApplication(() =>
        {
            using var settings = new UiSettingsScope();
            if (Application.ResourceAssembly == null) Application.ResourceAssembly = typeof(App).Assembly;
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var saved = app.Resources.MergedDictionaries.ToArray();
            bool previousDemo = MainWindow.DemoMode;
            MainWindow.DemoMode = true;
            using var store = new TempStore();
            MainWindow? window = null;
            try
            {
                app.Resources.MergedDictionaries.Clear();
                app.Resources.MergedDictionaries.Add(ThemeHarness.Create(Theme.Dark, ThemeHarness.FirstRunAccent(Theme.Dark)));
                foreach (string resource in new[] { "Controls/Controls.xaml", "Strings/en-US.xaml", "Controls/AppStyles.xaml", "Controls/OverlayScrollViewer.xaml" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/KillerNotes;component/" + resource, UriKind.Relative) });
                window = new MainWindow();
                new WindowInteropHelper(window).EnsureHandle();
                body(window, window);
            }
            finally
            {
                if (window != null) PinAutomationNames(window,new System.Collections.Generic.HashSet<DependencyObject>());
                if (window?.FindName("RootGrid") is Grid root)
                {
                    root.BeginAnimation(UIElement.OpacityProperty,null);
                    root.Opacity = 0;
                }
                window?.Close();
                // The motif memo (finished layers, pieces) is static and every shell test shares one dispatcher, so a
                // later test that opens a window at the same size painted finished layers at once instead of waiting
                // on the disk decode. Each shell test starts with no motif state.
                KillerNotes.Controls.ThemeMotif.ReleaseAll();
                MainWindow.DemoMode = previousDemo;
                app.Resources.MergedDictionaries.Clear();
                foreach (var resource in saved) app.Resources.MergedDictionaries.Add(resource);
            }
        });

        private static void PinAutomationNames(DependencyObject node, System.Collections.Generic.HashSet<DependencyObject> seen)
        {
            if (!seen.Add(node)) return;
            if (node is FrameworkElement element)
            {
                AutomationProperties.SetName(element,AutomationProperties.GetName(element) ?? "");
                if (element.ContextMenu != null) PinAutomationNames(element.ContextMenu,seen);
            }
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) PinAutomationNames(child,seen);
            if (node is Visual)
                for (int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++) PinAutomationNames(VisualTreeHelper.GetChild(node,i),seen);
        }
    }
}
