using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private sealed record ActionShortcut(string Gesture, Key Key, ModifierKeys Modifiers,
            string Label, string Category, Action<MainWindow> Run, string? Surface = null);

        private static ActionShortcut[]? _actionShortcuts;
        private static ActionShortcut[] ActionShortcuts() => _actionShortcuts ??=
        [
            new("Alt+S", Key.S, ModifierKeys.Alt, "Str_Ctx_ShareNote", "File", w => w.RunNoteAction(w.ShareNote_Click)),
            new("Alt+E", Key.E, ModifierKeys.Alt, "Str_Ctx_ExportVault", "File", w => w.ExportVault_Click(w, new RoutedEventArgs())),
            new("Alt+R", Key.R, ModifierKeys.Alt, "Str_Ctx_TitleColorReset", "Note", w => w.RunNoteAction(w.TitleColorReset_Click)),
            new("Alt+V", Key.V, ModifierKeys.Alt, "Str_Ctx_PreviewGlobal", "View", w => w.PreviewDetectGlobal_Click(w, new RoutedEventArgs())),
            new("Alt+F", Key.F, ModifierKeys.Alt, "Str_KS_ConvertFormat", "Note", w => w.RunNoteAction(w.ConvertFormat_Click)),
            new("Alt+N", Key.N, ModifierKeys.Alt, "Str_Ctx_NewMarkdownNote", "Note", w => w.NewMarkdownNote_Click(w, new RoutedEventArgs())),
            new("Alt+G", Key.G, ModifierKeys.Alt, "Str_Ctx_Group", "Note", w => w.OpenNoteSubmenu(w.GroupMenu)),
            new("Alt+K", Key.K, ModifierKeys.Alt, "Str_Ctx_Tags", "Note", w => w.OpenNoteSubmenu(w.TagsMenu)),
            new("Alt+A", Key.A, ModifierKeys.Alt, "Str_TT_Databases", "File", w => w.ManageDatabases_Click(w, new RoutedEventArgs()), "DatabasesBtn"),
            new("Alt+U", Key.U, ModifierKeys.Alt, "Str_TT_Lock", "File", w => w.LockButton_Click(w, new RoutedEventArgs()), "LockButton"),
            new("Alt+J", Key.J, ModifierKeys.Alt, "Str_TT_Language", "View", w => w.LangButton_Click(w.LangButton, new RoutedEventArgs()), "LangButton"),
            new("Alt+I", Key.I, ModifierKeys.Alt, "Str_TT_InsertImage", "Format", w => w.InsertImageBtn_Click(w.FmtImageBtn, new RoutedEventArgs()), "FmtImageBtn"),
            new("Alt+Q", Key.Q, ModifierKeys.Alt, "Str_Whisper_Menu", "View", w => w.SpeechModel_Click(w, new RoutedEventArgs())),
            new("Alt+W", Key.W, ModifierKeys.Alt, "Str_TT_TextColor", "Format", w => w.TextColorBtn_Click(w.TextColorBtn, new RoutedEventArgs()), "TextColorBtn"),
            new("Alt+Y", Key.Y, ModifierKeys.Alt, "Str_TT_FontSize", "Format", w => w.FontSizeBtn_Click(w.FontSizeBtn, new RoutedEventArgs()), "FontSizeBtn"),
            new("Alt+X", Key.X, ModifierKeys.Alt, "Str_Fonts_Open", "View", w => w.OpenFontsDialog()),
            new("Alt+Z", Key.Z, ModifierKeys.Alt, "Str_TT_CollapseMentions", "View", w => w.BacklinkCollapse_Click(w.BacklinkCollapseBtn, new RoutedEventArgs()), "BacklinkCollapseBtn"),
            new("Ctrl+F1", Key.F1, ModifierKeys.Control, "Str_Btn_Update", "Help", w => w.AboutUpdateButton_Click(w, new RoutedEventArgs()), "AboutUpdateButton"),
            new("Ctrl+F2", Key.F2, ModifierKeys.Control, "Str_Ctx_RenameGroup", "Note", w => w.RunGroupAction(w.RenameGroup_Click)),
            new("Ctrl+F3", Key.F3, ModifierKeys.Control, "Str_Ctx_SubTop", "Note", w => w.RunGroupAction(w.SubgroupsOnTop_Click)),
            new("Ctrl+F4", Key.F4, ModifierKeys.Control, "Str_Ctx_TemplatesGroup", "Note", w => w.RunGroupAction(w.TemplatesGroup_Click)),
            new("Ctrl+F5", Key.F5, ModifierKeys.Control, "Str_Ctx_DailyGroup", "Note", w => w.RunGroupAction(w.DailyGroup_Click)),
            new("Ctrl+F6", Key.F6, ModifierKeys.Control, "Str_Ctx_GroupColorReset", "Note", w => w.RunGroupAction(w.GroupColorReset_Click)),
            new("Ctrl+F7", Key.F7, ModifierKeys.Control, "Str_Ctx_DeleteGroup", "Note", w => w.RunGroupAction(w.DeleteGroup_Click)),
            new("Ctrl+F8", Key.F8, ModifierKeys.Control, "Str_Ctx_Restore", "Note", w => w.RestoreNote_Click(w, new RoutedEventArgs())),
            new("Ctrl+F9", Key.F9, ModifierKeys.Control, "Str_Ctx_DeleteForever", "Note", w => w.DeleteForever_Click(w, new RoutedEventArgs())),
            new("Ctrl+F11", Key.F11, ModifierKeys.Control, "Str_Ctx_EmptyTrash", "Note", w => w.EmptyTrash_Click(w, new RoutedEventArgs())),
            new("Ctrl+F12", Key.F12, ModifierKeys.Control, "Str_Fonts_Reset", "View", w => w.FontsReset_Click(w, new RoutedEventArgs()), "FontsResetBtn"),
            new("Ctrl+Shift+F1", Key.F1, ModifierKeys.Control | ModifierKeys.Shift, "Str_KS_ViewList", "Help", w => w.ApplyShortcutView(false, true), "KsViewListBtn"),
            new("Ctrl+Shift+F2", Key.F2, ModifierKeys.Control | ModifierKeys.Shift, "Str_KS_ViewKeyboard", "Help", w => w.ApplyShortcutView(true, true), "KsViewKeyboardBtn"),
            new("Ctrl+Shift+F3", Key.F3, ModifierKeys.Control | ModifierKeys.Shift, "Str_TT_FindCase", "Search", w => w.FindOption_Click(w.FindCaseBtn, new RoutedEventArgs()), "FindCaseBtn"),
            new("Ctrl+Shift+F4", Key.F4, ModifierKeys.Control | ModifierKeys.Shift, "Str_TT_FindWord", "Search", w => w.FindOption_Click(w.FindWordBtn, new RoutedEventArgs()), "FindWordBtn"),
            new("Ctrl+Shift+F5", Key.F5, ModifierKeys.Control | ModifierKeys.Shift, "Str_TT_FindRegex", "Search", w => w.FindOption_Click(w.FindRegexBtn, new RoutedEventArgs()), "FindRegexBtn"),
            new("Ctrl+Shift+F6", Key.F6, ModifierKeys.Control | ModifierKeys.Shift, "Str_Preview_Source", "View", w => w.PreviewSource_Click(w, new RoutedEventArgs()), "PreviewSourceMenuItem"),
            new("Ctrl+Shift+F7", Key.F7, ModifierKeys.Control | ModifierKeys.Shift, "Str_Preview_Rendered", "View", w => w.PreviewRendered_Click(w, new RoutedEventArgs()), "PreviewRenderedMenuItem"),
            new("Ctrl+Shift+F8", Key.F8, ModifierKeys.Control | ModifierKeys.Shift, "Str_Preview_Split", "View", w => w.PreviewSplit_Click(w, new RoutedEventArgs()), "PreviewSplitMenuItem"),
            new("Ctrl+Shift+F9", Key.F9, ModifierKeys.Control | ModifierKeys.Shift, "Str_Dict_EditRec", "Note", w => w.RunRecordingAction(w.RecEdit_Click), "RecEditMenuItem"),
            new("Ctrl+Shift+F10", Key.F10, ModifierKeys.Control | ModifierKeys.Shift, "Str_Dict_SaveAs", "File", w => w.RunRecordingAction(w.RecSaveAs_Click), "RecSaveAsMenuItem"),
            new("Ctrl+Shift+F11", Key.F11, ModifierKeys.Control | ModifierKeys.Shift, "Str_Dict_CopyFile", "File", w => w.RunRecordingAction(w.RecCopyFile_Click), "RecCopyFileMenuItem"),
            new("Ctrl+Shift+F12", Key.F12, ModifierKeys.Control | ModifierKeys.Shift, "Str_Btn_Install", "File", w => w.Install_Click(w, new RoutedEventArgs()), "InstallBtn"),
            new("Alt+4", Key.D4, ModifierKeys.Alt, "Str_TT_SortTimeOff", "View", w => w.SortTimeBtn_Click(w, new RoutedEventArgs()), "SortTimeBtn"),
            new("Alt+5", Key.D5, ModifierKeys.Alt, "Str_TT_SortAlphaOff", "View", w => w.SortAlphaBtn_Click(w, new RoutedEventArgs()), "SortAlphaBtn"),
            new("Alt+6", Key.D6, ModifierKeys.Alt, "Str_TT_SortCustom", "View", w => w.SortCustomBtn_Click(w, new RoutedEventArgs()), "SortCustomBtn"),
            new("Alt+8", Key.D8, ModifierKeys.Alt, "Str_TT_SidebarReplace", "Search", w => w.SidebarReplace_Click(w, new RoutedEventArgs()), "SidebarReplaceBtn"),
            new("Alt+9", Key.D9, ModifierKeys.Alt, "Str_TT_SidebarReplaceAll", "Search", w => w.SidebarReplaceAll_Click(w, new RoutedEventArgs())),
            new("Alt+7", Key.D7, ModifierKeys.Alt, "Str_Ctx_ReverseOrder", "View", w => w.SortMenuItem_Click(new MenuItem { Tag = "reverse" }, new RoutedEventArgs())),
        ];

        private static IEnumerable<KsBinding> ActionShortcutRows() =>
            ActionShortcuts().Select(b => new KsBinding(b.Gesture, b.Label, b.Category,
                [(b.Modifiers == ModifierKeys.Alt ? KbLayer.Alt :
                  b.Modifiers.HasFlag(ModifierKeys.Shift) ? KbLayer.CtrlShift : KbLayer.Ctrl,
                  b.Key.ToString(), b.Label)]));

        private bool HandleActionShortcut(KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            var binding = ActionShortcuts().FirstOrDefault(b => b.Key == key && b.Modifiers == Keyboard.Modifiers);
            if (binding == null) return false;
            if (binding.Surface != null && FindName(binding.Surface) is FrameworkElement surface &&
                surface is not MenuItem && (!surface.IsEnabled || surface.Visibility != Visibility.Visible)) return false;
            if (!e.IsRepeat) binding.Run(this);
            e.Handled = true;
            return true;
        }

        private void InitializeActionShortcutSurfaces()
        {
            DescribeExistingShortcutSurfaces();
            DescribeSurface(AboutVersionBlock, "Str_KS_ReleaseNotes", "Shift+F12");
            foreach (var binding in ActionShortcuts())
            {
                if (binding.Surface == null || FindName(binding.Surface) is not FrameworkElement surface) continue;
                System.Windows.Automation.AutomationProperties.SetAcceleratorKey(surface, binding.Gesture);
                if (surface is MenuItem menu) menu.InputGestureText = binding.Gesture;
                var tip = new StackPanel();
                var text = new TextBlock();
                text.SetResourceReference(TextBlock.TextProperty, binding.Label);
                tip.Children.Add(text);
                tip.Children.Add(new TextBlock { Text = binding.Gesture });
                surface.ToolTip = tip;
            }
        }

        private void DescribeExistingShortcutSurfaces()
        {
            (string Surface, string Label)[] aliases =
            [
                ("NewNoteBtn", "Str_KS_NewNote"), ("DensityBtn", "Str_KS_Density"),
                ("GraphRailBtn", "Str_KS_Graph"), ("ThemeButton", "Str_KS_Theme"),
                ("SidebarToggleBtn", "Str_KS_Sidebar"), ("DictationRailBtn", "Str_KS_Dictation"),
                ("FindRailBtn", "Str_KS_Find"), ("LineNumBtn", "Str_KS_LineNumbers"),
                ("OutlineRailBtn", "Str_KS_Outline"), ("PreviewModeBtn", "Str_KS_Preview"),
                ("SketchRailBtn", "Str_KS_SketchPad"), ("KalcRailBtn", "Str_KS_Calc"),
                ("FmtStrikeBtn", "Str_KS_Strike"), ("FmtMonoBtn", "Str_KS_Mono"),
                ("FmtCheckBtn", "Str_KS_Checkbox"), ("FmtHeadingBtn", "Str_KS_Headings"),
                ("FmtRuleBtn", "Str_KS_Rule"), ("CloseBtn", "Str_Sys_Close"),
                ("MaximizeBtn", "Str_Sys_Maximize"), ("MinimizeBtn", "Str_Sys_Minimize")
            ];
            foreach (var (name, label) in aliases)
            {
                var binding = KsTable.Last(b => b.Label == label && b.Keys.Length > 0);
                if (FindName(name) is FrameworkElement surface)
                    DescribeSurface(surface, label, binding.Keys);
            }
        }

        private static void DescribeSurface(FrameworkElement surface, string label, string gesture)
        {
            System.Windows.Automation.AutomationProperties.SetAcceleratorKey(surface, gesture);
            var tip = new StackPanel();
            var text = new TextBlock();
            text.SetResourceReference(TextBlock.TextProperty, label);
            tip.Children.Add(text);
            tip.Children.Add(new TextBlock { Text = gesture });
            surface.ToolTip = tip;
        }

        private static void DescribeMenuShortcuts(ItemsControl owner)
        {
            foreach (var item in owner.Items.OfType<MenuItem>())
            {
                if (item.InputGestureText.Length > 0)
                {
                    System.Windows.Automation.AutomationProperties.SetAcceleratorKey(item, item.InputGestureText);
                    if (item.ToolTip is string text && !text.Contains(item.InputGestureText))
                        item.ToolTip = text + " (" + item.InputGestureText + ")";
                }
                DescribeMenuShortcuts(item);
            }
        }

        private void SidebarHeader_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.None || e.Key is not (Key.Space or Key.Enter)) return;
            if (sender is FrameworkElement { DataContext: GroupHeader group })
            {
                if (!NoteStore.IsOpen || NoteStore.IsReadOnly) return;
                NoteStore.SetGroupCollapsed(group.Path, !group.Collapsed);
            }
            else if (sender is FrameworkElement { DataContext: TrashHeader })
            {
                bool collapsed = App.GetSetting(TrashCollapsedSetting) != "0";
                App.SetSetting(TrashCollapsedSetting, collapsed ? "0" : "1");
            }
            else return;
            RefreshList(preserveScroll: true);
            e.Handled = true;
        }

        private void TagChip_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.None || e.Key is not (Key.Space or Key.Enter)) return;
            if (sender is not FrameworkElement { DataContext: TagChip chip }) return;
            SearchBox.Text = chip.Name;
            e.Handled = true;
        }

        private void RunNoteAction(RoutedEventHandler action)
        {
            if (!NoteStore.IsOpen) return;
            if (NotesList.SelectedItems.OfType<Note>().Any())
            {
                action(this, new RoutedEventArgs());
                return;
            }
            var note = _notes.FirstOrDefault(n => n.Id == _currentId);
            if (note != null)
            {
                NotesList.SelectedItem = note;
                action(this, new RoutedEventArgs());
            }
        }

        private void RunRecordingAction(RoutedEventHandler action)
        {
            _ctxObject = null;
            action(this, new RoutedEventArgs());
        }

        private void RunGroupAction(RoutedEventHandler action)
        {
            if (!NoteStore.IsOpen) return;
            if (NoteStore.IsReadOnly && action.Method.Name is nameof(RenameGroup_Click) or nameof(DeleteGroup_Click) or nameof(GroupColorReset_Click)) return;
            var group = ResolveKeyboardGroup();
            if (group == null && Keyboard.FocusedElement is MenuItem) group = _ctxGroup;
            if (group == null) { FlashStatus(Loc("Str_St_PickGroupFirst")); return; }
            _ctxGroup = group;
            action(this, new RoutedEventArgs());
        }

        private void OpenNoteSubmenu(MenuItem submenu)
        {
            if (!NoteStore.IsOpen) return;
            if (!NotesList.SelectedItems.OfType<Note>().Any()) return;
            _noteContextTarget = true;
            var menu = NotesList.ContextMenu;
            menu.PlacementTarget = NotesList;
            menu.IsOpen = true;
            Dispatcher.BeginInvoke(new Action(() => { submenu.Focus(); submenu.IsSubmenuOpen = true; }));
        }
    }
}
