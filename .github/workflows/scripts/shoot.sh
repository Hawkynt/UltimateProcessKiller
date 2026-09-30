#!/usr/bin/env bash
# Capture one deterministic screenshot of the GUI. Usage: shoot.sh <scene> <output-dir>
#
# It runs the built GUI under Xvfb with a minimal window manager and photographs the mapped window.
# GDK_BACKEND=x11 is required: without it GDK will not map a window under Xvfb and the shot is blank.
# The caller must have built the GUI (Release) and compiled screenshot-wm + lswin next to this script.
set -euo pipefail

SCENE="${1:?usage: shoot.sh <scene> <output-dir>}"
OUT="${2:?usage: shoot.sh <scene> <output-dir>}"
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../../.." && pwd)"
GUI="$REPO/UltimateProcessKiller.Gui/bin/Release/net10.0/upk-gui.dll"

mkdir -p "$OUT"
export DISPLAY=:99
export GDK_BACKEND=x11

Xvfb :99 -screen 0 940x620x24 >/tmp/xvfb.log 2>&1 &
XVFB=$!
sleep 2
"$HERE/screenshot-wm" 2>/tmp/wm.log &
WM=$!
sleep 1
dotnet "$GUI" --demo --scene "$SCENE" --exit-after 60 >/tmp/gui.log 2>&1 &
GUI_PID=$!
sleep 10

cleanup() { kill "$GUI_PID" "$WM" "$XVFB" 2>/dev/null || true; }
trap cleanup EXIT

WID="$("$HERE/lswin" | sort -k2 -t' ' | tail -1 | cut -d' ' -f1)"
if [ -z "$WID" ]; then
  echo "::error::no GUI window mapped for scene '$SCENE'"; cat /tmp/gui.log || true; exit 1
fi

import -window "$WID" "$OUT/$SCENE.png"

# A blank capture is worse than none: assert the shot has real content.
read -r W H COLORS <<<"$(magick "$OUT/$SCENE.png" -format "%w %h %k" info:)"
echo "captured $SCENE.png: ${W}x${H}, ${COLORS} colors"
if [ "$COLORS" -lt 16 ] || [ "$W" -lt 800 ]; then
  echo "::error::screenshot for '$SCENE' looks blank (${W}x${H}, ${COLORS} colors)"; exit 1
fi
