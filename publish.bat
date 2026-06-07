@echo off
setlocal

set "ROOT_DIR=%~dp0"
set "PROJECT=%ROOT_DIR%BmsPreviewAudioGenerator\BmsPreviewAudioGenerator.csproj"
set "PUBLISH_DIR=%ROOT_DIR%publish"

if exist "%PUBLISH_DIR%" (
    rmdir /s /q "%PUBLISH_DIR%"
)

dotnet publish "%PROJECT%" ^
    -c Release ^
    -f net10.0 ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:IncludeAllContentForSelfExtract=true ^
    -p:DebugType=None ^
    -p:DebugSymbols=false ^
    -p:CetCompat=false ^
    -o "%PUBLISH_DIR%"

if errorlevel 1 (
    echo.
    echo Publish failed.
    exit /b 1
)

del /q "%PUBLISH_DIR%\*.pdb" 2>nul

echo.
echo Published to "%PUBLISH_DIR%"
endlocal
