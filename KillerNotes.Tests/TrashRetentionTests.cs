using System;
using System.Globalization;
using System.Linq;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection(NoteStoreCollection.Name)]
    public sealed class TrashRetentionTests
    {
        [Theory]
        [InlineData(null, 30)]
        [InlineData("", 30)]
        [InlineData("invalid", 30)]
        [InlineData("-1", 30)]
        [InlineData("3651", 30)]
        [InlineData("2147483648", 30)]
        [InlineData("0", 0)]
        [InlineData("7", 7)]
        [InlineData("3650", 3650)]
        public void MissingOrInvalidPreferencesKeepTheDefault(string? value, int expected) =>
            Assert.Equal(expected, TrashRetention.Resolve(value));

        [Theory]
        [InlineData("-1")]
        [InlineData("1.5")]
        [InlineData("3651")]
        [InlineData("")]
        public void InvalidInputCannotBecomeAPreference(string value) =>
            Assert.False(TrashRetention.TryParse(value, out _));

        [Theory]
        [InlineData(0, 3)]
        [InlineData(7, 0)]
        [InlineData(30, 1)]
        [InlineData(90, 2)]
        public void OpeningUsesThePreferenceAndKeepsLiveNotes(int days, int expectedTrash)
        {
            var original = TrashRetention.ReadSetting;
            string preference = "0";
            TrashRetention.ReadSetting = key => key == TrashRetention.DaysSetting ? preference : null;
            try
            {
                using var store = new TempStore();
                long live = NoteStore.Create("Keep live");
                var ages = new[] { 14, 45, 100 };
                var ids = ages.Select(age => NoteStore.Create("Deleted " + age)).ToArray();
                foreach (long id in ids) NoteStore.Trash(id);
                NoteStore.Close();
                using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = store.DbPath }.ConnectionString))
                {
                    db.Open();
                    for (int i = 0; i < ids.Length; i++)
                    {
                        using var command = db.CreateCommand();
                        command.CommandText = "UPDATE notes SET deleted = $stamp WHERE id = $id";
                        command.Parameters.AddWithValue("$stamp", DateTime.Now.AddDays(-ages[i]).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                        command.Parameters.AddWithValue("$id", ids[i]);
                        command.ExecuteNonQuery();
                    }
                }
                SqliteConnection.ClearAllPools();
                preference = days.ToString(CultureInfo.InvariantCulture);
                NoteStore.Open();

                Assert.Equal(expectedTrash, NoteStore.ListTrash().Count);
                Assert.Equal(live, Assert.Single(NoteStore.List()).Id);
                if (days == 0)
                {
                    Assert.Equal(0, NoteStore.PurgeTrash(0));
                    Assert.Equal(3, NoteStore.ListTrash().Count);
                }
            }
            finally { TrashRetention.ReadSetting = original; }
        }
    }
}
