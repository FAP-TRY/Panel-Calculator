@echo off
setlocal enabledelayedexpansion
title Build Installer — Kalkulator Panel Tritunggal Swarna

echo.
echo  ============================================================
echo   BUILD INSTALLER  ^|  Kalkulator Panel Tritunggal Swarna
echo  ============================================================
echo.

:: ── Locate project root (one level up from Installer\) ──────────
set "ROOT=%~dp0.."
set "INSTALLER=%~dp0"
cd /d "%INSTALLER%"

:: ── Locate Inno Setup compiler ───────────────────────────────────
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

:: ── Check .NET SDK ───────────────────────────────────────────────
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET SDK tidak ditemukan. Install dari https://dotnet.microsoft.com
    pause
    exit /b 1
)
for /f "tokens=*" %%v in ('dotnet --version') do set "DOTNET_VER=%%v"
echo [OK] .NET SDK     : %DOTNET_VER%

:: ── Install / update Obfuscar global tool ────────────────────────
echo.
echo [1/5] Memeriksa Obfuscar...
dotnet tool install --global Obfuscar.GlobalTool >nul 2>&1
if errorlevel 1 (
    dotnet tool update --global Obfuscar.GlobalTool >nul 2>&1
)
obfuscar.exe --version >nul 2>&1
if errorlevel 1 (
    echo [WARN] Obfuscar tidak ditemukan di PATH.
    echo        Coba tutup dan buka kembali Command Prompt, lalu jalankan ulang.
    echo        Melanjutkan tanpa obfuscation...
    set "SKIP_OBF=1"
) else (
    for /f "tokens=*" %%v in ('obfuscar.exe --version 2^>^&1') do set "OBF_VER=%%v"
    echo [OK] Obfuscar     : !OBF_VER!
    set "SKIP_OBF=0"
)

:: ── Clean previous build artifacts ───────────────────────────────
echo.
echo [2/5] Membersihkan hasil build sebelumnya...
if exist "app-raw"   rmdir /s /q "app-raw"
if exist "app-obf"   rmdir /s /q "app-obf"
if exist "app-final" rmdir /s /q "app-final"
if exist "Output"    rmdir /s /q "Output"
echo [OK] Bersih.

:: ── Publish multi-file self-contained ────────────────────────────
echo.
echo [3/5] Mempublish aplikasi (self-contained, multi-file)...
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

:: ── Obfuscate app assemblies ──────────────────────────────────────
if "%SKIP_OBF%"=="1" (
    echo.
    echo [4/5] Obfuscation DILEWATI ^(Obfuscar tidak tersedia^).
    echo       Menyalin langsung ke app-final...
    xcopy /e /i /q "app-raw" "app-final" >nul
    goto :package
)

echo.
echo [4/5] Menjalankan Obfuscar...
:: Obfuscar reads InPath/OutPath from obfuscar.xml
obfuscar.exe obfuscar.xml
if errorlevel 1 (
    echo [WARN] Obfuscar selesai dengan peringatan.
    echo        Memeriksa apakah output tersedia...
)

:: Merge: start with all raw files, then overwrite app DLLs with obfuscated ones
xcopy /e /i /q "app-raw" "app-final" >nul

:: Overwrite only the 3 app DLLs with obfuscated versions
for %%F in (
    PanelCalculator.WinForms.dll
    PanelCalculator.Core.dll
    PanelCalculator.Data.dll
) do (
    if exist "app-obf\%%F" (
        copy /y "app-obf\%%F" "app-final\%%F" >nul
        echo [OK] Obfuscated: %%F
    ) else (
        echo [WARN] %%F tidak ditemukan di output Obfuscar, menggunakan original.
    )
)

:: Remove PDB files from final build (don't ship debug symbols)
del /q "app-final\*.pdb" 2>nul

echo [OK] Obfuscation selesai.

:package
:: ── Compile Inno Setup installer ─────────────────────────────────
echo.
echo [5/5] Mengompilasi installer dengan Inno Setup...
"%ISCC%" "PanelCalculatorSetup.iss" /Q
if errorlevel 1 (
    echo [ERROR] Inno Setup gagal!
    pause
    exit /b 1
)

:: ── Done ──────────────────────────────────────────────────────────
echo.
echo  ============================================================
echo   SELESAI!
echo  ============================================================
echo.
echo  File installer:
for %%F in ("Output\*.exe") do echo    %%~fF
echo.
echo  Ukuran file:
for %%F in ("Output\*.exe") do (
    set /a SIZE=%%~zF / 1048576
    echo    %%~nxF  ^(!SIZE! MB^)
)
echo.

set /p OPEN=Buka folder Output? [Y/N]:
if /i "%OPEN%"=="Y" explorer "Output"

endlocal
