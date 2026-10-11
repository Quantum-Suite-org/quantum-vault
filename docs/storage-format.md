# Quantum Vault — Storage Format

## vault.json

The primary data file. Stored at:
`%APPDATA%\Quantum-suite-org\QuantumVault\vault.json`

### Schema

```json
{
  "version": 1,
  "qvault_version": "0.1.0",
  "notes_has_password": true,
  "notes_salt": "<base64 32-byte random salt>",
  "notes_verify": "<base64 32-byte PBKDF2 verification token>",
  "entries": [
    {
      "id":          "<UUID v4>",
      "type":        0,
      "title":       "My Note",
      "content":     "<base64(IV || AES-256-CBC ciphertext)>",
      "tags":        "work,important",
      "description": "Short description",
      "language":    "",
      "is_favorite": false,
      "is_dpapi":    false,
      "created_at":  1700000000,
      "updated_at":  1700001000
    }
  ]
}
```

### Entry Types

| Value | Name    | Description                        |
|-------|---------|------------------------------------|
| 0     | note    | Long-form text, password-encrypted |
| 1     | snippet | Code fragment, key-encrypted       |
| 2     | command | Shell command, key-encrypted       |

### Content Encoding

All content is encrypted at rest:

- **Notes**: `AES-256-CBC(PBKDF2_key, content)` → base64
- **Notes with DPAPI**: `"dpapi:" + base64(DPAPI_wrap(AES-256-CBC(PBKDF2_key, content)))`
- **Snippets/Commands**: `AES-256-CBC(random_key, content)` → base64

IV (16 bytes) is prepended to the ciphertext before base64 encoding.
PKCS7 padding is used.

### Key Files

#### vault.key
Raw 32-byte random AES key for snippets/commands. Binary, no encoding.

#### notes_salt / notes_verify
Stored inside `vault.json`:
- `notes_salt`: 32 random bytes, base64
- `notes_verify`: `PBKDF2-SHA256(password, salt, 200000)[32:64]`, base64
- Used to verify password on subsequent sessions without storing the key

## Export Format

Export produces a separate JSON file:

```json
{
  "format": "qvault-export",
  "format_version": 1,
  "qvault_version": "0.1.0",
  "exported_at": 1700000000,
  "note": "Content in this export is PLAINTEXT. Keep this file secure.",
  "entries": [
    {
      "id":          "<UUID>",
      "type":        1,
      "type_name":   "snippet",
      "title":       "My Snippet",
      "content":     "echo hello",
      "tags":        "bash",
      "description": "",
      "language":    "bash",
      "is_favorite": false,
      "created_at":  1700000000,
      "updated_at":  1700000000
    }
  ]
}
```

**Note content in exports is plaintext.** The export is intended for backup and migration only. Keep export files in a secure location.

## Atomic Writes

The vault file is written atomically:
1. Content serialized to `vault.json.tmp`
2. `MoveFileEx` with `MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH` replaces the live file

This prevents partial writes from corrupting the vault if the application crashes mid-save.

## Backup Files

Backups are stored in `%APPDATA%\...\QuantumVault\backups\`:
- Filename: `vault_backup_YYYYMMDD_HHMMSS.json`
- Format: identical to `vault.json` (encrypted; not plaintext)
- Backups can be restored by copying over `vault.json`
