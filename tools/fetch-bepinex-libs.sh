#!/usr/bin/env bash
# Downloads the BepInEx releases this mod compiles against into ./lib
# (only needed to build from source; players just use the DLLs in dist/).
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p lib && cd lib

fetch() { # url dir
  if [ -d "$2" ]; then echo "lib/$2 already present"; return; fi
  echo "Downloading $1"
  curl -fsSL -o "$2.zip" "$1"
  mkdir -p "$2" && (cd "$2" && unzip -q "../$2.zip") && rm "$2.zip"
}

fetch https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.2/BepInEx_win_x64_5.4.23.2.zip BepInEx5
fetch https://github.com/BepInEx/BepInEx/releases/download/v6.0.0-pre.2/BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2.zip BepInEx6-IL2CPP
