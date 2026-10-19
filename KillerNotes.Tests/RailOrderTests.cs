using System.Linq;
using System.Windows;
using System.Windows.Controls;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The family rail order (Shell/RailOrder.cs): theme, language and shortcuts hold the rail's
    /// corner, and the other rail buttons keep the order they were dragged into across restarts.
    /// The drag gesture needs a live mouse, so these pin what it saves and what a restart rebuilds.
    /// </summary>
    public sealed class RailOrderTests
    {
        private static readonly string[] Movable = { "A", "B", "C" };
        private static readonly string[] EdgeFirst = { "Theme", "Lang", "Help" };

        [Fact]
        public void No_saved_order_keeps_the_default_with_the_edge_buttons_last_and_theme_outermost()
        {
            Assert.Equal(new[] { "A", "B", "C", "Help", "Lang", "Theme" },
                RailOrder.Arrange(Movable, EdgeFirst, null));
        }

        [Fact]
        public void A_saved_order_is_restored()
        {
            Assert.Equal(new[] { "C", "A", "B", "Help", "Lang", "Theme" },
                RailOrder.Arrange(Movable, EdgeFirst, "C,A,B"));
        }

        [Fact]
        public void Unknown_duplicate_and_edge_names_in_a_saved_order_are_ignored()
        {
            Assert.Equal(new[] { "B", "A", "C", "Help", "Lang", "Theme" },
                RailOrder.Arrange(Movable, EdgeFirst, " B ,Gone,B,Theme,A,C"));
        }

        [Fact]
        public void A_button_missing_from_the_saved_order_keeps_its_default_place()
        {
            Assert.Equal(new[] { "A", "C", "B", "Help", "Lang", "Theme" },
                RailOrder.Arrange(Movable, EdgeFirst, "C,B"));
        }

        [Fact]
        public void The_saved_form_round_trips_through_Arrange()
        {
            string saved = RailOrder.Serialize(new[] { "B", "C", "A" });
            Assert.Equal(new[] { "B", "C", "A" }, RailOrder.Arrange(Movable, EdgeFirst, saved).Take(3));
        }

        [Fact]
        public void The_nearest_slot_follows_the_pointer()
        {
            var centers = new[] { 10.0, 40.0, 70.0 };
            Assert.Equal(0, RailOrder.NearestSlot(centers, -5));
            Assert.Equal(1, RailOrder.NearestSlot(centers, 50));
            Assert.Equal(2, RailOrder.NearestSlot(centers, 500));
            Assert.Equal(-1, RailOrder.NearestSlot(new double[0], 5));
        }

        [Fact]
        public void Attach_rebuilds_the_rail_in_the_saved_order_and_leaves_other_elements_in_place()
        {
            Sta.Run(() =>
            {
                var rail = new StackPanel();
                Button Make(string name) { var b = new Button { Name = name }; rail.Children.Add(b); return b; }
                var a = Make("A"); var b2 = Make("B"); var c = Make("C");
                var spacer = new Border(); rail.Children.Add(spacer);
                var help = Make("Help"); var lang = Make("Lang"); var theme = Make("Theme");
                string? saved = null;

                RailOrder.Attach(rail, new FrameworkElement[] { theme, lang, help },
                    new FrameworkElement[] { a, b2, c }, "C,A,B", s => saved = s);

                Assert.Equal(new UIElement[] { c, a, b2, spacer, help, lang, theme },
                    rail.Children.Cast<UIElement>().ToArray());
                Assert.Null(saved);
            });
        }
    }
}
