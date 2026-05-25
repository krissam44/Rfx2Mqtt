#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# EN: Publishes Rfx2Mqtt for the 3 target platforms as self-contained portable archives.
#     Run from the solution root:  ./scripts/publish-all.sh
#     Output: ./release/Rfx2Mqtt-<version>-<rid>.zip
#
# FR: Publie Rfx2Mqtt pour les 3 plateformes cibles en archives portables auto-contenues.
#     À lancer depuis la racine de la solution :  ./scripts/publish-all.sh
#     Sortie : ./release/Rfx2Mqtt-<version>-<rid>.zip
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

VERSION="${1:-1.0.0}"
CONFIG="${2:-Release}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="$ROOT/Rfx2Mqtt/Rfx2Mqtt.csproj"
RELEASE_DIR="$ROOT/release"

[ -f "$PROJ" ] || { echo "Project not found: $PROJ"; exit 1; }
mkdir -p "$RELEASE_DIR"

declare -a TARGETS=(
    "linux-x64|Linux x64 (PC/server)"
    "linux-arm64|Linux ARM64 (Raspberry Pi 4/5, RockPro)"
    "win-x64|Windows x64"
)

for entry in "${TARGETS[@]}"; do
    rid="${entry%%|*}"
    label="${entry##*|}"
    out_dir="$ROOT/artifacts/$rid"
    zip_path="$RELEASE_DIR/Rfx2Mqtt-$VERSION-$rid.zip"

    echo ""
    echo "─── Publishing $label ──────────────────────────────"
    rm -rf "$out_dir"

    dotnet publish "$PROJ" \
        -c "$CONFIG" \
        -r "$rid" \
        --self-contained true \
        -p:PublishSingleFile=false \
        -p:PublishReadyToRun=true \
        -o "$out_dir"

    # Replace published appsettings.json with the clean example (no credentials).
    example_settings="$ROOT/Rfx2Mqtt/appsettings.example.json"
    if [ -f "$example_settings" ]; then
        cp "$example_settings" "$out_dir/appsettings.json"
        echo "  [ok] appsettings.json replaced with example template"
    fi

    cat > "$out_dir/README-FIRST.txt" <<EOF
Rfx2Mqtt — portable build for $label
═══════════════════════════════════════════════════════════════

EN: To run this build:
  1. Edit appsettings.json (broker IP, port, RFXCom port name)
  2. On Linux:   chmod +x Rfx2Mqtt && ./Rfx2Mqtt
     On Windows: .\\Rfx2Mqtt.exe
  3. Open http://<host>:5080 in a browser.

FR: Pour lancer ce build :
  1. Éditer appsettings.json (IP broker, port, nom du port RFXCom)
  2. Sous Linux :   chmod +x Rfx2Mqtt && ./Rfx2Mqtt
     Sous Windows : .\\Rfx2Mqtt.exe
  3. Ouvrir http://<host>:5080 dans un navigateur.
EOF

    rm -f "$zip_path"
    (cd "$out_dir" && zip -qr "$zip_path" .)

    size_mb=$(du -m "$zip_path" | cut -f1)
    echo "  -> $zip_path (~${size_mb} MB)"
done

echo ""
echo "═══════════════════════════════════════════════════════"
echo "All builds done. Output in: $RELEASE_DIR"
echo "Builds terminés. Sortie dans : $RELEASE_DIR"
