using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace KillerNotes.Models
{
    // One row of the notes table, metadata only. The content blob (a FlowDocument saved as
    // a XamlPackage, which carries pasted images and tables inside one zip stream) is loaded
    // separately by NoteStore.LoadContent so the sidebar list stays light.
    public class Note : INotifyPropertyChanged
    {
        public long Id { get; set; }
        public string Title { get; set; } = "";
        public string Notebook { get; set; } = "";
        public string Tags { get; set; } = "";
        public DateTime Created { get; set; }
        public DateTime Modified { get; set; }
        public string Snippet { get; set; } = "";   // first line of plain text, for the list
        private string _titleColor = "";
        // "#RRGGBB", "" = follow the theme. Notifying so the color picker's live preview
        // repaints the sidebar row as the color changes (mirrors GroupColor).
        public string TitleColor
        {
            get => _titleColor;
            set
            {
                if (_titleColor == value) return;
                _titleColor = value;
                OnChanged(nameof(TitleColor));
                OnChanged(nameof(HasTitleColor));
                OnChanged(nameof(TitleBrush));
            }
        }
        public bool SpellCheck { get; set; }           // per-note spell check (off by default)
        public int SortOrder { get; set; }             // global custom-order position (#4)

        public string ModifiedDisplay => Modified.ToString("yyyy-MM-dd HH:mm");

        // Sidebar row density (1.1.0): 0 = Comfortable (title, snippet, date, tags),
        // 1 = Compact (title + tags), 2 = Minimal (title only). Set on every note by
        // RefreshList from the app-wide setting; the row template binds the visibilities.
        public int Density { get; set; }
        public Visibility SnippetVisibility => Density == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DateVisibility => Density == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ChipsVisibility => Density <= 1 ? Visibility.Visible : Visibility.Collapsed;

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
        // Subgroup nesting (1.1.0): depth of this note's owning group (0 = top-level group or
        // ungrouped). GutterWidth reserves the left indent (aligned with the group header);
        // Rails are the ancestor guide lines drawn in that gutter. Set by BuildSidebarItems.
        public int GroupDepth { get; set; }
        public bool IsNested => GroupDepth > 0;   // shared spine style binds this; only headers act on it
        public double GutterWidth => GroupDepth * 14;
        public List<GroupRail> Rails { get; set; } = new();
        private string _groupColor = "";
        public string GroupColor
        {
            get => _groupColor;
            set
            {
                if (_groupColor == value) return;
                _groupColor = value;
                OnChanged(nameof(GroupColor));
                OnChanged(nameof(HasGroupColor));
                OnChanged(nameof(GroupColorBrush));
            }
        }
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

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>One rendered tag pill (background = tag color, foreground by luminance).</summary>
    public class TagChip
    {
        public string Name { get; set; } = "";
        public Brush Background { get; set; } = Brushes.Gray;
        public Brush Foreground { get; set; } = Brushes.White;
    }

    /// <summary>One ancestor guide line drawn in a sidebar row's left gutter: a thin vertical
    /// rule in an ancestor group's color, so a parent's line runs down the left of its child
    /// subgroups (containment). One per nesting level above the row. Built by BuildSidebarItems.</summary>
    public class GroupRail
    {
        public int Level { get; set; }           // nesting level of the ancestor this rail represents
        public bool HasColor { get; set; }       // false = uncolored ancestor, template uses the muted theme line
        public Brush? Brush { get; set; }
        public bool IsLast { get; set; }          // this row is the bottom of the ancestor's subtree - round the cap
    }

    /// <summary>Sidebar group header row (#4). Lives in the composite sidebar list next
    /// to Note items; the implicit DataTemplate keys off the type. Not selectable - a
    /// header click toggles collapse instead (Groups.cs).</summary>
    public class GroupHeader : INotifyPropertyChanged
    {
        // Subgroups (1.1.0): Path is the group's full identity (top-level = its name; a child =
        // parentPath + GroupSep + leaf). Name is the LEAF only, shown in the sidebar; every
        // NoteStore call keys off Path. Depth (0 = top-level) drives the row's left indent;
        // GutterWidth reserves it and Rails are the ancestor guide lines drawn in that gutter.
        public string Path { get; set; } = "";
        public int Depth { get; set; }
        public bool IsNested => Depth > 0;   // a subgroup: its own spine starts just above its header text (see the spine style in MainWindow.xaml)
        public double GutterWidth => Depth * 14;
        public List<GroupRail> Rails { get; set; } = new();
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public bool Collapsed { get; set; }
        private string _nameColor = "";
        public string NameColor   // "#RRGGBB", "" = follow the theme
        {
            get => _nameColor;
            set
            {
                if (_nameColor == value) return;
                _nameColor = value;
                OnChanged(nameof(NameColor));
                OnChanged(nameof(HasColor));
                OnChanged(nameof(NameBrush));
                OnChanged(nameof(HasGroupColor));
                OnChanged(nameof(GroupColorBrush));
            }
        }
        public string Chevron => ((char)(Collapsed ? 0xE76C : 0xE70D)).ToString();   // MDL2 ChevronRight / ChevronDown
        public string CountDisplay => Count == 0 ? "" : Count.ToString();

        // Sidebar density (set by BuildSidebarItems from the app-wide setting): the compact
        // modes trim the header's breathing room too, not just the note rows. The header
        // template binds these instead of hardcoding its Padding/Margin.
        public int Density { get; set; }
        public Thickness HeaderPadding => new(0, Density == 0 ? 2 : 0, 0, 0);
        public Thickness HeaderMargin => Density switch
        {
            0 => new Thickness(0, 0, 0, -6),
            1 => new Thickness(0, 0, 0, -8),
            _ => new Thickness(0, -2, 0, -9),
        };

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

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
