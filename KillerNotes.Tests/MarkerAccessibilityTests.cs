using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>The payload marker a sketch or recording picture carries is storage, not text for
    /// people: screen readers announce a name for the picture, never the 70-character hash.</summary>
    public sealed class MarkerAccessibilityTests
    {
        [Theory]
        [InlineData(1)]   // sketch
        [InlineData(2)]   // recording
        public void MarkedPicturesAnnounceANameNotTheHash(int kind) => Sta.Run(() =>
        {
            var image = new Image { Source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8) };
            Sketch.MarkImage(image, Attachments.Marker((AttachmentKind)kind, new string('a', 64)));

            var peer = UIElementAutomationPeer.CreatePeerForElement(image);
            Assert.DoesNotContain("kn-", peer.GetHelpText());
            Assert.DoesNotContain("kn-", peer.GetName());
            Assert.False(string.IsNullOrEmpty(peer.GetName()));
        });
    }
}
