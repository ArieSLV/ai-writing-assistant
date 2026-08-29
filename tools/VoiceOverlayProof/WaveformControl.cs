using System.Drawing.Drawing2D;

namespace VoiceOverlayProof;

internal sealed class WaveformControl : Control
{
    private readonly AudioLevelBuffer _levels;
    private Color _waveColor = Color.FromArgb(254, 202, 202);

    public WaveformControl(AudioLevelBuffer levels)
    {
        _levels = levels;
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint,
            true);
    }

    public void SetWaveColor(Color color)
    {
        _waveColor = color;
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);

        var levels = _levels.Snapshot();
        if (levels.Length == 0 || ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(_waveColor, 3.0f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        var centerY = ClientSize.Height / 2.0f;
        var slotWidth = ClientSize.Width / (float)levels.Length;

        for (var index = 0; index < levels.Length; index++)
        {
            var visibleLevel = MathF.Sqrt(levels[index]);
            var halfHeight = Math.Max(2.0f, visibleLevel * (ClientSize.Height / 2.0f - 3.0f));
            var x = slotWidth * index + slotWidth / 2.0f;
            eventArgs.Graphics.DrawLine(pen, x, centerY - halfHeight, x, centerY + halfHeight);
        }
    }
}
