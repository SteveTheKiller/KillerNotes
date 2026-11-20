using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KillerNotes
{
    /// <summary>
    /// The non-generic TaskCompletionSource of later .NET versions, for kit code that awaits a plain completion.
    /// net48 only has the generic one, so this wraps TaskCompletionSource&lt;bool&gt;.
    /// </summary>
    internal sealed class TaskCompletionSource
    {
        private readonly TaskCompletionSource<bool> _inner = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Task => _inner.Task;
        public void SetResult() => _inner.SetResult(true);
        public bool TrySetResult() => _inner.TrySetResult(true);
        public void SetException(Exception error) => _inner.SetException(error);
        public bool TrySetException(Exception error) => _inner.TrySetException(error);
        public void SetCanceled() => _inner.SetCanceled();
        public bool TrySetCanceled() => _inner.TrySetCanceled();
    }
}

// In the global namespace so every kit namespace (KillerNotes.*, KillerFamily.*) sees these extensions.
/// <summary>
/// KillerNotes builds on net48, and the KillerUI kit files it copies use a few APIs that only exist on later .NET.
/// These app-owned members give the copied files the same calls with the same behavior, so the kit files stay
/// byte-identical to KillerUI instead of carrying net48 edits.
/// </summary>
internal static class Net48KitShims
{
    extension(Math)
    {
        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        public static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;
    }

    extension(Environment)
    {
        /// <summary>The running executable's full path, as Environment.ProcessPath reports it on later .NET.</summary>
        public static string? ProcessPath
        {
            get
            {
                try { using var self = Process.GetCurrentProcess(); return self.MainModule?.FileName; }
                catch (Exception) { return null; }
            }
        }
    }

    extension(File)
    {
        /// <summary>File.Move with the overwrite flag: false throws IOException when the target exists, as File.Move does.</summary>
        public static void Move(string sourceFileName, string destFileName, bool overwrite)
        {
            if (overwrite && File.Exists(destFileName)) File.Delete(destFileName);
            File.Move(sourceFileName, destFileName);
        }
    }

    extension(string)
    {
        /// <summary>Formats an interpolated string with the given provider, as string.Create(provider, $"...") does.</summary>
        public static string Create(IFormatProvider? provider, FormattableString text) => text.ToString(provider);
    }

    public static string[] Split(this string text, char separator, StringSplitOptions options) =>
        text.Split(new[] { separator }, options);

    public static bool EndsWith(this string text, char value) => text.Length > 0 && text[text.Length - 1] == value;

    public static bool StartsWith(this string text, char value) => text.Length > 0 && text[0] == value;

    public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
    {
        key = pair.Key;
        value = pair.Value;
    }

    /// <summary>
    /// Empties the table. net48's ConditionalWeakTable has no public Clear, so this calls its internal one when it
    /// is there. Without it nothing is lost: every value dies with its weak key once the key is released.
    /// </summary>
    public static void Clear<TKey, TValue>(this ConditionalWeakTable<TKey, TValue> table)
        where TKey : class where TValue : class
    {
        try
        {
            typeof(ConditionalWeakTable<TKey, TValue>)
                .GetMethod("Clear", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null)
                ?.Invoke(table, null);
        }
        catch (Exception) { }
    }

    /// <summary>Completes when the process exits, as Process.WaitForExitAsync does.</summary>
    public static Task WaitForExitAsync(this Process process, CancellationToken cancellationToken = default)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => done.TrySetResult(true);
        if (process.HasExited) done.TrySetResult(true);
        if (cancellationToken.CanBeCanceled) cancellationToken.Register(() => done.TrySetCanceled());
        return done.Task;
    }

    extension(ProcessStartInfo start)
    {
        /// <summary>
        /// The argument list of later .NET: every added argument is quoted the way the C runtime parses a command
        /// line and appended to Arguments, so the child receives exactly the strings that were added.
        /// </summary>
        public ICollection<string> ArgumentList => new ArgumentWriter(start);
    }

    private sealed class ArgumentWriter(ProcessStartInfo start) : ICollection<string>
    {
        private readonly List<string> _added = new();
        public int Count => _added.Count;
        public bool IsReadOnly => false;

        public void Add(string item)
        {
            _added.Add(item);
            string quoted = Quote(item);
            start.Arguments = string.IsNullOrEmpty(start.Arguments) ? quoted : start.Arguments + " " + quoted;
        }

        public void Clear() => throw new NotSupportedException();
        public bool Contains(string item) => _added.Contains(item);
        public void CopyTo(string[] array, int arrayIndex) => _added.CopyTo(array, arrayIndex);
        public bool Remove(string item) => throw new NotSupportedException();
        public IEnumerator<string> GetEnumerator() => _added.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return arg;
            var sb = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') sb.Append('\\', slashes * 2 + 1);
                else sb.Append('\\', slashes);
                slashes = 0;
                sb.Append(c);
            }
            sb.Append('\\', slashes * 2).Append('"');
            return sb.ToString();
        }
    }
}
