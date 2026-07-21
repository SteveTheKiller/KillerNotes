using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace KillerNotes.Models
{
    // One row of the notes table, metadata only. The content blob (a FlowDocument saved as
    // a XamlPackage, which carries pasted images and tables inside one zip stream) is loaded
    // separately by NoteStore.LoadContent so the sidebar list stays light.
    public class Note
    {
        public long Id { get; set; }
        public string Title { get; set; } = "";
        public string Notebook { get; set; } = "";
        public string Tags { get; set; } = "";
        public DateTime Created { get; set; }
        public DateTime Modified { get; set; }
        public string Snippet { get; set; } = "";   // first line of plain text, for the list
        public string TitleColor { get; set; } = "";   // "#RRGGBB", "" = follow the theme
        public bool SpellCheck { get; set; }           // per-note spell check (off by default)
        public int SortOrder { get; set; }             // global custom-order position (#4)

        public string ModifiedDisplay => Modified.ToString("yyyy-MM-dd HH:mm");

        // Sidebar binding helpers: the row title's DataTrigger switches to TitleBrush only
        // when a color is set, so uncolored titles keep the theme-reactive TextBrush.
        public bool HasTitleColor => TitleColor.Length > 0;
        public Brush? TitleBrush
        {
            get
            {
                if (!HasTitleColor) return null;
                try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(TitleColor)); }
                catch { return null; }
            }
        }

        // Group-membership cue (#8): notes filed in a group get an indented left stripe in the
        // sidebar so they read as nested under the group header. InGroup drives the indent + stripe
        // visibility; GroupColor is set by BuildSidebarItems from the parent group, so the stripe
        // matches a colored group, else the template falls back to a muted theme stripe.
        public bool InGroup => Notebook.Length > 0;
        public bool ShowGroupStripe => InGroup;
        // First/last member of the group (set by BuildSidebarItems) so the connector line can cap
        // its top and bottom cleanly - the segment is bounded to this group instead of running on.
        public bool IsFirstInGroup { get; set; }
        public bool IsLastInGroup { get; set; }
        public string GroupColor { get; set; } = "";
        public bool HasGroupColor => InGroup && GroupColor.Length > 0;
        public Brush? GroupColorBrush
        {
            get
            {
                if (!HasGroupColor) return null;
                try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(GroupColor)); }
                catch { return null; }
            }
        }

        /// <summary>Colored tag pills for the sidebar card, rebuilt by MainWindow
        /// (Tags.cs BuildChips) from the Tags CSV + the per-database definitions.</summary>
        public List<TagChip> Chips { get; } = [];
    }

    /// <summary>One rendered tag pill (background = tag color, foreground by luminance).</summary>
    public class TagChip
    {
        public string Name { get; set; } = "";
        public Brush Background { get; set; } = Brushes.Gray;
        public Brush Foreground { get; set; } = Brushes.White;
    }

    /// <summary>Sidebar group header row (#4). Lives in the composite sidebar list next
    /// to Note items; the implicit DataTemplate keys off the type. Not selectable - a
    /// header click toggles collapse instead (Groups.cs).</summary>
    public class GroupHeader
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public bool Collapsed { get; set; }
        public string NameColor { get; set; } = "";   // "#RRGGBB", "" = follow the theme
        public string Chevron => ((char)(Collapsed ? 0xE76C : 0xE70D)).ToString();   // MDL2 ChevronRight / ChevronDown
        public string CountDisplay => Count == 0 ? "" : Count.ToString();

        // Header-name binding helpers, mirroring Note.HasTitleColor / TitleBrush: the
        // DataTrigger switches to NameBrush only when a color is set, else theme TextBrush.
        public bool HasColor => NameColor.Length > 0;
        public Brush? NameBrush
        {
            get
            {
                if (!HasColor) return null;
                try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(NameColor)); }
                catch { return null; }
            }
        }

        // The group's colored spine (the shared container line). It's always on the header and caps
        // the top; when collapsed the header IS the whole line so it caps the bottom too (a short
        // pill), and when expanded the line runs on down through the notes. This replaces the
        // chevron - the line growing to reveal the notes is the expand/collapse affordance.
        public bool ShowGroupStripe => true;
        public bool IsFirstInGroup => true;
        public bool IsLastInGroup => Collapsed;
        public bool HasGroupColor => HasColor;
        public Brush? GroupColorBrush => NameBrush;
    }
}
