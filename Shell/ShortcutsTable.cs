using System.Collections.Generic;
using System.Linq;

namespace KillerNotes.Shell
{
    // THE shortcuts table. One row per binding, feeding BOTH the F1 list (Shortcuts.cs) and the
    // keyboard map (KeyboardMap.cs), so a key can never end up described one way in one view and
    // another way in the other - which is exactly what two parallel tables used to allow.
    //
    // The two label families are deliberate and BOTH are kept:
    //   Label - the long list description ("Collapse / expand the sidebar")
    //   Cap   - the short caption printed on the drawn key, which has to fit ("Sidebar")
    // Most rows reuse the long form as the cap where it already fits, which is why plenty of
    // Cap entries are Str_KS_* rather than Str_Kb_*.
    //
    // Keys is the list row's key column; Caps are the physical keys the map lights, each with
    // its own layer (one row can span layers: F7 and Ctrl+Shift+D are one binding). Either side
    // may be empty - a row with no Keys is map-only (the Apps key), and a row could carry no
    // Caps if it has no single key to light (Ctrl+Wheel rides on Ctrl+0).
    public partial class MainWindow
    {
        /// <summary>One binding, shared by both shortcut views.</summary>
        private sealed record KsBinding(
            string Keys,
            string Label,
            string Cat,
            (KbLayer Layer, string Id, string Cap)[] Caps,
            bool Listed = true);

