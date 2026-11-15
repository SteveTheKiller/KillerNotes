using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using KillerNotes.Models;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class StaLifetimeTests
    {
        [Fact]
        public void ShortLivedBindingThreadFinishesDispatcherShutdownBeforeReturning()
        {
            Dispatcher? dispatcher = null;
            Sta.Run(() =>
            {
                var note = new Note { Title = "Bound note" };
                var text = new TextBlock();
                text.SetBinding(TextBlock.TextProperty, new Binding(nameof(Note.Title)) { Source = note });
                text.Measure(new Size(200, 40));
                Assert.Equal("Bound note", text.Text);
                dispatcher = text.Dispatcher;
            });
            Assert.NotNull(dispatcher);
            Assert.True(dispatcher!.HasShutdownFinished, "Binding cleanup must finish while its STA thread can still answer dispatcher requests.");
        }
    }
}
