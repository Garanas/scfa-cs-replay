#!/usr/bin/env bash
# Opens a map in a published map editor and draws it in the 3D view, on a virtual display with
# Mesa's software OpenGL (llvmpipe): a deeper start check than editor-smoke.sh, for CI on Linux and
# for trying a build in a container. Without converted assets the editor needs a workspace: the
# script makes one whose assets are empty but current (a manifest naming two empty stand-in archives
# of the game), so the terrain draws with its plain colours and props as boxes. It checks that the
# 3D view started on OpenGL, saves its picture and the window's, and closes the editor.
#
#   tools/editor-smoke-3d.sh <editor executable> <output folder> <map folder> [port]
#
# Start a display first (Xvfb :99 &; export DISPLAY=:99); xvfb-run hangs in some containers.
set -euo pipefail

exe="$1"
out="$2"
map="$3"
port="${4:-5398}"
mkdir -p "$out"
out="$(cd "$out" && pwd)"
map="$(cd "$map" && pwd)"

export FAF_MAP_EDITOR_SETTINGS="$out/settings.json"
export DOTNET_BUNDLE_EXTRACT_BASE_DIR="$out/bundle"
# Avalonia turns llvmpipe down for the window unless asked; the 3D view needs OpenGL
export FAF_MAP_EDITOR_SOFTWARE_GL=1

ask() {
  curl -s -m 60 -H "Content-Type: application/json" -d "${2:-{\}}" "http://localhost:$port/$1"
}

pid=""
start() {
  "$exe" "$@" --automation "$port" >> "$out/editor.log" 2>&1 &
  pid=$!
}

# waits until the state answers and jq's test on it holds; fails after 120 seconds
wait_for() {
  for _ in $(seq 1 120); do
    if ! kill -0 "$pid" 2> /dev/null; then
      echo "The editor exited:" >&2
      cat "$out/editor.log" >&2
      exit 1
    fi
    if answer="$(ask state)" && [ -n "$answer" ] && echo "$answer" | jq -e "$1" > /dev/null 2>&1; then
      return 0
    fi
    sleep 1
  done
  echo "The editor did not reach '$1' within 120 seconds; its state:" >&2
  ask state | head -c 3000 >&2 || true
  exit 1
}

stop() {
  ask close > /dev/null || true
  for _ in $(seq 1 20); do
    kill -0 "$pid" 2> /dev/null || return 0
    sleep 1
  done
  kill "$pid" 2> /dev/null || true
}

# 1. without a workspace, for the version the assets' manifest has to name
rm -f "$out/settings.json" "$out/editor.log"
start
wait_for '.stage == "SetUp"'
build="$(ask state | jq -c '.build.assets')"
stop
echo "Assets of format $(echo "$build" | jq -r .format) by editor $(echo "$build" | jq -r .editor)"

# 2. a workspace with empty, current assets
workspace="$out/workspace"
rm -rf "$workspace" "$out/game" "$out/vault"
mkdir -p "$workspace/assets" "$out/game/gamedata" "$out/vault"
touch "$out/game/gamedata/env.scd" "$out/game/gamedata/units.scd"
jq -n --arg game "$out/game" --arg vault "$out/vault" \
  '{formatVersion: 1, gameFolder: $game, vaultFolder: $vault}' > "$workspace/workspace.json"
stamp="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
jq -n --argjson build "$build" --arg game "$out/game/gamedata" --arg stamp "$stamp" \
  '{formatVersion: $build.format, editorVersion: $build.editor, converted: $stamp,
    archives: [{path: ($game + "/env.scd"), size: 0, lastWriteUtc: $stamp}, {path: ($game + "/units.scd"), size: 0, lastWriteUtc: $stamp}],
    textures: 0, meshes: 0, props: 0, units: 0}' > "$workspace/assets/manifest.json"
jq -n --arg workspace "$workspace" '{workspace: $workspace}' > "$out/settings.json"

# 3. the map in the 3D view
start "$map"
wait_for '.stage == "Ready" and .map != null and .busy == false'
ask tab '{"name": "3d"}' > /dev/null
ask wait '{"idle": true}' > /dev/null || true
ask screenshot "{\"path\": \"$out/view3d.png\", \"settle\": true, \"gpu\": true}" > "$out/screenshot3d.json"
ask screenshot "{\"path\": \"$out/window.png\", \"settle\": true}" > /dev/null
ask state > "$out/state.json"
stop

status=0
renderer="$(jq -r '.gpu.renderer // empty' "$out/state.json")"
error="$(jq -r '.gpu.error // empty' "$out/state.json")"
echo "The map: $(jq -r '.map.name // "none"' "$out/state.json"); the 3D view: ${renderer:-no OpenGL}"
if [ -z "$renderer" ] || [ -n "$error" ]; then
  echo "The 3D view did not start: ${error:-no renderer}" >&2
  status=1
fi
if [ ! -s "$out/view3d.png" ]; then
  echo "No picture of the 3D view was written (screenshot3d.json):" >&2
  cat "$out/screenshot3d.json" >&2
  status=1
fi
[ "$status" -eq 0 ] && echo "The editor opened the map and drew it in 3D"
exit "$status"
