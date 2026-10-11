# Quantum Vault — Privacy Policy

## Summary

Quantum Vault collects no data from you. It has no network component.

## What Data Quantum Vault Stores

Quantum Vault stores only what you explicitly put into it:

- Your notes, snippets, and commands — encrypted on your local machine
- Your vault configuration (storage paths, settings)

All data is stored in: `%APPDATA%\Quantum-suite-org\QuantumVault\`

## What Data Quantum Vault Does NOT Collect

- No telemetry
- No crash reports sent anywhere
- No usage analytics
- No account information
- No network requests of any kind
- No third-party SDKs that collect data

## Encryption

- **Notes** are encrypted with AES-256-CBC. The key is derived from your
  master password using PBKDF2-SHA256 (200,000 iterations). The key is
  never written to disk.
- **Snippets and commands** are encrypted with AES-256-CBC using a randomly
  generated key stored in `vault.key` in your data directory.
- **DPAPI-protected notes** are additionally bound to your Windows user
  account using the Windows Data Protection API.

## Logs

Quantum Vault writes a local log file at:
`%APPDATA%\Quantum-suite-org\QuantumVault\logs\qvault.log`

Logs contain:
- Timestamps of operations (init, shutdown, save, load)
- Entry counts and error codes

Logs do **not** contain:
- Note, snippet, or command content
- Passwords or key material
- Personal information

## Exports

When you export your vault (Settings → Export, or `qvault export`), the
resulting JSON file contains **plaintext content**. This file is written
only where you choose to save it. Keep it secure.

## Third Parties

Quantum Vault has no third-party dependencies that handle your data.
It uses only Windows operating system APIs (CNG, DPAPI) for cryptography.

## Your Control

You have full control over your data:
- Data directory location can be changed
- Export/import at any time in your chosen format
- Delete individual entries or the entire vault
- The application can be uninstalled cleanly; your data directory persists
  and can be manually deleted from `%APPDATA%\Quantum-suite-org\QuantumVault\`
