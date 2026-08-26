@echo off
setlocal enabledelayedexpansion

set "ROOT_DIR=%~dp0"
set "PROJECT=%ROOT_DIR%BmsPreviewAudioGenerator\BmsPreviewAudioGenerator.csproj"
set "OUT_DIR=%ROOT_DIR%publish"

rem zip tool: prefer PowerShell Compress-Archive (built-in)
where tar >nul 2>nul && set "HAS_TAR=1"

echo ==============================================
echo  Publishing win-x64 ...
echo ==============================================
dotnet publish "%PROJECT%" -c Release -f net10.0 -p:PublishProfile="%ROOT_DIR%BmsPreviewAudioGenerator\Properties\PublishProfiles\Release-win-x64-singlefile.pubxml"
if errorlevel 1 (
    echo.
    echo Publish failed for win-x64.
    exit /b 1
)

echo ==============================================
echo  Publishing linux-x64 ...
echo ==============================================
dotnet publish "%PROJECT%" -c Release -f net10.0 -p:PublishProfile="%ROOT_DIR%BmsPreviewAudioGenerator\Properties\PublishProfiles\Release-linux-x64-singlefile.pubxml"
if errorlevel 1 (
    echo.
    echo Publish failed for linux-x64.
    exit /b 1
)

for %%P in (win_x64 linux_x64) do (
    if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"
    if exist "%OUT_DIR%\BmsPreviewAudioGenerator_%%P.zip" del /q "%OUT_DIR%\BmsPreviewAudioGenerator_%%P.zip"
    powershell -NoProfile -Command "Compress-Archive -Path '%ROOT_DIR%BmsPreviewAudioGenerator\bin\Release\publish\%%P\*' -DestinationPath '%OUT_DIR%\BmsPreviewAudioGenerator_%%P.zip' -Force"
    if errorlevel 1 (
        echo.
        echo Zip failed for %%P.
        exit /b 1
    )
    echo Created %OUT_DIR%\BmsPreviewAudioGenerator_%%P.zip
)

echo.
echo All done. Output:
dir /b "%OUT_DIR%\*.zip"
endlocal
