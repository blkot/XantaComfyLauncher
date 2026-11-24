@echo off
cd /d "%~dp0"
if not exist "target\release\gpui_spike.exe" (
  echo Build first with build.bat
  exit /b 1
)
start "" "target\release\gpui_spike.exe"
