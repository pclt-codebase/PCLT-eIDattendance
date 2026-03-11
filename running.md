## Eén CMD-commando (alles automatisch)

Voer dit uit vanaf eender welke map:

```cmd
cmd /c C:\Scripts\eIDAttendance\run-eid.cmd
```

Dit start `run-eid.cmd`, dat automatisch de Belgische middleware DLL zoekt, mock-modus uitschakelt en de app start met duidelijke foutmelding als middleware ontbreekt.

Bij fouten blijft het venster nu open tot je een toets indrukt.

Wil je het venster altijd open houden, gebruik dan:

```cmd
cmd /k C:\Scripts\eIDAttendance\run-eid.cmd
```

Ondersteunde DLL-namen: `beid_ff_pkcs11_64.dll`, `beid_ff_pkcs11_32.dll`, `beid_ff_pkcs11.dll`, `beidpkcs11.dll`.

## EXE build + distributie

Maak een releasepakket met:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File C:\Scripts\eIDAttendance\build-release.ps1 -Version 1.0.0
```

Output:

- `dist/eidattendance-<versie>-win-x64.zip`
- `dist/eidattendance-setup-<versie>-win-x64.exe`
- `dist/latest.json` (manifest template)

Pas in `dist/latest.json` de `url` en `installerUrl` aan naar je echte downloadlocaties en upload dan:

1. De zip (`eidattendance-<versie>-win-x64.zip`)
2. De installer (`eidattendance-setup-<versie>-win-x64.exe`)
3. Het manifest (`latest.json`)

Bijvoorbeeld op GitHub Releases, Azure Blob Storage of een andere publieke HTTPS-locatie.

## Updates voor eindgebruikers

Nieuwe installatie doe je met de installer-EXE:

1. Download `eidattendance-setup-<versie>-win-x64.exe`
2. Start de EXE
3. De app wordt geïnstalleerd in `%LocalAppData%\Programs\Pclt.EidAttendance`
4. Desktop + Startmenu snelkoppeling worden automatisch aangemaakt

In de uitgepakte app-map staat `update-eid.cmd`.

### Vanuit de app (aanbevolen)

Bij opstart controleert de app op updates als `update-manifest-url.txt` correct is ingesteld.

1. Open `update-manifest-url.txt`
2. Zet daar de publieke URL naar jouw `latest.json`
3. In de app ziet de gebruiker dan "Update beschikbaar" + knop **Download update**

Bij klik op **Download update** start de lokale updater automatisch, de app sluit af en de update wordt geïnstalleerd.

### Via script (alternatief)

Gebruiker kan ook nog steeds `update-eid.cmd` draaien om handmatig te updaten.

Manifest formaat:

```json
{
  "version": "1.0.0",
  "url": "https://jouw-host/eidattendance-1.0.0-win-x64.zip",
  "installerUrl": "https://jouw-host/eidattendance-setup-1.0.0-win-x64.exe"
}
```

Daarna start de gebruiker de app met `run-eid.cmd` (die nu automatisch de lokale `.exe` gebruikt als die aanwezig is).
