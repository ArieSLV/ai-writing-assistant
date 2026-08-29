namespace AiWritingAssistant;

internal enum ApplicationOperation
{
    Proofread,
    Translate,
    Voice
}

internal enum ApplicationOperationState
{
    Idle,
    TextProcessing,
    VoiceRecording,
    VoiceProcessing,
    ShuttingDown
}

internal sealed class ApplicationOperationCoordinator : IDisposable
{
    private readonly object _gate = new();
    private ApplicationOperationState _state = ApplicationOperationState.Idle;
    private long _leaseVersion;
    private CancellationTokenSource? _activeCancellation;
    private bool _disposed;

    public ApplicationOperationState State
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    public bool TryAcquire(ApplicationOperation operation, out ApplicationOperationLease? lease)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_state != ApplicationOperationState.Idle)
            {
                lease = null;
                return false;
            }

            _state = operation == ApplicationOperation.Voice
                ? ApplicationOperationState.VoiceRecording
                : ApplicationOperationState.TextProcessing;

            _activeCancellation = new CancellationTokenSource();
            var version = ++_leaseVersion;
            lease = new ApplicationOperationLease(this, operation, version, _activeCancellation.Token);
            return true;
        }
    }

    public void BeginShutdown()
    {
        CancellationTokenSource? cancellation;

        lock (_gate)
        {
            if (_disposed || _state == ApplicationOperationState.ShuttingDown)
                return;

            _state = ApplicationOperationState.ShuttingDown;
            cancellation = _activeCancellation;
        }

        cancellation?.Cancel();
    }

    internal bool TryEnterVoiceProcessing(long leaseVersion)
    {
        lock (_gate)
        {
            if (_disposed || leaseVersion != _leaseVersion || _state != ApplicationOperationState.VoiceRecording)
                return false;

            _state = ApplicationOperationState.VoiceProcessing;
            return true;
        }
    }

    internal void Release(long leaseVersion)
    {
        CancellationTokenSource? cancellation = null;

        lock (_gate)
        {
            if (_disposed || leaseVersion != _leaseVersion)
                return;

            cancellation = _activeCancellation;
            _activeCancellation = null;

            if (_state != ApplicationOperationState.ShuttingDown)
                _state = ApplicationOperationState.Idle;
        }

        cancellation?.Dispose();
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;

        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _state = ApplicationOperationState.ShuttingDown;
            cancellation = _activeCancellation;
            _activeCancellation = null;
        }

        cancellation?.Cancel();
        cancellation?.Dispose();
    }
}

internal sealed class ApplicationOperationLease : IDisposable
{
    private readonly ApplicationOperationCoordinator _owner;
    private readonly long _version;
    private int _disposed;

    internal ApplicationOperationLease(
        ApplicationOperationCoordinator owner,
        ApplicationOperation operation,
        long version,
        CancellationToken cancellationToken)
    {
        _owner = owner;
        _version = version;
        Operation = operation;
        CancellationToken = cancellationToken;
    }

    public ApplicationOperation Operation { get; }

    public CancellationToken CancellationToken { get; }

    public bool TryEnterVoiceProcessing()
    {
        return Operation == ApplicationOperation.Voice &&
               Volatile.Read(ref _disposed) == 0 &&
               _owner.TryEnterVoiceProcessing(_version);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _owner.Release(_version);
    }
}
