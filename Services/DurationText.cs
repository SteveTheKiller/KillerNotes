using System;
using System.Globalization;

namespace KillerNotes.Services
{
    /// <summary>Recording lengths as the pad, the chip and Markdown export show them: m:ss under an
    /// hour and h:mm:ss from an hour on, so a long take never wraps back to a few minutes.</summary>
    internal static class DurationText
    {
        internal static string Format(long ms)
        {
            if (ms < 0) ms = 0;
            long totalSeconds = ms / 1000;
            long hours = totalSeconds / 3600;
            int minutes = (int)(totalSeconds / 60 % 60);
            int seconds = (int)(totalSeconds % 60);
            return hours > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", hours, minutes, seconds)
                : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", totalSeconds / 60, seconds);
        }
    }
}
