# ─────────────────────────────────────────────────────────────────────────────
# EN: Publishes Rfx2Mqtt for the 3 target platforms as self-contained portable archives.
#     Run from the solution root:  pwsh scripts/publish-all.ps1
#     Output: ./release/Rfx2Mqtt-<version>-<rid>.zip
#
# FR: Publie Rfx2Mqtt pour les 3 plateformes cibles en archives portables auto-contenues.
#     À lancer depuis la racine de la solution :  pwsh scripts/publish-all.ps1
#     Sortie : ./release/Rfx2Mqtt-<version>-<rid>.zip
# ─────────────────────────────────────────────────────────────────────────────

param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "Rfx2Mqtt/Rfx2Mqtt.csproj"
$releaseDir = Join-Path $root "release"

if (-not (Test-Path $proj)) {
    Write-Error "Project not found / Projet introuvable : $proj"
    exit 1
}

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

# EN: One entry per target — (RID, friendly label).
# FR: Une entrée par cible — (RID, libellé lisible).
$targets = @(
    @{ Rid = "linux-x64";   Label = "Linux x64 (PC/server)" }
    @{ Rid = "linux-arm64"; Label = "Linux ARM64 (Raspberry Pi 4/5, RockPro)" }
    @{ Rid = "win-x64";     Label = "Windows x64" }
)

foreach ($t in $targets) {
    $rid = $t.Rid
    $label = $t.Label
    $outDir = Join-Path $root "artifacts/$rid"
    $zipPath = Join-Path $releaseDir "Rfx2Mqtt-$Version-$rid.zip"

    Write-Host ""
    Write-Host "─── Publishing $label ──────────────────────────────" -ForegroundColor Cyan

    # EN: Clean previous output for this RID so old files don't pile up.
    # FR: Nettoyer la sortie précédente pour ce RID pour éviter l'accumulation.
    if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }

    dotnet publish $proj `
        -c $Configuration `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=true `
        -o $outDir
    if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed for $rid"; exit 1 }

    # EN: Replace the published appsettings.json with the clean example (no credentials).
    #     The real appsettings.json is gitignored; the example is the safe template to ship.
    # FR: Remplacer l'appsettings.json publié par l'exemple propre (sans identifiants).
    #     Le vrai appsettings.json est dans le .gitignore ; l'exemple est le template à livrer.
    $exampleSettings = Join-Path $root "Rfx2Mqtt/appsettings.example.json"
    $publishedSettings = Join-Path $outDir "appsettings.json"
    if (Test-Path $exampleSettings) {
        Copy-Item -Force $exampleSettings $publishedSettings
        Write-Host "  [ok] appsettings.json replaced with example template" -ForegroundColor DarkGray
    }

    # EN: Drop a README beside the binaries.
    # FR: Déposer un README à côté des binaires.
    $readme = Join-Path $outDir "README-FIRST.txt"
    @"
Rfx2Mqtt — portable build for $label
═══════════════════════════════════════════════════════════════

EN: To run this build:
  1. Edit appsettings.json (broker IP, port, RFXCom port name)
  2. On Linux:   chmod +x Rfx2Mqtt && ./Rfx2Mqtt
     On Windows: .\Rfx2Mqtt.exe
  3. Open http://<host>:5080 in a browser.

FR: Pour lancer ce build :
  1. Éditer appsettings.json (IP broker, port, nom du port RFXCom)
  2. Sous Linux :   chmod +x Rfx2Mqtt && ./Rfx2Mqtt
     Sous Windows : .\Rfx2Mqtt.exe
  3. Ouvrir http://<host>:5080 dans un navigateur.

A blank data/devices.yaml will be created on first run, or migrated from the
legacy device sections of your appsettings.json if present.

Un data/devices.yaml vierge sera créé au premier lancement, ou migré depuis
les sections d'appareils legacy de votre appsettings.json si présentes.
"@ | Set-Content -Encoding UTF8 $readme

    # EN: Zip the published folder.
    # FR: Zipper le dossier publié.
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    Compress-Archive -Path "$outDir/*" -DestinationPath $zipPath -CompressionLevel Optimal

    $sizeMb = [Math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Host "  -> $zipPath ($sizeMb MB)" -ForegroundColor Green
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "All builds done. Output in: $releaseDir"
Write-Host "Builds terminés. Sortie dans : $releaseDir"
