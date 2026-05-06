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
echo [1/4] Membersihkan hasil build sebelumnya...
if exist "app-raw"   rmdir /s /q "app-raw"
if exist "app-obf"   rmdir /s /q "app-obf"
if exist "app-final" rmdir /s /q "app-final"
if exist "Output"    rmdir /s /q "Output"
echo [OK] Bersih.

:: ---- Publish multi-file self-contained --------------------------
echo.
echo [2/4] Mempublish aplikasi (self-contained, multi-file)...
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

:: ---- Prepare app-final (skip obfuscation) -----------------------
echo.
echo [3/4] Menyiapkan app-final...
echo       (Obfuscation dinonaktifkan: tidak kompatibel dengan .NET 8)
xcopy /e /i /q "app-raw" "app-final" >nul

:: Remove PDB debug symbols (do not ship)
del /q "app-final\*.pdb" 2>nul
echo [OK] app-final siap.

:: ---- Compile Inno Setup installer -------------------------------
echo.
echo [4/4] Mengompilasi installer dengan Inno Setup...
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
