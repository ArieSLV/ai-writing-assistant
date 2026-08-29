using System.Diagnostics;
using AiWritingAssistant.Audio;

namespace AiWritingAssistant.UI;

internal interface IVoiceDictationView : IDisposable
{
    event EventHandler? MaximumDurationReached;

    event EventHandler? CancelRequested;

    void ShowRecording();

    void HideOverlay();
}

internal sealed class RecordingOverlayForm : Form, IVoiceDictationView
{
    internal const string RecordingStatusText = "●  RECORDING";
    internal const string RecordingHintText = "Ctrl+Shift+G — stop and transcribe";
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private readonly Label _statusLabel;
    private readonly Label _timerLabel;
    private readonly Label _hintLabel;
    private readonly Button _cancelButton;
    private readonly AudioLevelControl _waveform;
    private readonly System.Windows.Forms.Timer _renderTimer;
    private readonly Stopwatch _recordingStopwatch = new();
    private readonly TimeSpan _maximumDuration;
    private bool _maximumRaised;
    private bool _isDragging;
    private bool _hasUserPosition;
    private Point _dragStartCursor;
    private Point _dragStartLocation;
    private Control? _dragSource;

    public RecordingOverlayForm(AudioLevelBuffer levels, TimeSpan maximumDuration)
    {
        _maximumDuration = maximumDuration;

        Text = "Voice Dictation";
        ClientSize = new Size(440, 168);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Color.FromArgb(31, 41, 55);

        _statusLabel = new Label
        {
            AutoSize = false,
            Location = new Point(18, 14),
            Size = new Size(300, 34),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 15),
            ForeColor = Color.White
        };

        _timerLabel = new Label
        {
            AutoSize = false,
            Location = new Point(330, 17),
            Size = new Size(90, 28),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Consolas", 13),
            ForeColor = Color.FromArgb(229, 231, 235)
        };

        _waveform = new AudioLevelControl(levels)
        {
            Location = new Point(18, 55),
            Size = new Size(404, 68),
            BackColor = Color.FromArgb(55, 65, 81)
        };

        _hintLabel = new Label
        {
            AutoSize = false,
            Location = new Point(18, 132),
            Size = new Size(310, 24),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(209, 213, 219)
        };

        _cancelButton = new Button
        {
            Name = "CancelRecordingButton",
            Location = new Point(342, 130),
            Size = new Size(80, 26),
            Text = "Cancel",
            AccessibleName = "Cancel voice recording",
            AccessibleDescription = "Stop recording and delete the audio without sending it for transcription",
            TabStop = false,
            Cursor = Cursors.Default,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(75, 85, 99)
        };
        _cancelButton.FlatAppearance.BorderSize = 0;
        _cancelButton.Click += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);

        Controls.Add(_statusLabel);
        Controls.Add(_timerLabel);
        Controls.Add(_waveform);
        Controls.Add(_hintLabel);
        Controls.Add(_cancelButton);

        AttachDragSurface(this);
        AttachDragSurface(_statusLabel);
        AttachDragSurface(_timerLabel);
        AttachDragSurface(_waveform);
        AttachDragSurface(_hintLabel);

        _renderTimer = new System.Windows.Forms.Timer { Interval = 33 };
        _renderTimer.Tick += (_, _) => RenderFrame();
        _renderTimer.Start();
    }

    public event EventHandler? MaximumDurationReached;

    public event EventHandler? CancelRequested;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExNoActivate | WsExToolWindow;
            return parameters;
        }
    }

    internal bool HasNoActivateStyles
    {
        get
        {
            var styles = CreateParams.ExStyle;
            return (styles & WsExNoActivate) != 0 && (styles & WsExToolWindow) != 0;
        }
    }

    public void ShowRecording()
    {
        _maximumRaised = false;
        _recordingStopwatch.Restart();
        _statusLabel.Text = RecordingStatusText;
        _timerLabel.Text = "00:00";
        _hintLabel.Text = RecordingHintText;
        _cancelButton.Visible = true;
        BackColor = Color.FromArgb(127, 29, 29);
        _waveform.SetWaveColor(Color.FromArgb(254, 202, 202));
        ShowAtBottomCenter();
        System.Media.SystemSounds.Asterisk.Play();
    }

    public void HideOverlay()
    {
        _recordingStopwatch.Stop();
        if (_dragSource is not null)
            _dragSource.Capture = false;

        _dragSource = null;
        EndDrag();
        Hide();
    }

    internal void EvaluateMaximumDuration(TimeSpan elapsed)
    {
        if (_maximumRaised || elapsed < _maximumDuration)
            return;

        _maximumRaised = true;
        MaximumDurationReached?.Invoke(this, EventArgs.Empty);
    }

    internal void PositionAtBottomCenter(Point referencePoint)
    {
        var workingArea = Screen.FromPoint(referencePoint).WorkingArea;
        Location = new Point(
            workingArea.Left + (workingArea.Width - Width) / 2,
            workingArea.Bottom - Height - 28);
    }

    internal void BeginDrag(Point cursorPosition)
    {
        _isDragging = true;
        _hasUserPosition = true;
        _dragStartCursor = cursorPosition;
        _dragStartLocation = Location;
    }

    internal void ContinueDrag(Point cursorPosition)
    {
        if (!_isDragging)
            return;

        var offset = new Size(
            cursorPosition.X - _dragStartCursor.X,
            cursorPosition.Y - _dragStartCursor.Y);
        Location = _dragStartLocation + offset;
    }

    internal void EndDrag()
    {
        _isDragging = false;
    }

    internal void PositionForNextShow(Point referencePoint)
    {
        if (!Visible && !_hasUserPosition)
            PositionAtBottomCenter(referencePoint);
    }

    private void RenderFrame()
    {
        if (_recordingStopwatch.IsRunning)
        {
            var elapsed = _recordingStopwatch.Elapsed;
            _timerLabel.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
            EvaluateMaximumDuration(elapsed);
        }

        _waveform.Invalidate();
    }

    private void ShowAtBottomCenter()
    {
        PositionForNextShow(Cursor.Position);
        if (!Visible)
            Show();
    }

    private void AttachDragSurface(Control control)
    {
        control.Cursor = Cursors.SizeAll;
        control.MouseDown += DragSurfaceOnMouseDown;
        control.MouseMove += DragSurfaceOnMouseMove;
        control.MouseUp += DragSurfaceOnMouseUp;
    }

    private void DragSurfaceOnMouseDown(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left || sender is not Control control)
            return;

        _dragSource = control;
        control.Capture = true;
        BeginDrag(control.PointToScreen(eventArgs.Location));
    }

    private void DragSurfaceOnMouseMove(object? sender, MouseEventArgs eventArgs)
    {
        if (!_isDragging || sender is not Control control)
            return;

        ContinueDrag(control.PointToScreen(eventArgs.Location));
    }

    private void DragSurfaceOnMouseUp(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left)
            return;

        if (_dragSource is not null)
            _dragSource.Capture = false;

        _dragSource = null;
        EndDrag();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderTimer.Dispose();
        }

        base.Dispose(disposing);
    }
}
