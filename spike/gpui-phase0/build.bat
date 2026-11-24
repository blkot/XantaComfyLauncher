@echo off
setlocal
set "VSDEV=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\Tools\VsDevCmd.bat"
if not exist "%VSDEV%" goto :no_vs
call "%VSDEV%" -arch=x64 -host_arch=x64
if errorlevel 1 exit /b 1
cd /d "%~dp0"
cargo build --release --locked
exit /b %ERRORLEVEL%

:no_vs
echo Visual Studio 2022 Build Tools not found:
echo   %VSDEV%
exit /b 1
