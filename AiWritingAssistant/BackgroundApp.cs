public class BackgroundApp : Form
{
    private KeyboardHook _hook;

    public BackgroundApp()
    {
        InitializeHook();
        InitializeTrayIcon();
    }

    private void InitializeHook()
    {
        _hook = new KeyboardHook();
        _hook.HotKeyPressed += () =>
        {
            var text = ClipboardManager.GetSelectedText();
            if (!string.IsNullOrEmpty(text))
            {
                // Ваша логика обработки текста
                ProcessText(text);
            }
            else
            {
                Console.WriteLine("Не удалось получить выделенный текст");
            }
        };
    }

    private void ProcessText(string text)
    {
        // Здесь будет вызов LLM для улучшения текста
        Console.WriteLine($"Обработка текста: {text}");
    }

    private void InitializeTrayIcon()
    {
        NotifyIcon trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "AI Writing Assistant"
        };

        trayIcon.ContextMenuStrip = new ContextMenuStrip();
        trayIcon.ContextMenuStrip.Items.Add("Выход", null, (s, e) => Application.Exit());
    }

    protected override void Dispose(bool disposing)
    {
        _hook?.Dispose();
        base.Dispose(disposing);
    }
}