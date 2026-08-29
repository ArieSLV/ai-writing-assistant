# Releasing

Releases are built by the Release workflow from a semantic-version tag.

## Checklist

1. Confirm CI is green on main.
2. Update CHANGELOG.md and the version in AiWritingAssistant/AiWritingAssistant.csproj.
3. Run the Release test suite locally.
4. Create and push an annotated tag such as v1.1.0.
5. Verify the Release workflow and download the archive and checksum from GitHub.
6. Smoke-test the extracted application on Windows.

The workflow creates a self-contained win-x64 archive, generates its SHA-256 checksum, and publishes both to GitHub Releases.
