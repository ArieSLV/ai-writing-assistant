using System.Diagnostics;

namespace VoiceOverlayProof;

internal sealed class RecordingOverlayForm : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private readonly Label _statusLabel;
    private readonly Label _timerLabel;
    private readonly Label _hintLabel;
    private readonly Button _cancelButton;
    private readonly WaveformControl _waveform;
    private readonly System.Windows.Forms.Timer _renderTimer;
    private readonly Stopwatch _recordingStopwatch = new();
    private readonly TimeSpan _maximumDuration;
    private bool _maximumRaised;

    public RecordingOverlayForm(AudioLevelBuffer levels, TimeSpan maximumDuration)
    {
        _maximumDuration = maximumDuration;

        Text = "Voice overlay proof";
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

        _waveform = new WaveformControl(levels)
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
            Text = "Отмена",
            AccessibleName = "Отменить запись голоса",
            AccessibleDescription = "Остановить запись и удалить аудио без отправки на распознавание",
            TabStop = false,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(75, 85, 99),
            Visible = false
        };
        _cancelButton.FlatAppearance.BorderSize = 0;
        _cancelButton.Click += (_, _) => RequestCancel();

        Controls.Add(_statusLabel);
        Controls.Add(_timerLabel);
        Controls.Add(_waveform);
        Controls.Add(_hintLabel);
        Controls.Add(_cancelButton);

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

    public void ShowReady()
    {
        _recordingStopwatch.Reset();
        _statusLabel.Text = "Готово к Voice proof";
        _timerLabel.Text = "";
        _hintLabel.Text = "Ctrl+Shift+G — начать запись";
        _cancelButton.Visible = false;
        BackColor = Color.FromArgb(30, 64, 175);
        _waveform.SetWaveColor(Color.FromArgb(191, 219, 254));
        ShowAtBottomCenter();
    }

    public void ShowRecording()
    {
        _maximumRaised = false;
        _recordingStopwatch.Restart();
        _statusLabel.Text = "●  ИДЁТ ЗАПИСЬ";
        _timerLabel.Text = "00:00";
        _hintLabel.Text = "Ctrl+Shift+G — остановить и подготовить WAV";
        _cancelButton.Visible = true;
        BackColor = Color.FromArgb(127, 29, 29);
        _waveform.SetWaveColor(Color.FromArgb(254, 202, 202));
        ShowAtBottomCenter();
        System.Media.SystemSounds.Asterisk.Play();
    }

    public void ShowProcessing()
    {
        _recordingStopwatch.Stop();
        _statusLabel.Text = "◌  ПОДГОТОВКА WAV";
        _hintLabel.Text = "Запись завершена; выполняется нормализация";
        _cancelButton.Visible = false;
        BackColor = Color.FromArgb(67, 56, 202);
        _waveform.SetWaveColor(Color.FromArgb(224, 231, 255));
    }

    public void ShowFinished()
    {
        _recordingStopwatch.Stop();
        _statusLabel.Text = "✓  ГОТОВО";
        _hintLabel.Text = "Формат проверен; временные файлы удаляются";
        _cancelButton.Visible = false;
        BackColor = Color.FromArgb(20, 83, 45);
        _waveform.SetWaveColor(Color.FromArgb(187, 247, 208));
        System.Media.SystemSounds.Exclamation.Play();
    }

    public void ShowFailure(string message)
    {
        _recordingStopwatch.Stop();
        _statusLabel.Text = "✕  ОШИБКА PROOF";
        _timerLabel.Text = "";
        _hintLabel.Text = message;
        _cancelButton.Visible = false;
        BackColor = Color.FromArgb(127, 29, 29);
        System.Media.SystemSounds.Hand.Play();
        ShowAtBottomCenter();
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

    internal void EvaluateMaximumDuration(TimeSpan elapsed)
    {
        if (!_maximumRaised && elapsed >= _maximumDuration)
        {
            _maximumRaised = true;
            MaximumDurationReached?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ShowCancelling()
    {
        _recordingStopwatch.Stop();
        _statusLabel.Text = "◌  ОТМЕНА";
        _hintLabel.Text = "Запись удаляется без отправки";
        _cancelButton.Visible = false;
        BackColor = Color.FromArgb(75, 85, 99);
        _waveform.SetWaveColor(Color.FromArgb(229, 231, 235));
    }

    public void ShowCancelled()
    {
        _recordingStopwatch.Stop();
        _statusLabel.Text = "✓  ОТМЕНЕНО";
        _hintLabel.Text = "Временная запись удалена";
        _cancelButton.Visible = false;
        BackColor = Color.FromArgb(55, 65, 81);
        _waveform.SetWaveColor(Color.FromArgb(209, 213, 219));
    }

    internal void RequestCancel()
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ShowAtBottomCenter()
    {
        PositionAtBottomCenter(Cursor.Position);

        if (!Visible)
        {
            Show();
        }
    }

    internal void PositionAtBottomCenter(Point referencePoint)
    {
        var workingArea = Screen.FromPoint(referencePoint).WorkingArea;
        Location = new Point(
            workingArea.Left + (workingArea.Width - Width) / 2,
            workingArea.Bottom - Height - 28);
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
