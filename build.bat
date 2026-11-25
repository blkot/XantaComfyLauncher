@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "PUBLISH_DIR=%~dp0artifacts\publish"
set "VERSION="
if not "%~1"=="" if /I not "%~1"=="--nopause" set "VERSION=%~1"
set "VERSION_ARG="
if defined VERSION set "VERSION_ARG=-p:Version=%VERSION%"

echo Building WinForms main UI (self-contained, multi-file)...
if defined VERSION echo Version: %VERSION%
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"

dotnet publish "%~dp0XantaComfyLauncher.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=false ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  %VERSION_ARG% -o "%PUBLISH_DIR%"

if errorlevel 1 (
  echo.
  echo WinForms build failed.
  if /I not "%~2"=="--nopause" if /I not "%~1"=="--nopause" pause
  exit /b 1
)

echo Building optional WPF UI under wpf\...
set "WPF_PUBLISH_DIR=%PUBLISH_DIR%\wpf"
dotnet publish "%~dp0XantaComfyLauncher.Wpf\XantaComfyLauncher.Wpf.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=false ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  %VERSION_ARG% -o "%WPF_PUBLISH_DIR%"

if errorlevel 1 (
  echo.
  echo WPF build failed.
  if /I not "%~2"=="--nopause" if /I not "%~1"=="--nopause" pause
  exit /b 1
)

echo Building optional GPUI UI under gpui\...
call "%~dp0spike\gpui-phase0\build.bat"
if errorlevel 1 (
  echo.
  echo GPUI build failed.
  if /I not "%~2"=="--nopause" if /I not "%~1"=="--nopause" pause
  exit /b 1
)

if not exist "%PUBLISH_DIR%\gpui" mkdir "%PUBLISH_DIR%\gpui"
copy /Y "%~dp0spike\gpui-phase0\target\release\gpui_spike.exe" "%PUBLISH_DIR%\gpui\XantaComfyLauncher.Gpui.exe" >nul
xcopy /E /I /Y "%~dp0spike\gpui-phase0\bin\corehost" "%PUBLISH_DIR%\gpui\corehost" >nul

echo.
echo Build complete:
echo   %PUBLISH_DIR%\XantaComfyLauncher.exe
echo   %PUBLISH_DIR%\wpf\XantaComfyLauncher.Wpf.exe
echo   %PUBLISH_DIR%\gpui\XantaComfyLauncher.Gpui.exe
echo.
echo Next: run pack-release.bat ^<version^> ^[channel^] to create an installer/update feed.
if /I not "%~2"=="--nopause" if /I not "%~1"=="--nopause" pause
