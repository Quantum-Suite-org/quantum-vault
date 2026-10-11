; Quantum Vault - Inno Setup Installer Script
; Version: 0.1.1
; Build with: iscc scripts\installer.iss

#define AppName "Quantum Vault"
#define AppVersion "0.1.1"
#define AppPublisher "Quantum Suite Org"
#define AppURL "https://github.com/quantum-suite-org/quantum-vault"
#define AppExeName "QuantumVault.exe"
#define AppId "{{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\Quantum-suite-org\QuantumVault
DefaultGroupName={#AppName}
OutputDir=..\dist
OutputBaseFilename=QuantumVault-{#AppVersion}-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}
; Data directory is NOT removed on uninstall (user data preservation)
; Users must manually delete %APPDATA%\Quantum-suite-org\QuantumVault if desired

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "addtopath"; Description: "Add qvault CLI to PATH"; GroupDescription: "Command Line Interface"

[Files]
; GUI executable and dependencies
Source: "..\src\gui\bin\Release\net48\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\gui\bin\Release\net48\*.dll"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "..\src\gui\bin\Release\net48\*.config"; DestDir: "{app}"; Flags: ignoreversion
; Web UI (index.html, styles.css, app.js, boot.js, ...)
Source: "..\src\gui\web\*"; DestDir: "{app}\web"; Flags: ignoreversion recursesubdirs createallsubdirs
; Core DLL
Source: "..\build\bin\qvault_core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\bin\qvault_core.pdb"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
; Documentation
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PRIVACY.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Install Python CLI into a bundled venv
Filename: "{cmd}"; Parameters: "/c python -m pip install --user ""{app}\qvault_cli.whl"" --quiet"; \
    Description: "Installing CLI tools"; Flags: runhidden waituntilterminated; \
    Check: FileExists(ExpandConstant('{app}\qvault_cli.whl'))
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; \
    Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  PathVar, AppPath: string;
begin
  if CurStep = ssPostInstall then
  begin
    if WizardIsTaskSelected('addtopath') then
    begin
      AppPath := ExpandConstant('{app}');
      RegQueryStringValue(HKLM, 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
        'Path', PathVar);
      if Pos(AppPath, PathVar) = 0 then
      begin
        PathVar := PathVar + ';' + AppPath;
        RegWriteStringValue(HKLM, 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
          'Path', PathVar);
      end;
    end;
  end;
end;



