@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "PUBLISH_DIR=%~dp0artifacts\publish"
set "VERSION="
if not "%~1"=="" if /I not "%~1"=="--nopause" set "VERSION=%~1"
set "VERSION_ARG="
if defined VERSION set "VERSION_ARG=-p:Version=%VERSION%"

echo Building XantaComfyLauncher (self-contained, multi-file)...
if defined VERSION echo Version: %VERSION%

if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"

dotnet publish "%~dp0XantaComfyLauncher.Wpf\XantaComfyLauncher.Wpf.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=false ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  %VERSION_ARG% -o "%PUBLISH_DIR%"

if errorlevel 1 (
  echo.
  echo Build failed.
  if /I not "%~2"=="--nopause" if /I not "%~1"=="--nopause" pause
  exit /b 1
)

echo.
echo Build complete:
echo   %PUBLISH_DIR%\XantaComfyLauncher.exe
echo.
echo Next: run pack-release.bat ^<version^> ^[channel^] to create an installer/update feed.
if /I not "%~2"=="--nopause" if /I not "%~1"=="--nopause" pause
