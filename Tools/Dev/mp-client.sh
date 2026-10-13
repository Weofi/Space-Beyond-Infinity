#!/usr/bin/env bash
# Start the development build as an MP test client (#65) that joins the editor host on this machine.
#   Tools/Dev/mp-client.sh [scenario=lab] [shot_interval_s=4]
# Flags: -skipintro (no stuck intro), -devscenario (same lab/planet set-up as the host), -devshots (self screenshots;
# with a lab it renders the lab camera and logs "[Dev] client<id>_<t> decals N").
# Log: %TEMP%/VoidwardDev/client.log (wait with: VW_LOG="$TMP_DIR/client.log" Tools/Dev/dev-wait.sh quickstart).
# Stop: Windows `Stop-Process -Name Voidward`, Mac `pkill -f Voidward.app`.
scenario="${1:-lab}"; shots="${2:-4}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
tmp="${TEMP:-${TMPDIR:-/tmp}}"; out="$tmp/VoidwardDev"; mkdir -p "$out"
if [ -f "$root/Builds/Windows-Dev/Voidward.exe" ]; then exe="$root/Builds/Windows-Dev/Voidward.exe"
elif [ -x "$root/Builds/macOS-Dev/Voidward.app/Contents/MacOS/Voidward" ]; then exe="$root/Builds/macOS-Dev/Voidward.app/Contents/MacOS/Voidward"
else echo "no development build found (Voidward/Build/... (Development))"; exit 1; fi
"$exe" -transport=utp -autojoin=127.0.0.1 -skipintro -devscenario="$scenario" -devshots="$shots" \
  -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "$out/client.log" > /dev/null 2>&1 &
echo "client started ($scenario), log: $out/client.log"
