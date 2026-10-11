# Quantum Vault

## Install

```powershell
winget install Quantum-suite-org.QuantumVault
```

Or download `QuantumVault-0.1.2-Setup.exe` from the [latest release](https://github.com/Quantum-Suite-org/quantum-vault/releases/latest).

Requires Windows 10/11 (x64) and the Microsoft Edge WebView2 Runtime. Without WebView2 the app opens its classic window instead.

**Lightweight secure local note and snippet manager — first Quantum Suite application.**

> v0.1.2 · Windows 10/11 · Offline-first · No accounts · MIT License

---

## What is Quantum Vault?

Quantum Vault is a native Windows desktop application for storing and searching your:

- **Notes** — encrypted with your master password (AES-256-CBC + PBKDF2-SHA256)
- **Snippets** — code fragments with language tagging and one-click copy
- **Commands** — shell commands you want to remember and copy quickly

Your notes, snippets and commands are stored on your machine. Quantum Vault has no cloud sync, no accounts and no telemetry. It makes network requests only for the optional features described under Privacy & Security.

---

## Installation

### WinGet (recommended, once published)
```powershell
winget install Quantum-suite-org.QuantumVault
```

### Manual
1. Download `QuantumVault-0.1.2-Setup.exe` from Releases
2. Run the installer
3. Launch **Quantum Vault** from Start Menu

### Requirements
- Windows 10 or 11 (x64)
- .NET Framework 4.8 (pre-installed on Windows 10 1903+)
- Python 3.8+ (for CLI, installed automatically by the setup)

---

## First Launch

1. Open Quantum Vault
2. The welcome screen explains the basics — click **Get Started**
3. Press **Ctrl+N** or click **+ New** to create your first entry
4. Choose a type: **Note**, **Snippet**, or **Command**
5. For notes, you will be prompted to set a master password on first use

---

## GUI Usage

### Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+N` | New entry |
| `Ctrl+F` | Focus search |
| `Ctrl+S` | Save current entry |
| `Ctrl+D` | Duplicate entry |
| `Ctrl+Delete` | Delete entry |
| `Ctrl+E` | Export vault |
| `Ctrl+,` | Open settings |
| `Escape` | Clear search |
| `↓` | Move focus to list from search |

### Sidebar Sections

| Section | Contents |
|---|---|
| Notes | All note entries |
| Snippets | All snippet entries |
| Commands | All command entries |
| Favorites | Starred entries |
| Recently Edited | Last 50 modified |
| All Entries | Everything |

### Notes Encryption

Notes are encrypted with AES-256-CBC using a key derived from your master password via PBKDF2-SHA256 (200,000 iterations). The key is never stored on disk. You must enter your password once per session to unlock notes.

Optionally check **DPAPI** in the note editor to additionally bind the note to your Windows user account. DPAPI-protected notes cannot be decrypted on another machine or Windows user account.

### Snippets and Commands

Snippets and commands are encrypted transparently with a randomly generated AES key stored in `vault.key` in your data directory. No password required — they are protected from casual inspection but readable by anyone with access to your `vault.key` file.

---

## CLI Usage

```powershell
qvault --help
qvault version
qvault list
qvault list --type snippet --json
qvault search "docker run"
qvault show abc123ef
qvault add snippet "Restart nginx" --content "systemctl restart nginx" --language bash
qvault add note "Passwords" --stdin   # reads from stdin
qvault copy abc123ef                  # copies content to clipboard
qvault export --path backup.json
qvault import backup.json
qvault backup
qvault stats
```

See [docs/cli-reference.md](docs/cli-reference.md) for full documentation.

---

## Storage

Data directory: `%APPDATA%\Quantum-suite-org\QuantumVault\`

| File | Purpose |
|---|---|
| `vault.json` | Encrypted entry store |
| `vault.key` | AES key for snippets/commands |
| `vault.lock` | Advisory file lock |
| `logs/qvault.log` | Core library log |
| `logs/qvault_gui.log` | GUI log |
| `backups/` | Timestamped backups |

See [docs/storage-format.md](docs/storage-format.md) for full schema documentation.

---

## Import / Export

### Export
```powershell
qvault export --path my_backup.json
```
Or: **Settings → Export Vault (JSON)**

Exports all entries as plaintext JSON. **Keep this file secure** — it contains unencrypted content.

### Import
```powershell
qvault import my_backup.json          # merge with existing
qvault import my_backup.json --replace  # replace all data
```
Or: **Settings → Import from JSON**

Import validates each entry individually. Malformed entries are skipped and counted separately — your existing data is never silently deleted.

### Backup
```powershell
qvault backup
```
Creates a timestamped copy of the encrypted `vault.json` in the `backups/` directory. To restore, copy a backup file over `vault.json` (requires the same password that was used when the backup was created).

---

## Development Setup

### Requirements
- Visual Studio 2022+ with C++ workload
- .NET Framework 4.8 SDK
- Python 3.8+
- PowerShell 5+

### Build C++ Core
```powershell
.\scripts\build_core.ps1
```

### Build GUI
```powershell
cd src\gui
dotnet build -c Release
```

### Install CLI (development)
```powershell
cd src\cli
pip install -e .
```

### Run Tests
```powershell
# First build the core DLL
.\scripts\build_core.ps1

# Copy DLL to CLI package directory for tests
Copy-Item build\bin\qvault_core.dll src\cli\qvault\

# Run tests
cd src\tests
pip install pytest
pytest -v
```

---

## Building a Release

See [docs/release.md](docs/release.md) for the full release checklist including:
- Building the installer
- Calculating SHA256
- Updating the WinGet manifest
- Submitting to winget-pkgs

---

## Architecture

See [docs/architecture.md](docs/architecture.md).

---

## Privacy & Security

- **No telemetry.** Quantum Vault sends no analytics or usage data. Vault contents are never sent automatically. Network requests happen only when you use features that need them: downloading the optional assistant model from Hugging Face, the assistant's web search and page-fetch tools (which send the query or URL to the websites involved), and checks made by Windows, WebView2 or winget. Avoid putting secrets into assistant prompts.
- **No accounts.** No registration or login is required.
- **Local encryption.** Notes use AES-256-CBC with PBKDF2-SHA256 (200,000 iterations).
- **Key never stored.** Your notes password is never written to disk.
- **Logs contain no content.** Only metadata (timestamps, entry counts) is logged.

See [PRIVACY.md](PRIVACY.md) for full details.

---

## License

MIT — see [LICENSE](LICENSE).

Quantum Vault is distributed under the MIT License. The MIT License covers the source code only. It does not grant rights in the names or branding. Copyright in the software, its documentation and artwork is claimed by Quantum Suite to the extent it subsists under applicable law. See [EULA.txt](EULA.txt) for the terms that apply to the compiled software and branding, and [PROVENANCE.md](PROVENANCE.md) for the provenance record.

Quantum Vault is licensed independently from the broader Quantum Suite ecosystem.

