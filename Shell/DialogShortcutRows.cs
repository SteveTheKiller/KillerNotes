using System.Collections.Generic;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private static IEnumerable<KsBinding> DialogShortcutRows()
        {
            yield return new("", "Str_Db_Title", "File", []);
            yield return new("Enter / Ctrl+O", "Str_Btn_Open", "File", []);
            yield return new("Ctrl+N", "Str_TT_NewDb", "File", []);
            yield return new("Ctrl+D", "Str_TT_DeleteDb", "File", []);
            yield return new("F2", "Str_Ctx_Rename", "File", []);
            yield return new("Alt+C", "Str_Ctx_CopyFile", "File", []);
            yield return new("Ctrl+E", "Str_Ctx_ExportKndb", "File", []);
            yield return new("Ctrl+R", "Str_Ctx_Reveal", "File", []);
            yield return new("Alt+E", "Str_TT_Explorer", "File", []);
            yield return new("Alt+F", "Str_TT_DataFolder", "File", []);
            yield return new("Ctrl+B", "Str_TT_Backups", "File", []);
            yield return new("Esc", "Str_Sys_Close", "File", []);

            yield return new("", "Str_Bk_Title", "File", []);
            yield return new("Ctrl+O", "Str_Btn_Browse", "File", []);
            yield return new("Ctrl+B", "Str_Btn_BackupNow", "File", []);
            yield return new("Ctrl+R", "Str_Btn_RestoreBackup", "File", []);
            yield return new("Alt+E", "Str_Bk_Enable", "File", []);
            yield return new("Alt+I", "Str_Bk_Interval", "File", []);
            yield return new("Alt+K", "Str_Bk_Keep", "File", []);
            yield return new("Enter / Esc", "Str_Btn_Done", "File", []);

            yield return new("", "Str_Hist_Title", "Note", []);
            yield return new("Enter / Ctrl+R", "Str_Btn_RestoreVersion", "Note", []);
            yield return new("Esc", "Str_Sys_Close", "Note", []);

            yield return new("", "Str_Lbl_Confirm", "Help", []);
            yield return new("Enter", "Str_Btn_OK", "Help", []);
            yield return new("Alt+C", "Str_KS_DialogOption", "Help", []);
            yield return new("Esc", "Str_Btn_Cancel", "Help", []);

            yield return new("", "Str_TT_Lock", "File", []);
            yield return new("Enter", "Str_Btn_OK", "File", []);
            yield return new("Esc", "Str_Btn_Cancel", "File", []);
            yield return new("Ctrl+N", "Str_Pw_NewDbBtn", "File", []);
            yield return new("Ctrl+O", "Str_Btn_OpenOther", "File", []);

            yield return new("", "Str_Tags_Title", "Note", []);
            yield return new("Enter / Ctrl+N", "Str_Btn_TagAdd", "Note", []);
            yield return new("F2", "Str_TT_TagRename", "Note", []);
            yield return new("Alt+C", "Str_TT_TagRecolor", "Note", []);
            yield return new("Ctrl+D", "Str_TT_TagDelete", "Note", []);
            yield return new("Alt+N", "Str_TT_TagPickColor", "Note", []);
            yield return new("Esc", "Str_Btn_Done", "Note", []);

            yield return new("", "Str_Whisper_Title", "View", []);
            yield return new("Enter", "Str_KS_WhisperAction", "View", []);
            yield return new("Esc", "Str_Btn_Cancel", "View", []);
            yield return new("Alt+1", "Str_Whisper_Tiny", "View", []);
            yield return new("Alt+2", "Str_Whisper_Base", "View", []);
            yield return new("Alt+3", "Str_Whisper_Small", "View", []);

            yield return new("", "Str_Dict_Title", "View", []);
            yield return new("Alt+R", "Str_Dict_Record", "View", []);
            yield return new("Alt+P", "Str_Dict_Play", "View", []);
            yield return new("Alt+T", "Str_Dict_Transcribe", "View", []);
            yield return new("Ctrl+Enter", "Str_Dict_Print", "View", []);
            yield return new("Alt+E", "Str_Dict_Embed", "View", []);
            yield return new("Alt+S", "Str_Dict_Slice", "View", []);
            yield return new("Alt+C", "Str_Dict_CopySeg", "View", []);
            yield return new("Alt+D", "Str_Dict_DeleteSeg", "View", []);
            yield return new("Alt+V", "Str_Dict_PasteSeg", "View", []);
            yield return new("Alt+L", "Str_Dict_ClearCuts", "View", []);
            yield return new("Alt+Z", "Str_Dict_UndoEdit", "View", []);
            yield return new("Alt+Left / Alt+Right", "Str_KS_DictationSeek", "View", []);
            yield return new("Esc", "Str_Sys_Close", "View", []);

            yield return new("", "Str_KS_SecGraph", "View", []);
            yield return new("Y", "Str_Ctx_GraphColor", "View", []);
            yield return new("H", "Str_Ctx_GraphGhosts", "View", []);
            yield return new("S", "Str_Ctx_GraphSaveArrange", "View", []);
            yield return new("Alt+A", "Str_Ctx_GraphArrange", "View", []);
            yield return new("Alt+F", "Str_Ctx_GraphForget", "View", []);
            yield return new("Alt+S", "Str_Ctx_GraphShape", "View", []);
            yield return new("Alt+1", "Str_Ctx_GraphShapeFree", "View", []);
            yield return new("Alt+2", "Str_Ctx_GraphArrangeCircle", "View", []);
            yield return new("Alt+3", "Str_Ctx_GraphArrangeGrid", "View", []);
            yield return new("Alt+4", "Str_Ctx_GraphArrangeGroups", "View", []);
            yield return new("Alt+5", "Str_Ctx_GraphShapeSpiral", "View", []);
            yield return new("Alt+6", "Str_Ctx_GraphShapeWave", "View", []);
            yield return new("Alt+L", "Str_Graph_Legend", "View", []);

            yield return new("", "Str_KS_SecSketch", "View", []);
            yield return new("F2", "Str_Sketch_Width", "View", []);
            yield return new("F3", "Str_Sketch_Opacity", "View", []);
            yield return new("F4", "Str_Sketch_MoreColors", "View", []);
            yield return new("F5", "Str_Sketch_Zoom", "View", []);
            yield return new("F6", "Str_Sketch_Fill", "View", []);
            yield return new("F8", "Str_Sketch_Clear", "View", []);
            yield return new("F10", "Str_Sketch_EditText", "View", []);
            yield return new("Ctrl+C", "Str_Sketch_CopyImage", "View", []);
            yield return new("Ctrl+D", "Str_Sketch_Duplicate", "View", []);
            yield return new("Ctrl+Shift+Up", "Str_Sketch_Front", "View", []);
            yield return new("Ctrl+Shift+Down", "Str_Sketch_Back", "View", []);
            yield return new("Ctrl+Shift+A", "Str_Sketch_Arc", "View", []);
            yield return new("Ctrl+Shift+L", "Str_Sketch_Straighten", "View", []);
            yield return new("Ctrl+Shift+O", "Str_Sketch_ResetOpacity", "View", []);
            yield return new("Ctrl+B", "Str_Sketch_Bold", "View", []);
            yield return new("Alt+Up / Alt+Down", "Str_Btn_More", "View", []);
            yield return new("Enter / Space", "Str_KS_SketchSwatch", "View", []);
        }
    }
}
