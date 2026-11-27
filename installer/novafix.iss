; NOVA FIX — Inno Setup 6 installer script (fully offline).
; Build: iscc installer\novafix.iss   (after scripts\package-offline.ps1)
;
; Creator attribution is mandatory metadata in every release.

#define MyAppName "NOVA FIX"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "Danial Mousavi"
#define MyAppURL "https://instagram.com/dani.m7016"
#define MyAppExeName "NOVA.FIX.exe"

[Setup]
AppId={{A7E5C1D4-93B2-4E7A-8F01-NOVAFIX2000}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
; Installer must NOT require internet.
DisableReadyPage=no
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=NOVA-FIX-Setup-{#MyAppVersion}
SetupIconFile=..\assets\icons\novafix.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
; Product metadata — creator attribution (spec §1)
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} — Windows Diagnostic, Repair, Performance Optimization and System Maintenance Software
VersionInfoProductName={#MyAppName}
VersionInfoProductTextVersion={#MyAppVersion}
VersionInfoTextVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
; Persian messages would be added as compiler:Languages\Farsi.isl when bundled.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; \
    GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; publish output produced by scripts\package-offline.ps1
Source: "..\publish\novafix\*"; DestDir: "{app}"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; \
    Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: \
    "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; User data (reports/settings) is preserved by default; offer cleanup:
Type: filesandordirs; Name: "{localappdata}\NOVA FIX\logs"

[Code]
// Record creator attribution in uninstall registry entry (already via AppPublisher).
function InitializeSetup(): Boolean;
begin
  Result := True;
  // Fully offline installer: no download tasks, no web URLs fetched.
end;
