@echo off
setlocal
title BD2ModManager
cd /d "%~dp0"

fltmc >nul 2>&1
if errorlevel 1 (
    echo Requesting administrator privileges...
    powershell -NoProfile -Command "try { Start-Process -FilePath '%~f0' -WorkingDirectory '%~dp0' -Verb RunAs; exit 0 } catch { exit 1 }"
    if errorlevel 1 (
        echo Administrator privileges were not granted. The manager cannot start.
        pause
        exit /b 1
    )
    exit /b 0
)

if exist "src-tauri\target\release\BD2ModManager.exe" (
    start "" "%~dp0src-tauri\target\release\BD2ModManager.exe"
    exit /b 0
)

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo ERROR: Microsoft C++ Build Tools are not installed.
    echo Install Visual Studio Build Tools with the Desktop development with C++ workload.
    pause
    exit /b 1
)
"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath >nul
if errorlevel 1 (
    echo ERROR: The Visual C++ compiler workload was not found.
    echo Install the Desktop development with C++ workload in Visual Studio Installer.
    pause
    exit /b 1
)

if exist "E:\Program Files\nodejs\node.exe" set "PATH=E:\Program Files\nodejs;%PATH%"
if exist "%USERPROFILE%\.cargo\bin\cargo.exe" set "PATH=%USERPROFILE%\.cargo\bin;%PATH%"

if not exist "node_modules\.bin\tauri.cmd" (
    echo ERROR: Frontend dependencies are missing.
    echo Install the dependencies from this project's lockfile first.
    pause
    exit /b 1
)
where node >nul 2>&1
if errorlevel 1 (
    echo ERROR: Node.js was not found in PATH.
    pause
    exit /b 1
)
where npm >nul 2>&1
if errorlevel 1 (
    echo ERROR: npm was not found in PATH.
    pause
    exit /b 1
)
where cargo >nul 2>&1
if errorlevel 1 (
    echo ERROR: Rust Cargo was not found in PATH.
    pause
    exit /b 1
)

set "CARGO_CONFIG=src-tauri\.cargo\config.toml"
set "CREATED_CARGO_CONFIG="
if not exist "%CARGO_CONFIG%" (
    if not exist "src-tauri\.cargo" mkdir "src-tauri\.cargo"
    (
        echo [source.crates-io]
        echo replace-with = "rsproxy"
        echo [http]
        echo proxy = ""
    ) > "%CARGO_CONFIG%"
    set "CREATED_CARGO_CONFIG=1"
)

echo Starting BD2ModManager in Tauri development mode...
call "node_modules\.bin\tauri.cmd" dev
set "EXIT_CODE=%ERRORLEVEL%"
if defined CREATED_CARGO_CONFIG (
    del /q "%CARGO_CONFIG%" >nul 2>&1
    rmdir "src-tauri\.cargo" >nul 2>&1
)
if not "%EXIT_CODE%"=="0" (
    echo.
    echo BD2ModManager could not start. Check the error above.
    pause
)
exit /b %EXIT_CODE%
