# Security policy

## Supported versions

| Version | Supported |
|---|---|
| Latest release | Yes |
| Older releases | Best effort |

## Report a vulnerability

Do not open a public issue for a suspected vulnerability or exposed credential. Use GitHub's private vulnerability reporting feature from the repository **Security** tab and include:

- the affected version or commit;
- reproduction steps or a proof of concept;
- expected impact;
- any suggested remediation.

You should receive an acknowledgement within seven days. Please allow reasonable time for investigation and a coordinated fix before public disclosure.

## Credential safety

The application expects Google API keys in environment variables. Never include real credentials in issues, pull requests, screenshots, logs, recordings, settings files, or test fixtures. If a key is exposed, revoke or rotate it immediately with its provider.
