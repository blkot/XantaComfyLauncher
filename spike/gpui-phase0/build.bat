@echo off
setlocal
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
set "VSDIR="
if exist "%VSWHERE%" for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSDIR=%%i"
if not defined VSDIR goto :no_vs
set "VSDEV=%VSDIR%\Common7\Tools\VsDevCmd.bat"
if not exist "%VSDEV%" goto :no_vs
call "%VSDEV%" -arch=x64 -host_arch=x64
if errorlevel 1 exit /b 1
cd /d "%~dp0"

echo Building C# CoreHost...
dotnet publish "..\..\XantaComfyLauncher.CoreHost\XantaComfyLauncher.CoreHost.csproj" -c Release -o "bin\corehost" --nologo
if errorlevel 1 exit /b 1

echo Building GPUI...
cargo build --release --locked
exit /b %ERRORLEVEL%

:no_vs
echo Visual Studio 2022 C++ build tools not found.
echo Install Visual Studio Build Tools with the C++ workload.
exit /b 1
