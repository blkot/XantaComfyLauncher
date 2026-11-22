@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "PUBLISH_DIR=%~dp0artifacts\portable"
echo Building XantaComfyLauncher portable single-file...

if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"

dotnet publish "%~dp0XantaComfyLauncher.Wpf\XantaComfyLauncher.Wpf.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  -o "%PUBLISH_DIR%"

if errorlevel 1 (
  echo.
  echo Portable build failed.
  if /I not "%~1"=="--nopause" pause
  exit /b 1
)

echo.
echo Portable build complete:
echo   %PUBLISH_DIR%\XantaComfyLauncher.exe
if /I not "%~1"=="--nopause" pause
