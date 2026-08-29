using System.Runtime.InteropServices;

namespace AiWritingAssistant;

internal interface IClipboardService
{
    Task SetTextAsync(string text, CancellationToken cancellationToken);
}

internal sealed class ClipboardService : IClipboardService
{
    private const int MaximumAttempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(75);
    private readonly Action<string> _setText;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly bool _requireStaThread;

    public ClipboardService()
        : this(Clipboard.SetText, Task.Delay, requireStaThread: true)
    {
    }

    internal ClipboardService(
        Action<string> setText,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        bool requireStaThread = false)
    {
        _setText = setText ?? throw new ArgumentNullException(nameof(setText));
        _delay = delay ?? Task.Delay;
        _requireStaThread = requireStaThread;
    }

    public async Task SetTextAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Clipboard text must not be empty.", nameof(text));
        if (_requireStaThread && Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Clipboard writes must run on the application STA thread.");

        ExternalException? lastException = null;

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _setText(text);
                return;
            }
            catch (ExternalException exception) when (attempt < MaximumAttempts)
            {
                lastException = exception;
                await _delay(RetryDelay, cancellationToken);
            }
            catch (ExternalException exception)
            {
                lastException = exception;
            }
        }

        throw new InvalidOperationException(
            "Transcript is ready, but the clipboard is unavailable.",
            lastException);
    }
}
