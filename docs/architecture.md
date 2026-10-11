# Quantum Vault — Architecture

## Overview

Quantum Vault is a three-layer application:

```
┌─────────────────────────────────────────────┐
│  GUI (C# / WPF / .NET Framework 4.8)        │
│  MainWindow, EntryEditorPanel, SettingsPanel │
│  P/Invoke → C ABI                           │
└────────────────────┬────────────────────────┘
                     │ C ABI (qvault_core.dll)
┌────────────────────▼────────────────────────┐
│  Core Library (C++ / Windows CNG)            │
│  AES-256-CBC · PBKDF2-SHA256 · DPAPI        │
│  JSON persistence · File locking            │
│  Search · CRUD · Import/Export · Backup     │
└────────────────────┬────────────────────────┘
                     │ ctypes C ABI
┌────────────────────▼────────────────────────┐
│  CLI (Python 3 / ctypes)                    │
│  qvault list/search/add/show/copy/export... │
└─────────────────────────────────────────────┘
```

## Component Details

### qvault_core.dll (C++)

- **Language**: C++17, MSVC cl.exe
- **Dependencies**: Windows only — bcrypt.dll (CNG), crypt32.dll (DPAPI), shell32.dll, kernel32.dll
- **Crypto**: AES-256-CBC via BCryptEncrypt/BCryptDecrypt; PBKDF2-SHA256 via BCryptKeyDerivation; DPAPI via CryptProtectData
- **Persistence**: Single `vault.json` file, written atomically via MoveFileEx
- **Locking**: Exclusive file lock (`vault.lock`) acquired on init; advisory — CLI and GUI can coexist but writes are serialized at the OS level
- **Thread safety**: Single global mutex protects all state; safe for GUI + CLI concurrent reads

### GUI (C# WPF)

- **Framework**: .NET Framework 4.8 (inbox on Windows 10/11)
- **Binding**: P/Invoke into qvault_core.dll C ABI
- **UI approach**: Pure code-behind (no MVVM framework dependency) with ResourceDictionary theming
- **Controls**: All custom-built in code — no third-party UI libraries

### CLI (Python)

- **Runtime**: Python 3.8+ (standard library only)
- **Binding**: ctypes into qvault_core.dll
- **Distribution**: pip-installable package; bundled into qvault.exe via PyInstaller for WinGet release
- **Output**: Human-readable by default; `--json` for machine-readable (PowerShell-friendly)

## Data Flow

```
User types password
      │
      ▼
PBKDF2-SHA256(password, salt, 200000 iter) → 64 bytes
      │           │
      │           └─ [32:64] stored as verify token in vault.json
      └─ [0:32] used as AES-256-CBC key (never stored)
                    │
                    ▼
            BCryptEncrypt(key, IV, plaintext) → IV‖ciphertext
                    │
              [optional] CryptProtectData wraps ciphertext (DPAPI)
                    │
              base64 → stored in vault.json "content" field
```

## File Layout (data directory)

```
%APPDATA%\Quantum-suite-org\QuantumVault\
  vault.json      — encrypted entry store
  vault.key       — random AES key for snippets/commands
  vault.lock      — advisory file lock (auto-deleted on close)
  logs\
    qvault.log    — application log (no content logged)
    qvault_gui.log — GUI-layer log
  backups\
    vault_backup_YYYYMMDD_HHMMSS.json — timestamped backups
```

## Security Model

- Notes: AES-256-CBC, key derived from user password via PBKDF2-SHA256 (200,000 iterations). Key never written to disk.
- Snippets/Commands: AES-256-CBC, random key in `vault.key`. Transparent to user.
- DPAPI: Optional additional layer wrapping the AES ciphertext. Binds data to Windows user account + machine.
- Export: Decrypts to plaintext. User warned explicitly.
- Logging: Only metadata logged (entry IDs, timestamps, counts). Content never logged.

## Limitations (v0.1.0)

- No multi-user support
- No sync or cloud backup (by design)
- `vault.key` protects snippets/commands from casual inspection but is not passphrase-protected
- DPAPI protection is per-user/machine; exported data loses DPAPI binding
