@echo off
setlocal EnableExtensions EnableDelayedExpansion

cd /d "%~dp0"

set "DEFAULT_MANIFEST_URL=https://raw.githubusercontent.com/pclt-codebase/PCLT-eIDattendance/main/latest.json"
set "MANIFEST_FILE=%~dp0update-manifest-url.txt"
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

set "INSTALL_DIR=%~dp0"
if "%INSTALL_DIR:~-1%"=="\" set "INSTALL_DIR=%INSTALL_DIR:~0,-1%"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0update-eid.ps1" -InstallDir "%INSTALL_DIR%" -ManifestUrl "%RESOLVED_MANIFEST_URL%" %WAITPID_ARG%
set "EXITCODE=%ERRORLEVEL%"

if not "%EXITCODE%"=="0" (
  echo.
  echo [FOUT] Update is mislukt. Druk op een toets om af te sluiten...
  pause >nul
)

exit /b %EXITCODE%
