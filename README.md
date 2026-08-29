# AI Writing Assistant

Windows tray application for proofreading, translation, and voice dictation. Text actions and voice transcription use separate Google API credentials so they can be billed and managed independently.

## Hotkeys

| Hotkey | Action |
|---|---|
| `Ctrl+Shift+D` | Proofread clipboard text |
| `Ctrl+Shift+F` | Translate clipboard text to English |
| `Ctrl+Shift+G` | Start or stop Voice Dictation |

Voice Dictation shows a movable English waveform window only while the microphone is recording. Press `Ctrl+Shift+G` again to stop. The window disappears immediately; upload, transcription, success, and failure are then shown through the tray icon and tooltip. A successful transcript replaces the clipboard contents.

The tray menu groups `Proofread`, `Translate`, and `Text Model Settings` together. Voice Dictation is a separate block because it always uses Google Transcribe and its separate Voice credential/model configuration.

## Credentials

Set credentials outside the repository:

- `AI_WRITING_ASSISTANT_GOOGLE_API_KEY` — Proofread and Translate;
- `AI_WRITING_ASSISTANT_GOOGLE_VOICE_API_KEY` — Voice Dictation only.

Voice never falls back to the Text key, and Text actions never use the Voice key. Restart the application after changing a user-level environment variable.

## Voice behavior and privacy

- Uses the Windows default recording device.
- Stops and transcribes automatically after at most 10 minutes.
- Normalizes captured audio to a WAV accepted by Gemini Transcribe.
- Attempts to delete both the local temporary recording and the uploaded Google file on every terminal path.
- Does not write API keys, transcript text, raw API responses, or audio content to application logs.
- Preserves the existing clipboard when recording is cancelled or transcription fails.

Google API usage is billable to the project associated with the Voice key. An empty balance, exhausted quota, or invalid credential is reported as a failed Voice action in the tray.

## Configuration

`AiWritingAssistant.settings.json` next to the installed executable stores non-secret settings such as provider/model selection. The default voice model is `gemini-3.5-transcribe`. API keys must remain in environment variables and must not be added to this file.

## Build and test

~~~powershell
dotnet test AiWritingAssistant.Tests\AiWritingAssistant.Tests.csproj -c Release
dotnet publish AiWritingAssistant\AiWritingAssistant.csproj -c Release -o <publish-directory>
~~~

## Troubleshooting

- If a hotkey does nothing, exit another running copy of the application and restart this one; global hotkeys can be owned by only one process.
- If Voice Dictation reports that its key is missing, verify `AI_WRITING_ASSISTANT_GOOGLE_VOICE_API_KEY` at the user or process level and restart.
- If transcription reports quota or rate-limit failure, verify the balance and quota of the personal Google project used by the Voice key.
- If recording cannot start, verify that Windows has a default input device and permits desktop microphone access.
