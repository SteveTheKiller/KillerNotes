using System.Windows.Media;

namespace KillerNotes.Models
{
    // A row in the two font combos. Public (not nested) so WPF's ItemTemplate
    // bindings can reflect on Display / Fam.
    public sealed class FontChoice
    {
        public string Display { get; set; } = "";
        public string Value { get; set; } = "";      // "" | "sys:<family>" | "file:<family>"
        public FontFamily? Fam { get; set; }         // null = inherit (the Default row)
        public override string ToString() => Display;
    }
}
