#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
echo '==> Model tests'
swiftc GPTDeskTopMonitor/Sources/MonitorModels.swift Tests/MonitorCoreTests.swift -o "${TMPDIR:-/tmp/}gptdesktop-monitor-model-tests"
"${TMPDIR:-/tmp/}gptdesktop-monitor-model-tests"
echo '==> Xcode macOS app build'
xcodebuild -project GPTDeskTopMonitor.xcodeproj -scheme 'GPTDeskTop Monitor Only' -configuration Debug -destination 'platform=macOS' -derivedDataPath build CODE_SIGNING_ALLOWED=NO build
