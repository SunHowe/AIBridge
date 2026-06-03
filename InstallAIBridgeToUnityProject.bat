@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PY_SCRIPT=%SCRIPT_DIR%Tools~\ProjectLinker\install_aibridge_to_unity_project.py"

if "%~1"=="" (
    echo [AIBridge] Drag a Unity project folder onto this batch file.
    goto ErrorPause
)

if not exist "%PY_SCRIPT%" (
    echo [AIBridge] Installer script not found: %PY_SCRIPT%
    goto ErrorPause
)

where python >nul 2>nul
if not errorlevel 1 (
    python "%PY_SCRIPT%" %* --package-root "%SCRIPT_DIR%."
    set "EXIT_CODE=%ERRORLEVEL%"
    goto Finish
)

where py >nul 2>nul
if not errorlevel 1 (
    py -3 "%PY_SCRIPT%" %* --package-root "%SCRIPT_DIR%."
    set "EXIT_CODE=%ERRORLEVEL%"
    goto Finish
)

echo [AIBridge] Python 3 was not found. Please install Python 3 or add it to PATH.
goto ErrorPause

:ErrorPause
set "EXIT_CODE=1"

:Finish
if not defined AIBRIDGE_NO_PAUSE pause
exit /b %EXIT_CODE%
