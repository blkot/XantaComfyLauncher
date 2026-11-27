@echo off
cd /d "%~dp0"
if not exist "target\release\gpui_spike.exe" (
  echo Build first with build.bat
  exit /b 1
)
if not exist "bin\corehost\XantaComfyLauncher.CoreHost.exe" (
  echo CoreHost not found. Run build.bat first.
  exit /b 1
)
set "XANTA_COREHOST_EXE=%~dp0bin\corehost\XantaComfyLauncher.CoreHost.exe"
start "" "target\release\gpui_spike.exe"
