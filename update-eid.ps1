param(
    [Parameter(Mandatory = $false)]
    [string]$InstallDir = "",

    [Parameter(Mandatory = $false)]
    [string]$ManifestUrl = "",

    [Parameter(Mandatory = $false)]
    [int]$WaitPid = 0
)

$ErrorActionPreference = 'Stop'

function Resolve-ManifestUrl([string]$manifestUrlArg, [string]$installPath) {
    if (-not [string]::IsNullOrWhiteSpace($manifestUrlArg)) {
        return $manifestUrlArg.Trim()
    }

    $configPath = Join-Path $installPath 'update-manifest-url.txt'
    if (Test-Path $configPath) {
        foreach ($line in Get-Content $configPath) {
            $value = $line.Trim()
            if (-not [string]::IsNullOrWhiteSpace($value) -and -not $value.StartsWith('#')) {
                return $value
            }
        }
    }

    $fromEnvironment = [System.Environment]::GetEnvironmentVariable('EID_UPDATE_MANIFEST_URL')
    if (-not [string]::IsNullOrWhiteSpace($fromEnvironment)) {
        return $fromEnvironment.Trim()
    }

    return ''
}

function Get-VersionObject([string]$value) {
    try {
        return [Version]$value
    }
    catch {
        return [Version]'0.0.0'
    }
}

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $InstallDir = $PSScriptRoot
}

$installPath = (Resolve-Path $InstallDir).Path
$resolvedManifestUrl = Resolve-ManifestUrl $ManifestUrl $installPath
if ([string]::IsNullOrWhiteSpace($resolvedManifestUrl)) {
    throw 'Geen update manifest URL gevonden. Zet een URL in update-manifest-url.txt of variabele EID_UPDATE_MANIFEST_URL.'
}

$versionFile = Join-Path $installPath 'version.txt'
$runScriptPath = Join-Path $installPath 'run-eid.cmd'
$appExePath = Join-Path $installPath 'Pclt.EidAttendance.App.exe'
$currentVersionString = if (Test-Path $versionFile) { (Get-Content $versionFile -Raw).Trim() } else { '0.0.0' }
$currentVersion = Get-VersionObject $currentVersionString

Write-Host "[INFO] Huidige versie: $currentVersionString"
Write-Host "[INFO] Manifest ophalen: $resolvedManifestUrl"
$manifest = Invoke-RestMethod -Uri $resolvedManifestUrl -Method Get

if ($WaitPid -gt 0) {
    try {
        $process = Get-Process -Id $WaitPid -ErrorAction SilentlyContinue
        if ($null -ne $process) {
            Write-Host "[INFO] Wachten op afsluiten van proces $WaitPid..."
            $process.WaitForExit(60000)
        }
    }
    catch {
        Write-Host "[INFO] Kon proces $WaitPid niet monitoren. Verder met update."
    }
}

if (-not $manifest.version -or -not $manifest.url) {
    throw "Manifest is ongeldig. Vereist: version + url"
}

$remoteVersionString = [string]$manifest.version
$remoteVersion = Get-VersionObject $remoteVersionString

Write-Host "[INFO] Beschikbare versie: $remoteVersionString"
if ($remoteVersion -le $currentVersion) {
    Write-Host "[OK] Je gebruikt al de nieuwste versie."
    exit 0
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("eidattendance-update-" + [System.Guid]::NewGuid().ToString("N"))
$zipPath = Join-Path $tempRoot 'update.zip'
$extractPath = Join-Path $tempRoot 'extract'

New-Item -ItemType Directory -Path $tempRoot, $extractPath | Out-Null

try {
    Write-Host "[INFO] Download updatepakket..."
    Invoke-WebRequest -Uri ([string]$manifest.url) -OutFile $zipPath

    Write-Host "[INFO] Uitpakken..."
    Expand-Archive -Path $zipPath -DestinationPath $extractPath -Force

    Write-Host "[INFO] Bestanden bijwerken..."
    Copy-Item "$extractPath\*" $installPath -Recurse -Force

    Set-Content -Path $versionFile -Value $remoteVersionString -NoNewline

    # Start opnieuw op na een succesvolle update zodat gebruiker meteen de nieuwe versie draait.
    if (Test-Path $appExePath) {
        Start-Process -FilePath $appExePath -WorkingDirectory $installPath | Out-Null
        Write-Host "[INFO] Applicatie herstart via Pclt.EidAttendance.App.exe"
    }
    elseif (Test-Path $runScriptPath) {
        Start-Process -FilePath $runScriptPath -WorkingDirectory $installPath | Out-Null
        Write-Host "[INFO] Applicatie herstart via run-eid.cmd"
    }
    else {
        Write-Host "[WAARSCHUWING] Herstart overgeslagen: geen runscript of app-exe gevonden."
    }

    Write-Host "[OK] Update voltooid naar versie $remoteVersionString"
    exit 0
}
catch {
    Write-Error "Update mislukt: $($_.Exception.Message)"
    exit 1
}
finally {
    Remove-Item -Recurse -Force $tempRoot -ErrorAction SilentlyContinue
}
