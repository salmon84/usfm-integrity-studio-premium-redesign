#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "macOS packaging requires macOS."
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"
PROJECT_DIR="$REPO_DIR/Premium"
RID="${1:-osx-arm64}"
VERSION="${2:-0.1.0}"
REVISION="${3:-$(git -C "$REPO_DIR" rev-parse --verify HEAD)}"
ASSEMBLY_NAME="UISPremiumRedesign"
PRODUCT_NAME="USFM Integrity Studio Premium"
PACKAGE_NAME="USFM-Integrity-Studio-Premium"
PUBLISH_DIR="$PROJECT_DIR/publish/$RID"
DIST_DIR="$PROJECT_DIR/dist"
APP_DIR="$DIST_DIR/$PRODUCT_NAME.app"
CONTENTS_DIR="$APP_DIR/Contents"
DMG_STAGE="$PROJECT_DIR/publish/dmg-$RID"
ICONSET_DIR="$PROJECT_DIR/publish/UISPremium.iconset"

case "$RID" in
  osx-arm64|osx-x64) ;;
  *) echo "Unsupported macOS runtime: $RID"; exit 2 ;;
esac

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export AVALONIA_TELEMETRY_OPTOUT=1

rm -rf "$PUBLISH_DIR" "$APP_DIR" "$DMG_STAGE" "$ICONSET_DIR"
mkdir -p "$PUBLISH_DIR" "$DIST_DIR" "$CONTENTS_DIR/MacOS" \
  "$CONTENTS_DIR/Resources/legal" "$DMG_STAGE" "$ICONSET_DIR"

dotnet publish "$PROJECT_DIR/UsfmIntegrityStudio.csproj" \
  --configuration Release \
  --runtime "$RID" \
  --self-contained true \
  --output "$PUBLISH_DIR" \
  -p:PublishSingleFile=false \
  -p:PublishTrimmed=false \
  -p:RestoreLockedMode=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -p:Version="$VERSION" \
  -p:SourceRevisionId="$REVISION" \
  -p:BuildChannel=official \
  -p:OfficialBuild=true

cp -R "$PUBLISH_DIR"/. "$CONTENTS_DIR/MacOS/"
chmod +x "$CONTENTS_DIR/MacOS/$ASSEMBLY_NAME"

cp "$REPO_DIR/LICENSE" "$CONTENTS_DIR/Resources/legal/LICENSE.txt"
cp "$REPO_DIR/COPYRIGHT.md" "$CONTENTS_DIR/Resources/legal/"
cp "$REPO_DIR/PRIVACY.md" "$CONTENTS_DIR/Resources/legal/"
cp "$REPO_DIR/THIRD_PARTY_NOTICES.md" "$CONTENTS_DIR/Resources/legal/"
cp "$PROJECT_DIR/NOTICE" "$CONTENTS_DIR/Resources/legal/NOTICE.txt"
cp "$PROJECT_DIR/TRADEMARK_POLICY.md" "$CONTENTS_DIR/Resources/legal/"
cp "$PROJECT_DIR/packages.lock.json" \
  "$CONTENTS_DIR/Resources/legal/DEPENDENCIES.lock.json"

for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$PROJECT_DIR/Assets/dgv-logo.png" \
    --out "$ICONSET_DIR/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z "$double" "$double" "$PROJECT_DIR/Assets/dgv-logo.png" \
    --out "$ICONSET_DIR/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET_DIR" -o "$CONTENTS_DIR/Resources/UISPremium.icns"

cat > "$CONTENTS_DIR/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>$PRODUCT_NAME</string>
  <key>CFBundleDisplayName</key><string>$PRODUCT_NAME</string>
  <key>CFBundleIdentifier</key><string>org.digitalglobalvillage.uispremium</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleExecutable</key><string>$ASSEMBLY_NAME</string>
  <key>CFBundleIconFile</key><string>UISPremium</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
EOF

plutil -lint "$CONTENTS_DIR/Info.plist"

ditto -c -k --sequesterRsrc --keepParent "$APP_DIR" \
  "$DIST_DIR/${PACKAGE_NAME}-${VERSION}-$RID.app.zip"

cp -R "$APP_DIR" "$DMG_STAGE/"
ln -s /Applications "$DMG_STAGE/Applications"
hdiutil create \
  -volname "$PRODUCT_NAME $VERSION" \
  -srcfolder "$DMG_STAGE" \
  -ov \
  -format UDZO \
  "$DIST_DIR/${PACKAGE_NAME}-${VERSION}-$RID.dmg" >/dev/null

echo "Created macOS packages for $RID"
