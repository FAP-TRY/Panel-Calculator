; ============================================================
;  Inno Setup Script — Kalkulator Panel Tritunggal Swarna
; ============================================================

#define AppName      "Kalkulator Panel Tritunggal Swarna"
#define AppVersion   "1.3.0"
#define AppPublisher "PT Tritunggal Swarna"
; Nama file EXE setelah terinstall (di Program Files). Match dengan output
; build-release-singlefile.ps1 yang menulis publish\PanelCalculator.exe.
#define AppExeName   "PanelCalculator.exe"
; AppId TIDAK boleh diganti — ini dipakai Inno Setup untuk mendeteksi
; install lama dengan AppId yang sama, sehingga upgrade flow jalan otomatis.
#define AppId        "{{A3B7C2D1-1234-4E56-9F0A-TTS2025PANEL}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://tritunggalswarna.com
DefaultDirName={autopf}\TritunggalSwarna\KalkulatorPanel
DefaultGroupName={#AppName}
OutputDir=Installer
OutputBaseFilename=KalkulatorPanel-TTS-v1.3.0-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin

; ── Upgrade detection ────────────────────────────────────────────
; - VersionInfoVersion memungkinkan Inno Setup membandingkan versi EXE
;   lama vs baru dan menampilkan "an older version is installed" otomatis.
; - CloseApplications + RestartApplications: minta user tutup aplikasi
;   yang sedang berjalan (kalau ada) sebelum file di-replace.
; - DisableProgramGroupPage=yes: skip halaman Start Menu (tidak diperlukan).
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no
UninstallDisplayName={#AppName} v{#AppVersion}
AppendDefaultDirName=no

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
; Main executable (self-contained single-file bundle, native libs embedded).
; Path RELATIF terhadap lokasi .iss script (= root repo). Sebelumnya path
; hardcoded ke C:\Projects\Panel Calculator\publish\ — bug yang bikin
; installer salah package EXE dari folder lain.
Source: "publish\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remove the SQLite database only if the user chooses to; leave AppData folder intact by default
; (Database lives in %AppData%\PanelCalculator\ — we do NOT delete it on uninstall)

; ── Custom messages (Bahasa Indonesia) ───────────────────────────
[Messages]
SetupAppTitle=Setup — {#AppName}
SetupWindowTitle=Setup — {#AppName} v{#AppVersion}
WelcomeLabel2=Setup akan menginstal {#AppName} versi {#AppVersion} ke komputer Anda.%n%nDisarankan menutup semua aplikasi lain sebelum melanjutkan.
ReadyMemoTasks=Tugas tambahan:
FinishedHeadingLabel=Instalasi Selesai
FinishedLabelNoIcons=Setup telah selesai menginstal {#AppName} v{#AppVersion} ke komputer Anda.
FinishedLabel=Setup telah selesai menginstal {#AppName} v{#AppVersion} ke komputer Anda. Aplikasi bisa dijalankan dari shortcut yang sudah dibuat.

; Deteksi versi lama: ditampilkan otomatis oleh Inno Setup kalau AppId
; sudah ada di registry dengan versi lebih lama (AppVersion compare).
ConfirmUninstall=Apakah Anda yakin ingin menghapus %1 sepenuhnya?

; ── Code section: deteksi & info versi lama untuk user ──────────
[Code]
function GetUninstallString(): string;
var
  sUnInstPath: string;
  sUnInstallString: string;
begin
  sUnInstPath := ExpandConstant('Software\Microsoft\Windows\CurrentVersion\Uninstall\{#emit SetupSetting("AppId")}_is1');
  sUnInstallString := '';
  if not RegQueryStringValue(HKLM, sUnInstPath, 'UninstallString', sUnInstallString) then
    RegQueryStringValue(HKCU, sUnInstPath, 'UninstallString', sUnInstallString);
  Result := sUnInstallString;
end;

function IsUpgrade(): Boolean;
begin
  Result := (GetUninstallString() <> '');
end;

function GetOldVersion(): string;
var
  sUnInstPath: string;
  sOldVersion: string;
begin
  sUnInstPath := ExpandConstant('Software\Microsoft\Windows\CurrentVersion\Uninstall\{#emit SetupSetting("AppId")}_is1');
  sOldVersion := '';
  if not RegQueryStringValue(HKLM, sUnInstPath, 'DisplayVersion', sOldVersion) then
    RegQueryStringValue(HKCU, sUnInstPath, 'DisplayVersion', sOldVersion);
  Result := sOldVersion;
end;

function InitializeSetup(): Boolean;
var
  oldVer: string;
  msg: string;
begin
  Result := True;
  if IsUpgrade() then
  begin
    oldVer := GetOldVersion();
    if oldVer = '' then oldVer := '(versi tidak diketahui)';
    msg :=
      'Terdeteksi instalasi lama: ' + oldVer + #13#10 +
      'Akan di-upgrade ke versi: {#AppVersion}' + #13#10#13#10 +
      'Database, riwayat estimasi, dan lisensi Anda di %AppData%\PanelCalculator\ TIDAK akan dihapus.' + #13#10#13#10 +
      'Lanjutkan dengan upgrade?';
    if MsgBox(msg, mbConfirmation, MB_YESNO) = IDNO then
    begin
      Result := False;
    end;
  end;
end;
