# Quantum Vault CLI

Command-line interface for [Quantum Vault](../../README.md).

## Install

```powershell
pip install .
```

## Usage

```
qvault --help
qvault list
qvault search "docker"
qvault add snippet "Restart nginx" --content "sudo systemctl restart nginx" --language bash
qvault show <id>
qvault copy <id>
qvault export --path backup.json
qvault import backup.json
qvault stats
qvault version
```

## JSON output

Every command accepts `--json` for machine-readable output:

```powershell
qvault list --json | ConvertFrom-Json
qvault search "api" --json
```

## Notes password

The CLI will prompt interactively for the notes master password when needed.
Pass `--password <pw>` to `qvault add note` to skip the prompt in scripts
(be aware of shell history security implications).
