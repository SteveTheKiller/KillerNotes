using System;

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

        public string ModifiedDisplay => Modified.ToString("yyyy-MM-dd HH:mm");
    }
}
