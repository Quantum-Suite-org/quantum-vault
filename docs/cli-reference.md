# Quantum Vault — CLI Reference

## Synopsis

```
qvault [--json] [--data-dir DIR] <command> [options]
```

## Global Options

| Option | Description |
|---|---|
| `--json` | Output as JSON (machine-readable) |
| `--data-dir DIR` | Override vault data directory |
| `--version` | Print version and exit |

## Commands

### `qvault version`
Print version information.
```
qvault version
qvault version --json
```

### `qvault list`
List vault entries.
```
qvault list
qvault list --type snippet
qvault list --type command
qvault list --favorites
qvault list --sort title
qvault list --limit 20
qvault list --json
```
Options: `--type note|snippet|command`, `--favorites`, `--sort updated|created|title`, `--limit N`, `--json`

### `qvault search <query>`
Search entries by title, tags, description.
```
qvault search "docker"
qvault search nginx --type command
qvault search api --content     # also searches content of snippets/commands
qvault search "git log" --json
```
Options: `--type`, `--content`, `--limit N`, `--json`

### `qvault show <id>`
Show full details and content of an entry.
```
qvault show abc123ef
qvault show abc123ef --json
```
Accepts a full UUID or a unique prefix (minimum 4 characters).

### `qvault add <type> <title>`
Add a new entry.
```
qvault add snippet "Git stash pop" --content "git stash pop" --language bash --tags git
qvault add command "Restart nginx" --content "systemctl restart nginx"
qvault add note   "Meeting notes" --stdin      # reads content from stdin
qvault add note   "Quick note"    --content "hello world" --password mypassword
```
Options: `--content TEXT`, `--stdin`, `--tags TAGS`, `--description DESC`, `--language LANG`, `--password PW`, `--json`

Notes require the master password. CLI prompts interactively unless `--password` is provided.

### `qvault delete <id>`
Delete an entry (prompts for confirmation unless `--yes`).
```
qvault delete abc123ef
qvault delete abc123ef --yes
```

### `qvault copy <id>`
Copy entry content to clipboard. Falls back to stdout if clipboard unavailable.
```
qvault copy abc123ef
```

### `qvault export`
Export all entries to a JSON file.
```
qvault export
qvault export --path C:\Users\me\backup.json
```

### `qvault import <path>`
Import entries from a JSON export file.
```
qvault import backup.json
qvault import backup.json --replace    # replace all data (default: merge)
```

### `qvault backup`
Create a timestamped backup of the vault data file.
```
qvault backup
qvault backup --dir C:\backups
```

### `qvault stats`
Show vault statistics.
```
qvault stats
qvault stats --json
```

## PowerShell Integration

```powershell
# List all snippets as PowerShell objects
$snips = qvault list --type snippet --json | ConvertFrom-Json

# Search and get first result's content
$result = qvault search "nginx" --json | ConvertFrom-Json
$result[0].content

# Add a snippet from clipboard
qvault add snippet "From clipboard" --content (Get-Clipboard) --tags clipboard

# Export and open folder
qvault export --path "$env:USERPROFILE\Desktop\vault_backup.json"
Invoke-Item "$env:USERPROFILE\Desktop"
```

## Exit Codes

| Code | Meaning |
|------|---------|
| 0    | Success |
| 1    | Error (message printed to stderr) |
| 130  | Interrupted (Ctrl+C) |
