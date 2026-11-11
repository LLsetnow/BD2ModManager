@echo off
setlocal
for /f "delims=" %%V in ('dotnet --version') do set "BD2_CSC_SDK_VERSION=%%V"
for /f "tokens=1,2 delims=[]" %%A in ('dotnet --list-sdks') do for /f "tokens=1" %%V in ("%%A") do if "%%V"=="%BD2_CSC_SDK_VERSION%" set "BD2_CSC_SDK_ROOT=%%B"
if not defined BD2_CSC_SDK_ROOT exit /b 1
dotnet exec "%BD2_CSC_SDK_ROOT%\%BD2_CSC_SDK_VERSION%\Roslyn\bincore\csc.dll" %*
