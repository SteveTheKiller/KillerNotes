using System;
using System.Globalization;

namespace KillerNotes.Services
{
    internal static class TrashRetention
    {
        internal const string DaysSetting = "TrashRetentionDays";
        internal const int DefaultDays = 30;
        internal const int MaximumDays = 3650;
        internal static Func<string, string?> ReadSetting = App.GetSetting;

        internal static int Days => Resolve(ReadSetting(DaysSetting));

        internal static int Resolve(string? value) =>
            TryParse(value, out int days) ? days : DefaultDays;

        internal static bool TryParse(string? value, out int days) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out days)
            && days >= 0 && days <= MaximumDays;
    }
}
