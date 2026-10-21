using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace KillerNotes.Services
{
    /// <summary>
    /// Publishes an embedded native library to its on-disk cache so the copy that gets loaded is
    /// byte-for-byte the one this build ships. The cache used to be accepted when its SIZE
    /// matched, so a damaged file of the same length kept breaking startup instead of being
    /// repaired. Same pattern as Killendar's SQLCipher cache: compare by SHA-256, write a fresh
    /// copy beside it and swap it in, all under a per-file lock shared by every running instance.
    /// </summary>
    internal static class NativeCache
    {
        internal static void PublishVerified(string target, byte[] contents)
        {
            target = Path.GetFullPath(target);
            byte[] expected;
            using (var sha = SHA256.Create()) expected = sha.ComputeHash(contents);
            string mutexName;
            using (var sha = SHA256.Create())
                mutexName = @"Global\KillerNotes.NativeCache." + BitConverter.ToString(
                    sha.ComputeHash(Encoding.UTF8.GetBytes(target.ToUpperInvariant()))).Replace("-", "");

            using var mutex = new Mutex(false, mutexName);
            bool acquired = false;
            try
            {
                try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("Timed out waiting to update the native library cache: " + target);
                PublishLocked(target, contents, expected);
            }
            finally { if (acquired) mutex.ReleaseMutex(); }
        }

        private static void PublishLocked(string target, byte[] contents, byte[] expected)
        {
            string directory = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(directory);
            if (Matches(target, expected)) return;
            string temp = Path.Combine(directory, Path.GetFileNameWithoutExtension(target) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(contents, 0, contents.Length);
                    file.Flush(true);
                }
                try
                {
                    if (File.Exists(target)) File.Replace(temp, target, null);
                    else File.Move(temp, target);
                }
                // Loaded by another running instance: fine if that copy is already correct.
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && Matches(target, expected)) { }
            }
            finally
            {
                try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            if (!Matches(target, expected))
                throw new IOException("The cached native library differs from the one in this build and could not be replaced: " + target);
        }

        private static bool Matches(string target, byte[] expected)
        {
            try
            {
                using var file = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sha = SHA256.Create();
                return sha.ComputeHash(file).SequenceEqual(expected);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
