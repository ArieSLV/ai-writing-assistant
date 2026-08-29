namespace VoiceOverlayProof;

internal sealed class FixturePromptForm : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int ContentLeft = 24;
    private const int ContentWidth = 712;
    private const int PromptTop = 58;
    private const int MinimumPromptHeight = 108;

    public FixturePromptForm(FixtureRecordingOptions options)
    {
        Text = "Voice fixture prompt";
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Color.FromArgb(17, 24, 39);

        var title = new Label
        {
            AutoSize = false,
            Location = new Point(ContentLeft, 16),
            Size = new Size(ContentWidth, 34),
            Text = $"{options.Fixture.Id} · take {options.Take:00} · {options.Fixture.TargetDuration}",
            Font = new Font("Segoe UI Semibold", 15),
            ForeColor = Color.FromArgb(147, 197, 253),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var promptFont = new Font("Segoe UI", 13);
        var measuredPrompt = TextRenderer.MeasureText(
            options.Fixture.Prompt,
            promptFont,
            new Size(ContentWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        var promptHeight = Math.Max(MinimumPromptHeight, measuredPrompt.Height + 12);

        var prompt = new Label
        {
            AutoSize = false,
            Location = new Point(ContentLeft, PromptTop),
            Size = new Size(ContentWidth, promptHeight),
            Text = options.Fixture.Prompt,
            Font = promptFont,
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var hintTop = prompt.Bottom + 8;
        var hint = new Label
        {
            AutoSize = false,
            Location = new Point(ContentLeft, hintTop),
            Size = new Size(ContentWidth, 24),
            Text = "Ctrl+Shift+G — начать; прочитайте сценарий естественно; Ctrl+Shift+G — остановить",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(156, 163, 175),
            TextAlign = ContentAlignment.MiddleLeft
        };

        ClientSize = new Size(760, hint.Bottom + 12);

        Controls.Add(title);
        Controls.Add(prompt);
        Controls.Add(hint);
    }

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

    public void ShowAtTopCenter()
    {
        var workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(
            workingArea.Left + (workingArea.Width - Width) / 2,
            workingArea.Top + 28);
        Show();
    }
}
