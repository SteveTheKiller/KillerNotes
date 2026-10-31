using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Serialization;
using System.Windows.Controls;
using System.Windows.Threading;
using KillerNotes.Shell;

namespace KillerNotes.Tests
{
    /// <summary>
    /// A main window that was never constructed or shown: its editor is a bare RichTextBox and its
    /// collection and timer fields exist, so the editor gestures can be driven without a window.
    /// </summary>
    internal static class EditorShell
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        public static (MainWindow Shell, RichTextBox Editor) Make()
        {
            var shell = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
            foreach (var field in typeof(MainWindow).GetFields(Members))
            {
                if (field.GetValue(shell) != null) continue;
                if (field.FieldType == typeof(DispatcherTimer)) field.SetValue(shell, new DispatcherTimer());
                else if (field.FieldType.IsGenericType && !field.FieldType.IsInterface &&
                         field.FieldType.Namespace == "System.Collections.Generic" && field.FieldType.GetConstructor(Type.EmptyTypes) != null &&
                         !field.Name.StartsWith("_recording", StringComparison.Ordinal) && !field.Name.StartsWith("_pasted", StringComparison.Ordinal))
                    field.SetValue(shell, Activator.CreateInstance(field.FieldType));
            }
            var editor = new RichTextBox();
            Set(shell, "Editor", editor);
            Set(shell, "_currentId", 1L);
            Set(shell, "StatusText", new TextBlock());
            return (shell, editor);
        }

        public static void Set(MainWindow shell, string name, object? value) =>
            (typeof(MainWindow).GetField(name, Members) ?? throw new MissingFieldException(name)).SetValue(shell, value);

        public static object? Get(MainWindow shell, string name) =>
            (typeof(MainWindow).GetField(name, Members) ?? throw new MissingFieldException(name)).GetValue(shell);

        public static object? Invoke(MainWindow shell, string name, params object?[] args)
        {
            try { return (typeof(MainWindow).GetMethod(name, Members) ?? throw new MissingMethodException(name)).Invoke(shell, args); }
            catch (TargetInvocationException ex) { ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); throw; }
        }
    }
}
