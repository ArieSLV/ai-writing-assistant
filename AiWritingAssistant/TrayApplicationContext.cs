#pragma warning disable SKEXP0070

using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.SemanticKernel.ChatCompletion;
using OllamaSharp;
using OllamaSharp.Models;

namespace AiWritingAssistant;

public sealed class HotKeyEventArgs : EventArgs
{
    public int HotKeyId { get; }

    public HotKeyEventArgs(int hotKeyId)
    {
        HotKeyId = hotKeyId;
    }
}

public sealed class HotKeyMessageWindow : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;

    public event EventHandler<HotKeyEventArgs>? HotKeyPressed;

    public HotKeyMessageWindow()
    {
        CreateHandle(new CreateParams());
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey)
            HotKeyPressed?.Invoke(this, new HotKeyEventArgs(m.WParam.ToInt32()));

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        DestroyHandle();
    }
}

[Experimental("SKEXP0070")]
public sealed class TrayApplicationContext : ApplicationContext
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const int HotkeyIdProofread = 1;
    private const int HotkeyIdTranslate = 2;
    private const uint ModControlShift = 0x6;
    private const uint VkD = 0x44;
    private const uint VkF = 0x46;
    private const int MaxAttempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CompletionStateDuration = TimeSpan.FromSeconds(2.5);

    private readonly NotifyIcon _trayIcon;
    private readonly HotKeyMessageWindow _hotKeyWindow;
    private readonly Icon _originalIcon;
    private readonly Dictionary<string, Icon> _iconCache = new(StringComparer.Ordinal);
    private readonly AppLogger _logger;
    private readonly AppSettingsStore _settingsStore;
    private readonly LanguageModelServiceFactory _serviceFactory;
    private readonly AppSettings _settings;

    private readonly ToolStripMenuItem _geminiProviderMenuItem;
    private readonly ToolStripMenuItem _ollamaProviderMenuItem;
    private readonly ToolStripTextBox _geminiModelTextBox;
    private readonly ToolStripTextBox _ollamaBaseUrlTextBox;
    private readonly ToolStripMenuItem _ollamaReasoningMenuItem;
    private readonly ToolStripMenuItem _refreshOllamaModelsMenuItem;
    private readonly ToolStripMenuItem _ollamaModelsMenuItem;

    private IReadOnlyList<string> _ollamaModelNames = Array.Empty<string>();
    private string? _ollamaModelLoadError;
    private bool _hasLoadedOllamaModels;
    private bool _isProcessing;
    private bool _isRefreshingOllamaModels;
    private int _ollamaRefreshVersion;
    private CancellationTokenSource? _statusResetCancellation;

    private enum TrayIconState
    {
        Idle,
        Processing,
        Retrying,
        Succeeded,
        Failed,
    }

    public TrayApplicationContext()
    {
        _serviceFactory = new LanguageModelServiceFactory();
        _logger = new AppLogger(Path.Combine(AppContext.BaseDirectory, "AiWritingAssistant.log"));
        _settingsStore = new AppSettingsStore(Path.Combine(AppContext.BaseDirectory, "AiWritingAssistant.settings.json"), _logger);
        _settings = _settingsStore.Load();
        _settings.Normalize();
        _logger.Info(
            $"Application started. Log='{_logger.FilePath}', Provider='{_settings.SelectedProvider}', GeminiModel='{_settings.GeminiModel}', OllamaUrl='{_settings.OllamaBaseUrl}', OllamaModel='{_settings.OllamaModel ?? "<none>"}', OllamaReasoningEnabled={_settings.OllamaReasoningEnabled}.");

        _originalIcon = new Icon(Path.Combine(AppContext.BaseDirectory, "Resources", "General.ico"));

        _geminiProviderMenuItem = new ToolStripMenuItem("Gemini");
        _ollamaProviderMenuItem = new ToolStripMenuItem("Ollama");
        _geminiModelTextBox = CreateSettingsTextBox();
        _ollamaBaseUrlTextBox = CreateSettingsTextBox();
        _ollamaReasoningMenuItem = new ToolStripMenuItem("Enable Ollama Reasoning")
        {
            CheckOnClick = true,
        };
        _refreshOllamaModelsMenuItem = new ToolStripMenuItem("Refresh Ollama Models");
        _ollamaModelsMenuItem = new ToolStripMenuItem("Ollama Models");

        _trayIcon = new NotifyIcon
        {
            Icon = _originalIcon,
            ContextMenuStrip = BuildContextMenu(),
            Visible = true,
            Text = "AI Writing Assistant",
        };

        _hotKeyWindow = new HotKeyMessageWindow();
        _hotKeyWindow.HotKeyPressed += (_, e) =>
        {
            switch (e.HotKeyId)
            {
                case HotkeyIdProofread:
                    _ = PerformClipboardActionAsync("Proofread Clipboard Text", BuildProofreadPrompt);
                    break;
                case HotkeyIdTranslate:
                    _ = PerformClipboardActionAsync("Translate Clipboard Text to English", BuildTranslatePrompt);
                    break;
            }
        };

        RegisterHotKey(_hotKeyWindow.Handle, HotkeyIdProofread, ModControlShift, VkD);
        RegisterHotKey(_hotKeyWindow.Handle, HotkeyIdTranslate, ModControlShift, VkF);

        UpdateSettingsUi();
        SetTrayState(TrayIconState.Idle);

        if (_settings.SelectedProvider == LanguageModelProvider.Ollama)
            _ = RefreshOllamaModelsAsync();
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var proofreadItem = new ToolStripMenuItem("Proofread Clipboard Text");
        proofreadItem.Click += (_, _) => _ = PerformClipboardActionAsync("Proofread Clipboard Text", BuildProofreadPrompt);

        var translateItem = new ToolStripMenuItem("Translate Clipboard Text to English");
        translateItem.Click += (_, _) => _ = PerformClipboardActionAsync("Translate Clipboard Text to English", BuildTranslatePrompt);

        var modelSettingsItem = new ToolStripMenuItem("Model Settings");

        _geminiProviderMenuItem.Click += async (_, _) => await SetSelectedProviderAsync(LanguageModelProvider.Gemini);
        _ollamaProviderMenuItem.Click += async (_, _) => await SetSelectedProviderAsync(LanguageModelProvider.Ollama);

        BindTextBoxCommit(_geminiModelTextBox, ApplyGeminiModelAsync);
        BindTextBoxCommit(_ollamaBaseUrlTextBox, ApplyOllamaBaseUrlAsync);

        _refreshOllamaModelsMenuItem.Click += async (_, _) => await RefreshOllamaModelsAsync();
        _ollamaReasoningMenuItem.Click += (_, _) => ToggleOllamaReasoning();

        modelSettingsItem.DropDownItems.Add(CreateSectionLabel("Provider"));
        modelSettingsItem.DropDownItems.Add(_geminiProviderMenuItem);
        modelSettingsItem.DropDownItems.Add(_ollamaProviderMenuItem);
        modelSettingsItem.DropDownItems.Add(new ToolStripSeparator());
        modelSettingsItem.DropDownItems.Add(CreateSectionLabel("Gemini Model"));
        modelSettingsItem.DropDownItems.Add(_geminiModelTextBox);
        modelSettingsItem.DropDownItems.Add(new ToolStripSeparator());
        modelSettingsItem.DropDownItems.Add(CreateSectionLabel("Ollama Server URL"));
        modelSettingsItem.DropDownItems.Add(_ollamaBaseUrlTextBox);
        modelSettingsItem.DropDownItems.Add(_ollamaReasoningMenuItem);
        modelSettingsItem.DropDownItems.Add(_refreshOllamaModelsMenuItem);
        modelSettingsItem.DropDownItems.Add(_ollamaModelsMenuItem);

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();

        menu.Items.Add(proofreadItem);
        menu.Items.Add(translateItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(modelSettingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        return menu;
    }

    private static ToolStripMenuItem CreateSectionLabel(string text)
    {
        return new ToolStripMenuItem(text) { Enabled = false };
    }

    private static ToolStripTextBox CreateSettingsTextBox()
    {
        return new ToolStripTextBox
        {
            AutoSize = false,
            Width = 260,
        };
    }

    private void BindTextBoxCommit(ToolStripTextBox textBox, Func<Task> commitAsync)
    {
        textBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;

            e.SuppressKeyPress = true;
            e.Handled = true;
            await commitAsync();
        };

        textBox.Leave += async (_, _) => await commitAsync();
    }

    private async Task SetSelectedProviderAsync(LanguageModelProvider provider)
    {
        if (_settings.SelectedProvider == provider)
        {
            if (provider == LanguageModelProvider.Ollama)
                await RefreshOllamaModelsAsync();

            return;
        }

        _settings.SelectedProvider = provider;
        TrySaveSettings();
        UpdateSettingsUi();

        if (provider == LanguageModelProvider.Ollama)
            await RefreshOllamaModelsAsync();
    }

    private Task ApplyGeminiModelAsync()
    {
        var value = (_geminiModelTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            _geminiModelTextBox.Text = _settings.GeminiModel;
            return Task.CompletedTask;
        }

        if (string.Equals(value, _settings.GeminiModel, StringComparison.Ordinal))
            return Task.CompletedTask;

        _settings.GeminiModel = value;
        TrySaveSettings();
        UpdateSettingsUi();
        return Task.CompletedTask;
    }

    private async Task ApplyOllamaBaseUrlAsync()
    {
        var value = (_ollamaBaseUrlTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            _ollamaBaseUrlTextBox.Text = _settings.OllamaBaseUrl;
            return;
        }

        if (string.Equals(value, _settings.OllamaBaseUrl, StringComparison.Ordinal))
            return;

        _settings.OllamaBaseUrl = value;
        TrySaveSettings();
        UpdateSettingsUi();
        await RefreshOllamaModelsAsync();
    }

    private async Task RefreshOllamaModelsAsync()
    {
        var refreshVersion = ++_ollamaRefreshVersion;
        _isRefreshingOllamaModels = true;
        _ollamaModelLoadError = null;
        _ollamaModelNames = Array.Empty<string>();
        _logger.Info($"Refreshing Ollama models from '{_settings.OllamaBaseUrl}'.");
        UpdateSettingsUi();

        try
        {
            var client = _serviceFactory.CreateOllamaApiClient(_settings.OllamaBaseUrl, _settings.OllamaModel);
            var models = await client.ListLocalModelsAsync(CancellationToken.None);
            var modelNames = models
                .Select(x => x.Name?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToArray();

            if (refreshVersion != _ollamaRefreshVersion)
                return;

            _ollamaModelNames = modelNames;
            _hasLoadedOllamaModels = true;

            if (modelNames.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(_settings.OllamaModel))
                {
                    _settings.OllamaModel = null;
                    TrySaveSettings();
                }
            }
            else if (!_settings.OllamaModel.IsIn(modelNames))
            {
                _settings.OllamaModel = modelNames[0];
                TrySaveSettings();
            }

            _logger.Info(
                $"Loaded {modelNames.Length} Ollama model(s) from '{_settings.OllamaBaseUrl}'. SelectedModel='{_settings.OllamaModel ?? "<none>"}'.");
        }
        catch (Exception ex)
        {
            if (refreshVersion != _ollamaRefreshVersion)
                return;

            _ollamaModelLoadError = ex.Message;
            _hasLoadedOllamaModels = true;
            _logger.Error($"Failed to refresh Ollama models from '{_settings.OllamaBaseUrl}'.", ex);
        }
        finally
        {
            if (refreshVersion == _ollamaRefreshVersion)
            {
                _isRefreshingOllamaModels = false;
                UpdateSettingsUi();
            }
        }
    }

    private void UpdateSettingsUi()
    {
        _geminiProviderMenuItem.Checked = _settings.SelectedProvider == LanguageModelProvider.Gemini;
        _ollamaProviderMenuItem.Checked = _settings.SelectedProvider == LanguageModelProvider.Ollama;
        _ollamaReasoningMenuItem.Checked = _settings.OllamaReasoningEnabled;

        if (!string.Equals(_geminiModelTextBox.Text, _settings.GeminiModel, StringComparison.Ordinal))
            _geminiModelTextBox.Text = _settings.GeminiModel;

        if (!string.Equals(_ollamaBaseUrlTextBox.Text, _settings.OllamaBaseUrl, StringComparison.Ordinal))
            _ollamaBaseUrlTextBox.Text = _settings.OllamaBaseUrl;

        _refreshOllamaModelsMenuItem.Enabled = !_isRefreshingOllamaModels;
        UpdateOllamaModelsMenu();
    }

    private void ToggleOllamaReasoning()
    {
        var isEnabled = _ollamaReasoningMenuItem.Checked;
        if (_settings.OllamaReasoningEnabled == isEnabled)
            return;

        _settings.OllamaReasoningEnabled = isEnabled;
        TrySaveSettings();
        _logger.Info($"Ollama reasoning {(isEnabled ? "enabled" : "disabled")}.");
        UpdateSettingsUi();
    }

    private void UpdateOllamaModelsMenu()
    {
        _ollamaModelsMenuItem.DropDownItems.Clear();

        if (_isRefreshingOllamaModels)
        {
            _ollamaModelsMenuItem.DropDownItems.Add(new ToolStripMenuItem("Loading local Ollama models...") { Enabled = false });
            return;
        }

        if (!string.IsNullOrWhiteSpace(_ollamaModelLoadError))
        {
            _ollamaModelsMenuItem.DropDownItems.Add(
                new ToolStripMenuItem($"Unable to load models: {TrimForMenu(_ollamaModelLoadError)}") { Enabled = false });
            return;
        }

        if (!_hasLoadedOllamaModels)
        {
            _ollamaModelsMenuItem.DropDownItems.Add(new ToolStripMenuItem("Refresh to load Ollama models") { Enabled = false });
            return;
        }

        if (_ollamaModelNames.Count == 0)
        {
            _ollamaModelsMenuItem.DropDownItems.Add(new ToolStripMenuItem("No local Ollama models found") { Enabled = false });
            return;
        }

        foreach (var modelName in _ollamaModelNames)
        {
            var menuItem = new ToolStripMenuItem(modelName)
            {
                Checked = string.Equals(_settings.OllamaModel, modelName, StringComparison.Ordinal),
            };

            menuItem.Click += (_, _) =>
            {
                _settings.OllamaModel = modelName;
                TrySaveSettings();
                UpdateSettingsUi();
            };

            _ollamaModelsMenuItem.DropDownItems.Add(menuItem);
        }
    }

    private static string TrimForMenu(string value)
    {
        const int maxLength = 72;
        return value.Length <= maxLength ? value : $"{value[..(maxLength - 3)]}...";
    }

    private async Task PerformClipboardActionAsync(string operationName, Func<string, string> promptFactory)
    {
        if (_isProcessing)
            return;

        _isProcessing = true;
        CancelPendingStatusReset();
        SetTrayState(TrayIconState.Processing);

        var succeeded = false;

        try
        {
            var clipboardText = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
            _logger.Info(
                $"Starting '{operationName}' using {DescribeCurrentProviderForLog()}. ClipboardLength={clipboardText.Length}.");

            var resultText = await ProceedTextAsync(operationName, promptFactory(clipboardText));
            if (!string.IsNullOrWhiteSpace(resultText))
            {
                Clipboard.SetText(resultText);
                succeeded = true;
                _logger.Info($"'{operationName}' succeeded. ResultLength={resultText.Length}.");
            }
        }
        catch (Exception ex)
        {
            succeeded = false;
            _logger.Error($"Unhandled error while executing '{operationName}'.", ex);
        }
        finally
        {
            _isProcessing = false;
        }

        if (!succeeded)
            _logger.Warning($"'{operationName}' failed after all retry attempts.");

        await FlashCompletionStateAsync(succeeded ? TrayIconState.Succeeded : TrayIconState.Failed);
    }

    private async Task<string?> ProceedTextAsync(string operationName, string prompt)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            SetTrayState(TrayIconState.Processing);

            try
            {
                var resultText = _settings.SelectedProvider switch
                {
                    LanguageModelProvider.Gemini => await GenerateGeminiResponseAsync(prompt),
                    LanguageModelProvider.Ollama => await GenerateOllamaResponseAsync(prompt),
                    _ => throw new InvalidOperationException("The selected language model provider is not supported."),
                };

                if (string.IsNullOrWhiteSpace(resultText))
                    throw new InvalidOperationException("The language model returned an empty response.");

                return resultText;
            }
            catch (Exception ex)
            {
                if (attempt >= MaxAttempts)
                {
                    _logger.Error(
                        $"Attempt {attempt}/{MaxAttempts} failed for '{operationName}' using {DescribeCurrentProviderForLog()}. No retries left.",
                        ex);
                    return null;
                }

                _logger.Warning(
                    $"Attempt {attempt}/{MaxAttempts} failed for '{operationName}' using {DescribeCurrentProviderForLog()}. Retrying in {RetryDelay.TotalSeconds:0.#} seconds.",
                    ex);

                SetTrayState(TrayIconState.Retrying, attempt, MaxAttempts);
                await Task.Delay(RetryDelay);
            }
        }

        return null;
    }

    private async Task<string?> GenerateGeminiResponseAsync(string prompt)
    {
        var service = _serviceFactory.CreateGeminiChatCompletionService(_settings);
        var executionSettings = _serviceFactory.CreateGeminiExecutionSettings();
        var result = await service.GetChatMessageContentsAsync(prompt, executionSettings);
        return result?.FirstOrDefault()?.Content;
    }

    private async Task<string?> GenerateOllamaResponseAsync(string prompt)
    {
        var think = _settings.OllamaReasoningEnabled;

        try
        {
            return await GenerateOllamaResponseAsync(prompt, think);
        }
        catch (Exception ex) when (ShouldRetryOllamaWithoutThink(ex))
        {
            _logger.Warning(
                $"Ollama rejected explicit think={think}. Retrying without the think parameter for model '{_settings.OllamaModel ?? "<none>"}'.",
                ex);
            return await GenerateOllamaResponseAsync(prompt, think: null);
        }
    }

    private async Task<string?> GenerateOllamaResponseAsync(string prompt, bool? think)
    {
        var client = _serviceFactory.CreateOllamaApiClient(_settings);
        var chat = new Chat(client)
        {
            Options = new RequestOptions(),
        };

        if (think.HasValue)
            chat.Think = think.Value;

        var builder = new StringBuilder();

        await foreach (var token in chat.SendAsync(prompt, CancellationToken.None))
            builder.Append(token);

        return builder.ToString().Trim();
    }

    private static bool ShouldRetryOllamaWithoutThink(Exception ex)
    {
        var message = ex.Message;
        if (!message.Contains("think", StringComparison.OrdinalIgnoreCase))
            return false;

        return message.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unmarshal", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("bad request", StringComparison.OrdinalIgnoreCase);
    }

    private async Task FlashCompletionStateAsync(TrayIconState state)
    {
        CancelPendingStatusReset();

        var cancellation = new CancellationTokenSource();
        _statusResetCancellation = cancellation;

        SetTrayState(state);

        try
        {
            await Task.Delay(CompletionStateDuration, cancellation.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (!cancellation.IsCancellationRequested && !_isProcessing)
            SetTrayState(TrayIconState.Idle);
    }

    private void CancelPendingStatusReset()
    {
        _statusResetCancellation?.Cancel();
        _statusResetCancellation?.Dispose();
        _statusResetCancellation = null;
    }

    private void SetTrayState(TrayIconState state, int currentAttempt = 0, int maxAttempts = 0)
    {
        _trayIcon.Icon = state switch
        {
            TrayIconState.Idle => _originalIcon,
            TrayIconState.Processing => GetOrCreateIcon("processing", CreateProcessingIcon),
            TrayIconState.Retrying => GetOrCreateIcon($"retry-{currentAttempt}-{maxAttempts}", () => CreateRetryIcon(currentAttempt, maxAttempts)),
            TrayIconState.Succeeded => GetOrCreateIcon("succeeded", () => CreateCompletionIcon(isSuccess: true)),
            TrayIconState.Failed => GetOrCreateIcon("failed", () => CreateCompletionIcon(isSuccess: false)),
            _ => _originalIcon,
        };
    }

    private Icon GetOrCreateIcon(string key, Func<Icon> factory)
    {
        if (_iconCache.TryGetValue(key, out var icon))
            return icon;

        icon = factory();
        _iconCache[key] = icon;
        return icon;
    }

    private Icon CreateProcessingIcon()
    {
        return CreateOverlayIcon(g =>
        {
            using var badgeBrush = new SolidBrush(Color.FromArgb(220, 197, 40, 40));
            using var badgePen = new Pen(Color.White, 2f);
            var badgeBounds = CreateCircleBounds(32, 18);
            g.FillEllipse(badgeBrush, badgeBounds);
            g.DrawEllipse(badgePen, badgeBounds);
        });
    }

    private Icon CreateRetryIcon(int currentAttempt, int maxAttempts)
    {
        return CreateOverlayIcon(g =>
        {
            var badgeBounds = new RectangleF(1, 18, 30, 12);
            using var path = CreateRoundedRectanglePath(badgeBounds, 5f);
            using var badgeBrush = new SolidBrush(Color.FromArgb(232, 183, 28, 28));
            using var badgePen = new Pen(Color.White, 1.4f);
            using var font = new Font(FontFamily.GenericSansSerif, 6.5f, FontStyle.Bold, GraphicsUnit.Point);
            using var textBrush = new SolidBrush(Color.White);
            using var stringFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };

            g.FillPath(badgeBrush, path);
            g.DrawPath(badgePen, path);
            g.DrawString($"{currentAttempt}/{maxAttempts}", font, textBrush, badgeBounds, stringFormat);
        });
    }

    private Icon CreateCompletionIcon(bool isSuccess)
    {
        return CreateOverlayIcon(g =>
        {
            var backgroundColor = isSuccess ? Color.FromArgb(230, 29, 128, 73) : Color.FromArgb(230, 183, 28, 28);
            using var badgeBrush = new SolidBrush(backgroundColor);
            using var badgePen = new Pen(Color.White, 2f);
            using var symbolPen = new Pen(Color.White, 3f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };

            var badgeBounds = CreateCircleBounds(32, 20);
            g.FillEllipse(badgeBrush, badgeBounds);
            g.DrawEllipse(badgePen, badgeBounds);

            if (isSuccess)
            {
                g.DrawLines(symbolPen, new[]
                {
                    new PointF(10.5f, 18f),
                    new PointF(14.5f, 22f),
                    new PointF(21.5f, 11.5f),
                });
            }
            else
            {
                g.DrawLine(symbolPen, 11f, 11f, 21f, 21f);
                g.DrawLine(symbolPen, 21f, 11f, 11f, 21f);
            }
        });
    }

    private Icon CreateOverlayIcon(Action<Graphics> drawOverlay)
    {
        const int size = 32;

        using var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(Color.Transparent);
        graphics.DrawIcon(_originalIcon, new Rectangle(0, 0, size, size));
        drawOverlay(graphics);

        var iconHandle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(iconHandle).Clone();
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    private static RectangleF CreateCircleBounds(float canvasSize, float diameter)
    {
        const float margin = 1f;
        return new RectangleF(canvasSize - diameter - margin, canvasSize - diameter - margin, diameter, diameter);
    }

    private static GraphicsPath CreateRoundedRectanglePath(RectangleF bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }

    private void TrySaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            // Keep the current in-memory settings for this session.
            _logger.Error("Failed to persist settings. The current values will remain active only for this session.", ex);
        }
    }

    private string DescribeCurrentProviderForLog()
    {
        return _settings.SelectedProvider switch
        {
            LanguageModelProvider.Gemini =>
                $"provider=Gemini, model='{_settings.GeminiModel}'",
            LanguageModelProvider.Ollama =>
                $"provider=Ollama, model='{_settings.OllamaModel ?? "<none>"}', url='{_settings.OllamaBaseUrl}', reasoning={_settings.OllamaReasoningEnabled}",
            _ =>
                $"provider={_settings.SelectedProvider}",
        };
    }

    private string BuildProofreadPrompt(string clipboardText)
    {
        return $"""
                <Role>
                    The elite proofreader for English texts, specializing in computer and technology-related content.
                </Role>

                <NegativeInstructions>
                    <Instruction>
                        <Body>
                            Do not provide introductions, conclusions, or meta-comments. Only output the corrected text.
                        </Body>
                        <ExampleTextToAvoid>
                            "Here is the edited version of your text:"
                        </ExampleTextToAvoid>
                    </Instruction>
                    <Instruction>
                        <Body>
                            Do not explain your reasoning or editing process.
                        </Body>
                    </Instruction>
                    <Instruction>
                        <Body>
                            Do not refer to yourself or your role.
                        </Body>
                    </Instruction>
                </NegativeInstructions>

                <Guidelines>
                    1. Correct all grammar, spelling, and punctuation errors.
                    2. If a phrase would naturally be said differently by a native speaker, rewrite it while preserving the original meaning and tone.
                    3. Maintain the text's structure, spacing, indentation, and formatting. Do not introduce additional line breaks or remove existing ones, unless absolutely necessary for clarity.
                    4. Use simple, everyday vocabulary if possible, unless the context calls for specialized terminology.
                    5. If something in the text is unclear or ambiguous, you may ask a concise clarification question.
                    6. Provide only the corrected text and nothing else.
                </Guidelines>

                <Process>
                    1. Read and analyze the entire source text thoroughly.
                    2. Correct any grammatical, spelling, or stylistic errors.
                    3. Ensure the resulting text flows naturally as if written by a native English speaker.
                </Process>

                <Format>
                    - Do not enclose your output in quotes or add extra commentary.
                    - Preserve any markdown, code blocks, or special formatting if present.
                    - Output only the final corrected text, with no additional explanations.
                </Format>

                <Example>
                    <Input>
                        so i has start working on new program 
                        it for sure fix all bug, said me friend 
                        i not so sure about that
                    </Input>
                    <Output>
                        So I started working on a new program.
                        “It will definitely fix all the bugs,” my friend told me.
                        I’m not so sure about that.
                    </Output>
                </Example>

                <text_to_edit>
                    {clipboardText}
                </text_to_edit>
                """;
    }

    private string BuildTranslatePrompt(string clipboardText)
    {
        return $"""
                <Role>
                    The elite translator for computer and technology-related texts.
                </Role>

                <NegativeInstructions>
                    <Instruction>
                        <Body>
                            Do not provide introductions, conclusions, or meta-comments. Only output the translated text.
                        </Body>
                        <ExampleTextToAvoid>
                            "Here's the translation of the given text:"
                        </ExampleTextToAvoid>
                    </Instruction>
                    <Instruction>
                        <Body>
                            Do not explain your reasoning or translation process.
                        </Body>
                    </Instruction>
                    <Instruction>
                        <Body>
                            Do not refer to yourself or your role.
                        </Body>
                    </Instruction>
                </NegativeInstructions>

                <Guidelines>
                    1. Translate into clear, simple English.
                    2. Preserve the original meaning and tone.
                    3. Use everyday, common vocabulary unless the source uses specialized terminology.
                    4. If the source text is ambiguous, reformulate to maintain clarity.
                    5. If absolutely necessary, ask concise clarification questions.
                    6. Otherwise, provide only the translation and nothing else.
                </Guidelines>

                <Process>
                    1. Read and analyze the entire source text thoroughly.
                    2. Produce a confident, context-aware translation.
                </Process>

                <Format>
                    - Use code blocks or markdown lists only if relevant to the text (e.g., code samples).
                    - Use GitHub-flavored Markdown when formatting is necessary.
                    - Deliver the final result as a ready-to-use translation without additional commentary.
                </Format>

                <Example>
                    <Input>
                        Слушай, ты не поверишь, что со мной вчера приключилось! Короче, сижу я, работаю над своим новым проектом, и тут бац - комп зависает намертво. Ну, думаю, приехали. Перезагружаю, а он даже до загрузки винды не доходит. Я уж было решил, что накрылась моя материнка или проц, но потом вспомнил про недавнее обновление драйверов. Залез в BIOS, сбросил настройки, и - о чудо! - всё заработало как часы. Вот так одна кнопка спасла меня от похода в сервис и потери кучи денег. Теперь я первым делом всегда проверяю BIOS, если что-то идёт не так. Кстати, ты backup данных регулярно делаешь? А то мало ли что...
                    </Input>
                    <Output>
                        You won't believe what happened to me yesterday! I was sitting there, working on my new project, and then—bam! My computer froze completely. I thought, "Great, that's it." I restarted it, but it wouldn't even reach the Windows loading screen. I was sure my motherboard or CPU had died, but then I remembered I'd recently updated my drivers. I went into the BIOS, reset the settings, and—miracle!—it all worked perfectly. That one button saved me from a trip to the repair shop and a lot of money. Now, I always check the BIOS first if something goes wrong. By the way, do you back up your data regularly? You never know what might happen...
                    </Output>
                </Example>

                <text_to_translate>{clipboardText}</text_to_translate>
                """;
    }

    private void ExitApplication()
    {
        CancelPendingStatusReset();
        _logger.Info("Application shutdown requested.");

        UnregisterHotKey(_hotKeyWindow.Handle, HotkeyIdProofread);
        UnregisterHotKey(_hotKeyWindow.Handle, HotkeyIdTranslate);
        _hotKeyWindow.Dispose();

        _trayIcon.Visible = false;
        _trayIcon.Dispose();

        foreach (var icon in _iconCache.Values)
            icon.Dispose();

        _originalIcon.Dispose();

        Application.Exit();
    }
}

internal static class StringExtensions
{
    public static bool IsIn(this string? value, IEnumerable<string> candidates)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return candidates.Any(candidate => string.Equals(value, candidate, StringComparison.Ordinal));
    }
}
