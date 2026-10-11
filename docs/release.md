# Quantum Vault — Release Checklist

## Pre-Release

- [ ] All tests passing: `pytest src/tests/ -v`
- [ ] Core DLL builds clean with no warnings: `.\scripts\build_core.ps1`
- [ ] GUI builds clean: `dotnet build src\gui\QuantumVault.csproj -c Release`
- [ ] CLI installs and `qvault --help` works
- [ ] Manual smoke test: create note, snippet, command; search; export; import; backup
- [ ] Version string updated in:
  - `src/core/qvault_core.h` (`QVAULT_VERSION_STR`)
  - `src/cli/qvault/__init__.py`
  - `src/cli/qvault/cli.py` (`QVAULT_VERSION`)
  - `src/cli/pyproject.toml`
  - `src/gui/QuantumVault.csproj`
  - `winget/QuantumSuiteOrg.QuantumVault.yaml`
  - `winget/QuantumSuiteOrg.QuantumVault.installer.yaml`
  - `winget/QuantumSuiteOrg.QuantumVault.locale.en-US.yaml`

## Build Installer

1. Build core DLL (Release):
   ```powershell
   .\scripts\build_core.ps1 -Config Release
   ```

2. Build GUI (Release):
   ```powershell
   dotnet publish src\gui\QuantumVault.csproj -c Release -o build\publish
   ```

3. Build Python CLI wheel:
   ```powershell
   cd src\cli
   pip install build
   python -m build
   ```

4. Bundle with Inno Setup:
   - Install [Inno Setup](https://jrsoftware.org/isinfo.php)
   - Edit `scripts\installer.iss` with correct paths and version
   - Compile: `iscc scripts\installer.iss`
   - Output: `build\QuantumVault-0.1.0-Setup.exe`

## Calculate SHA256

```powershell
certutil -hashfile build\QuantumVault-0.1.0-Setup.exe SHA256
```

Copy the hash (no spaces). This is your `InstallerSha256`.

## Update WinGet Manifest

Edit `winget\QuantumSuiteOrg.QuantumVault.installer.yaml`:
- Replace `PLACEHOLDER_URL` with the GitHub release download URL
- Replace `PLACEHOLDER_SHA256` with the SHA256 from above
- Update `ProductCode` with the Inno Setup generated GUID

## Validate Manifest

```powershell
winget validate --manifest winget\
```

## Test Installation

```powershell
winget install --manifest winget\ --accept-package-agreements
```

Verify:
- [ ] Application launches from Start Menu
- [ ] `qvault version` works in PowerShell
- [ ] Data directory created at correct path
- [ ] Uninstall works: `winget uninstall QuantumSuiteOrg.QuantumVault`
- [ ] Data directory is NOT deleted on uninstall (user data preserved)

## GitHub Release

1. Tag the commit: `git tag v0.1.0`
2. Push tag: `git push origin v0.1.0`
3. Create GitHub Release, attach `QuantumVault-0.1.0-Setup.exe`
4. Copy the direct download URL for use in the manifest

## WinGet Submission

1. Fork [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs)
2. Create directory: `manifests/q/QuantumSuiteOrg/QuantumVault/0.1.0/`
3. Copy the three manifest files there
4. Submit pull request following the winget-pkgs contribution guidelines
5. The WinGet bot will validate automatically

**Note**: Do not claim the package is accepted until the PR is merged.
