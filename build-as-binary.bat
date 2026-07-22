@echo off
setlocal

set "CONFIG=Release"
set "TARGET_FRAMEWORK=net10.0-windows10.0.19041.0"
set "RUNTIME_ID=win-x64"
set "APP_NAME=AIUsageMonitor"
set "DEPLOYMENT_MODE=single-file"

for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd_HHmmss"') do set "BUILD_TIMESTAMP=%%i"
for /f %%i in ('powershell -NoProfile -Command "([xml](Get-Content -Raw -Path 'AIUsageMonitor.csproj')).Project.PropertyGroup.ApplicationDisplayVersion | Select-Object -First 1"') do set "APP_VERSION=%%i"
set "ARCHIVE_DIR=build\%CONFIG%_%BUILD_TIMESTAMP%"
set "ZIP_PATH=%ARCHIVE_DIR%\%APP_NAME%-v%APP_VERSION%-%RUNTIME_ID%-%DEPLOYMENT_MODE%-%BUILD_TIMESTAMP%.zip"
set "PUBLISH_DIR=bin\%CONFIG%\%TARGET_FRAMEWORK%\%RUNTIME_ID%\publish"

echo Cleaning previous build outputs...
dotnet clean ./AIUsageMonitor.csproj -f %TARGET_FRAMEWORK% -c %CONFIG% -v quiet
if errorlevel 1 exit /b 1

if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
if exist bin rmdir /s /q bin
if exist obj rmdir /s /q obj
if exist tempbin rmdir /s /q tempbin
if exist tempobj rmdir /s /q tempobj

dotnet restore
if errorlevel 1 exit /b 1

dotnet publish ./AIUsageMonitor.csproj ^
 -f net10.0-windows10.0.19041.0 ^
 -c %CONFIG% ^
 -r win-x64 ^
 --self-contained true ^
 -p:UseMonoRuntime=false ^
 -p:WindowsPackageType=None ^
 -p:PublishSingleFile=true ^
 -p:IncludeNativeLibrariesForSelfExtract=true ^
 -p:EnableCompressionInSingleFile=true ^
 -p:PublishReadyToRun=false ^
 -p:DebugType=None ^
 -p:DebugSymbols=false
if errorlevel 1 exit /b 1

mkdir "%ARCHIVE_DIR%"
move /Y "%PUBLISH_DIR%\%APP_NAME%.exe" "%ARCHIVE_DIR%\%APP_NAME%.exe"
if errorlevel 1 exit /b 1

powershell -NoProfile -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; if (Test-Path -LiteralPath '%ZIP_PATH%') { Remove-Item -LiteralPath '%ZIP_PATH%' -Force }; $zip = [System.IO.Compression.ZipFile]::Open('%ZIP_PATH%', 'Create'); try { [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Resolve-Path -LiteralPath '%ARCHIVE_DIR%\%APP_NAME%.exe'), '%APP_NAME%.exe', [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null } finally { $zip.Dispose() }"
if errorlevel 1 exit /b 1

echo.
echo Built executable:
echo %CD%\%ARCHIVE_DIR%\%APP_NAME%.exe
echo.
echo Built release zip:
echo %CD%\%ZIP_PATH%
echo.
echo Note:
echo This is a self-contained single-file build. The target PC does not need a separate .NET runtime install.

endlocal
