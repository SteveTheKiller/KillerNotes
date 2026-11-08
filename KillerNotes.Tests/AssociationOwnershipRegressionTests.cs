using System;
using Microsoft.Win32;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class AssociationOwnershipRegressionTests
    {
        [Theory]
        [InlineData(".knote", "KillerNotes.Note")]
        [InlineData(".kndb", "KillerNotes.Database")]
        public void UnregisterPreservesAnotherApplicationsExtensionRegistration(string extension, string progId)
        {
            string path = @"Software\KillerNotesTests\" + Guid.NewGuid().ToString("N");
            try
            {
                using var root = Registry.CurrentUser.CreateSubKey(path);
                using (var key = root.CreateSubKey(@"Software\Classes\" + extension))
                {
                    key.SetValue("", "OtherApp.Document");
                    key.SetValue("Content Type", "application/other");
                    using var open = key.CreateSubKey("OpenWithProgids");
                    open.SetValue(progId, "");
                    open.SetValue("OtherApp.Document", "");
                    using var metadata = key.CreateSubKey("OtherAppMetadata");
                    metadata.SetValue("setting", "keep");
                }

                App.UnregisterFileAssociations(root);

                using var remaining = root.OpenSubKey(@"Software\Classes\" + extension);
                Assert.NotNull(remaining);
                Assert.Equal("OtherApp.Document", remaining!.GetValue(""));
                Assert.Equal("application/other", remaining.GetValue("Content Type"));
                using var other = remaining.OpenSubKey("OtherAppMetadata");
                Assert.Equal("keep", other!.GetValue("setting"));
                using var openWith = remaining.OpenSubKey("OpenWithProgids");
                Assert.NotNull(openWith);
                Assert.NotNull(openWith!.GetValue("OtherApp.Document"));
                Assert.Null(openWith.GetValue(progId));
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
        }

        [Theory]
        [InlineData(".knote", "KillerNotes.Note", false)]
        [InlineData(".kndb", "KillerNotes.Database", false)]
        [InlineData(".knote", "KillerNotes.Note", true)]
        [InlineData(".kndb", "KillerNotes.Database", true)]
        public void UnregisterRemovesOwnDefaultAndDeletesOnlyEmptyExtensions(string extension, string progId, bool keepOtherData)
        {
            string path = @"Software\KillerNotesTests\" + Guid.NewGuid().ToString("N");
            try
            {
                using var root = Registry.CurrentUser.CreateSubKey(path);
                using (var key = root.CreateSubKey(@"Software\Classes\" + extension))
                {
                    key.SetValue("", progId.ToUpperInvariant());
                    if (keepOtherData) key.SetValue("Content Type", "application/other");
                    using var open = key.CreateSubKey("OpenWithProgids");
                    open.SetValue(progId, "");
                }

                App.UnregisterFileAssociations(root);

                using var remaining = root.OpenSubKey(@"Software\Classes\" + extension);
                if (keepOtherData)
                {
                    Assert.NotNull(remaining);
                    Assert.Null(remaining!.GetValue(""));
                    Assert.Equal("application/other", remaining.GetValue("Content Type"));
                    using var open = remaining.OpenSubKey("OpenWithProgids");
                    Assert.Null(open);
                }
                else Assert.Null(remaining);
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
        }
    }
}
