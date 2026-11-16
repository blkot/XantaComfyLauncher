@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "PUBLISH_DIR=%~dp0artifacts\framework-dependent"
echo Building framework-dependent XantaComfyLauncher...
echo Requires .NET Desktop Runtime 9 on the target machine.

if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"

dotnet publish XantaComfyLauncher.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained false ^
  -p:PublishSingleFile=false ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  -o "%PUBLISH_DIR%"

if errorlevel 1 (
  echo.
  echo Build failed.
  if /I not "%~1"=="--nopause" pause
  exit /b 1
)

echo.
echo Build complete:
echo   %PUBLISH_DIR%\XantaComfyLauncher.exe
if /I not "%~1"=="--nopause" pause
