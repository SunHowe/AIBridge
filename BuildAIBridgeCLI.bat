@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PROJECT_FILE=%SCRIPT_DIR%Tools~\AIBridgeCLI\AIBridgeCLI.csproj"
set "CODE_INDEX_PROJECT_FILE=%SCRIPT_DIR%Tools~\AIBridgeCodeIndex\AIBridgeCodeIndex.csproj"
set "OUTPUT_DIR=%SCRIPT_DIR%Tools~\CLI\win-x64"
set "CODE_INDEX_OUTPUT_DIR=%OUTPUT_DIR%\CodeIndex"
set "CONFIGURATION=Release"
set "RUNTIME_ID=win-x64"

if not exist "%PROJECT_FILE%" (
    echo [AIBridge] Project file not found: %PROJECT_FILE%
    exit /b 1
)

if not exist "%CODE_INDEX_PROJECT_FILE%" (
    echo [AIBridge] CodeIndex project file not found: %CODE_INDEX_PROJECT_FILE%
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [AIBridge] dotnet command not found. Please install .NET SDK 8.0 or later.
    exit /b 1
)

echo [AIBridge] Build CLI project...
echo [AIBridge] Project: %PROJECT_FILE%
echo [AIBridge] Output : %OUTPUT_DIR%

dotnet publish "%PROJECT_FILE%" ^
    -c %CONFIGURATION% ^
    -r %RUNTIME_ID% ^
    --self-contained false ^
    -p:PublishSingleFile=true ^
    -o "%OUTPUT_DIR%"

if errorlevel 1 (
    echo [AIBridge] Build failed.
    exit /b 1
)

echo [AIBridge] Build CodeIndex daemon...
echo [AIBridge] Project: %CODE_INDEX_PROJECT_FILE%
echo [AIBridge] Output : %CODE_INDEX_OUTPUT_DIR%

dotnet publish "%CODE_INDEX_PROJECT_FILE%" ^
    -c %CONFIGURATION% ^
    -r %RUNTIME_ID% ^
    --self-contained false ^
    -p:PublishSingleFile=false ^
    -o "%CODE_INDEX_OUTPUT_DIR%"

if errorlevel 1 (
    echo [AIBridge] CodeIndex build failed.
    exit /b 1
)

echo [AIBridge] Build succeeded.
exit /b 0
