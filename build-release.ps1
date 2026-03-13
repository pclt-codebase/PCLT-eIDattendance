param(
    [Parameter(Mandatory = $false)]
    [string]$Version = "1.0.0",

    [Parameter(Mandatory = $false)]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishDir = Join-Path $root "dist\$Runtime"
$installerPublishDir = Join-Path $root "dist\installer\$Runtime"
$stageDir = Join-Path $root "dist\stage"
$zipPath = Join-Path $root "dist\eidattendance-$Version-$Runtime.zip"
$setupPath = Join-Path $root "dist\eidattendance-setup-$Version-$Runtime.exe"
$manifestPath = Join-Path $root "dist\latest.json"

Write-Host "[INFO] Clean output folders..."
Remove-Item -Recurse -Force $publishDir, $installerPublishDir, $stageDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $publishDir, $installerPublishDir, $stageDir | Out-Null

Write-Host "[INFO] Publish self-contained app ($Runtime)..."
dotnet publish "$root\Pclt.EidAttendance.App\Pclt.EidAttendance.App.csproj" `
    -c Release `
    -r $Runtime `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:PublishTrimmed=false `
    /p:Version=$Version `
    -o $publishDir

Write-Host "[INFO] Publish standalone installer ($Runtime)..."
dotnet publish "$root\Pclt.EidAttendance.Installer\Pclt.EidAttendance.Installer.csproj" `
    -c Release `
    -r $Runtime `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:PublishTrimmed=false `
    /p:Version=$Version `
    -o $installerPublishDir

Write-Host "[INFO] Prepare distributable package..."
Copy-Item "$publishDir\Pclt.EidAttendance.App.exe" "$stageDir\Pclt.EidAttendance.App.exe" -Force
Copy-Item "$root\Pclt.EidAttendance.App\app.ico" "$stageDir\app.ico" -Force
Copy-Item "$root\run-eid.cmd" "$stageDir\run-eid.cmd" -Force
Copy-Item "$root\update-eid.cmd" "$stageDir\update-eid.cmd" -Force
Copy-Item "$root\update-eid.ps1" "$stageDir\update-eid.ps1" -Force
Copy-Item "$root\update-manifest-url.txt" "$stageDir\update-manifest-url.txt" -Force
Set-Content -Path (Join-Path $stageDir "version.txt") -Value $Version -NoNewline

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
Compress-Archive -Path "$stageDir\*" -DestinationPath $zipPath

if (Test-Path $setupPath) {
    Remove-Item $setupPath -Force
}

Copy-Item "$installerPublishDir\Pclt.EidAttendance.Installer.exe" "$setupPath" -Force

$manifest = @{
    version      = $Version
    url          = "https://YOUR-HOST/eidattendance-$Version-$Runtime.zip"
    installerUrl = "https://YOUR-HOST/eidattendance-setup-$Version-$Runtime.exe"
}
$manifest | ConvertTo-Json | Set-Content -Path $manifestPath -Encoding UTF8

Write-Host "[OK] Klaar"
Write-Host "[INFO] Zip : $zipPath"
Write-Host "[INFO] Setup EXE : $setupPath"
Write-Host "[INFO] Manifest template: $manifestPath"
Write-Host "[INFO] Pas latest.json 'url' en 'installerUrl' aan naar jouw echte downloadlinks en upload alle artifacts."
