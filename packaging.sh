#!/usr/bin/env bash
#
# Builds all four release variants and packages them into /tmp/uptime.
#
#   Linux builds
#     fx-linux-x64.tar.gz
#     scd-linux-x64.tar.gz
#   
#   Windows builds
#     fx-win-x64.zip
#     scd-win-x64.zip
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/dotnet-uptime/dotnet-uptime.csproj"
PUBROOT="$SCRIPT_DIR/dotnet-uptime/bin/Release/net10.0/publish"
OUT="/tmp/uptime"

# the Windows archives need zip
command -v zip >/dev/null 2>&1 || { echo "error: 'zip' is required but not installed." >&2; exit 1; }

echo "==> Preparing $OUT"
rm -rf "$OUT"
mkdir -p "$OUT"

echo "==> Cleaning previous publish output"
rm -rf "$PUBROOT"

publish() {
    echo "==> Publishing $1"
    dotnet publish "$PROJECT" -p:PublishProfile="$1" --nologo -v minimal
}

publish fxdependent-linux-x64
publish selfcontained-linux-x64
publish fxdependent-win-x64
publish selfcontained-win-x64

echo "==> Archiving Linux builds (tar.gz)"
tar -czf "$OUT/fx-linux-x64.tar.gz"  -C "$PUBROOT/fxdependent-linux-x64" .
tar -czf "$OUT/scd-linux-x64.tar.gz" -C "$PUBROOT/linux-x64" .

echo "==> Archiving Windows builds (zip)"
( cd "$PUBROOT/fxdependent-win-x64" && zip -qr "$OUT/fx-win-x64.zip" . )
( cd "$PUBROOT/win-x64"             && zip -qr "$OUT/scd-win-x64.zip" . )

echo "==> Contents of $OUT"
ls -lah "$OUT"
