using System;
using System.Globalization;
using System.Linq;
using KillerNotes.Cli;

namespace KillerNotes.Services
{
    public static partial class NoteStore
    {
        internal static string ReadMcpCommand(string[] args)
        {
            if (_db == null) throw new InvalidOperationException("Open and unlock KillerNotes first");
            return Program.ReadActive(args, _db);
        }
    }
}