        private static readonly KsBinding[] KsTable =
        [
            new("F1", "Str_KS_ThisList", "Help", [(KbLayer.Base, "F1", "Str_Kb_Shortcuts")]),
            new("F2", "Str_KS_Rename", "Note", [(KbLayer.Base, "F2", "Str_KS_Rename")]),
            // The two searches are separate keys now: F3 is the sidebar's cross-note search,
            // Ctrl+F is find-in-this-note. F3 also steps matches while the find bar is open,
            // which is a state the map cannot draw, so the cap keeps its primary meaning.
            new("F3", "Str_KS_Search", "Search", [(KbLayer.Base, "F3", "Str_KS_Search")]),
            new("Ctrl+F", "Str_KS_Find", "Search", [(KbLayer.Ctrl, "F", "Str_KS_Find")]),
            new("Ctrl+H", "Str_KS_Replace", "Search", [(KbLayer.Ctrl, "H", "Str_KS_Replace")]),
            // Context-sensitive, and the label says so rather than leaving it to be found: with a
            // group header selected it graphs that group, otherwise the whole notebook.
            new("Ctrl+Shift+B", "Str_KS_Graph", "View", [(KbLayer.CtrlShift, "B", "Str_Kb_Graph")]),
            new("F4", "Str_KS_Preview", "View", [(KbLayer.Base, "F4", "Str_Kb_Preview")]),
            new("F5", "Str_KS_Sidebar", "View", [(KbLayer.Base, "F5", "Str_Kb_Sidebar")]),
            new("F6", "Str_KS_FormatBar", "View", [(KbLayer.Base, "F6", "Str_Kb_FormatBar")]),
            new("F7 / Ctrl+Shift+D", "Str_KS_SketchPad", "View", [(KbLayer.Base, "F7", "Str_KS_SketchPad"), (KbLayer.CtrlShift, "D", "Str_KS_SketchPad")]),
            new("F8", "Str_KS_Export", "File", [(KbLayer.Base, "F8", "Str_Kb_Export")]),
            new("F9", "Str_KS_Calc", "View", [(KbLayer.Base, "F9", "Str_KS_Calc")]),
            new("F10 / Ctrl+M", "Str_KS_Dictation", "View", [(KbLayer.Base, "F10", "Str_Kb_Dictation"), (KbLayer.Ctrl, "M", "Str_Kb_Dictation")]),
            new("Ctrl+F10", "Str_KS_SortCycle", "View", [(KbLayer.Ctrl, "F10", "Str_KS_SortCycle")]),
            new("F11", "Str_KS_Fullscreen", "View", [(KbLayer.Base, "F11", "Str_KS_Fullscreen")]),
            new("F12", "Str_KS_About", "Help", [(KbLayer.Base, "F12", "Str_KS_About")]),
            // The Alt layer. Alt+Left/Right are the browser's own back and forward, so they cost
            // nothing to learn; the mouse thumb buttons do the same thing (NoteHistory.cs).
            new("Alt+Left", "Str_KS_NavBack", "Note", [(KbLayer.Alt, "Left", "Str_Kb_NavBack")]),
            new("Alt+Right", "Str_KS_NavForward", "Note", [(KbLayer.Alt, "Right", "Str_Kb_NavForward")]),
            new("Alt+L", "Str_KS_LineNumbers", "View", [(KbLayer.Alt, "L", "Str_Kb_LineNumbers")]),
            new("Alt+M", "Str_KS_HideMentions", "View", [(KbLayer.Alt, "M", "Str_Kb_HideMentions")]),
            new("Alt+P", "Str_KS_Pin", "Note", [(KbLayer.Alt, "P", "Str_Kb_Pin")]),
            new("Alt+C", "Str_KS_Checkbox", "Format", [(KbLayer.Alt, "C", "Str_Kb_Checkbox")]),
            new("Ctrl+N", "Str_KS_NewNote", "Note", [(KbLayer.Ctrl, "N", "Str_KS_NewNote")]),
            new("Ctrl+G", "Str_KS_NewGroup", "Note", [(KbLayer.Ctrl, "G", "Str_KS_NewGroup")]),
            new("Ctrl+T", "Str_KS_ManageTags", "Note", [(KbLayer.Ctrl, "T", "Str_KS_ManageTags")]),
            new("Ctrl+K", "Str_KS_Link", "Format", [(KbLayer.Ctrl, "K", "Str_KS_Link")]),
            new("Ctrl+O", "Str_KS_OpenFiles", "File", [(KbLayer.Ctrl, "O", "Str_KS_OpenFiles")]),
            new("Ctrl+S", "Str_KS_Save", "File", [(KbLayer.Ctrl, "S", "Str_Kb_SaveNow")]),
            new("Ctrl+B / I / U", "Str_KS_BIU", "Format",
                [
                 (KbLayer.Ctrl, "B", "Str_Kb_Bold"),
                 (KbLayer.Ctrl, "I", "Str_Kb_Italic"),
                 (KbLayer.Ctrl, "U", "Str_Kb_Underline")]),
            new("Ctrl+Shift+S", "Str_KS_Strike", "Format", [(KbLayer.CtrlShift, "S", "Str_KS_Strike")]),
            new("Ctrl+Shift+M", "Str_KS_Mono", "Format", [(KbLayer.CtrlShift, "M", "Str_KS_Mono")]),
            new("Ctrl+Shift+H", "Str_KS_Highlight", "Format", [(KbLayer.CtrlShift, "H", "Str_Kb_Highlight")]),
            new("Ctrl+Shift+R", "Str_KS_Rule", "Format", [(KbLayer.CtrlShift, "R", "Str_Kb_Rule")]),
            new("Ctrl+Shift+L / N", "Str_KS_Lists", "Format", [(KbLayer.CtrlShift, "L", "Str_Kb_Bullets"), (KbLayer.CtrlShift, "N", "Str_Kb_Numbered")]),
            new("Ctrl+Shift+J", "Str_KS_ConvertList", "Format", [(KbLayer.CtrlShift, "J", "Str_KS_ConvertList")]),
            new("Ctrl+Shift+G", "Str_KS_NewSubgroup", "Note", [(KbLayer.CtrlShift, "G", "Str_KS_NewSubgroup")]),
            new("Ctrl+Shift+K", "Str_KS_GroupColor", "Note", [(KbLayer.CtrlShift, "K", "Str_KS_GroupColor")]),
            new("Ctrl+Shift+W", "Str_KS_WordWrap", "View", [(KbLayer.CtrlShift, "W", "Str_KS_WordWrap")]),
            new("Ctrl+Shift+E", "Str_KS_Syntax", "View", [(KbLayer.CtrlShift, "E", "Str_KS_Syntax")]),
            new("Ctrl+Shift+C", "Str_KS_TitleColor", "Note", [(KbLayer.CtrlShift, "C", "Str_KS_TitleColor")]),
            new("Ctrl+Shift+P", "Str_KS_Spell", "Format", [(KbLayer.CtrlShift, "P", "Str_KS_Spell")]),
            new("Ctrl+Shift+T", "Str_KS_Table", "Format", [(KbLayer.CtrlShift, "T", "Str_KS_Table")]),
            new("Ctrl+Shift+A", "Str_KS_Theme", "View", [(KbLayer.CtrlShift, "A", "Str_KS_Theme")]),
            new("Ctrl+D", "Str_KS_Density", "View", [(KbLayer.Ctrl, "D", "Str_KS_Density")]),
            new("Ctrl+Enter", "Str_KS_CalcPrint", "Note", [(KbLayer.Ctrl, "Enter", "Str_KS_CalcPrint")]),
            new("Ctrl+Shift+Enter", "Str_KS_CalcPrintEq", "Note", [(KbLayer.CtrlShift, "Enter", "Str_KS_CalcPrintEq")]),
            new("Ctrl+1 - 9", "Str_KS_Tags", "Note",
                [
                 (KbLayer.Ctrl, "D1", "Str_Kb_Tag"),
                 (KbLayer.Ctrl, "D2", "Str_Kb_Tag"),
                 (KbLayer.Ctrl, "D3", "Str_Kb_Tag"),
                 (KbLayer.Ctrl, "D4", "Str_Kb_Tag"),
                 (KbLayer.Ctrl, "D5", "Str_Kb_Tag"),
                 (KbLayer.Ctrl, "D6", "Str_Kb_Tag"),
                 (KbLayer.Ctrl, "D7", "Str_Kb_Tag"),
                 (KbLayer.Ctrl, "D8", "Str_Kb_Tag"),
                 (KbLayer.Ctrl, "D9", "Str_Kb_Tag")]),
            new("Ctrl+Shift+> / <", "Str_KS_FontSize", "Format",
                [
                 (KbLayer.CtrlShift, "Period", "Str_Kb_FontUp"),
                 (KbLayer.CtrlShift, "Comma", "Str_Kb_FontDown")]),
            new("Ctrl+Wheel / Ctrl+0", "Str_KS_Zoom", "View", [(KbLayer.Ctrl, "D0", "Str_Kb_ZoomReset")]),
            new("Ctrl+Shift +/- / 0", "Str_KS_AppSize", "View",
                [
                 (KbLayer.CtrlShift, "Equals", "Str_KS_AppSize"),
                 (KbLayer.CtrlShift, "Minus", "Str_KS_AppSize"),
                 (KbLayer.CtrlShift, "D0", "Str_KS_AppSize")]),
            new("Ctrl+X / C", "Str_KS_CutCopy", "Edit", [(KbLayer.Ctrl, "X", "Str_Kb_Cut"), (KbLayer.Ctrl, "C", "Str_Kb_Copy")], Listed: false),
            new("Ctrl+V", "Str_KS_Paste", "Edit", [(KbLayer.Ctrl, "V", "Str_Kb_Paste")], Listed: false),
            new("Ctrl+Z / Y", "Str_KS_Undo", "Edit", [(KbLayer.Ctrl, "Z", "Str_Kb_Undo"), (KbLayer.Ctrl, "Y", "Str_Kb_Redo")], Listed: false),
            new("Ctrl+A", "Str_KS_SelectAll", "Edit", [(KbLayer.Ctrl, "A", "Str_Kb_SelectAll")], Listed: false),
            new("Ctrl+Home / End", "Str_KS_NoteNav", "Edit", [(KbLayer.Ctrl, "Home", "Str_Kb_NoteTop"), (KbLayer.Ctrl, "End", "Str_Kb_NoteEnd")], Listed: false),
            new("Ctrl+Left / Right", "Str_KS_WordJump", "Edit", [(KbLayer.Ctrl, "Left", "Str_Kb_WordLeft"), (KbLayer.Ctrl, "Right", "Str_Kb_WordRight")], Listed: false),
            new("Ctrl+Bksp / Del", "Str_KS_DelWord", "Edit", [(KbLayer.Ctrl, "Back", "Str_Kb_DelWordL"), (KbLayer.Ctrl, "Del", "Str_Kb_DelWordR")], Listed: false),
            new("Ctrl+L / E / R / J", "Str_KS_Align", "Format",
                [
                 (KbLayer.Ctrl, "L", "Str_Kb_AlignL"),
                 (KbLayer.Ctrl, "E", "Str_Kb_AlignC"),
                 (KbLayer.Ctrl, "R", "Str_Kb_AlignR"),
                 (KbLayer.Ctrl, "J", "Str_Kb_AlignJ")]),
            new("Ctrl+] / [", "Str_KS_FontSize", "Format", [(KbLayer.Ctrl, "RBr", "Str_Kb_FontUp"), (KbLayer.Ctrl, "LBr", "Str_Kb_FontDown")]),
            new("Delete", "Str_KS_Delete", "Note", [(KbLayer.Base, "Del", "Str_Kb_DeleteNote")]),
            new("Esc", "Str_KS_Esc", "Help", [(KbLayer.Base, "Esc", "Str_KS_Esc")]),
            new("", "", "Edit", [(KbLayer.Base, "Menu", "Str_Kb_CtxMenu")]),

            // ---- OTHER WINDOWS ----
            //
            // Everything above is the main window. The graph and the SketchPad are separate
            // windows with their own key spaces, and their bare letters mean nothing while you
            // are typing a note - so these carry NO Caps and are list-only. Painting V or G on
            // the drawn keyboard would say the main window does something it does not.
            //
            // A row with empty Keys and a non-empty Label is a SECTION HEADER, which is what
            // keeps that distinction visible instead of leaving these to read as more main-window
            // bindings. (The Menu row above has both empty and stays map-only.)
            //
            // Deliberately CONSOLIDATED: the twelve SketchPad tools and the five graph
            // arrangements are one row each. This list was already too long before these were
            // added, and twenty-six more one-key rows would have made it a dump.
            new("", "Str_KS_SecGraph", "View", []),
            new("R / C / G / B / T", "Str_KS_GrArrange", "View", []),
            new("V", "Str_KS_GrVisualizer", "View", []),
            new("N", "Str_KS_GrNextShape", "View", []),
            new("P", "Str_KS_GrPin", "View", []),
            new("K", "Str_KS_GrLock", "View", []),
            new("F", "Str_KS_GrIsolate", "View", []),
            new("L", "Str_KS_GrLabels", "View", []),
            new("Ctrl+A / Ctrl+C", "Str_KS_GrSelect", "View", []),
            new("Enter / Esc", "Str_KS_GrOpen", "View", []),

            new("", "Str_KS_SecSketch", "View", []),
            new("V P L A R O G B T E C I", "Str_KS_SkTools", "View", []),
            new("Ctrl+Z / Ctrl+Y", "Str_KS_SkUndo", "View", []),
            new("Ctrl+V", "Str_KS_SkPaste", "View", []),
            new("Ctrl+Enter", "Str_KS_SkPrint", "View", []),
            new("Ctrl+Wheel", "Str_KS_SkZoom", "View", []),
            new("Delete", "Str_KS_SkDelete", "View", []),
            new("Enter / Esc", "Str_KS_SkClose", "View", []),
        ];

