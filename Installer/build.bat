@echo off
setlocal enabledelayedexpansion
title Build Installer - Kalkulator Panel Tritunggal Swarna

echo.
echo  ============================================================
echo   BUILD INSTALLER  -  Kalkulator Panel Tritunggal Swarna
echo  ============================================================
echo.

:: Locate project root (one level up from Installer\)
set "ROOT=%~dp0.."
set "INSTALLER=%~dp0"
cd /d "%INSTALLER%"

:: ---- Locate Inno Setup compiler ---------------------------------
set "ISCC="
for %%P in (
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    "C:\Program Files\Inno Setup 6\ISCC.exe"
    "C:\Program Files (x86)\Inno Setup 5\ISCC.exe"
) do (
    if exist %%P (
        set "ISCC=%%~P"
        goto :found_inno
    )
)
echo [ERROR] Inno Setup tidak ditemukan!
echo.
echo  Unduh dan install Inno Setup 6 (gratis) dari:
echo  https://jrsoftware.org/isdl.php
echo.
pause
exit /b 1

:found_inno
echo [OK] Inno Setup  : %ISCC%

:: ---- Check .NET SDK ---------------------------------------------
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET SDK tidak ditemukan. Install dari https://dotnet.microsoft.com
    pause
    exit /b 1
)
for /f "tokens=*" %%v in ('dotnet --version') do set "DOTNET_VER=%%v"
echo [OK] .NET SDK     : %DOTNET_VER%

:: ---- Clean previous artifacts -----------------------------------
echo.
echo [1/5] Membersihkan hasil build sebelumnya...
if exist "app-raw"   rmdir /s /q "app-raw"
if exist "app-obf"   rmdir /s /q "app-obf"
if exist "app-final" rmdir /s /q "app-final"
if exist "Output"    rmdir /s /q "Output"
echo [OK] Bersih.

:: ---- Publish multi-file self-contained --------------------------
echo.
echo [2/5] Mempublish aplikasi (self-contained, multi-file)...
echo       Ini mungkin memakan waktu 1-2 menit...
dotnet publish "%ROOT%\PanelCalculator.WinForms\PanelCalculator.WinForms.csproj" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=false ^
    -p:PublishReadyToRun=true ^
    --nologo ^
    -o "%INSTALLER%app-raw" ^
    -v minimal
if errorlevel 1 (
    echo [ERROR] Publish gagal!
    pause
    exit /b 1
)
echo [OK] Publish selesai.

:: ---- Run Obfuscar (rename + string-encryption) ------------------
echo.
echo [3/5] Mengaktifkan Obfuscar (rename method/field + encrypt string)...

:: Verify obfuscar.xml exists
if not exist "obfuscar.xml" (
    echo [ERROR] File konfigurasi 'obfuscar.xml' tidak ditemukan di %INSTALLER%
    pause
    exit /b 1
)

:: Locate / install Obfuscar.GlobalTool. We probe twice because a fresh
:: `dotnet tool install --global` may extend PATH only after the shell
:: restarts; falling back to the default %USERPROFILE%\.dotnet\tools path
:: lets us still invoke the tool in the same build session.
set "OBFUSCAR_EXE="
where obfuscar.console>nul 2>&1
if not errorlevel 1 (
    set "OBFUSCAR_EXE=obfuscar.console"
) else (
    if exist "%USERPROFILE%\.dotnet\tools\obfuscar.console.exe" (
        set "OBFUSCAR_EXE=%USERPROFILE%\.dotnet\tools\obfuscar.console.exe"
    )
)

if "%OBFUSCAR_EXE%"=="" (
    echo       Obfuscar belum terpasang. Memasang Obfuscar.GlobalTool...
    dotnet tool install --global Obfuscar.GlobalTool >nul 2>&1
    if errorlevel 1 (
        :: Try update — maybe already installed in another version
        dotnet tool update --global Obfuscar.GlobalTool >nul 2>&1
    )
    if exist "%USERPROFILE%\.dotnet\tools\obfuscar.console.exe" (
        set "OBFUSCAR_EXE=%USERPROFILE%\.dotnet\tools\obfuscar.console.exe"
    ) else (
        where obfuscar.console>nul 2>&1
        if not errorlevel 1 set "OBFUSCAR_EXE=obfuscar.console"
    )
)

if "%OBFUSCAR_EXE%"=="" (
    echo [ERROR] Obfuscar.GlobalTool gagal dipasang. Install manual:
    echo         dotnet tool install --global Obfuscar.GlobalTool
    pause
    exit /b 1
)

echo [OK] Obfuscar     : %OBFUSCAR_EXE%
"%OBFUSCAR_EXE%" "obfuscar.xml"
if errorlevel 1 (
    echo.
    echo [ERROR] Obfuscar gagal! Lihat pesan di atas.
    echo         Sementara nonaktifkan: edit Installer\build.bat dan
    echo         ganti step [3/5] dengan xcopy app-raw app-final.
    pause
    exit /b 1
)
echo [OK] Obfuscation selesai.

:: ---- Assemble app-final -----------------------------------------
:: Strategy: start from app-raw (has all runtime DLLs + native libs +
:: the .exe launcher with [STAThread]). Overlay our 3 obfuscated
:: assemblies on top. This way runtime files stay untouched while only
:: our IL gets the protection.
echo.
echo [4/5] Menyiapkan app-final (overlay obfuscated DLLs)...
xcopy /e /i /q "app-raw" "app-final" >nul

:: Overlay obfuscated assemblies (override the plain ones from app-raw)
copy /y "app-obf\PanelCalculator.WinForms.dll" "app-final\PanelCalculator.WinForms.dll" >nul
copy /y "app-obf\PanelCalculator.Core.dll"     "app-final\PanelCalculator.Core.dll"     >nul
copy /y "app-obf\PanelCalculator.Data.dll"     "app-final\PanelCalculator.Data.dll"     >nul

:: Remove PDB debug symbols (do not ship) — both raw and obfuscated
del /q "app-final\*.pdb" 2>nul
echo [OK] app-final siap (DLL aplikasi sudah ter-obfuscate).

:: ---- Compile Inno Setup installer -------------------------------
echo.
echo [5/5] Mengompilasi installer dengan Inno Setup...
"%ISCC%" "PanelCalculatorSetup.iss"
if errorlevel 1 (
    echo.
    echo [ERROR] Inno Setup gagal! Periksa pesan error di atas.
    pause
    exit /b 1
)

:: ---- Done -------------------------------------------------------
echo.
echo  ============================================================
echo   SELESAI!
echo  ============================================================
echo.
echo  File installer:
for %%F in ("Output\*.exe") do (
    echo    %%~fF
    set /a SIZEMB=%%~zF / 1048576
    echo    Ukuran: !SIZEMB! MB
)
echo.
set /p OPEN=Buka folder Output? [Y/N]:
if /i "%OPEN%"=="Y" explorer "Output"

endlocal
