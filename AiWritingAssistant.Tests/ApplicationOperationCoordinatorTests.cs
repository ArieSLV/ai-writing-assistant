namespace AiWritingAssistant.Tests;

public sealed class ApplicationOperationCoordinatorTests
{
    [Fact]
    public void Lease_is_exclusive_and_release_returns_to_idle()
    {
        using var coordinator = new ApplicationOperationCoordinator();

        Assert.True(coordinator.TryAcquire(ApplicationOperation.Proofread, out var first));
        Assert.Equal(ApplicationOperationState.TextProcessing, coordinator.State);
        Assert.False(coordinator.TryAcquire(ApplicationOperation.Voice, out var rejected));
        Assert.Null(rejected);

        first!.Dispose();

        Assert.Equal(ApplicationOperationState.Idle, coordinator.State);
        Assert.True(coordinator.TryAcquire(ApplicationOperation.Voice, out var voice));
        voice!.Dispose();
    }

    [Fact]
    public void Voice_lease_can_enter_processing_once()
    {
        using var coordinator = new ApplicationOperationCoordinator();
        Assert.True(coordinator.TryAcquire(ApplicationOperation.Voice, out var lease));

        Assert.True(lease!.TryEnterVoiceProcessing());
        Assert.Equal(ApplicationOperationState.VoiceProcessing, coordinator.State);
        Assert.False(lease.TryEnterVoiceProcessing());
    }

    [Fact]
    public void Shutdown_cancels_active_lease_and_rejects_new_work()
    {
        using var coordinator = new ApplicationOperationCoordinator();
        Assert.True(coordinator.TryAcquire(ApplicationOperation.Translate, out var lease));

        coordinator.BeginShutdown();

        Assert.Equal(ApplicationOperationState.ShuttingDown, coordinator.State);
        Assert.True(lease!.CancellationToken.IsCancellationRequested);
        Assert.False(coordinator.TryAcquire(ApplicationOperation.Voice, out _));
    }
}
