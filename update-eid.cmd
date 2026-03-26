@echo off
setlocal EnableExtensions EnableDelayedExpansion

cd /d "%~dp0"

if /I not "%~1"=="--relay" (
  set "EID_UPDATE_SCRIPT_DIR=%~dp0"
  set "TEMP_LAUNCHER=%TEMP%\eidattendance-update-%RANDOM%%RANDOM%%RANDOM%.cmd"
  copy /y "%~f0" "!TEMP_LAUNCHER!" >nul
  call "!TEMP_LAUNCHER!" --relay %*
  set "EXITCODE=%ERRORLEVEL%"
  del /q "!TEMP_LAUNCHER!" >nul 2>&1
  exit /b !EXITCODE!
)

shift /1

set "SCRIPT_DIR=%EID_UPDATE_SCRIPT_DIR%"
if not defined SCRIPT_DIR set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"

set "DEFAULT_MANIFEST_URL=https://raw.githubusercontent.com/pclt-codebase/PCLT-eIDattendance/main/latest.json"
set "MANIFEST_FILE=%SCRIPT_DIR%\update-manifest-url.txt"
set "RESOLVED_MANIFEST_URL="

if exist "%MANIFEST_FILE%" (
  for /f "usebackq tokens=* delims=" %%L in ("%MANIFEST_FILE%") do (
    set "LINE=%%L"
    if defined LINE (
      if not "!LINE:~0,1!"=="#" (
        if not defined RESOLVED_MANIFEST_URL set "RESOLVED_MANIFEST_URL=!LINE!"
      )
    )
  )
)

if not defined RESOLVED_MANIFEST_URL if defined EID_UPDATE_MANIFEST_URL set "RESOLVED_MANIFEST_URL=%EID_UPDATE_MANIFEST_URL%"
if not defined RESOLVED_MANIFEST_URL set "RESOLVED_MANIFEST_URL=%DEFAULT_MANIFEST_URL%"

set "WAITPID="
if /I "%~1"=="--wait-pid" set "WAITPID=%~2"

set "WAITPID_ARG="
if defined WAITPID set "WAITPID_ARG=-WaitPid %WAITPID%"

set "INSTALL_DIR=%SCRIPT_DIR%"

powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%\update-eid.ps1" -InstallDir "%INSTALL_DIR%" -ManifestUrl "%RESOLVED_MANIFEST_URL%" %WAITPID_ARG%
set "EXITCODE=%ERRORLEVEL%"

if not "%EXITCODE%"=="0" (
  echo.
  echo [FOUT] Update is mislukt. Druk op een toets om af te sluiten...
  pause >nul
)

exit /b %EXITCODE%
