; ============================================================
;  Inno Setup Script — Kalkulator Panel Tritunggal Swarna
; ============================================================

#define AppName      "Kalkulator Panel Tritunggal Swarna"
#define AppVersion   "1.2.2"
#define AppPublisher "PT Tritunggal Swarna"
#define AppExeName   "PanelCalculator.WinForms.exe"
#define AppId        "{{A3B7C2D1-1234-4E56-9F0A-TTS2025PANEL}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://tritunggalswarna.com
DefaultDirName={autopf}\TritunggalSwarna\KalkulatorPanel
DefaultGroupName={#AppName}
OutputDir=C:\Projects\Panel Calculator\Installer
OutputBaseFilename=KalkulatorPanel-TTS-v1.2.2-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin

; --- Password protection ---
Password=TTS2025
Encryption=yes

; --- Minimum Windows version: Windows 10 ---
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Buat {cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Main executable (self-contained, no .NET runtime needed)
Source: "C:\Projects\Panel Calculator\publish\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remove the SQLite database only if the user chooses to; leave AppData folder intact by default
; (Database lives in %AppData%\PanelCalculator\ — we do NOT delete it on uninstall)
