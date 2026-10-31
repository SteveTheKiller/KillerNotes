using System;

namespace KillerNotes.Services
{
    // APP-OWNED, and temporary: the family AboutController catches this type, and it ships inside
    // KillerUI's streaming UpdateService, which KillerNotes has not taken because that version needs
    // System.Text.Json. Nothing here throws it yet. Taking that UpdateService brings its own copy,
    // so delete this file then.
    internal sealed class UpdateVerificationException : Exception
    {
        internal UpdateVerificationException(string message) : base(message) { }
    }
}
