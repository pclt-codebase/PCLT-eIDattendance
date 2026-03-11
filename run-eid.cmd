@echo off
setlocal EnableExtensions

cd /d "%~dp0"

set "EID_PKCS11_PATH="
if "%PROCESSOR_ARCHITECTURE%"=="AMD64" (
  if exist "C:\Program Files (x86)\Belgium Identity Card\FireFox Plugin Manifests\beid_ff_pkcs11_64.dll" set "EID_PKCS11_PATH=C:\Program Files (x86)\Belgium Identity Card\FireFox Plugin Manifests\beid_ff_pkcs11_64.dll"
) else (
  if exist "C:\Program Files (x86)\Belgium Identity Card\FireFox Plugin Manifests\beid_ff_pkcs11_32.dll" set "EID_PKCS11_PATH=C:\Program Files (x86)\Belgium Identity Card\FireFox Plugin Manifests\beid_ff_pkcs11_32.dll"
)

if not defined EID_PKCS11_PATH if exist "C:\Program Files (x86)\Belgium Identity Card\EidViewer\beid_ff_pkcs11.dll" set "EID_PKCS11_PATH=C:\Program Files (x86)\Belgium Identity Card\EidViewer\beid_ff_pkcs11.dll"
if not defined EID_PKCS11_PATH if exist "C:\Program Files\Belgium Identity Card\beidpkcs11.dll" set "EID_PKCS11_PATH=C:\Program Files\Belgium Identity Card\beidpkcs11.dll"
if not defined EID_PKCS11_PATH if exist "C:\Program Files (x86)\Belgium Identity Card\beidpkcs11.dll" set "EID_PKCS11_PATH=C:\Program Files (x86)\Belgium Identity Card\beidpkcs11.dll"

if not defined EID_PKCS11_PATH (
  echo [FOUT] beID middleware DLL niet gevonden.
  echo Gezocht naar: beid_ff_pkcs11_64.dll, beid_ff_pkcs11_32.dll, beid_ff_pkcs11.dll, beidpkcs11.dll
  echo Installeer de Belgische eID middleware en probeer opnieuw.
  echo.
  echo Druk op een toets om dit venster te sluiten...
  pause >nul
  exit /b 1
)

set "EID_ALLOW_MOCK=0"
echo [INFO] EID_PKCS11_PATH=%EID_PKCS11_PATH%
echo [INFO] EID_ALLOW_MOCK=%EID_ALLOW_MOCK%
echo [INFO] Start app...

if exist ".\Pclt.EidAttendance.App.exe" (
  .\Pclt.EidAttendance.App.exe
) else (
  dotnet run --project .\Pclt.EidAttendance.App
)
set "EXITCODE=%ERRORLEVEL%"

echo [INFO] Afgesloten met code %EXITCODE%.
if not "%EXITCODE%"=="0" (
  echo.
  echo [FOUT] Er is een fout opgetreden. Druk op een toets om dit venster te sluiten...
  pause >nul
)
exit /b %EXITCODE%
