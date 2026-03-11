param(
    [Parameter(Mandatory = $true)]
    [string]$InstallDir,

    [Parameter(Mandatory = $true)]
    [string]$ManifestUrl,

    [Parameter(Mandatory = $false)]
    [int]$WaitPid = 0
)

$ErrorActionPreference = 'Stop'

function Get-VersionObject([string]$value) {
    try {
        return [Version]$value
    }
    catch {
        return [Version]'0.0.0'
    }
}

$installPath = (Resolve-Path $InstallDir).Path
$versionFile = Join-Path $installPath 'version.txt'
$currentVersionString = if (Test-Path $versionFile) { (Get-Content $versionFile -Raw).Trim() } else { '0.0.0' }
$currentVersion = Get-VersionObject $currentVersionString

Write-Host "[INFO] Huidige versie: $currentVersionString"
Write-Host "[INFO] Manifest ophalen: $ManifestUrl"
$manifest = Invoke-RestMethod -Uri $ManifestUrl -Method Get

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
