# Contributing

Thank you for helping improve AI Writing Assistant. Small, focused changes with a clear user benefit are easiest to review.

## Before you start

- Search existing issues and pull requests.
- Open an issue before starting a large behavior or architecture change.
- Never include API keys, transcripts, recordings, logs with private data, or generated build output.

## Development setup

Development requires Windows and the .NET 10 SDK.

~~~powershell
git clone https://github.com/ArieSLV/ai-writing-assistant.git
cd ai-writing-assistant
dotnet restore AiWritingAssistant.sln
dotnet test AiWritingAssistant.sln -c Release --no-restore
~~~

Google credentials are not required for the automated tests. Manual API testing must use environment variables described in the README.

## Pull requests

1. Create a branch from main.
2. Keep the change focused and update tests and documentation with behavior changes.
3. Run the Release test suite locally.
4. Use a concise imperative commit subject.
5. Complete the pull request template and call out privacy, credential, clipboard, audio, or accessibility impacts.

Pull requests must not weaken credential separation, log user content, or leave temporary audio behind after a completed operation.
