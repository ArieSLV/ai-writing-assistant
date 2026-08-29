using System.Drawing;
using System.Windows.Forms;
using VoiceOverlayProof;

Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

var allFit = true;
var maxClientHeight = 0;

for (var fixtureNumber = 1; fixtureNumber <= 9; fixtureNumber++)
{
    var fixtureId = $"F-{fixtureNumber:00}";
    var fixture = FixtureCatalog.Get(fixtureId);
    var options = new FixtureRecordingOptions(fixture, 1, "layout-proof");

    using var form = new FixturePromptForm(options);
    _ = form.Handle;

    var prompt = form.Controls
        .OfType<Label>()
        .Single(label => label.Text == fixture.Prompt);

    var required = TextRenderer.MeasureText(
        prompt.Text,
        prompt.Font,
        new Size(prompt.ClientSize.Width, int.MaxValue),
        TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

    var promptFits = required.Height <= prompt.ClientSize.Height;
    var controlsFit = form.Controls.Cast<Control>().Max(control => control.Bottom) <= form.ClientSize.Height;
    var fixtureFits = promptFits && controlsFit;

    allFit &= fixtureFits;
    maxClientHeight = Math.Max(maxClientHeight, form.ClientSize.Height);

    Console.WriteLine(
        $"FIXTURE={fixtureId};PROMPT_HEIGHT={prompt.ClientSize.Height};REQUIRED_HEIGHT={required.Height};" +
        $"CLIENT_HEIGHT={form.ClientSize.Height};PROMPT_FITS={promptFits};CONTROLS_FIT={controlsFit}");
}

Console.WriteLine($"FIXTURE_PROMPTS_FIT={(allFit ? "9/9" : "FAILED")}");
Console.WriteLine($"FIXTURE_PROMPT_MAX_CLIENT_HEIGHT={maxClientHeight}");

return allFit ? 0 : 1;
