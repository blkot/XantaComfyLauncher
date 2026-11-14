@echo off
setlocal EnableExtensions
cd /d "%~dp0"
echo Building framework-dependent ComfyUI Launcher...
dotnet publish ComfyLauncher.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained false ^
  -p:PublishSingleFile=true ^
  -o "%~dp0..\LauncherApp"
if errorlevel 1 (
  echo.
  echo Build failed.
  pause
  exit /b 1
)
echo.
echo Build complete:
echo   %~dp0..\LauncherApp\ComfyLauncher.exe
pause
