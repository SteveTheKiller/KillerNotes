// ═══════════════════════════════════════════════════════════
//  QUICK NOTE  -  capture without opening the notepad
// ═══════════════════════════════════════════════════════════
//
// 1.6.0, KN-P04. One small window (Controls/QuickNoteWindow) for a thought, saved straight to the
// open notepad: a new note in Inbox or another group, or added to today's daily note. The note
// being edited in the main window is never switched or reloaded; adding to it when it happens to
// be today's note goes through the editor, so the two cannot overwrite each other.
//
// Ways in, all ending in ShowQuickNote:
//   - the tray icon's menu (Close to system tray, or "Keep an icon in the system tray"),
//   - the taskbar jump list and KillerNotes.exe --quick-note (a running copy gets it by pipe),
//   - an optional global shortcut, off by default and limited to Win+Alt choices.
//
// A locked or read-only notepad keeps the draft in the window with the reason and, when locked,
// an Unlock button. Esc closes and keeps the draft for next time; it lives in memory only, never
// in settings, because the notepad it is headed for may be encrypted.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Shell;
using KillerNotes.Controls;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private QuickNoteWindow? _quickNote;
        private string _quickDraft = "";
        private List<(string Label, string? Group)> _quickTargets = [];   // Group null = today's note

        private const string QuickTargetSetting = "QuickNoteTarget";
        private const string KeepTrayIconSetting = "KeepTrayIcon";

        internal static bool KeepTrayIcon => App.GetSetting(KeepTrayIconSetting) == "1";

        /// <summary>Opens the quick-note window, or brings it forward with its draft.</summary>
        internal void ShowQuickNote()
        {
            if (_quickNote != null)
            {
                RefreshQuickNoteState();
                if (_quickNote.WindowState == WindowState.Minimized) _quickNote.WindowState = WindowState.Normal;
                _quickNote.Activate();
                _quickNote.FocusText();
                return;
            }
            var window = new QuickNoteWindow { Text = _quickDraft };
            window.SaveRequested += SaveQuickNote;
            window.UnlockRequested += () =>
            {
                ShowFromTray();
                _security.Unlock();
                RefreshQuickNoteState();
                _quickNote?.Activate();
            };
            window.Closed += (_, _) =>
            {
                if (_quickNote == window) _quickNote = null;
                _quickDraft = window.Text;   // Esc keeps it; a save cleared it first
            };
            _quickNote = window;
            RefreshQuickNoteState();
            window.Show();
            window.Activate();
            window.Topmost = true; window.Topmost = false;   // in front of whatever was focused
        }

        /// <summary>Rebuilds the destination list and the locked or read-only banner.</summary>
        private void RefreshQuickNoteState()
        {
            if (_quickNote == null) return;
            string inbox = Loc("Str_Grp_Inbox");
            _quickTargets = [(string.Format(Loc("Str_Quick_TargetGroup"), inbox), inbox)];
            if (NoteStore.IsOpen)
                foreach (var (path, _, _, _) in NoteStore.ListGroupTree())
                    if (!string.Equals(path, inbox, StringComparison.OrdinalIgnoreCase))
                        _quickTargets.Add((string.Format(Loc("Str_Quick_TargetGroup"), path.Replace(NoteStore.GroupSep, " / ")), path));
            _quickTargets.Add((string.Format(Loc("Str_Quick_TargetToday"), TodayTitle()), null));

            string saved = App.GetSetting(QuickTargetSetting) ?? "";
            int selected = saved == "today" ? _quickTargets.Count - 1
                : Math.Max(0, _quickTargets.FindIndex(t => t.Group != null && string.Equals("group:" + t.Group, saved, StringComparison.OrdinalIgnoreCase)));
            _quickNote.SetTargets(_quickTargets.Select(t => t.Label).ToList(), selected);

            if (_security.IsLocked) _quickNote.SetBlocked(Loc("Str_Quick_Locked"), canUnlock: true);
            else if (!NoteStore.IsOpen) _quickNote.SetBlocked(Loc("Str_Quick_NotOpen"), canUnlock: false);
            else if (NoteStore.IsReadOnly) _quickNote.SetBlocked(string.Format(Loc("Str_St_ReadOnly"), NoteStore.ReadOnlyOwner), canUnlock: false);
            else _quickNote.SetBlocked(null, canUnlock: false);
        }

        private void SaveQuickNote()
        {
            if (_quickNote is not { } window) return;
            RefreshQuickNoteState();
            if (_security.IsLocked || !NoteStore.IsOpen || NoteStore.IsReadOnly) return;   // the banner already says why
            string text = window.Text;
            if (string.IsNullOrWhiteSpace(text)) { window.SetBlocked(Loc("Str_Quick_Empty"), canUnlock: false); return; }
            if (QuickCapture.TooLarge(text)) { window.SetBlocked(Loc("Str_Quick_TooLarge"), canUnlock: false); return; }

            int index = window.TargetIndex;
            if (index < 0 || index >= _quickTargets.Count) index = 0;
            var (label, group) = _quickTargets[index];
            try
            {
                if (group != null)
                {
                    QuickCapture.CreateNote(group, text, Loc("Str_Untitled"));
                    App.SetSetting(QuickTargetSetting, "group:" + group);
                }
                else
                {
                    long id = EnsureTodayNote();   // DailyNote.cs - made if need be, never opened
                    if (id < 0) throw new InvalidOperationException(string.Format(Loc("Str_St_ReadOnly"), NoteStore.ReadOnlyOwner));
                    if (id == _currentId) AppendToOpenNote(text);
                    else QuickCapture.Append(id, text);
                    App.SetSetting(QuickTargetSetting, "today");
                }
            }
            catch (Exception ex)
            {
                window.SetBlocked(string.Format(Loc("Str_Quick_Failed"), ex.Message), canUnlock: false);
                return;
            }

            _quickDraft = "";
            window.Text = "";
            window.Close();
            RefreshList(preserveScroll: true);
            FlashStatus(string.Format(Loc("Str_St_QuickSaved"), label));
            ShowTipOnce("quick");   // SaveState.cs
        }

        /// <summary>Adds the text to the note open in the editor, through the editor, then saves.</summary>
        private void AppendToOpenNote(string text)
        {
            if (!CanEditCurrentNote()) throw new InvalidOperationException(StatusText.Text);
            if (_loadFailed) throw new InvalidOperationException(_loadError);
            var lines = text.Replace("\r\n", "\n").Trim('\n').Split('\n');
            if (CurrentIsMarkdown) Editor.Document.Blocks.Add(new Paragraph());   // the blank line between
            foreach (string line in lines) Editor.Document.Blocks.Add(new Paragraph(new Run(line)));
            MarkDirty();
            if (!SaveCurrentNote(refreshList: false)) throw new InvalidOperationException(StatusText.Text);
        }

        // ---- Start-up, jump list, tray and the global shortcut ----

        private HwndSource? _hotkeySource;
        private const int HotkeyId = 0x4B4E;   // "KN"
        private const int WM_HOTKEY = 0x0312;

        private void InitQuickNote()
        {
            ApplyJumpList();
            Closed += (_, _) =>
            {
                if (_hotkeySource != null)
                {
                    UnregisterHotKey(_hotkeySource.Handle, HotkeyId);
                    _hotkeySource.Dispose();
                }
                _quickNote?.Close();
            };
            string? saved = App.GetSetting(QuickNoteHotkey.Setting);
            string? taken = RegisterQuickHotkey(saved);
            // Taken since last time (another app started first): the choice stays, the card says so.
            InitQuickNoteOptions(taken == null ? null : string.Format(taken, saved));
            _tray.RefreshIcon();
        }

        /// <summary>The taskbar's right-click Quick note task. Rebuilt on a language switch.</summary>
        private void ApplyJumpList()
        {
            try
            {
                string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (exe.Length == 0 || DemoMode) return;
                var list = new JumpList { ShowFrequentCategory = false, ShowRecentCategory = false };
                list.JumpItems.Add(new JumpTask
                {
                    Title = Loc("Str_Quick_Title"),
                    Description = Loc("Str_Quick_JumpHint"),
                    ApplicationPath = exe,
                    Arguments = "--quick-note",
                    IconResourcePath = exe,
                });
                JumpList.SetJumpList(Application.Current, list);
                list.Apply();
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException) { /* the taskbar is optional */ }
        }

        /// <summary>Registers the Win+Alt+N shortcut. Returns the "already taken" message
        /// when another app holds it, null otherwise (including off).</summary>
        private string? RegisterQuickHotkey(string? choice)
        {
            if (_hotkeySource != null) UnregisterHotKey(_hotkeySource.Handle, HotkeyId);
            if (!QuickNoteHotkey.TryParse(choice, out uint modifiers, out uint key)) return null;
            if (_hotkeySource == null)
            {
                // Its own hidden window, so the shortcut works while the main one is in the tray.
                _hotkeySource = new HwndSource(new HwndSourceParameters("KillerNotesQuickNote") { WindowStyle = unchecked((int)0x80000000), Width = 0, Height = 0 });
                _hotkeySource.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId) { ShowQuickNote(); handled = true; }
                    return IntPtr.Zero;
                });
            }
            return RegisterHotKey(_hotkeySource.Handle, HotkeyId, modifiers, key) ? null : Loc("Str_About_HotkeyTaken");
        }

        // ---- About card options ----

        private bool _initializingQuickOptions;

        private void InitQuickNoteOptions(string? hotkeyNote = null)
        {
            _initializingQuickOptions = true;
            AboutKeepTrayIcon.IsChecked = KeepTrayIcon;
            AboutShowTips.IsChecked = App.GetSetting(ShowTipsSetting) != "0";   // SaveState.cs
            AboutQuickHotkey.IsChecked = QuickNoteHotkey.TryParse(App.GetSetting(QuickNoteHotkey.Setting), out _, out _);
            if (hotkeyNote != null) SetHotkeyNote(hotkeyNote);
            _initializingQuickOptions = false;
        }

        private void SetHotkeyNote(string? note)
        {
            AboutQuickHotkeyNote.Text = note ?? "";
            AboutQuickHotkeyNote.Visibility = note == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void AboutKeepTrayIcon_Click(object sender, RoutedEventArgs e)
        {
            App.SetSetting(KeepTrayIconSetting, AboutKeepTrayIcon.IsChecked == true ? "1" : "0");
            _tray.RefreshIcon();
        }

        private void AboutQuickHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (_initializingQuickOptions) return;
            string choice = AboutQuickHotkey.IsChecked == true ? QuickNoteHotkey.Shortcut : "";
            string? taken = RegisterQuickHotkey(choice);
            if (taken != null)
            {
                // Taken by another app: say so and untick rather than leave a dead shortcut.
                RegisterQuickHotkey(null);
                App.SetSetting(QuickNoteHotkey.Setting, "");
                AboutQuickHotkey.IsChecked = false;
                SetHotkeyNote(string.Format(taken, choice));
                return;
            }
            App.SetSetting(QuickNoteHotkey.Setting, choice);
            SetHotkeyNote(null);
        }

        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    }
}
