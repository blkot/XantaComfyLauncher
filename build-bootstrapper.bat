@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "VERSION=%~1"
if "%VERSION%"=="" set "VERSION=0.1.0"
set "SETUP_EXE=%~dp0artifacts\releases\XantaComfyLauncher-stable-Setup.exe"
set "OUTPUT_DIR=%~dp0artifacts\installer"

if not exist "%SETUP_EXE%" (
  echo Velopack Setup payload not found:
  echo   %SETUP_EXE%
  echo Run pack-release.bat first.
  exit /b 1
)

if exist "%OUTPUT_DIR%" rmdir /s /q "%OUTPUT_DIR%"
mkdir "%OUTPUT_DIR%"

echo Building XantaComfyLauncher installer with directory selection...
dotnet publish "%~dp0Bootstrapper\XantaComfyLauncher.Bootstrapper.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  -p:Version=%VERSION% ^
  -p:SetupPayloadPath="%SETUP_EXE%" ^
  -o "%OUTPUT_DIR%"

if errorlevel 1 (
  echo.
  echo Installer build failed.
  exit /b 1
)

copy /Y "%OUTPUT_DIR%\XantaComfyLauncher-Installer.exe" "%~dp0artifacts\releases\XantaComfyLauncher-Installer.exe" >nul
if errorlevel 1 exit /b 1

echo.
echo Installer built:
echo   %~dp0artifacts\releases\XantaComfyLauncher-Installer.exe
exit /b 0
