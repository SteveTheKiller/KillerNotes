using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The network writer reservation (NoteStore.TryAcquireLock). A lock file that cannot be
    /// created for any reason means "no reservation", so the database opens read-only instead of
    /// the open failing outright.
    /// </summary>
    public sealed class WriterLockTests
    {
        [Theory]
        [InlineData("C:\\killernotes-lock-test\\bad|name.lock")]
        [InlineData("C:\\killernotes-lock-test:stream:extra.lock")]
        public void A_lock_path_the_file_system_rejects_gives_no_reservation(string path)
        {
            Assert.Null(NoteStore.TryAcquireLock(path));
        }
    }
}
