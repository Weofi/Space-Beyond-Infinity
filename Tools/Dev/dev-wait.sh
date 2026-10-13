#!/usr/bin/env bash
# Wait for a dev test harness marker (#65) in the Unity editor log instead of sleeping.
#   Tools/Dev/dev-wait.sh <tag> [timeout_seconds=180]
# Finds the last "[Dev] begin <tag>" and waits until a "[Dev] end <tag>" line follows it, then prints that line.
# Exit 0 on success, 1 when the end line contains FAILED, 2 on timeout / no begin marker.
# Log path: $VW_LOG, else Logs/Editor.log (Windows, project-local), else ~/Library/Logs/Unity/Editor.log (Mac).
tag="$1"; timeout="${2:-180}"
[ -z "$tag" ] && { echo "usage: dev-wait.sh <tag> [timeout]"; exit 2; }
root="$(cd "$(dirname "$0")/../.." && pwd)"
log="${VW_LOG:-$root/Logs/Editor.log}"
[ -f "$log" ] || log="$HOME/Library/Logs/Unity/Editor.log"
end=$((SECONDS + timeout))
while [ $SECONDS -lt $end ]; do
  begin=$(grep -a -n -F "[Dev] begin $tag" "$log" | tail -1 | cut -d: -f1)
  if [ -n "$begin" ]; then
    line=$(tail -n +"$begin" "$log" | grep -a -F "[Dev] end $tag" | head -1)
    if [ -n "$line" ]; then
      echo "$line"
      case "$line" in *FAILED*) exit 1 ;; esac
      exit 0
    fi
  fi
  sleep 1
done
echo "timeout waiting for [Dev] end $tag"
exit 2
