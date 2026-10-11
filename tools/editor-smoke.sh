#!/usr/bin/env bash
# Starts a published map editor without a workspace, waits until its automation answers, checks
# that it shows the setup page with the window's controls, saves a screenshot of the window and
# closes it. For CI on Linux (run under xvfb-run) and for trying a build in a container.
#
#   tools/editor-smoke.sh <editor executable> <output folder> [port]
set -euo pipefail

exe="$1"
out="$2"
port="${3:-5399}"
mkdir -p "$out"
out="$(cd "$out" && pwd)"

# a settings file of its own, so nothing of a real workspace is read or written
export FAF_MAP_EDITOR_SETTINGS="$out/settings.json"
export DOTNET_BUNDLE_EXTRACT_BASE_DIR="$out/bundle"

"$exe" --automation "$port" > "$out/editor.log" 2>&1 &
pid=$!

ask() {
  curl -s -m 10 -H "Content-Type: application/json" -d "${2:-{\}}" "http://localhost:$port/$1"
}

stage=""
for _ in $(seq 1 90); do
  if ! kill -0 "$pid" 2> /dev/null; then
    echo "The editor exited before its automation answered:" >&2
    cat "$out/editor.log" >&2
    exit 1
  fi
  if answer="$(ask state)" && [ -n "$answer" ]; then
    stage="$(echo "$answer" | jq -r '.stage // empty')"
    [ -n "$stage" ] && break
  fi
  sleep 1
done

if [ -z "$stage" ]; then
  echo "The editor's automation did not answer within 90 seconds" >&2
  cat "$out/editor.log" >&2
  kill "$pid" 2> /dev/null || true
  exit 1
fi
echo "Stage: $stage"
ask state > "$out/state.json"
ask ui > "$out/ui.json"
ask screenshot "{\"path\": \"$out/window.png\", \"settle\": true}" > "$out/screenshot.json"

status=0
if [ "$stage" != "SetUp" ]; then
  echo "Expected the setup page (no workspace), got stage $stage" >&2
  status=1
fi
if ! jq -e '.. | strings | select(test("Workspace folder"; "i"))' "$out/ui.json" > /dev/null; then
  echo "The setup page's controls are missing from the window (ui.json)" >&2
  status=1
fi
if [ ! -s "$out/window.png" ]; then
  echo "No screenshot was written (screenshot.json)" >&2
  status=1
fi

ask close > /dev/null || true
for _ in $(seq 1 20); do
  kill -0 "$pid" 2> /dev/null || break
  sleep 1
done
kill "$pid" 2> /dev/null || true
[ "$status" -eq 0 ] && echo "The editor started, showed the setup page and closed"
exit "$status"
