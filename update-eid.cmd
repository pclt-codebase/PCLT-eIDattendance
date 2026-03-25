@echo off
setlocal EnableExtensions

cd /d "%~dp0"

set "WAITPID="
if /I "%~1"=="--wait-pid" set "WAITPID=%~2"

set "WAITPID_ARG="
if defined WAITPID set "WAITPID_ARG=-WaitPid %WAITPID%"

set "INSTALL_DIR=%~dp0"
if "%INSTALL_DIR:~-1%"=="\" set "INSTALL_DIR=%INSTALL_DIR:~0,-1%"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0update-eid.ps1" -InstallDir "%INSTALL_DIR%" %WAITPID_ARG%
set "EXITCODE=%ERRORLEVEL%"

if not "%EXITCODE%"=="0" (
  echo.
  echo [FOUT] Update is mislukt. Druk op een toets om af te sluiten...
  pause >nul
)

exit /b %EXITCODE%
