using AiWritingAssistant.Audio;
using AiWritingAssistant.UI;

namespace AiWritingAssistant.Tests;

public sealed class RecordingOverlayFormTests
{
    [Fact]
    public void Overlay_has_non_activating_styles_and_maximum_fires_once()
    {
        Exception? failure = null;
        var hasStyles = false;
        var maximumEvents = 0;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new RecordingOverlayForm(
                    new AudioLevelBuffer(),
                    TimeSpan.FromSeconds(1));
                form.MaximumDurationReached += (_, _) => maximumEvents++;
                hasStyles = form.HasNoActivateStyles;
                form.EvaluateMaximumDuration(TimeSpan.FromSeconds(1));
                form.EvaluateMaximumDuration(TimeSpan.FromSeconds(2));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.True(hasStyles);
        Assert.Equal(1, maximumEvents);
    }

    [Fact]
    public void Overlay_can_be_dragged_and_keeps_the_selected_position()
    {
        Exception? failure = null;
        Point? movedLocation = null;
        Point? locationAfterDragEnded = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new RecordingOverlayForm(
                    new AudioLevelBuffer(),
                    TimeSpan.FromSeconds(10))
                {
                    Location = new Point(100, 200)
                };

                var cancelButton = form.Controls.Find("CancelRecordingButton", false).Single();
                Assert.Equal(Cursors.SizeAll, form.Cursor);
                Assert.Equal(Cursors.Default, cancelButton.Cursor);

                form.BeginDrag(new Point(300, 400));
                form.ContinueDrag(new Point(325, 430));
                movedLocation = form.Location;
                form.EndDrag();
                form.ContinueDrag(new Point(500, 600));
                form.PositionForNextShow(new Point(10, 10));
                locationAfterDragEnded = form.Location;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.Equal(new Point(125, 230), movedLocation);
        Assert.Equal(movedLocation, locationAfterDragEnded);
    }

    [Fact]
    public void Recording_overlay_copy_and_accessibility_are_English()
    {
        Exception? failure = null;
        string? visibleAndAccessibleCopy = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new RecordingOverlayForm(
                    new AudioLevelBuffer(),
                    TimeSpan.FromSeconds(10));
                var cancelButton = form.Controls.Find("CancelRecordingButton", false).Single();
                visibleAndAccessibleCopy = string.Join(
                    " | ",
                    RecordingOverlayForm.RecordingStatusText,
                    RecordingOverlayForm.RecordingHintText,
                    cancelButton.Text,
                    cancelButton.AccessibleName,
                    cancelButton.AccessibleDescription);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.NotNull(visibleAndAccessibleCopy);
        Assert.Contains("RECORDING", visibleAndAccessibleCopy, StringComparison.Ordinal);
        Assert.Contains("Cancel", visibleAndAccessibleCopy, StringComparison.Ordinal);
        Assert.DoesNotMatch("[А-Яа-яЁё]", visibleAndAccessibleCopy);
    }
}
