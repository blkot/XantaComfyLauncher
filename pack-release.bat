@echo off
setlocal EnableExtensions
cd /d "%~dp0"

if "%~1"=="" (
  echo Usage: pack-release.bat ^<version^> [channel]
  echo Example: pack-release.bat 0.1.0 stable
  echo          pack-release.bat 0.2.0-beta.1 beta
  exit /b 1
)

set "VERSION=%~1"
set "CHANNEL=%~2"
if "%CHANNEL%"=="" set "CHANNEL=stable"
set "PUBLISH_DIR=%~dp0artifacts\publish"
set "RELEASES_DIR=%~dp0artifacts\releases"
set "FULL_PACKAGE=%RELEASES_DIR%\XantaComfyLauncher-%VERSION%-%CHANNEL%-full.nupkg"

if exist "%FULL_PACKAGE%" (
  echo.
  echo Release %VERSION% already exists in channel %CHANNEL%.
  echo Bump the version or remove the old package before repacking.
  exit /b 1
)

echo Building version %VERSION%...
call "%~dp0build.bat" "%VERSION%" --nopause
if errorlevel 1 exit /b 1

where vpk >nul 2>nul
if errorlevel 1 (
  echo.
  echo Velopack CLI ^(vpk^) was not found.
  echo Install it with:
  echo   dotnet tool install --global vpk
  exit /b 1
)

if not exist "%RELEASES_DIR%" mkdir "%RELEASES_DIR%"

echo Packing XantaComfyLauncher %VERSION% ^(%CHANNEL%^)...
vpk pack ^
  --packId XantaComfyLauncher ^
  --packVersion "%VERSION%" ^
  --packDir "%PUBLISH_DIR%" ^
  --mainExe XantaComfyLauncher.exe ^
  --packTitle "XantaComfyLauncher" ^
  --packAuthors "Xanta" ^
  --icon "%~dp0Assets\XantaComfyLauncher.ico" ^
  --channel "%CHANNEL%" ^
  --runtime win-x64 ^
  --instLocation PerUser ^
  --outputDir "%RELEASES_DIR%"

if errorlevel 1 (
  echo.
  echo Packaging failed.
  exit /b 1
)

if not exist "%FULL_PACKAGE%" (
  echo.
  echo Packaging failed: expected package was not created:
  echo   %FULL_PACKAGE%
  exit /b 1
)

call "%~dp0build-bootstrapper.bat" "%VERSION%"
if errorlevel 1 exit /b 1

echo.
echo Release artifacts:
dir /b "%RELEASES_DIR%"
echo.
echo Upload the complete contents of:
echo   %RELEASES_DIR%
echo to your static update feed or GitHub Releases.
exit /b 0
