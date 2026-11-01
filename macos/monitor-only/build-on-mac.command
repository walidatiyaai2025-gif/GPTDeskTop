#!/bin/zsh
set -euo pipefail
cd "$(dirname "$0")"
if ! xcode-select -p >/dev/null 2>&1; then
  echo "Xcode command line tools are required. Install/choose Xcode first."
  exit 1
fi
xcodebuild -project GPTDeskTopMonitor.xcodeproj \
  -scheme 'GPTDeskTop Monitor Only' \
  -configuration Release \
  -destination 'generic/platform=macOS' \
  -derivedDataPath "$PWD/build" \
  CODE_SIGNING_ALLOWED=NO build
APP="$PWD/build/Build/Products/Release/GPTDeskTop Monitor Only.app"
if [[ ! -d "$APP" ]]; then echo "Build reported success but app bundle not found"; exit 1; fi
mkdir -p "$PWD/dist"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$PWD/dist/GPTDeskTop-Monitor-Only-Mac.zip"
echo "Ready: $APP"
echo "ZIP: $PWD/dist/GPTDeskTop-Monitor-Only-Mac.zip"
