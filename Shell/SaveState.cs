// ═══════════════════════════════════════════════════════════
//  SAVE AND PRIVACY STATE, TIPS  -  what the footer always says
// ═══════════════════════════════════════════════════════════
//
// 1.6.0, KN-P09. A quiet corner of the footer answers the two questions a new user has without
// asking: is my writing saved ("Saved", "Unsaved changes", "Save failed", the time in the
// tooltip), and is this notepad protected (a padlock, closed for a password, open with "anyone
// with the file can read it"). It sits beside the backup warning (Backup.cs) and adds nothing to
// the editing area.
//
// Tips: the first time Lock now, a backup, an export, an import or a quick note happens, the
// status line adds one sentence on what it means. Each is shown once; About turns them off.

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private enum SaveIndicator { Hidden, Saved, Unsaved, Failed }

        private SaveIndicator _saveIndicator = SaveIndicator.Hidden;

        /// <summary>Updates the footer's save state. Cheap when nothing changes, since it runs on
        /// every keystroke through MarkDirty.</summary>
        private void SetSaveState(SaveIndicator state, DateTime? when = null)
        {
            if (SaveStateText == null) return;
            if (state == _saveIndicator && state != SaveIndicator.Saved) return;
            _saveIndicator = state;
            SaveStateText.Visibility = state == SaveIndicator.Hidden ? Visibility.Collapsed : Visibility.Visible;
            (string text, string tip) = state switch
            {
                SaveIndicator.Saved => (Loc("Str_Save_Saved"),
                    string.Format(Loc("Str_Save_SavedAt"), (when ?? DateTime.Now).ToString("g", CultureInfo.CurrentCulture))),
                SaveIndicator.Unsaved => (Loc("Str_Save_Unsaved"), Loc("Str_Save_UnsavedHint")),
                SaveIndicator.Failed => (Loc("Str_Save_Failed"), Loc("Str_Save_FailedHint")),
                _ => ("", ""),
            };
            SaveStateText.Text = text;
            SaveStateText.ToolTip = tip;
            AutomationProperties.SetHelpText(SaveStateText, tip);
        }

        /// <summary>The footer padlock: closed with a password, open without, hidden with no notepad.</summary>
        private void ShowPrivacyState()
        {
            if (PadlockGlyph == null) return;
            if (!NoteStore.IsOpen || _security.IsLocked)
            {
                PadlockGlyph.Visibility = Visibility.Collapsed;
                return;
            }
            bool encrypted = NoteStore.HasPassword;
            PadlockGlyph.Text = ((char)(encrypted ? 0xE72E : 0xE785)).ToString();
            string tip = Loc(encrypted ? "Str_Privacy_Encrypted" : "Str_Privacy_Plain");
            PadlockGlyph.ToolTip = tip;
            AutomationProperties.SetName(PadlockGlyph, tip);
            PadlockGlyph.Visibility = Visibility.Visible;
        }

        // ---- Tips ----

        private const string ShowTipsSetting = "ShowTips";
        private const string TipsSeenSetting = "TipsSeen";

        /// <summary>Adds the tip for this feature to the status line, the first time only.</summary>
        private void ShowTipOnce(string key)
        {
            var tips = new TipState(App.GetSetting(ShowTipsSetting) != "0", App.GetSetting(TipsSeenSetting) ?? "");
            if (DemoMode || !tips.TakeTip(key)) return;
            App.SetSetting(TipsSeenSetting, tips.Seen);
            _statusTimer.Stop();   // a tip stays until something else is said
            string now = StatusText.Text.Trim();
            StatusText.Text = (now.Length > 0 ? now + "  " : "") + Loc("Str_Tip_" + key);
        }

        private void AboutShowTips_Click(object sender, RoutedEventArgs e) =>
            App.SetSetting(ShowTipsSetting, AboutShowTips.IsChecked == true ? "1" : "0");
    }
}
