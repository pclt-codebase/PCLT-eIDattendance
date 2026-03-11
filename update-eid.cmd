@echo off
setlocal EnableExtensions EnableDelayedExpansion

cd /d "%~dp0"

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

if not defined RESOLVED_MANIFEST_URL (
  echo [FOUT] Geen update manifest URL gevonden.
  echo Zet een geldige URL in update-manifest-url.txt of variabele EID_UPDATE_MANIFEST_URL.
  echo.
  pause >nul
  exit /b 1
)

set "WAITPID="
if /I "%~1"=="--wait-pid" set "WAITPID=%~2"

set "WAITPID_ARG="
if defined WAITPID set "WAITPID_ARG=-WaitPid %WAITPID%"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0update-eid.ps1" -InstallDir "%~dp0" -ManifestUrl "%RESOLVED_MANIFEST_URL%" %WAITPID_ARG%
set "EXITCODE=%ERRORLEVEL%"

if not "%EXITCODE%"=="0" (
  echo.
  echo [FOUT] Update is mislukt. Druk op een toets om af te sluiten...
  pause >nul
)

exit /b %EXITCODE%