        // ---- The two views, derived from the table above ----
        // These are ordinary static initializers in the SAME file as KsTable, so they are
        // guaranteed to run after it (initializer order is only unspecified ACROSS partial
        // files, not within one).

        /// <summary>(keys, string-resource key) pairs for the F1 list, in table order. A pair with
        /// empty Keys and a Label is a section header; one with both empty is map-only and is
        /// dropped here.</summary>
        private static readonly (string Keys, string Action)[] ShortcutMap =
            [.. KsTable.Where(b => b.Listed && (b.Keys.Length > 0 || b.Label.Length > 0))
                   .Select(b => (b.Keys, b.Label))];

        /// <summary>key id -> (category, caption resource key), per layer, for the drawn map.
        /// Categories map 1:1 to the KnCat* theme brushes; captions resolve through Loc() so
        /// language switches repaint.</summary>
        private static readonly Dictionary<KbLayer, Dictionary<string, (string Cat, string Label)>> KbMap =
            KsTable.SelectMany(b => b.Caps.Select(c => (c.Layer, c.Id, b.Cat, c.Cap)))
                   .GroupBy(x => x.Layer)
                   .ToDictionary(g => g.Key,
                                 g => g.ToDictionary(x => x.Id, x => (x.Cat, x.Cap)));
    }
}
