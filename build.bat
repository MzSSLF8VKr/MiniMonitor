@echo off
setlocal enabledelayedexpansion
title MiniMonitor Build Script
cd /d "%~dp0"

REM ============================================================
REM  MiniMonitor - one-click build for Windows
REM  Output: a self-contained single MiniMonitor.exe (no runtime
REM  needs to be installed on the target machine)
REM ============================================================

REM ---- enable ANSI colors (Windows 10 1607+) ----
for /F %%a in ('echo prompt $E ^| cmd') do set "ESC=%%a"
set "GREEN=%ESC%[92m"
set "RED=%ESC%[91m"
set "YELLOW=%ESC%[93m"
set "CYAN=%ESC%[96m"
set "RESET=%ESC%[0m"

echo.
echo %CYAN%============================================================%RESET%
echo %CYAN%  MiniMonitor Build %RESET%
echo %CYAN%============================================================%RESET%
echo.

REM ---- 1. check dotnet SDK ----
echo [STEP 1/3] Checking .NET 8 SDK ...
where dotnet >nul 2>&1
if errorlevel 1 (
    echo %RED%[ERROR]%RESET% dotnet SDK was not found on this machine.
    echo.
    echo [HINT] Please install the .NET 8 SDK first:
    echo        Official : https://dotnet.microsoft.com/download/dotnet/8.0
    echo        CN mirror: https://mirrors.huaweicloud.com/dotnet/
    echo        Download the "x64 Windows" SDK installer, then re-run this script.
    echo.
    pause
    exit /b 1
)
for /f "delims=" %%v in ('dotnet --version') do set "DOTNET_VER=%%v"
echo %GREEN%[OK]%RESET% dotnet SDK found, version: %DOTNET_VER%

REM ---- 2. restore NuGet packages (up to 20 retries) ----
set /a TRIES=0
:restore
set /a TRIES+=1
echo.
echo [STEP 2/3] Restoring NuGet packages ^(attempt !TRIES!/20^) ...
echo          ^> dotnet restore --configfile nuget.config -r win-x64
dotnet restore --configfile nuget.config -r win-x64 --verbosity minimal
if not errorlevel 1 goto restore_done
if !TRIES! geq 20 (
    echo.
    echo %RED%[ERROR]%RESET% NuGet restore failed after 20 attempts.
    echo [HINT] Check your network, or edit nuget.config to switch mirrors.
    pause
    exit /b 1
)
echo %YELLOW%[RETRY]%RESET% restore failed, waiting 3 seconds ...
timeout /t 3 /nobreak >nul
goto restore
:restore_done
echo %GREEN%[OK]%RESET% NuGet packages restored.

REM ---- 3. publish self-contained single-file exe (up to 20 retries) ----
set /a TRIES=0
:publish
set /a TRIES+=1
echo.
echo [STEP 3/3] Publishing self-contained single-file exe ^(attempt !TRIES!/20^) ...
echo          ^> dotnet publish -c Release -r win-x64 --self-contained true
dotnet publish MiniMonitor.csproj -c Release -r win-x64 --self-contained true ^
  --no-restore ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=None -p:DebugSymbols=false ^
  --verbosity minimal
if not errorlevel 1 goto publish_done
if !TRIES! geq 20 (
    echo.
    echo %RED%[ERROR]%RESET% publish failed after 20 attempts.
    pause
    exit /b 1
)
echo %YELLOW%[RETRY]%RESET% publish failed, waiting 3 seconds ...
timeout /t 3 /nobreak >nul
goto publish
:publish_done

set "OUT=bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
echo.
echo %CYAN%============================================================%RESET%
echo %GREEN%[OK] Build succeeded!%RESET%
echo.
echo Output folder: %OUT%
echo Exe file    : %OUT%\MiniMonitor.exe
echo.
echo Notes:
echo   - Right-click MiniMonitor.exe and "Run as administrator" on first launch.
echo   - A UAC prompt is required because temperature reading loads a kernel driver.
echo   - Right-click the floating window for settings (NIC / opacity / theme).
echo %CYAN%============================================================%RESET%
echo.
pause
