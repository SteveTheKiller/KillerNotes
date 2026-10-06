using System.Collections.Generic;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private static IEnumerable<KsBinding> PickerShortcutRows()
        {
            yield return new("", "Str_Dlg_PickColor", "Format", []);
            yield return new("Alt+E", "Str_TT_Eyedropper", "Format", []);
            yield return new("Arrows, Enter / Esc (eyedropper)", "Str_TT_Eyedropper", "Format", []);
            yield return new("Alt+R", "Str_TT_ReplaceSwatch", "Format", []);
            yield return new("Alt+D", "Str_TT_ResetSwatches", "Format", []);
            yield return new("Alt+1 through Alt+9", "Str_TT_SwatchUse", "Format", []);
            yield return new("Alt+S then arrows (saturation/value)", "Str_Dlg_PickColor", "Format", []);
            yield return new("Alt+H then Up/Down (hue)", "Str_Dlg_PickColor", "Format", []);
            yield return new("Shift+arrows (larger steps)", "Str_Dlg_PickColor", "Format", []);
            yield return new("Enter / Esc", "Str_Btn_OK", "Format", []);
            yield return new("", "Str_KS_Picker", "File", []);
            yield return new("Ctrl+L / Alt+D", "Str_KS_PickerLocation", "File", []);
            yield return new("Alt+Up", "Str_TT_Up", "File", []);
            yield return new("Alt+Left / Alt+Right", "Str_KS_PickerHistory", "File", []);
            yield return new("F4 / Alt+Down", "Str_TT_RecentLocations", "File", []);
            yield return new("Alt+H", "Str_TT_ShowHidden", "File", []);
            yield return new("Alt+P", "Str_KS_PickerPreview", "File", []);
            yield return new("Ctrl+Shift+1 through Ctrl+Shift+4", "Str_TT_ViewIcons", "File", []);
            yield return new("Ctrl+Shift+5", "Str_TT_ViewList", "File", []);
            yield return new("Ctrl+Shift+6", "Str_TT_ViewDetails", "File", []);
            yield return new("Alt+1", "Str_Col_Name", "File", []);
            yield return new("Alt+2", "Str_Col_Size", "File", []);
            yield return new("Alt+3", "Str_Col_Modified", "File", []);
            yield return new("F5", "Str_KS_PickerRefresh", "File", []);
            yield return new("Alt+B", "Str_Menu_PinPlace", "File", []);
            yield return new("Alt+Shift+B", "Str_Menu_UnpinPlace", "File", []);
            yield return new("Alt+Q, Ctrl+Shift+Up/Down", "Str_Menu_PinPlace", "File", []);
            yield return new("Enter / Esc", "Str_Btn_Open", "File", []);
        }
    }
}
