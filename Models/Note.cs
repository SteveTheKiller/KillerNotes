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
}
