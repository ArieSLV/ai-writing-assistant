using System.Runtime.InteropServices;

namespace AiWritingAssistant.Tests;

public sealed class ClipboardServiceTests
{
    [Fact]
    public async Task Busy_clipboard_is_retried_without_changing_the_text()
    {
        var attempts = 0;
        string? written = null;
        var service = new ClipboardService(
            text =>
            {
                attempts++;
                if (attempts < 3)
                    throw new ExternalException("busy");
                written = text;
            },
            (_, _) => Task.CompletedTask);

        await service.SetTextAsync("transcript", CancellationToken.None);

        Assert.Equal(3, attempts);
        Assert.Equal("transcript", written);
    }

    [Fact]
    public async Task Permanent_busy_clipboard_has_controlled_error()
    {
        var service = new ClipboardService(
            _ => throw new ExternalException("busy"),
            (_, _) => Task.CompletedTask);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetTextAsync("transcript", CancellationToken.None));

        Assert.Equal("Transcript is ready, but the clipboard is unavailable.", exception.Message);
    }

    [Fact]
    public async Task Empty_text_is_rejected_before_clipboard_access()
    {
        var calls = 0;
        var service = new ClipboardService(_ => calls++);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SetTextAsync(" ", CancellationToken.None));

        Assert.Equal(0, calls);
    }
}
