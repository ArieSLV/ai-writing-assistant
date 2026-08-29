param(
    [ValidateRange(1, 600)]
    [int] $DurationSeconds = 5
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$captureDll = Join-Path (
    [System.IO.Path]::GetTempPath()) (
    'AiWritingAssistant\voice-capture-build\bin\VoiceCaptureProof\debug\VoiceCaptureProof.dll')

if (-not (Test-Path -LiteralPath $captureDll)) {
    throw "Capture proof executable is missing: $captureDll"
}

$dotnetPath = (Get-Command dotnet).Source
$captureOutput = $null
$captureError = $null
$canonicalPath = $null

$form = [System.Windows.Forms.Form]::new()
$form.Text = 'Voice capture proof'
$form.ClientSize = [System.Drawing.Size]::new(520, 190)
$form.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.ShowInTaskbar = $false
$form.TopMost = $true
$form.BackColor = [System.Drawing.Color]::FromArgb(31, 41, 55)

$statusLabel = [System.Windows.Forms.Label]::new()
$statusLabel.AutoSize = $false
$statusLabel.Location = [System.Drawing.Point]::new(20, 18)
$statusLabel.Size = [System.Drawing.Size]::new(480, 54)
$statusLabel.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
$statusLabel.Font = [System.Drawing.Font]::new('Segoe UI Semibold', 20)
$statusLabel.ForeColor = [System.Drawing.Color]::White
$statusLabel.Text = 'Запись начнётся через 3…'
$form.Controls.Add($statusLabel)

$timerLabel = [System.Windows.Forms.Label]::new()
$timerLabel.AutoSize = $false
$timerLabel.Location = [System.Drawing.Point]::new(20, 74)
$timerLabel.Size = [System.Drawing.Size]::new(480, 32)
$timerLabel.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
$timerLabel.Font = [System.Drawing.Font]::new('Consolas', 16)
$timerLabel.ForeColor = [System.Drawing.Color]::FromArgb(209, 213, 219)
$timerLabel.Text = 'Приготовьтесь'
$form.Controls.Add($timerLabel)

$progress = [System.Windows.Forms.ProgressBar]::new()
$progress.Location = [System.Drawing.Point]::new(40, 120)
$progress.Size = [System.Drawing.Size]::new(440, 24)
$progress.Minimum = 0
$progress.Maximum = $DurationSeconds * 10
$progress.Value = 0
$form.Controls.Add($progress)

$hintLabel = [System.Windows.Forms.Label]::new()
$hintLabel.AutoSize = $false
$hintLabel.Location = [System.Drawing.Point]::new(20, 151)
$hintLabel.Size = [System.Drawing.Size]::new(480, 25)
$hintLabel.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
$hintLabel.Font = [System.Drawing.Font]::new('Segoe UI', 10)
$hintLabel.ForeColor = [System.Drawing.Color]::FromArgb(156, 163, 175)
$hintLabel.Text = 'Дождитесь красного состояния и говорите только тогда.'
$form.Controls.Add($hintLabel)

$state = @{
    Phase = 'Countdown'
    Countdown = [System.Diagnostics.Stopwatch]::StartNew()
    Recording = $null
    Finished = $null
    Process = $null
    StdoutTask = $null
    StderrTask = $null
}

$uiTimer = [System.Windows.Forms.Timer]::new()
$uiTimer.Interval = 100

$uiTimer.Add_Tick({
    try {
        if ($state.Phase -eq 'Countdown') {
            $remaining = [Math]::Ceiling(3.0 - $state.Countdown.Elapsed.TotalSeconds)
            if ($remaining -gt 0) {
                $statusLabel.Text = "Запись начнётся через $remaining…"
                return
            }

            $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
            $startInfo.FileName = $dotnetPath
            [void] $startInfo.ArgumentList.Add($captureDll)
            [void] $startInfo.ArgumentList.Add('record')
            [void] $startInfo.ArgumentList.Add($DurationSeconds.ToString())
            $startInfo.UseShellExecute = $false
            $startInfo.CreateNoWindow = $true
            $startInfo.RedirectStandardOutput = $true
            $startInfo.RedirectStandardError = $true

            $state.Process = [System.Diagnostics.Process]::Start($startInfo)
            $state.StdoutTask = $state.Process.StandardOutput.ReadToEndAsync()
            $state.StderrTask = $state.Process.StandardError.ReadToEndAsync()
            $state.Recording = [System.Diagnostics.Stopwatch]::StartNew()
            $state.Phase = 'Recording'

            $form.BackColor = [System.Drawing.Color]::FromArgb(127, 29, 29)
            $statusLabel.Text = '●  ИДЁТ ЗАПИСЬ'
            $timerLabel.Text = "00:00 / 00:$($DurationSeconds.ToString('00'))"
            $hintLabel.Text = 'Говорите сейчас'
            [System.Media.SystemSounds]::Asterisk.Play()
            return
        }

        if ($state.Phase -eq 'Recording') {
            $elapsed = $state.Recording.Elapsed
            $shownSeconds = [Math]::Min($elapsed.TotalSeconds, $DurationSeconds)
            $timerLabel.Text = "00:$([Math]::Floor($shownSeconds).ToString('00')) / 00:$($DurationSeconds.ToString('00'))"
            $progress.Value = [Math]::Min(
                $progress.Maximum,
                [Math]::Floor($shownSeconds * 10))

            if (-not $state.Process.HasExited) {
                return
            }

            $state.Process.WaitForExit()
            $script:captureOutput = $state.StdoutTask.GetAwaiter().GetResult()
            $script:captureError = $state.StderrTask.GetAwaiter().GetResult()

            if ($state.Process.ExitCode -eq 0) {
                $form.BackColor = [System.Drawing.Color]::FromArgb(20, 83, 45)
                $statusLabel.Text = '✓  ЗАПИСЬ ЗАВЕРШЕНА'
                $timerLabel.Text = "00:$($DurationSeconds.ToString('00'))"
                $hintLabel.Text = 'Файл проверяется и будет удалён'
                $progress.Value = $progress.Maximum
                [System.Media.SystemSounds]::Exclamation.Play()
            }
            else {
                $form.BackColor = [System.Drawing.Color]::FromArgb(127, 29, 29)
                $statusLabel.Text = '✕  ОШИБКА ЗАПИСИ'
                $timerLabel.Text = "Exit code $($state.Process.ExitCode)"
                $hintLabel.Text = 'Подробность вернётся в proof log'
                [System.Media.SystemSounds]::Hand.Play()
            }

            $state.Finished = [System.Diagnostics.Stopwatch]::StartNew()
            $state.Phase = 'Finished'
            return
        }

        if ($state.Phase -eq 'Finished' -and $state.Finished.Elapsed.TotalSeconds -ge 2.0) {
            $uiTimer.Stop()
            $form.Close()
        }
    }
    catch {
        $script:captureError = $_.Exception.Message
        $uiTimer.Stop()
        $form.Close()
    }
})

$form.Add_Shown({ $uiTimer.Start() })

try {
    [System.Windows.Forms.Application]::Run($form)

    if (-not [string]::IsNullOrWhiteSpace($captureOutput)) {
        $captureOutput.TrimEnd() -split "`r?`n" | ForEach-Object { $_ }
        $pathLine = $captureOutput.TrimEnd() -split "`r?`n" |
            Where-Object { $_ -like 'WAV_PATH=*' } |
            Select-Object -Last 1

        if (-not [string]::IsNullOrWhiteSpace($pathLine)) {
            $canonicalPath = $pathLine.Substring('WAV_PATH='.Length)
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($captureError)) {
        "CAPTURE_ERROR_PRESENT=True"
    }
}
finally {
    $uiTimer.Dispose()
    $form.Dispose()

    if ($state.Process) {
        $state.Process.Dispose()
    }

    if (-not [string]::IsNullOrWhiteSpace($canonicalPath) -and
        (Test-Path -LiteralPath $canonicalPath)) {
        Remove-Item -LiteralPath $canonicalPath -Force
    }

    "CANONICAL_DELETED=$([string]::IsNullOrWhiteSpace($canonicalPath) -or -not (Test-Path -LiteralPath $canonicalPath))"
}
