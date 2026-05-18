; ============================================================
;  Inno Setup Script — Kalkulator Panel Tritunggal Swarna
; ============================================================

#define AppName      "Kalkulator Panel Tritunggal Swarna"
#define AppVersion   "1.2.5"
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
OutputBaseFilename=KalkulatorPanel-TTS-v1.2.5-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin

; --- Password protection ---
; Password TIDAK lagi di-hardcode di file ini (file ini di-commit ke git public).
; Sebelum compile installer, set environment variable di komputer build:
;   setx PANELCALC_INSTALL_PASSWORD "PasswordYangBaru2026"
; lalu buka shell BARU sebelum jalankan ISCC.
; Catatan: password lama "TTS2025" sudah ter-leak di git history — JANGAN dipakai lagi.
#define InstallPassword GetEnv("PANELCALC_INSTALL_PASSWORD")
#if InstallPassword == ""
  #error Environment variable PANELCALC_INSTALL_PASSWORD belum di-set. Lihat komentar di atas.
#endif
Password={#InstallPassword}
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
